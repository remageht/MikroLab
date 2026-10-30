using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MikroLab.Core.Assembler;
using MikroLab.Core.Cpu;
using MikroLab.Core.Debugging;
using MikroLab.Core.Devices;
using MikroLab.Core.Formats;
using MikroLab.Core.Machine;
using MikroLab.Core.Memory;

namespace MikroLab.Presentation
{
    /// <summary>Severity of a message emitted by <see cref="MachineHost"/>.</summary>
    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Owns the emulated machine and the execution thread. Both workspaces use
    /// this object, so switching between Studio and Laboratory never starts a
    /// second CPU or loses the current program. The class has no UI
    /// dependencies and lives in the portable presentation layer.
    /// </summary>
    public sealed class MachineHost : IDisposable
    {
        private readonly Machine _machine;
        private readonly MachineSession _session;
        private readonly MatrixKeyboard _keyboard = new MatrixKeyboard(4, 4);
        private readonly HashSet<int> _breakpointLines = new HashSet<int>();
        private readonly HashSet<ushort> _manualBreakpoints = new HashSet<ushort>();
        private readonly Dictionary<int, ushort> _lineAddresses = new Dictionary<int, ushort>();
        private readonly List<AddressRange> _ranges = new List<AddressRange>();
        private readonly List<MemoryWatchpoint> _watchpoints = new List<MemoryWatchpoint>();
        private ushort _entryPoint = 0x8000;
        private MachineProfile _profile = MachineProfile.Vntu;
        private bool _disposed;

        public MachineHost()
        {
            _machine = new Machine(
                sound: new PcSpeaker(),
                clock: new DemoClock(),
                profile: CpuProfile.Extended);
            _session = new MachineSession(_machine)
            {
                ThrottleLikeOriginal = true,
                InstructionsPerSlice = 2000
            };
            _machine.Ppi.Keyboard = _keyboard;

            _session.ExecutionError += message => Output?.Invoke("Ошибка выполнения: " + message, LogLevel.Error);
            _session.UnsupportedCommand += (pc, opcode) =>
                Output?.Invoke("Неподдерживаемая команда " + opcode.ToString("X2") + "h по адресу " + pc.ToString("X4") + "h", LogLevel.Error);
        }

        // The display latches occupy 83F8h..83FFh, directly below the CPU's
        // default 83FEh stack. Starting user programs at 83F0h keeps CALL/RET
        // stack traffic away from the latches and the SEGGG input at 83F4h.
        public const ushort InitialStackPointer = 0x83F0;

        public Machine Machine => _machine;
        public MachineSession Session => _session;
        public RunState State => _session.State;
        public ExecutionLog ExecutionLog => _session.ExecutionLog;
        public MatrixKeyboard Keyboard => _keyboard;
        public bool CanStepBack => _session.CanStepBack;
        public PauseReason PauseReason => _session.PauseReason;
        public ushort? StopAddress => _session.StopAddress;
        public string StopReason => StopReasonText.Describe(_session);
        public ExecutionLogEntry LastLogEntry
        {
            get
            {
                ExecutionLogEntry[] entries = _session.ExecutionLog.GetLast(1);
                return entries.Length > 0 ? entries[0] : null;
            }
        }
        public bool IsProgramLoaded { get; private set; }
        public AssembleResult LastAssembly { get; private set; }
        public string LastSource { get; private set; } = string.Empty;
        public ushort EntryPoint => _entryPoint;
        public MachineProfile Profile => _profile;
        public ushort Origin => _profile.Origin;
        public ushort StackPointer => _profile.StackPointer;
        public CpuProfile CpuProfile => _machine.Cpu.Profile;

        public event Action<CpuProfile> CpuProfileChanged;
        public event Action<string, LogLevel> Output;

        public void SwitchCpuProfile(CpuProfile profile)
        {
            if (_machine.Cpu.Profile == profile) return;

            _session.SwitchProfile(profile);
            string desc = profile == CpuProfile.Legacy
                ? "Legacy (Лабораторные 1–4, особенности КР580, регистры 83E4h)"
                : "Newest / Extended (Лабораторная 5+, полный Intel 8080)";
            Output?.Invoke("Профиль процессора: " + desc, LogLevel.Info);
            CpuProfileChanged?.Invoke(profile);
        }

        // ------------------------------------------------------------------ assemble/load

        public AssembleResult Assemble(string source)
        {
            if (_session.State == RunState.Running)
            {
                _session.Pause();
            }

            AssembleResult result = I8080Assembler.Assemble(source ?? string.Empty, _profile.Origin);
            LastAssembly = result;
            LastSource = source ?? string.Empty;

            if (!result.Success)
            {
                IsProgramLoaded = false;
                Output?.Invoke("Сборка не удалась · ошибок: " + result.Errors.Count, LogLevel.Error);
                return result;
            }

            ushort entry = result.StartAddress == _profile.Origin
                ? _profile.EntryPoint
                : result.StartAddress;

            _session.WithMachine(machine =>
            {
                machine.Cpu.Reset();
                machine.State = RunState.Stopped;
                machine.Display.Reset();
                machine.Ppi.Reset();

                int clearStart = _profile.Origin;
                int clearEnd = Math.Min(0xFFFF, clearStart + 0x3FF);
                for (int address = clearStart; address <= clearEnd; address++)
                {
                    machine.Memory.WriteByte((ushort)address, 0);
                }

                foreach (AssembledChunk chunk in result.Chunks)
                {
                    for (int i = 0; i < chunk.Data.Length; i++)
                    {
                        machine.Memory.WriteByte((ushort)(chunk.Address + i), chunk.Data[i]);
                    }
                }

                machine.Cpu.State.PC = entry;
                machine.Cpu.State.SP = _profile.StackPointer;
            });

            BuildAddressMap(result);
            ApplyBreakpoints();
            _session.ClearExecutionLog();
            _entryPoint = entry;
            IsProgramLoaded = true;

            int bytes = result.Chunks.Sum(chunk => chunk.Data.Length);
            string range = result.EndAddress > result.StartAddress
                ? result.StartAddress.ToString("X4") + "h–" + (result.EndAddress - 1).ToString("X4") + "h"
                : "—";
            Output?.Invoke(
                "Собрано · " + bytes + " байт · " + range + " · символов: " + result.Symbols.Count +
                " · профиль " + _profile.Name,
                LogLevel.Success);

            return result;
        }

        /// <summary>
        /// Switches the board to another memory/stack preset. The caller is
        /// expected to reassemble the current source so the new default ORG
        /// takes effect.
        /// </summary>
        public void ApplyProfile(MachineProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (_session.State == RunState.Running)
            {
                _session.Pause();
            }

            _profile = profile;
            _session.WithMachine(machine =>
            {
                machine.Cpu.State.PC = profile.EntryPoint;
                machine.Cpu.State.SP = profile.StackPointer;
            });
            _entryPoint = profile.EntryPoint;
            Output?.Invoke("Профиль · " + profile, LogLevel.Info);
        }

        public void ConfigureProfile(string name, ushort origin, ushort entryPoint, ushort stackPointer)
        {
            ApplyProfile(new MachineProfile(name, origin, entryPoint, stackPointer));
        }

        public void LoadSource(string source)
        {
            LastSource = source ?? string.Empty;
            Assemble(LastSource);
        }

        // ------------------------------------------------------------------ Intel HEX / state files

        /// <summary>
        /// Replaces memory with an Intel HEX image, resets devices and points
        /// the CPU at the start address (8000h when the file has none).
        /// </summary>
        public IntelHexImage ImportHex(string path)
        {
            if (_session.State == RunState.Running)
            {
                _session.Pause();
            }

            IntelHexImage image = IntelHex.Load(path);
            if (image.ByteCount == 0)
            {
                throw new InvalidDataException("Файл Intel HEX не содержит данных.");
            }

            ushort start = image.StartAddress ?? _profile.Origin;

            _session.Reset(ResetKind.Soft);
            _session.WithMachine(machine =>
            {
                for (int address = 0; address < MemoryMap.MemSize; address++)
                {
                    machine.Memory.WriteByte((ushort)address, 0);
                }

                foreach (IntelHexSegment segment in image.Segments)
                {
                    for (int i = 0; i < segment.Data.Length; i++)
                    {
                        machine.Memory.WriteByte((ushort)(segment.Address + i), segment.Data[i]);
                    }
                }

                machine.Cpu.State.PC = start;
                machine.Cpu.State.SP = _profile.StackPointer;
            });

            _lineAddresses.Clear();
            _ranges.Clear();
            ClearBreakpoints();
            _session.ClearExecutionLog();
            _entryPoint = start;
            IsProgramLoaded = true;
            LastAssembly = null;

            Output?.Invoke(
                "Импорт HEX · " + image.ByteCount + " байт · PC=" + start.ToString("X4") + "h",
                LogLevel.Success);
            return image;
        }

        /// <summary>Exports the profile's 1 KiB program area as Intel HEX.</summary>
        public void ExportHex(string path)
        {
            if (_session.State == RunState.Running)
            {
                _session.Pause();
            }

            int start = _profile.Origin;
            int end = Math.Min(0xFFFF, start + 0x3FF);
            MachineSnapshot snapshot = _session.CaptureSnapshot();
            IntelHex.Save(path, snapshot.Memory, (ushort)start, (ushort)end);
            Output?.Invoke(
                "Экспорт HEX · " + start.ToString("X4") + "h–" + end.ToString("X4") + "h · " + path,
                LogLevel.Success);
        }

        // ------------------------------------------------------------------ .580 images

        /// <summary>
        /// Loads an original KP580 .580 image: 64 KiB RAM plus registers, PC
        /// and SP. Flags are not part of the format and start from reset.
        /// </summary>
        public Kr580Image Import580(string path)
        {
            if (_session.State == RunState.Running)
            {
                _session.Pause();
            }

            Kr580Image image = Kr580Image.Load(path);
            _session.Reset(ResetKind.Soft);
            _session.WithMachine(machine =>
            {
                machine.Restore(image.ToSnapshot());
                machine.Display.Reset();
                machine.Ppi.Reset();
            });

            _lineAddresses.Clear();
            _ranges.Clear();
            ClearBreakpoints();
            _session.ClearExecutionLog();
            _entryPoint = image.PC;
            IsProgramLoaded = true;
            LastAssembly = null;

            Output?.Invoke(
                "Импорт .580 · PC=" + image.PC.ToString("X4") + "h · SP=" +
                image.SP.ToString("X4") + "h · " + path,
                LogLevel.Success);
            return image;
        }

        /// <summary>Writes the complete machine as an original .580 image.</summary>
        public void Export580(string path)
        {
            if (_session.State == RunState.Running)
            {
                _session.Pause();
            }

            MachineSnapshot snapshot = _session.CaptureSnapshot();
            Kr580Image.FromSnapshot(snapshot).Save(path);
            Output?.Invoke("Экспорт .580 · PC=" + snapshot.PC.ToString("X4") +
                "h · SP=" + snapshot.SP.ToString("X4") + "h · " + path, LogLevel.Success);
        }

        // ------------------------------------------------------------------ raw binary images

        /// <summary>
        /// Loads a raw binary at the profile origin (or at 0000h when the file
        /// is a complete 64 KiB image). No metadata is available in .bin, so
        /// PC starts at the load address and SP comes from the profile.
        /// </summary>
        public ushort ImportBinary(string path)
        {
            if (_session.State == RunState.Running)
            {
                _session.Pause();
            }

            byte[] data = File.ReadAllBytes(path);
            if (data.Length == 0)
            {
                throw new InvalidDataException("Бинарный файл пуст.");
            }

            int start = data.Length == MemoryMap.MemSize ? 0 : _profile.Origin;
            if (start + data.Length > MemoryMap.MemSize)
            {
                throw new InvalidDataException(
                    "Бинарный образ не помещается в 64K по адресу " + start.ToString("X4") + "h.");
            }

            _session.Reset(ResetKind.Soft);
            _session.WithMachine(machine =>
            {
                for (int address = 0; address < MemoryMap.MemSize; address++)
                {
                    machine.Memory.WriteByte((ushort)address, 0);
                }

                for (int i = 0; i < data.Length; i++)
                {
                    machine.Memory.WriteByte((ushort)(start + i), data[i]);
                }

                machine.Cpu.State.Flags.Reset();
                machine.Cpu.State.F = 0x02;
                machine.Cpu.State.PC = (ushort)start;
                machine.Cpu.State.SP = _profile.StackPointer;
                machine.State = RunState.Stopped;
                machine.Display.Reset();
                machine.Ppi.Reset();
            });

            _lineAddresses.Clear();
            _ranges.Clear();
            ClearBreakpoints();
            _session.ClearExecutionLog();
            _entryPoint = (ushort)start;
            IsProgramLoaded = true;
            LastAssembly = null;

            Output?.Invoke(
                "Импорт BIN · " + data.Length + " байт · PC=" + start.ToString("X4") + "h · " + path,
                LogLevel.Success);
            return (ushort)start;
        }

        /// <summary>Writes the profile's 1 KiB program area as a raw binary.</summary>
        public void ExportBinary(string path)
        {
            if (_session.State == RunState.Running)
            {
                _session.Pause();
            }

            int start = _profile.Origin;
            int length = Math.Min(0x400, MemoryMap.MemSize - start);
            MachineSnapshot snapshot = _session.CaptureSnapshot();
            var data = new byte[length];
            Array.Copy(snapshot.Memory, start, data, 0, length);
            File.WriteAllBytes(path, data);
            Output?.Invoke(
                "Экспорт BIN · " + start.ToString("X4") + "h–" +
                (start + length - 1).ToString("X4") + "h · " + path,
                LogLevel.Success);
        }

        /// <summary>Saves a paused, consistent snapshot of the whole machine.</summary>
        public void SaveState(string path)
        {
            if (_session.State == RunState.Running)
            {
                _session.Pause();
            }

            MachineSnapshot snapshot = _session.CaptureSnapshot();
            MachineStateSerializer.Save(path, snapshot, IsProgramLoaded, _entryPoint);
            Output?.Invoke("Состояние сохранено · " + path, LogLevel.Success);
        }

        /// <summary>Restores a machine-state file without touching the editor text.</summary>
        public void LoadState(string path)
        {
            if (_session.State == RunState.Running)
            {
                _session.Pause();
            }

            MachineStateFile state = MachineStateSerializer.Load(path);
            _session.WithMachine(machine => machine.Restore(state.Snapshot));

            _lineAddresses.Clear();
            _ranges.Clear();
            ClearBreakpoints();
            _session.ClearExecutionLog();
            _entryPoint = state.EntryPoint;
            IsProgramLoaded = state.ProgramLoaded;
            LastAssembly = null;

            Output?.Invoke("Состояние загружено · " + path, LogLevel.Success);
        }

        /// <summary>
        /// Writes one CPU register (A…L, F, M, PC, SP) while the machine is
        /// paused. F updates both the PSW byte and the live flag register.
        /// </summary>
        public bool SetRegister(string name, ushort value)
        {
            if (name == null) return false;
            if (_session.State == RunState.Running)
            {
                Output?.Invoke("Остановите выполнение, чтобы редактировать регистры.", LogLevel.Warning);
                return false;
            }

            string upper = name.Trim().ToUpperInvariant();
            bool isWord = upper == "PC" || upper == "SP";
            if (!isWord && value > 0xFF)
            {
                Output?.Invoke("Значение регистра " + upper + " должно быть в диапазоне 00h–FFh.", LogLevel.Warning);
                return false;
            }
            if (!IsRegisterName(upper))
            {
                Output?.Invoke("Неизвестный регистр: " + name, LogLevel.Warning);
                return false;
            }

            _session.WithMachine(machine =>
            {
                switch (upper)
                {
                    case "A": machine.Cpu.State.A = (byte)value; break;
                    case "B": machine.Cpu.State.B = (byte)value; break;
                    case "C": machine.Cpu.State.C = (byte)value; break;
                    case "D": machine.Cpu.State.D = (byte)value; break;
                    case "E": machine.Cpu.State.E = (byte)value; break;
                    case "H": machine.Cpu.State.H = (byte)value; break;
                    case "L": machine.Cpu.State.L = (byte)value; break;
                    case "M": machine.Cpu.State.M = (byte)value; break;
                    case "F":
                        machine.Cpu.State.F = (byte)value;
                        machine.Cpu.State.Flags.FromByte((byte)value);
                        break;
                    case "PC": machine.Cpu.State.PC = value; break;
                    case "SP": machine.Cpu.State.SP = value; break;
                }
            });

            string text = isWord ? value.ToString("X4") : value.ToString("X2");
            Output?.Invoke("Регистр · " + upper + " ← " + text + "h", LogLevel.Info);
            return true;
        }

        private static bool IsRegisterName(string name)
        {
            switch (name)
            {
                case "A":
                case "B":
                case "C":
                case "D":
                case "E":
                case "H":
                case "L":
                case "M":
                case "F":
                case "PC":
                case "SP":
                    return true;
                default:
                    return false;
            }
        }

        private void BuildAddressMap(AssembleResult result)
        {
            _lineAddresses.Clear();
            _ranges.Clear();

            foreach (ListingLine line in result.Listing)
            {
                if (!line.HasAddress || line.Bytes == null || line.Bytes.Length == 0)
                {
                    continue;
                }

                _lineAddresses[line.LineNumber] = line.Address;
                _ranges.Add(new AddressRange(line.Address, line.Bytes.Length, line.LineNumber));
            }

            _ranges.Sort((a, b) => a.Address.CompareTo(b.Address));
        }

        public int? FindLineForAddress(ushort address)
        {
            foreach (AddressRange range in _ranges)
            {
                if (address >= range.Address && address < range.Address + range.Length)
                {
                    return range.Line;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ run control

        public bool Run()
        {
            if (_session.State == RunState.Halted)
            {
                Output?.Invoke("Процессор остановлен (HLT). Сначала выполните сброс.", LogLevel.Warning);
                return false;
            }
            if (!IsProgramLoaded)
            {
                Output?.Invoke("Сначала соберите программу (F7).", LogLevel.Warning);
                return false;
            }
            if (_session.State == RunState.Running)
            {
                return true;
            }

            _session.Run();
            return true;
        }

        public void Pause()
        {
            if (_session.State == RunState.Running)
            {
                _session.Pause();
            }
        }

        public void Step()
        {
            if (_session.State == RunState.Halted)
            {
                Output?.Invoke("Процессор остановлен (HLT). Сначала выполните сброс.", LogLevel.Warning);
                return;
            }
            if (!IsProgramLoaded)
            {
                Output?.Invoke("Сначала соберите программу (F7).", LogLevel.Warning);
                return;
            }

            if (_session.State == RunState.Running)
            {
                _session.Pause();
            }

            _session.Step();
        }

        public void Reset()
        {
            _session.Reset(ResetKind.Soft);
            _session.WithMachine(machine =>
            {
                machine.Cpu.State.PC = EntryPoint;
                machine.Cpu.State.SP = _profile.StackPointer;
            });
        }

        // ------------------------------------------------------------------ breakpoints

        public IReadOnlyCollection<int> BreakpointLines => _breakpointLines;

        public bool ToggleBreakpointLine(int line)
        {
            if (line <= 0)
            {
                return false;
            }

            bool added;
            if (_breakpointLines.Contains(line))
            {
                _breakpointLines.Remove(line);
                added = false;
            }
            else
            {
                _breakpointLines.Add(line);
                added = true;
            }

            ApplyBreakpoints();
            return added;
        }

        public void ClearBreakpoints()
        {
            _breakpointLines.Clear();
            ApplyBreakpoints();
        }

        /// <summary>Removes an editor-line breakpoint without touching address breakpoints.</summary>
        public void RemoveBreakpointLine(int line)
        {
            if (_breakpointLines.Remove(line)) ApplyBreakpoints();
        }

        /// <summary>
        /// Removes every instruction breakpoint (line and manual) but leaves
        /// memory watchpoints intact, so "clear all" in the dialog does not
        /// silently disable observation points.
        /// </summary>
        public void ClearAllBreakpoints()
        {
            _breakpointLines.Clear();
            _manualBreakpoints.Clear();
            ApplyBreakpoints();
        }

        /// <summary>Maps an editor line to the address generated by the last assembly.</summary>
        public bool TryGetAddressForLine(int line, out ushort address)
        {
            return _lineAddresses.TryGetValue(line, out address);
        }

        /// <summary>
        /// Removes a breakpoint by address, whether it was added manually or
        /// through an editor line. Used by the debug-points dialog.
        /// </summary>
        public void RemoveBreakpointAt(ushort address)
        {
            bool changed = _manualBreakpoints.Remove(address);
            foreach (int line in new List<int>(_breakpointLines))
            {
                if (_lineAddresses.TryGetValue(line, out ushort mapped) && mapped == address)
                {
                    _breakpointLines.Remove(line);
                    changed = true;
                }
            }

            if (changed) ApplyBreakpoints();
        }

        public IReadOnlyCollection<ushort> ManualBreakpoints => _manualBreakpoints;

        public bool ToggleAddressBreakpoint(ushort address)
        {
            bool added;
            if (_manualBreakpoints.Contains(address))
            {
                _manualBreakpoints.Remove(address);
                added = false;
            }
            else
            {
                _manualBreakpoints.Add(address);
                added = true;
            }
            ApplyBreakpoints();
            return added;
        }

        public void AddAddressBreakpoint(ushort address)
        {
            if (_manualBreakpoints.Add(address)) ApplyBreakpoints();
        }

        public void RemoveAddressBreakpoint(ushort address)
        {
            if (_manualBreakpoints.Remove(address)) ApplyBreakpoints();
        }

        public void AddWatchpoint(ushort address, MemoryAccess access)
        {
            var watchpoint = new MemoryWatchpoint(address, access);
            if (!_watchpoints.Contains(watchpoint))
            {
                _watchpoints.Add(watchpoint);
                _session.SetWatchpoints(_watchpoints);
            }
        }

        public void RemoveWatchpoint(MemoryWatchpoint watchpoint)
        {
            if (_watchpoints.Remove(watchpoint))
            {
                _session.SetWatchpoints(_watchpoints);
            }
        }

        public ushort[] AddressBreakpoints => _session.GetBreakpoints();

        public MemoryWatchpoint[] Watchpoints => _session.GetWatchpoints();

        public void ClearWatchpoints()
        {
            _watchpoints.Clear();
            _session.ClearWatchpoints();
        }

        public void StepBack()
        {
            if (_session.StepBack())
            {
                Output?.Invoke("Шаг назад · PC=" + CurrentPc().ToString("X4") + "h", LogLevel.Info);
            }
            else
            {
                Output?.Invoke("Нет снимков для шага назад.", LogLevel.Warning);
            }
        }

        public void RunTo(ushort address)
        {
            if (_session.State == RunState.Halted)
            {
                Output?.Invoke("Процессор остановлен (HLT). Сначала выполните сброс.", LogLevel.Warning);
                return;
            }
            if (!IsProgramLoaded)
            {
                Output?.Invoke("Сначала соберите программу (F7).", LogLevel.Warning);
                return;
            }

            _session.RunTo(address);
            Output?.Invoke("Выполнение до " + address.ToString("X4") + "h", LogLevel.Info);
        }

        public void ClearExecutionLog()
        {
            _session.ClearExecutionLog();
            Output?.Invoke("Журнал очищен", LogLevel.Info);
        }

        public void ExportExecutionLog(string path, ExecutionLogFormat format)
        {
            if (_session.State == RunState.Running) _session.Pause();
            _session.ExecutionLog.Save(path, format);
            Output?.Invoke("Журнал сохранён · " + path, LogLevel.Success);
        }

        public void ToggleFlag(string name)
        {
            if (name == null) return;
            if (_session.State == RunState.Running)
            {
                Output?.Invoke("Остановите выполнение, чтобы редактировать флаги.", LogLevel.Warning);
                return;
            }

            _session.WithMachine(machine =>
            {
                FlagReg flags = machine.Cpu.State.Flags;
                switch (name.Trim().ToUpperInvariant())
                {
                    case "C": flags.C = !flags.C; break;
                    case "Z": flags.Z = !flags.Z; break;
                    case "S": flags.S = !flags.S; break;
                    case "P": flags.P = !flags.P; break;
                    case "AC": flags.AC = !flags.AC; break;
                    default: return;
                }
                machine.Cpu.State.Flags = flags;
                machine.Cpu.State.F = flags.ToByte();
            });

            Output?.Invoke("Флаг · " + name.ToUpperInvariant() + " переключён", LogLevel.Info);
        }

        // ------------------------------------------------------------------ matrix keyboard

        /// <summary>
        /// Presses or releases one key of the matrix keyboard attached to port
        /// C. The PPI input latch is refreshed immediately so a running scan
        /// loop sees the new column state on its next IN instruction.
        /// </summary>
        public void SetKeyState(int row, int column, bool pressed)
        {
            if (row < 0 || row >= _keyboard.Rows || column < 0 || column >= _keyboard.Columns)
            {
                return;
            }

            _session.WithMachine(machine =>
            {
                _keyboard.SetKey(row, column, pressed);
                machine.Ppi.RefreshKeyboard();
            });
        }

        public void ClearKeys()
        {
            _session.WithMachine(machine =>
            {
                _keyboard.Clear();
                machine.Ppi.RefreshKeyboard();
            });
        }

        public void ClearMemory()
        {
            _session.WithMachine(machine =>
            {
                for (int address = 0; address < 65536; address++)
                {
                    machine.Memory.WriteByte((ushort)address, 0);
                }
            });
            Output?.Invoke("Очистка памяти: все 64 КБ заполнены нулями (00h).", LogLevel.Info);
        }

        private void ApplyBreakpoints()
        {
            var addresses = new List<ushort>();
            foreach (int line in _breakpointLines)
            {
                if (_lineAddresses.TryGetValue(line, out ushort address))
                {
                    addresses.Add(address);
                }
            }
            foreach (ushort address in _manualBreakpoints)
            {
                addresses.Add(address);
            }
            _session.SetBreakpoints(addresses.Distinct());
        }

        public int ActiveBreakpointCount
        {
            get
            {
                return _session.GetBreakpoints().Length;
            }
        }

        // ------------------------------------------------------------------ state

        public MachineSnapshot Snapshot()
        {
            return _session.CaptureSnapshot();
        }

        public ushort CurrentPc()
        {
            MachineSnapshot snapshot = _session.LastSnapshot;
            return snapshot != null ? snapshot.PC : _machine.Cpu.State.PC;
        }

        public ushort[] CallStack()
        {
            if (_session.State == RunState.Running)
            {
                return Array.Empty<ushort>();
            }
            return _session.GetCallStack();
        }

        public void WithMachine(Action<Machine> action)
        {
            _session.WithMachine(action);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }

        private readonly struct AddressRange
        {
            public ushort Address { get; }
            public int Length { get; }
            public int Line { get; }

            public AddressRange(ushort address, int length, int line)
            {
                Address = address;
                Length = length;
                Line = line;
            }
        }
    }

    internal sealed class DemoClock : IInterruptibleClock
    {
        private volatile bool _stop;

        public void Delay(int milliseconds)
        {
            // A student program must never freeze the execution thread for
            // seconds. Real hardware would wait; a desktop emulator should
            // remain responsive, so long monitor delays are capped. The wait
            // is sliced and can be aborted by pause/reset/dispose.
            int remaining = Math.Max(0, Math.Min(milliseconds, 1500));
            while (remaining > 0 && !_stop)
            {
                int chunk = Math.Min(remaining, 10);
                Thread.Sleep(chunk);
                remaining -= chunk;
            }
        }

        public void RequestStop()
        {
            _stop = true;
        }

        public void ClearStop()
        {
            _stop = false;
        }
    }

    internal sealed class PcSpeaker : ISound
    {
        private int _playing;

        public void PlayTone(uint frequency, uint duration)
        {
            if (frequency < 37 || frequency > 32767) return;
            if (Interlocked.Exchange(ref _playing, 1) != 0) return;

            uint ms = Math.Max(20, Math.Min(duration, 250));
            Task.Run(() =>
            {
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        Console.Beep((int)frequency, (int)ms);
                    }
                }
                catch
                {
                    // Some environments have no beep device.
                }
                finally
                {
                    Interlocked.Exchange(ref _playing, 0);
                }
            });
        }

        public void PlayTestTone()
        {
            PlayTone(880, 150);
        }
    }
}

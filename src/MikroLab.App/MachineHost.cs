using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MikroLab.Core.Assembler;
using MikroLab.Core.Cpu;
using MikroLab.Core.Devices;
using MikroLab.Core.Machine;
using MikroLab.Core.Memory;

namespace MikroLab.App
{
    internal enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Owns the emulated machine and the execution thread. Both workspaces use
    /// this object, so switching between Studio and Laboratory never starts a
    /// second CPU or loses the current program.
    /// </summary>
    internal sealed class MachineHost : IDisposable
    {
        private readonly Machine _machine;
        private readonly MachineSession _session;
        private readonly HashSet<int> _breakpointLines = new HashSet<int>();
        private readonly Dictionary<int, ushort> _lineAddresses = new Dictionary<int, ushort>();
        private readonly List<AddressRange> _ranges = new List<AddressRange>();
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
        public bool IsProgramLoaded { get; private set; }
        public AssembleResult LastAssembly { get; private set; }
        public string LastSource { get; private set; } = string.Empty;
        public ushort EntryPoint => LastAssembly != null ? LastAssembly.StartAddress : (ushort)0x8000;

        public event Action<string, LogLevel> Output;

        // ------------------------------------------------------------------ assemble/load

        public AssembleResult Assemble(string source)
        {
            if (_session.State == RunState.Running)
            {
                _session.Pause();
            }

            AssembleResult result = I8080Assembler.Assemble(source ?? string.Empty, 0x8000);
            LastAssembly = result;
            LastSource = source ?? string.Empty;

            if (!result.Success)
            {
                IsProgramLoaded = false;
                Output?.Invoke("Сборка не удалась · ошибок: " + result.Errors.Count, LogLevel.Error);
                return result;
            }

            _session.WithMachine(machine =>
            {
                machine.Cpu.Reset();
                machine.State = RunState.Stopped;
                machine.Display.Reset();
                machine.Ppi.Lamps = 0;
                machine.Ppi.InputWord = 0xFF;
                machine.Ppi.ControlWord = 0xFF;

                for (int address = 0x8000; address <= 0x83FF; address++)
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

                machine.Cpu.State.PC = result.StartAddress;
                machine.Cpu.State.SP = InitialStackPointer;
            });

            BuildAddressMap(result);
            ApplyBreakpoints();
            IsProgramLoaded = true;

            int bytes = result.Chunks.Sum(chunk => chunk.Data.Length);
            string range = result.EndAddress > result.StartAddress
                ? result.StartAddress.ToString("X4") + "h–" + (result.EndAddress - 1).ToString("X4") + "h"
                : "—";
            Output?.Invoke(
                "Собрано · " + bytes + " байт · " + range + " · символов: " + result.Symbols.Count,
                LogLevel.Success);

            return result;
        }

        public void LoadSource(string source)
        {
            LastSource = source ?? string.Empty;
            Assemble(LastSource);
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
                machine.Cpu.State.SP = InitialStackPointer;
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
            _session.SetBreakpoints(addresses);
        }

        public int ActiveBreakpointCount
        {
            get
            {
                int count = 0;
                foreach (int line in _breakpointLines)
                {
                    if (_lineAddresses.ContainsKey(line)) count++;
                }
                return count;
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

    internal sealed class DemoClock : IClock
    {
        public void Delay(int milliseconds)
        {
            // A student program must never freeze the execution thread for
            // seconds. Real hardware would wait; a desktop emulator should
            // remain responsive, so long monitor delays are capped.
            int wait = Math.Max(0, Math.Min(milliseconds, 1500));
            if (wait > 0)
            {
                Thread.Sleep(wait);
            }
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
                    Console.Beep((int)frequency, (int)ms);
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

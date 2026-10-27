using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MikroLab.Core.Devices;

namespace MikroLab.Core.Machine
{
    /// <summary>
    /// Owns the execution thread for a <see cref="Machine"/> and exposes a
    /// thread-safe control surface for debuggers and other hosts.
    /// </summary>
    public sealed class MachineSession : IDisposable
    {
        private readonly Machine _machine;
        private readonly object _lock = new object();
        private readonly HashSet<ushort> _breakpoints = new HashSet<ushort>();
        private Thread _runThread;
        private volatile bool _isRunning;
        private volatile bool _shouldStop;
        private volatile bool _isDisposed;
        private volatile MachineSnapshot _lastSnapshot;
        private int _skipBreakpointOnce = -1;
        private int _breakpointAddress = -1;

        public Machine Machine => _machine;
        public RunState State => _machine.State;

        /// <summary>
        /// Last snapshot published by the execution loop. Safe to read from any
        /// thread and never blocks, even while the machine is executing.
        /// </summary>
        public MachineSnapshot LastSnapshot => _lastSnapshot;

        /// <summary>True when execution stopped because a breakpoint was reached.</summary>
        public bool IsPausedAtBreakpoint => _breakpointAddress >= 0;

        /// <summary>Address of the breakpoint that stopped execution, if any.</summary>
        public ushort? BreakpointAddress
        {
            get
            {
                int value = _breakpointAddress;
                return value >= 0 ? (ushort)value : (ushort?)null;
            }
        }

        public event Action<MachineSnapshot> SnapshotUpdated;
        public event Action<string> ExecutionError;
        public event Action<ushort, byte> UnsupportedCommand;

        /// <summary>Sleep 5 ms per instruction slice to mimic the original timing.</summary>
        public bool ThrottleLikeOriginal { get; set; } = true;

        /// <summary>Instructions executed between timing sleeps and snapshot checks.</summary>
        public int InstructionsPerSlice { get; set; } = 2000;

        public MachineSession(Machine machine)
        {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            if (_machine.Diagnostics == null)
            {
                _machine.Diagnostics = new SessionDiagnostics(this);
            }
        }

        private class SessionDiagnostics : IDiagnostics
        {
            private readonly MachineSession _session;
            public SessionDiagnostics(MachineSession session) => _session = session;
            public void ReportError(string message) => _session.ExecutionError?.Invoke(message);
            public void ReportUnsupported(int pc, byte opcode) => _session.UnsupportedCommand?.Invoke((ushort)pc, opcode);
        }

        // ---------------------------------------------------------------- run control

        public void Run()
        {
            lock (_lock)
            {
                if (_isRunning) return;

                // When the user resumes from a breakpoint, execute the
                // instruction under the PC once instead of immediately
                // stopping on the same breakpoint again.
                _skipBreakpointOnce = _machine.Cpu.State.PC;
                _breakpointAddress = -1;
                _machine.State = RunState.Running;
                _isRunning = true;
                _shouldStop = false;

                _runThread = new Thread(RunWorker)
                {
                    IsBackground = true,
                    Name = "MachineExecutionLoop"
                };
                _runThread.Start();
            }
        }

        public void Pause()
        {
            lock (_lock)
            {
                if (!_isRunning) return;
                _shouldStop = true;
            }

            _runThread?.Join(500);

            lock (_lock)
            {
                _machine.State = RunState.Paused;
                _isRunning = false;
                _breakpointAddress = -1;
            }
            PublishSnapshot();
        }

        public void Step()
        {
            if (_isRunning)
            {
                Pause();
            }

            lock (_lock)
            {
                _breakpointAddress = -1;
                _skipBreakpointOnce = -1;
                _machine.State = RunState.Running;
                bool ok = _machine.Step();
                _machine.State = ok ? RunState.Paused : RunState.Halted;
            }
            PublishSnapshot();
        }

        public void Reset(ResetKind kind = ResetKind.Soft)
        {
            lock (_lock)
            {
                if (_isRunning)
                {
                    _shouldStop = true;
                }
            }

            _runThread?.Join(500);

            lock (_lock)
            {
                _isRunning = false;
                _breakpointAddress = -1;
                _skipBreakpointOnce = -1;
                _machine.Reset(kind);
            }
            PublishSnapshot();
        }

        // ---------------------------------------------------------------- breakpoints

        /// <summary>Replaces the breakpoint set. Thread-safe.</summary>
        public void SetBreakpoints(IEnumerable<ushort> addresses)
        {
            lock (_lock)
            {
                _breakpoints.Clear();
                if (addresses != null)
                {
                    foreach (ushort address in addresses)
                    {
                        _breakpoints.Add(address);
                    }
                }
            }
        }

        public ushort[] GetBreakpoints()
        {
            lock (_lock)
            {
                var result = new ushort[_breakpoints.Count];
                _breakpoints.CopyTo(result);
                Array.Sort(result);
                return result;
            }
        }

        public bool HasBreakpoint(ushort address)
        {
            lock (_lock)
            {
                return _breakpoints.Contains(address);
            }
        }

        /// <summary>Debug-only call stack (return addresses, top first).</summary>
        public ushort[] GetCallStack()
        {
            lock (_lock)
            {
                var addresses = _machine.Cpu.GetCallStack();
                var result = new ushort[addresses.Count];
                for (int i = 0; i < addresses.Count; i++)
                {
                    result[i] = addresses[i];
                }
                return result;
            }
        }

        // ---------------------------------------------------------------- host access

        /// <summary>
        /// Runs a mutation (memory edit, tumbler change, register poke) under
        /// the machine lock and publishes a fresh snapshot afterwards.
        /// </summary>
        public void WithMachine(Action<Machine> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            lock (_lock)
            {
                action(_machine);
            }
            PublishSnapshot();
        }

        /// <summary>
        /// Returns the latest snapshot without blocking while the machine runs.
        /// When paused/stopped a fresh snapshot is captured.
        /// </summary>
        public MachineSnapshot CaptureSnapshot()
        {
            if (_isRunning)
            {
                return _lastSnapshot;
            }

            lock (_lock)
            {
                MachineSnapshot snapshot = _machine.Capture();
                _lastSnapshot = snapshot;
                return snapshot;
            }
        }

        // ---------------------------------------------------------------- worker

        private void RunWorker()
        {
            var stopwatch = Stopwatch.StartNew();
            long lastSnapshotTime = 0;

            try
            {
                while (!_shouldStop && !_isDisposed)
                {
                    bool canContinue = true;
                    bool breakpointHit = false;

                    lock (_lock)
                    {
                        for (int i = 0; i < InstructionsPerSlice; i++)
                        {
                            if (_shouldStop) break;

                            ushort pc = _machine.Cpu.State.PC;
                            if (_breakpoints.Contains(pc) && pc != _skipBreakpointOnce)
                            {
                                _machine.State = RunState.Paused;
                                _breakpointAddress = pc;
                                _shouldStop = true;
                                breakpointHit = true;
                                break;
                            }

                            _skipBreakpointOnce = -1;
                            canContinue = _machine.Step();
                            if (!canContinue)
                            {
                                _machine.State = RunState.Halted;
                                break;
                            }
                        }
                    }

                    long now = stopwatch.ElapsedMilliseconds;
                    if (breakpointHit || now - lastSnapshotTime >= 33) // ~30 Hz
                    {
                        PublishSnapshot();
                        lastSnapshotTime = now;
                    }

                    if (breakpointHit)
                        break;

                    if (!canContinue)
                        break;

                    if (ThrottleLikeOriginal)
                    {
                        // Sleep 5ms every slice of instructions to match original responsiveness & timing
                        Thread.Sleep(5);
                    }
                    else
                    {
                        Thread.Yield();
                    }
                }
            }
            catch (Exception ex)
            {
                ExecutionError?.Invoke(ex.Message);
            }
            finally
            {
                lock (_lock)
                {
                    _isRunning = false;
                    if (_machine.State == RunState.Running)
                        _machine.State = RunState.Paused;
                }
                PublishSnapshot();
            }
        }

        public void PublishSnapshot()
        {
            MachineSnapshot snap;
            lock (_lock)
            {
                snap = _machine.Capture();
                _lastSnapshot = snap;
            }
            SnapshotUpdated?.Invoke(snap);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _shouldStop = true;
            _runThread?.Join(500);
        }
    }
}

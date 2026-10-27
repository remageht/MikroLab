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
        private const int StopTimeoutMilliseconds = 1000;
        private const int PacingChunkMilliseconds = 5;

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
        private int _targetInstructionsPerSecond;

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

        /// <summary>
        /// Target execution rate in instructions per second. Zero disables the
        /// rate limit (use <see cref="ThrottleLikeOriginal"/> for the original
        /// 5 ms pacing). The worker adapts the slice size and sleeps in short
        /// chunks, so pause and reset stay responsive.
        /// </summary>
        public int TargetInstructionsPerSecond
        {
            get => _targetInstructionsPerSecond;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _targetInstructionsPerSecond = value;
            }
        }

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
            ThrowIfDisposed();

            lock (_lock)
            {
                if (_isRunning) return;

                // Resume from a breakpoint at the current PC: execute the
                // instruction under PC once instead of stopping on the same
                // breakpoint again. A fresh run must still stop there.
                int currentPc = _machine.Cpu.State.PC;
                _skipBreakpointOnce = _breakpointAddress == currentPc ? currentPc : -1;
                _breakpointAddress = -1;
                _machine.State = RunState.Running;
                _isRunning = true;
                _shouldStop = false;

                ClearClockStop();

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
            if (!_isRunning) return;

            // Set the stop flag and interrupt a monitor delay before waiting
            // for the machine lock: the worker holds that lock while the
            // delay is in progress.
            _shouldStop = true;
            RequestClockStop();

            lock (_lock)
            {
                if (!_isRunning) return;
                _shouldStop = true;
            }

            Thread worker = _runThread;
            if (worker == Thread.CurrentThread)
            {
                // Called from a snapshot handler on the execution thread:
                // the worker exits on its own and publishes the final state.
                return;
            }

            if (worker != null && !worker.Join(StopTimeoutMilliseconds))
            {
                // Never pretend to be paused while the worker is still alive.
                ExecutionError?.Invoke("Поток выполнения не остановился за " +
                    StopTimeoutMilliseconds + " мс.");
                return;
            }

            lock (_lock)
            {
                if (_machine.State == RunState.Running)
                {
                    _machine.State = RunState.Paused;
                    _breakpointAddress = -1;
                }
                _isRunning = false;
            }
            PublishSnapshot();
        }

        public void Step()
        {
            ThrowIfDisposed();

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
            ThrowIfDisposed();

            bool wasRunning;
            if (_isRunning)
            {
                // Same ordering as Pause: set the stop flag and abort a monitor
                // delay before trying to acquire a lock held by the step.
                _shouldStop = true;
                RequestClockStop();
            }

            lock (_lock)
            {
                wasRunning = _isRunning;
                if (wasRunning)
                {
                    _shouldStop = true;
                }
            }

            if (wasRunning)
            {
                Thread worker = _runThread;
                if (worker != null && worker != Thread.CurrentThread &&
                    !worker.Join(StopTimeoutMilliseconds))
                {
                    ExecutionError?.Invoke("Сброс отменён: поток выполнения не остановился.");
                    return;
                }
            }

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
            ThrowIfDisposed();

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
            ThrowIfDisposed();
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
            long pacedInstructions = 0;
            long paceStartMilliseconds = 0;
            int paceTarget = 0;

            try
            {
                while (!_shouldStop && !_isDisposed)
                {
                    bool canContinue = true;
                    bool breakpointHit = false;
                    int executed = 0;
                    int target = TargetInstructionsPerSecond;
                    int slice = Math.Max(1, InstructionsPerSlice);
                    if (target > 0)
                    {
                        // Aim for ~5 ms of work per slice so rate changes and
                        // pause requests are observed quickly.
                        slice = Math.Max(1, Math.Min(slice, target / 200));
                    }

                    lock (_lock)
                    {
                        for (int i = 0; i < slice; i++)
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
                            executed++;
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

                    if (breakpointHit || !canContinue)
                        break;

                    if (target > 0)
                    {
                        if (target != paceTarget)
                        {
                            paceTarget = target;
                            pacedInstructions = 0;
                            paceStartMilliseconds = stopwatch.ElapsedMilliseconds;
                        }

                        pacedInstructions += executed;
                        long targetMs = paceStartMilliseconds +
                            pacedInstructions * 1000L / target;
                        SleepUntil(targetMs, stopwatch);
                    }
                    else
                    {
                        paceTarget = 0;
                        pacedInstructions = 0;
                        if (ThrottleLikeOriginal)
                        {
                            // Sleep 5ms every slice of instructions to match original responsiveness & timing.
                            Thread.Sleep(5);
                        }
                        else
                        {
                            Thread.Yield();
                        }
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
                    if (ReferenceEquals(_runThread, Thread.CurrentThread))
                        _runThread = null;
                }
                PublishSnapshot();
            }
        }

        /// <summary>
        /// Sleeps until the cumulative target time is reached, in short chunks
        /// so a pause request is never ignored for long.
        /// </summary>
        private void SleepUntil(long targetMilliseconds, Stopwatch stopwatch)
        {
            while (!_shouldStop && !_isDisposed)
            {
                long remaining = targetMilliseconds - stopwatch.ElapsedMilliseconds;
                if (remaining <= 0) return;

                Thread.Sleep((int)Math.Min(remaining, PacingChunkMilliseconds));
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
            RequestClockStop();

            Thread worker = _runThread;
            if (worker != null && worker != Thread.CurrentThread)
            {
                worker.Join(StopTimeoutMilliseconds * 2);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(MachineSession));
        }

        private void RequestClockStop()
        {
            if (_machine.Clock is IInterruptibleClock clock)
                clock.RequestStop();
        }

        private void ClearClockStop()
        {
            if (_machine.Clock is IInterruptibleClock clock)
                clock.ClearStop();
        }
    }
}

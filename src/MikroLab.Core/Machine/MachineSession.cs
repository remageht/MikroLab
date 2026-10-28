using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MikroLab.Core.Debugging;
using MikroLab.Core.Devices;
using MikroLab.Core.Memory;

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
        private readonly HashSet<MemoryWatchpoint> _watchpoints = new HashSet<MemoryWatchpoint>();
        private readonly HashSet<ushort> _readWatchpoints = new HashSet<ushort>();
        private readonly HashSet<ushort> _writeWatchpoints = new HashSet<ushort>();
        private readonly ExecutionLog _executionLog = new ExecutionLog();
        private readonly StepHistory _stepHistory = new StepHistory();
        private Thread _runThread;
        private volatile bool _isRunning;
        private volatile bool _shouldStop;
        private volatile bool _isDisposed;
        private volatile bool _logExecution = true;
        private volatile MachineSnapshot _lastSnapshot;
        private int _skipBreakpointOnce = -1;
        private int _breakpointAddress = -1;
        private int _runToAddress = -1;
        private int _stopAddress = -1;
        private int _watchpointAddress = -1;
        private int _watchpointAccess;
        private int _pauseReason;
        private volatile int _pendingWatchpointAddress = -1;
        private volatile int _pendingWatchpointAccess;
        private int _targetInstructionsPerSecond;

        public Machine Machine => _machine;
        public RunState State => _machine.State;

        /// <summary>
        /// Last snapshot published by the execution loop. Safe to read from any
        /// thread and never blocks, even while the machine is executing.
        /// </summary>
        public MachineSnapshot LastSnapshot => _lastSnapshot;

        /// <summary>Bounded instruction log shared by the UI and exporters.</summary>
        public ExecutionLog ExecutionLog => _executionLog;

        /// <summary>Snapshots taken before single-steps, newest last.</summary>
        public StepHistory StepHistory => _stepHistory;

        public bool CanStepBack => _stepHistory.CanStepBack;

        /// <summary>When true, every executed instruction is appended to the log.</summary>
        public bool LogExecution
        {
            get => _logExecution;
            set => _logExecution = value;
        }

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

        public bool IsPausedAtWatchpoint => _watchpointAddress >= 0;

        public ushort? WatchpointAddress
        {
            get
            {
                int value = _watchpointAddress;
                return value >= 0 ? (ushort)value : (ushort?)null;
            }
        }

        public MemoryAccess? WatchpointAccess
        {
            get
            {
                int address = _watchpointAddress;
                return address >= 0 ? (MemoryAccess)_watchpointAccess : (MemoryAccess?)null;
            }
        }

        /// <summary>Address associated with the stop reason (breakpoint, watch or run-to).</summary>
        public ushort? StopAddress
        {
            get
            {
                int value = _stopAddress;
                return value >= 0 ? (ushort)value : (ushort?)null;
            }
        }

        public PauseReason PauseReason => (PauseReason)_pauseReason;

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

            _machine.AddressSpace.MemoryAccessed += OnMemoryAccessed;
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
            StartRun(clearRunTo: true);
        }

        /// <summary>
        /// Runs until the CPU reaches <paramref name="address"/> (before
        /// executing the instruction there) or another stop condition fires.
        /// </summary>
        public void RunTo(ushort address)
        {
            ThrowIfDisposed();

            if (_isRunning)
            {
                Pause();
            }

            bool stopImmediately;
            bool halted;
            lock (_lock)
            {
                _runToAddress = address;
                _pauseReason = (int)PauseReason.None;
                _breakpointAddress = -1;
                _watchpointAddress = -1;
                _stopAddress = -1;
                _pendingWatchpointAddress = -1;
                _stepHistory.Clear();

                halted = _machine.State == RunState.Halted;
                stopImmediately = !halted && _machine.Cpu.State.PC == address;

                if (halted)
                {
                    _runToAddress = -1;
                    _pauseReason = (int)PauseReason.Halted;
                }
                else if (stopImmediately)
                {
                    _runToAddress = -1;
                    _machine.State = RunState.Paused;
                    _pauseReason = (int)PauseReason.RunToAddress;
                    _stopAddress = address;
                }
            }

            if (halted || stopImmediately)
            {
                PublishSnapshot();
                return;
            }

            StartRun(clearRunTo: false);
        }

        private void StartRun(bool clearRunTo)
        {
            lock (_lock)
            {
                if (_isRunning) return;

                if (clearRunTo)
                {
                    _runToAddress = -1;
                    _pauseReason = (int)PauseReason.None;
                    // A fresh run starts a fresh journal. Step/run-to keep the
                    // existing log so a paused debugging session is not lost.
                    _executionLog.Clear();
                }

                // Resume from a breakpoint at the current PC: execute the
                // instruction under PC once instead of stopping on the same
                // breakpoint again. A fresh run must still stop there.
                int currentPc = _machine.Cpu.State.PC;
                _skipBreakpointOnce = _breakpointAddress == currentPc ? currentPc : -1;
                _breakpointAddress = -1;
                _watchpointAddress = -1;
                _stopAddress = -1;
                _pendingWatchpointAddress = -1;
                _machine.State = RunState.Running;
                _isRunning = true;
                _shouldStop = false;
                _stepHistory.Clear();

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
                    _runToAddress = -1;
                    _pauseReason = (int)PauseReason.Manual;
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
                _runToAddress = -1;
                _watchpointAddress = -1;
                _stopAddress = -1;
                _pendingWatchpointAddress = -1;

                ushort pc = _machine.Cpu.State.PC;
                ulong cyclesBefore = _machine.Cpu.State.Cycles;
                bool log = _logExecution;
                DisassembledInstruction instruction = log
                    ? I8080Disassembler.Decode(_machine.Memory, pc)
                    : default;

                _stepHistory.Push(_machine.Capture(), log);
                _machine.State = RunState.Running;
                bool ok = _machine.Step();
                if (log)
                {
                    AppendLog(pc, instruction, cyclesBefore);
                }

                bool watchpoint = TakePendingWatchpoint();
                if (!ok)
                {
                    _machine.State = RunState.Halted;
                    if (!watchpoint) _pauseReason = (int)PauseReason.Halted;
                }
                else
                {
                    _machine.State = RunState.Paused;
                }

                if (!watchpoint)
                {
                    _pauseReason = ok ? (int)PauseReason.None : (int)PauseReason.Halted;
                }
            }
            PublishSnapshot();
        }

        /// <summary>
        /// Restores the machine state captured before the most recent single
        /// step. Returns false when the history is empty.
        /// </summary>
        public bool StepBack()
        {
            ThrowIfDisposed();

            if (_isRunning)
            {
                Pause();
            }

            MachineSnapshot snapshot;
            lock (_lock)
            {
                if (!_stepHistory.TryPop(out snapshot, out bool logged)) return false;

                // Rewind the matching journal entry as well; otherwise the log
                // keeps instructions that were undone by this step.
                if (logged)
                {
                    _executionLog.RemoveLast();
                }

                _breakpointAddress = -1;
                _skipBreakpointOnce = -1;
                _runToAddress = -1;
                _watchpointAddress = -1;
                _stopAddress = -1;
                _pendingWatchpointAddress = -1;

                _machine.Restore(snapshot);
                _machine.State = snapshot.Halted ? RunState.Halted : RunState.Paused;
                _pauseReason = (int)(snapshot.Halted ? PauseReason.Halted : PauseReason.Manual);
            }
            PublishSnapshot();
            return true;
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
                _runToAddress = -1;
                _watchpointAddress = -1;
                _stopAddress = -1;
                _pauseReason = (int)PauseReason.None;
                _pendingWatchpointAddress = -1;
                _stepHistory.Clear();
                _executionLog.Clear();
                _machine.Reset(kind);
            }
            PublishSnapshot();
        }

        public void ClearExecutionLog()
        {
            ThrowIfDisposed();
            _executionLog.Clear();
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

        // ---------------------------------------------------------------- watchpoints

        /// <summary>Replaces the read/write watchpoint set. Thread-safe.</summary>
        public void SetWatchpoints(IEnumerable<MemoryWatchpoint> watchpoints)
        {
            ThrowIfDisposed();

            lock (_lock)
            {
                _watchpoints.Clear();
                _readWatchpoints.Clear();
                _writeWatchpoints.Clear();

                if (watchpoints != null)
                {
                    foreach (MemoryWatchpoint watchpoint in watchpoints)
                    {
                        if (!_watchpoints.Add(watchpoint)) continue;
                        if (watchpoint.WatchesRead) _readWatchpoints.Add(watchpoint.Address);
                        if (watchpoint.WatchesWrite) _writeWatchpoints.Add(watchpoint.Address);
                    }
                }

                _pendingWatchpointAddress = -1;
            }
        }

        public MemoryWatchpoint[] GetWatchpoints()
        {
            lock (_lock)
            {
                var result = new MemoryWatchpoint[_watchpoints.Count];
                _watchpoints.CopyTo(result);
                Array.Sort(result, (left, right) =>
                {
                    int address = left.Address.CompareTo(right.Address);
                    return address != 0 ? address : left.Access.CompareTo(right.Access);
                });
                return result;
            }
        }

        public bool HasWatchpoint(ushort address, MemoryAccess access)
        {
            lock (_lock)
            {
                if ((access & MemoryAccess.Read) != 0 && _readWatchpoints.Contains(address)) return true;
                return (access & MemoryAccess.Write) != 0 && _writeWatchpoints.Contains(address);
            }
        }

        public void ClearWatchpoints()
        {
            SetWatchpoints(null);
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
        /// the machine lock and publishes a fresh snapshot afterwards. The
        /// single-step history is invalidated because arbitrary edits are not
        /// represented in the snapshot ring.
        /// </summary>
        public void WithMachine(Action<Machine> action)
        {
            ThrowIfDisposed();
            if (action == null) throw new ArgumentNullException(nameof(action));

            lock (_lock)
            {
                _stepHistory.Clear();
                _pendingWatchpointAddress = -1;
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

        private void OnMemoryAccessed(ushort address, byte value, MemoryAccess access)
        {
            if (_pendingWatchpointAddress >= 0) return;

            if ((access & MemoryAccess.Read) != 0 && _readWatchpoints.Contains(address))
            {
                _pendingWatchpointAddress = address;
                _pendingWatchpointAccess = (int)MemoryAccess.Read;
                return;
            }

            if ((access & MemoryAccess.Write) != 0 && _writeWatchpoints.Contains(address))
            {
                _pendingWatchpointAddress = address;
                _pendingWatchpointAccess = (int)MemoryAccess.Write;
            }
        }

        private bool TakePendingWatchpoint()
        {
            int address = _pendingWatchpointAddress;
            if (address < 0) return false;

            _watchpointAddress = address;
            _watchpointAccess = _pendingWatchpointAccess;
            _stopAddress = address;
            _pauseReason = (int)PauseReason.Watchpoint;
            _pendingWatchpointAddress = -1;
            return true;
        }

        private void AppendLog(ushort pc, DisassembledInstruction instruction, ulong cyclesBefore)
        {
            ulong cyclesAfter = _machine.Cpu.State.Cycles;
            ulong cycles = cyclesAfter >= cyclesBefore ? cyclesAfter - cyclesBefore : 0;
            _executionLog.Append(pc, instruction, _machine.Cpu.State, cycles);
        }

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
                    bool watchpointHit = false;
                    bool runToHit = false;
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

                            if (_runToAddress >= 0 && pc == _runToAddress)
                            {
                                _machine.State = RunState.Paused;
                                _runToAddress = -1;
                                _pauseReason = (int)PauseReason.RunToAddress;
                                _stopAddress = pc;
                                _shouldStop = true;
                                runToHit = true;
                                break;
                            }

                            if (_breakpoints.Contains(pc) && pc != _skipBreakpointOnce)
                            {
                                _machine.State = RunState.Paused;
                                _breakpointAddress = pc;
                                _stopAddress = pc;
                                _pauseReason = (int)PauseReason.Breakpoint;
                                _shouldStop = true;
                                breakpointHit = true;
                                break;
                            }

                            _skipBreakpointOnce = -1;
                            _pendingWatchpointAddress = -1;

                            ulong cyclesBefore = _machine.Cpu.State.Cycles;
                            bool log = _logExecution;
                            DisassembledInstruction instruction = log
                                ? I8080Disassembler.Decode(_machine.Memory, pc)
                                : default;

                            canContinue = _machine.Step();
                            executed++;
                            if (log)
                            {
                                AppendLog(pc, instruction, cyclesBefore);
                            }

                            if (TakePendingWatchpoint())
                            {
                                watchpointHit = true;
                                _shouldStop = true;
                            }

                            if (!canContinue)
                            {
                                _machine.State = RunState.Halted;
                                if (_pauseReason == (int)PauseReason.None)
                                    _pauseReason = (int)PauseReason.Halted;
                                break;
                            }

                            if (watchpointHit) break;
                        }
                    }

                    long now = stopwatch.ElapsedMilliseconds;
                    if (breakpointHit || watchpointHit || runToHit || now - lastSnapshotTime >= 33) // ~30 Hz
                    {
                        PublishSnapshot();
                        lastSnapshotTime = now;
                    }

                    if (breakpointHit || watchpointHit || runToHit || !canContinue)
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
                    _runToAddress = -1;
                    _pendingWatchpointAddress = -1;
                    if (_machine.State == RunState.Running)
                        _machine.State = RunState.Paused;
                    if (_machine.State == RunState.Halted)
                    {
                        if (_pauseReason == (int)PauseReason.None)
                            _pauseReason = (int)PauseReason.Halted;
                    }
                    else if (_pauseReason == (int)PauseReason.None)
                    {
                        _pauseReason = (int)PauseReason.Manual;
                    }
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
            _machine.AddressSpace.MemoryAccessed -= OnMemoryAccessed;

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

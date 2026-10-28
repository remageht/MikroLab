using System;
using MikroLab.Core.Machine;

namespace MikroLab.Core.Debugging
{
    /// <summary>
    /// Bounded history of machine snapshots used by "step back". A snapshot is
    /// pushed before each single-step; the newest snapshot is restored first.
    /// The capacity is intentionally bounded to keep 64 KiB memory copies from
    /// growing without limit.
    /// </summary>
    public sealed class StepHistory
    {
        public const int MinCapacity = 100;
        public const int MaxCapacity = 500;
        public const int DefaultCapacity = 200;

        private readonly object _sync = new object();
        private readonly MachineSnapshot[] _items;
        private readonly bool[] _logged;
        private int _start;
        private int _count;
        private long _revision;

        public StepHistory(int capacity = DefaultCapacity)
        {
            if (capacity < MinCapacity || capacity > MaxCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    capacity,
                    "Step history capacity must be between " + MinCapacity + " and " + MaxCapacity + ".");
            }
            _items = new MachineSnapshot[capacity];
            _logged = new bool[capacity];
        }

        public int Capacity => _items.Length;

        public int Count
        {
            get
            {
                lock (_sync) return _count;
            }
        }

        public bool CanStepBack => Count > 0;

        public long Revision
        {
            get
            {
                lock (_sync) return _revision;
            }
        }

        public void Push(MachineSnapshot snapshot)
        {
            Push(snapshot, false);
        }

        /// <summary>
        /// Pushes a snapshot and records whether the matching instruction was
        /// written to the execution log, so step-back can rewind both in sync.
        /// </summary>
        public void Push(MachineSnapshot snapshot, bool logged)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            lock (_sync)
            {
                int index;
                if (_count < _items.Length)
                {
                    index = (_start + _count) % _items.Length;
                    _count++;
                }
                else
                {
                    index = _start;
                    _start = (_start + 1) % _items.Length;
                }
                _items[index] = snapshot;
                _logged[index] = logged;
                _revision++;
            }
        }

        public bool TryPop(out MachineSnapshot snapshot)
        {
            return TryPop(out snapshot, out _);
        }

        public bool TryPop(out MachineSnapshot snapshot, out bool logged)
        {
            lock (_sync)
            {
                if (_count == 0)
                {
                    snapshot = null;
                    logged = false;
                    return false;
                }

                int index = (_start + _count - 1) % _items.Length;
                snapshot = _items[index];
                logged = _logged[index];
                _items[index] = null;
                _logged[index] = false;
                _count--;
                if (_count == 0)
                {
                    _start = 0;
                }
                _revision++;
                return true;
            }
        }

        public MachineSnapshot Peek()
        {
            lock (_sync)
            {
                if (_count == 0) return null;
                return _items[(_start + _count - 1) % _items.Length];
            }
        }

        public MachineSnapshot[] ToArray()
        {
            lock (_sync)
            {
                var result = new MachineSnapshot[_count];
                for (int i = 0; i < _count; i++)
                {
                    result[i] = _items[(_start + i) % _items.Length];
                }
                return result;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                Array.Clear(_items, 0, _items.Length);
                Array.Clear(_logged, 0, _logged.Length);
                _start = 0;
                _count = 0;
                _revision++;
            }
        }
    }
}

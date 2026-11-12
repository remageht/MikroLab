using System;
using System.Collections.Generic;
using System.Text;

namespace MikroLab.Core.Cpu.I8086
{
    /// <summary>
    /// Text console used by the emulated DOS services (INT 21h / INT 16h).
    /// The studio shows it in the output panel, the CLI prints it to stdout.
    /// </summary>
    public sealed class I8086Console
    {
        private readonly StringBuilder _output = new StringBuilder();
        private readonly Queue<byte> _input = new Queue<byte>();

        /// <summary>Maximum number of characters kept in the scrollback buffer.</summary>
        public int Capacity { get; set; } = 64 * 1024;

        public string Output => _output.ToString();

        public void Write(char value)
        {
            _output.Append(value);
            if (_output.Length > Capacity)
                _output.Remove(0, _output.Length - Capacity);
        }

        public void Write(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            _output.Append(text);
            if (_output.Length > Capacity)
                _output.Remove(0, _output.Length - Capacity);
        }

        public void Clear()
        {
            _output.Clear();
            _input.Clear();
        }

        /// <summary>Queues a key that the program will read from INT 16h.</summary>
        public void InjectKey(byte key)
        {
            _input.Enqueue(key);
        }

        /// <summary>Queues a key typed in the studio.</summary>
        public void InjectKey(char key)
        {
            InjectKey((byte)key);
        }

        /// <summary>
        /// Takes a queued key. When the queue is empty the synthesised Enter is
        /// returned so that programs waiting for "press any key" keep running
        /// instead of blocking the emulator.
        /// </summary>
        public byte ReadKey()
        {
            if (_input.Count > 0)
                return _input.Dequeue();
            return (byte)'\r';
        }

        public bool HasPendingInput => _input.Count > 0;
    }
}

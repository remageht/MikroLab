using System;

namespace MikroLab.Core.Devices
{
    /// <summary>
    /// Read side of a matrix keyboard connected to one PPI port. Rows are
    /// selected by the low nibble (active low) and columns are returned in the
    /// high nibble (also active low), matching the usual 4×4 wiring:
    /// PC0..PC3 = rows, PC4..PC7 = columns.
    /// </summary>
    public interface IKeyboardMatrix
    {
        /// <summary>Returns column pins for the selected rows, high nibble, active low.</summary>
        byte ReadColumns(byte rowSelect);
    }

    /// <summary>
    /// 4×4 matrix keyboard (a 3×4 matrix is supported by passing rows: 3).
    /// Keys are stored by row/column; reading a row returns 0 for a pressed
    /// column and 1 for a released one, so the result can be fed directly to
    /// the upper half of an active-low PPI port C input latch.
    /// </summary>
    public sealed class MatrixKeyboard : IKeyboardMatrix
    {
        private readonly bool[,] _pressed;

        public MatrixKeyboard(int rows = 4, int columns = 4)
        {
            if (rows < 1 || rows > 4)
                throw new ArgumentOutOfRangeException(nameof(rows), "Keyboard rows must be between 1 and 4.");
            if (columns < 1 || columns > 4)
                throw new ArgumentOutOfRangeException(nameof(columns), "Keyboard columns must be between 1 and 4.");

            Rows = rows;
            Columns = columns;
            _pressed = new bool[rows, columns];
        }

        public int Rows { get; }
        public int Columns { get; }
        public int KeyCount => Rows * Columns;

        public bool IsKeyDown(int row, int column)
        {
            return row >= 0 && row < Rows && column >= 0 && column < Columns && _pressed[row, column];
        }

        public void SetKey(int row, int column, bool pressed)
        {
            if (row < 0 || row >= Rows || column < 0 || column >= Columns) return;
            _pressed[row, column] = pressed;
        }

        public void Clear()
        {
            Array.Clear(_pressed, 0, _pressed.Length);
        }

        /// <summary>Key code 1…N (row-major) or 0 when nothing is pressed.</summary>
        public int FindPressedKey()
        {
            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    if (_pressed[row, column]) return row * Columns + column + 1;
                }
            }
            return 0;
        }

        public byte ReadColumns(byte rowSelect)
        {
            byte columns = 0xF0;
            for (int row = 0; row < Rows; row++)
            {
                // Active-low row select: a zero bit enables that row.
                if ((rowSelect & (1 << row)) != 0) continue;
                for (int column = 0; column < Columns; column++)
                {
                    if (_pressed[row, column])
                    {
                        columns &= (byte)~(1 << (4 + column));
                    }
                }
            }
            return columns;
        }
    }
}

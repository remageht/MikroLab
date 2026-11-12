using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MikroLab.Core.Assembler.X64
{
    /// <summary>
    /// Evaluator of constant expressions for the x86-64 assembler. Unlike the
    /// 16-bit evaluator of the KR580 assembler it works with 64-bit values and
    /// supports the usual set of operators.
    /// </summary>
    internal static class X64Expression
    {
        public static bool TryEvaluate(
            string text,
            IDictionary<string, long> symbols,
            out long value,
            out string error)
        {
            value = 0;
            error = null;
            try
            {
                Parser parser = new Parser(text ?? string.Empty, symbols);
                value = parser.ParseExpression();
                parser.SkipWhitespace();
                if (!parser.AtEnd)
                {
                    error = "Неожиданный символ '" + parser.Current + "' в выражении";
                    return false;
                }

                return true;
            }
            catch (FormatException failure)
            {
                error = failure.Message;
                return false;
            }
        }

        private sealed class Parser
        {
            private readonly string _text;
            private readonly IDictionary<string, long> _symbols;
            private int _position;

            public Parser(string text, IDictionary<string, long> symbols)
            {
                _text = text;
                _symbols = symbols;
            }

            public bool AtEnd => _position >= _text.Length;

            public char Current => _position < _text.Length ? _text[_position] : '\0';

            public void SkipWhitespace()
            {
                while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
                    _position++;
            }

            private bool Match(char value)
            {
                SkipWhitespace();
                if (_position < _text.Length && _text[_position] == value)
                {
                    _position++;
                    return true;
                }

                return false;
            }

            private bool Peek(string token)
            {
                SkipWhitespace();
                if (_position + token.Length > _text.Length)
                    return false;
                return string.Compare(_text, _position, token, 0, token.Length,
                    StringComparison.Ordinal) == 0;
            }

            public long ParseExpression() => ParseOr();

            private long ParseOr()
            {
                long left = ParseXor();
                while (true)
                {
                    SkipWhitespace();
                    if (Peek("||"))
                    {
                        _position += 2;
                        long right = ParseXor();
                        left = (left != 0 || right != 0) ? 1 : 0;
                        continue;
                    }

                    if (Match('|'))
                    {
                        left |= ParseXor();
                        continue;
                    }

                    return left;
                }
            }

            private long ParseXor()
            {
                long left = ParseAnd();
                while (Match('^'))
                    left ^= ParseAnd();
                return left;
            }

            private long ParseAnd()
            {
                long left = ParseShift();
                while (true)
                {
                    SkipWhitespace();
                    if (Peek("&&"))
                    {
                        _position += 2;
                        long right = ParseShift();
                        left = (left != 0 && right != 0) ? 1 : 0;
                        continue;
                    }

                    if (Match('&'))
                    {
                        left &= ParseShift();
                        continue;
                    }

                    return left;
                }
            }

            private long ParseShift()
            {
                long left = ParseAdd();
                while (true)
                {
                    SkipWhitespace();
                    if (Peek("<<"))
                    {
                        _position += 2;
                        left <<= (int)ParseAdd();
                        continue;
                    }

                    if (Peek(">>"))
                    {
                        _position += 2;
                        left >>= (int)ParseAdd();
                        continue;
                    }

                    return left;
                }
            }

            private long ParseAdd()
            {
                long left = ParseMul();
                while (true)
                {
                    SkipWhitespace();
                    if (_position < _text.Length && _text[_position] == '+')
                    {
                        _position++;
                        left += ParseMul();
                        continue;
                    }

                    if (_position < _text.Length && _text[_position] == '-')
                    {
                        _position++;
                        left -= ParseMul();
                        continue;
                    }

                    return left;
                }
            }

            private long ParseMul()
            {
                long left = ParseUnary();
                while (true)
                {
                    SkipWhitespace();
                    if (_position < _text.Length && _text[_position] == '*')
                    {
                        _position++;
                        left *= ParseUnary();
                        continue;
                    }

                    if (_position < _text.Length && _text[_position] == '/')
                    {
                        _position++;
                        long divisor = ParseUnary();
                        if (divisor == 0)
                            throw new FormatException("Деление на ноль в выражении");
                        left /= divisor;
                        continue;
                    }

                    if (_position < _text.Length && _text[_position] == '%')
                    {
                        _position++;
                        long divisor = ParseUnary();
                        if (divisor == 0)
                            throw new FormatException("Деление на ноль в выражении");
                        left %= divisor;
                        continue;
                    }

                    return left;
                }
            }

            private long ParseUnary()
            {
                SkipWhitespace();
                if (Match('-'))
                    return -ParseUnary();
                if (Match('+'))
                    return ParseUnary();
                if (Match('~'))
                    return ~ParseUnary();
                return ParsePrimary();
            }

            private long ParsePrimary()
            {
                SkipWhitespace();
                if (_position >= _text.Length)
                    throw new FormatException("Неожиданный конец выражения");

                char c = _text[_position];
                if (c == '(')
                {
                    _position++;
                    long value = ParseExpression();
                    if (!Match(')'))
                        throw new FormatException("Не хватает закрывающей скобки");
                    return value;
                }

                if (c == '$')
                {
                    _position++;
                    return ParseHexDigits();
                }

                if (c == '\'')
                    return ParseCharacter();

                if (char.IsDigit(c))
                    return ParseNumber();

                if (IsIdentifierStart(c))
                {
                    string name = ParseIdentifier();
                    long symbolValue;
                    if (_symbols != null && _symbols.TryGetValue(name, out symbolValue))
                        return symbolValue;
                    throw new FormatException("Неизвестный символ '" + name + "'");
                }

                throw new FormatException("Неожиданный символ '" + c + "' в выражении");
            }

            private long ParseNumber()
            {
                int start = _position;
                while (_position < _text.Length)
                {
                    char c = _text[_position];
                    if (char.IsLetterOrDigit(c) || c == '_')
                        _position++;
                    else
                        break;
                }

                string token = _text.Substring(start, _position - start);
                return ParseNumericToken(token);
            }

            private long ParseHexDigits()
            {
                long value = 0;
                int digits = 0;
                while (_position < _text.Length && IsHexDigit(_text[_position]))
                {
                    value = (value << 4) | (long)(uint)HexValue(_text[_position]);
                    _position++;
                    digits++;
                }

                if (digits == 0)
                    throw new FormatException("Ожидалась шестнадцатеричная цифра");
                return value;
            }

            private long ParseCharacter()
            {
                _position++;
                if (_position >= _text.Length)
                    throw new FormatException("Незакрытый символьный литерал");

                char value = _text[_position];
                _position++;
                if (_position < _text.Length && _text[_position] == '\'')
                    _position++;
                return value;
            }

            private string ParseIdentifier()
            {
                int start = _position;
                _position++;
                while (_position < _text.Length && IsIdentifierPart(_text[_position]))
                    _position++;
                return _text.Substring(start, _position - start);
            }
        }

        private static long ParseNumericToken(string token)
        {
            if (token.Length == 0)
                throw new FormatException("Ожидалось число");

            string lower = token.ToLowerInvariant();
            if (lower.StartsWith("0x", StringComparison.Ordinal))
                return long.Parse(lower.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (lower.StartsWith("0b", StringComparison.Ordinal))
                return long.Parse(lower.Substring(2), NumberStyles.BinaryNumber, CultureInfo.InvariantCulture);
            if (lower.StartsWith("0o", StringComparison.Ordinal))
                return long.Parse(lower.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture);

            char last = lower[lower.Length - 1];
            if (last == 'h')
                return long.Parse(lower.Substring(0, lower.Length - 1), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture);
            if (last == 'o')
                return long.Parse(lower.Substring(0, lower.Length - 1), NumberStyles.Integer,
                    CultureInfo.InvariantCulture);
            if (last == 'b')
                return long.Parse(lower.Substring(0, lower.Length - 1), NumberStyles.BinaryNumber,
                    CultureInfo.InvariantCulture);
            if (last == 'd')
                return long.Parse(lower.Substring(0, lower.Length - 1), NumberStyles.Integer,
                    CultureInfo.InvariantCulture);

            return long.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_' || c == '.' || c == '@';

        private static bool IsIdentifierPart(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '@' || c == '$';

        private static bool IsHexDigit(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'a' && c <= 'f')
                return c - 'a' + 10;
            return c - 'A' + 10;
        }
    }
}

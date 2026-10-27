using System;
using System.Collections.Generic;
using System.Text;

namespace MikroLab.Core.Assembler
{
    internal static class AsmText
    {
        public static bool IsIdentifierStart(char c)
        {
            return (c >= 'A' && c <= 'Z') ||
                   (c >= 'a' && c <= 'z') ||
                   c == '_' || c == '@' || c == '?' || c == '.';
        }

        public static bool IsIdentifierPart(char c)
        {
            return IsIdentifierStart(c) || (c >= '0' && c <= '9');
        }

        public static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') ||
                   (c >= 'a' && c <= 'f') ||
                   (c >= 'A' && c <= 'F');
        }

        public static int HexValue(char c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'a' && c <= 'f')
                return c - 'a' + 10;
            return c - 'A' + 10;
        }

        // Decodes a quoted string starting at start. Returns the decoded characters
        // and the index just past the closing quote.
        public static bool TryDecodeQuoted(string text, int start, out string value, out int endIndex, out string error)
        {
            value = string.Empty;
            endIndex = start;
            error = null;

            if (start < 0 || start >= text.Length || (text[start] != '\'' && text[start] != '"'))
            {
                error = "Expected a quoted string";
                return false;
            }

            char quote = text[start];
            StringBuilder builder = new StringBuilder();
            int i = start + 1;

            while (i < text.Length)
            {
                char c = text[i];

                if (c == quote)
                {
                    value = builder.ToString();
                    endIndex = i + 1;
                    return true;
                }

                if (c == '\\')
                {
                    i++;
                    if (i >= text.Length)
                    {
                        error = "Unterminated string literal";
                        return false;
                    }

                    char escape = text[i];
                    switch (escape)
                    {
                        case 'n': builder.Append('\n'); i++; break;
                        case 'r': builder.Append('\r'); i++; break;
                        case 't': builder.Append('\t'); i++; break;
                        case '\\': builder.Append('\\'); i++; break;
                        case '\'': builder.Append('\''); i++; break;
                        case '"': builder.Append('"'); i++; break;
                        case '0': builder.Append('\0'); i++; break;
                        case 'x':
                        case 'X':
                        {
                            i++;
                            int digits = 0;
                            int hexValue = 0;
                            while (i < text.Length && digits < 2 && IsHexDigit(text[i]))
                            {
                                hexValue = (hexValue << 4) | HexValue(text[i]);
                                digits++;
                                i++;
                            }

                            if (digits == 0)
                            {
                                error = "\\x escape requires at least one hexadecimal digit";
                                return false;
                            }

                            builder.Append((char)hexValue);
                            break;
                        }
                        default:
                            error = "Unknown escape sequence '\\" + escape + "'";
                            return false;
                    }

                    continue;
                }

                builder.Append(c);
                i++;
            }

            error = "Unterminated string literal";
            return false;
        }

        public static bool TryDecodeStrictString(string text, out byte[] bytes, out string error)
        {
            bytes = null;
            error = null;

            string trimmed = text.Trim();
            if (trimmed.Length == 0 || (trimmed[0] != '\'' && trimmed[0] != '"'))
            {
                error = "Expected a quoted string";
                return false;
            }

            string value;
            int end;
            if (!TryDecodeQuoted(trimmed, 0, out value, out end, out error))
                return false;

            if (end != trimmed.Length)
            {
                error = "Unexpected characters after string literal";
                return false;
            }

            bytes = new byte[value.Length];
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] > 0xFF)
                {
                    error = "String literal contains a non-ASCII character";
                    return false;
                }

                bytes[i] = (byte)value[i];
            }

            return true;
        }
    }

    internal static class ExpressionEvaluator
    {
        public static bool TryEvaluate(
            string text,
            IReadOnlyDictionary<string, ushort> symbols,
            int location,
            out long value,
            out string error)
        {
            try
            {
                Parser parser = new Parser(text ?? string.Empty, symbols, location);
                value = parser.ParseExpression();
                parser.SkipWhitespace();
                if (!parser.AtEnd)
                {
                    error = "Unexpected character '" + parser.Current + "' in expression";
                    value = 0;
                    return false;
                }

                error = null;
                return true;
            }
            catch (FormatException ex)
            {
                value = 0;
                error = ex.Message;
                return false;
            }
        }

        private sealed class Parser
        {
            private readonly string _text;
            private readonly IReadOnlyDictionary<string, ushort> _symbols;
            private readonly int _location;
            private int _pos;

            public Parser(string text, IReadOnlyDictionary<string, ushort> symbols, int location)
            {
                _text = text;
                _symbols = symbols;
                _location = location;
            }

            public bool AtEnd
            {
                get { return _pos >= _text.Length; }
            }

            public char Current
            {
                get { return _pos < _text.Length ? _text[_pos] : '\0'; }
            }

            public void SkipWhitespace()
            {
                while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
                    _pos++;
            }

            private bool Match(char c)
            {
                SkipWhitespace();
                if (_pos < _text.Length && _text[_pos] == c)
                {
                    _pos++;
                    return true;
                }

                return false;
            }

            private bool MatchSequence(string sequence)
            {
                SkipWhitespace();
                if (_pos + sequence.Length > _text.Length)
                    return false;

                if (string.CompareOrdinal(_text, _pos, sequence, 0, sequence.Length) != 0)
                    return false;

                _pos += sequence.Length;
                return true;
            }

            public long ParseExpression()
            {
                return ParseOr();
            }

            private long ParseOr()
            {
                long value = ParseXor();
                while (true)
                {
                    SkipWhitespace();
                    if (_pos < _text.Length && _text[_pos] == '|' &&
                        !(_pos + 1 < _text.Length && _text[_pos + 1] == '|'))
                    {
                        _pos++;
                        value |= ParseXor();
                    }
                    else
                    {
                        break;
                    }
                }

                return value;
            }

            private long ParseXor()
            {
                long value = ParseAnd();
                while (Match('^'))
                    value ^= ParseAnd();
                return value;
            }

            private long ParseAnd()
            {
                long value = ParseShift();
                while (true)
                {
                    SkipWhitespace();
                    if (_pos < _text.Length && _text[_pos] == '&' &&
                        !(_pos + 1 < _text.Length && _text[_pos + 1] == '&'))
                    {
                        _pos++;
                        value &= ParseShift();
                    }
                    else
                    {
                        break;
                    }
                }

                return value;
            }

            private long ParseShift()
            {
                long value = ParseAdditive();
                while (true)
                {
                    if (MatchSequence("<<"))
                    {
                        value = ShiftLeft(value, ParseAdditive());
                    }
                    else if (MatchSequence(">>"))
                    {
                        value = ShiftRight(value, ParseAdditive());
                    }
                    else
                    {
                        break;
                    }
                }

                return value;
            }

            private long ParseAdditive()
            {
                long value = ParseMultiplicative();
                while (true)
                {
                    SkipWhitespace();
                    if (Match('+'))
                    {
                        value = CheckedAdd(value, ParseMultiplicative());
                    }
                    else if (Match('-'))
                    {
                        value = CheckedSubtract(value, ParseMultiplicative());
                    }
                    else
                    {
                        break;
                    }
                }

                return value;
            }

            private long ParseMultiplicative()
            {
                long value = ParseUnary();
                while (true)
                {
                    SkipWhitespace();
                    if (Match('*'))
                    {
                        value = CheckedMultiply(value, ParseUnary());
                    }
                    else if (Match('/'))
                    {
                        long divisor = ParseUnary();
                        if (divisor == 0)
                            throw new FormatException("Division by zero");
                        value /= divisor;
                    }
                    else if (Match('%'))
                    {
                        long divisor = ParseUnary();
                        if (divisor == 0)
                            throw new FormatException("Division by zero");
                        value %= divisor;
                    }
                    else
                    {
                        break;
                    }
                }

                return value;
            }

            private long ParseUnary()
            {
                SkipWhitespace();
                if (Match('+'))
                    return ParseUnary();
                if (Match('-'))
                    return CheckedNegate(ParseUnary());
                if (Match('~'))
                    return ~ParseUnary();
                return ParsePrimary();
            }

            private long ParsePrimary()
            {
                SkipWhitespace();
                if (_pos >= _text.Length)
                    throw new FormatException("Unexpected end of expression");

                char c = _text[_pos];

                if (c == '(')
                {
                    _pos++;
                    long value = ParseExpression();
                    if (!Match(')'))
                        throw new FormatException("Missing closing parenthesis");
                    return value;
                }

                if (c == '\'')
                    return ParseCharacterLiteral();

                if (c == '"')
                    throw new FormatException("String literals are only valid as DB operands");

                if (c == '$' || (c >= '0' && c <= '9'))
                    return ParseNumber();

                if (AsmText.IsIdentifierStart(c))
                {
                    string name = ParseIdentifier();
                    ushort symbolValue;
                    if (_symbols != null && _symbols.TryGetValue(name, out symbolValue))
                        return symbolValue;

                    throw new FormatException("Undefined symbol '" + name + "'");
                }

                throw new FormatException("Unexpected character '" + c + "' in expression");
            }

            private string ParseIdentifier()
            {
                int start = _pos;
                _pos++;
                while (_pos < _text.Length && AsmText.IsIdentifierPart(_text[_pos]))
                    _pos++;
                return _text.Substring(start, _pos - start);
            }

            private long ParseCharacterLiteral()
            {
                string value;
                int end;
                string error;
                if (!AsmText.TryDecodeQuoted(_text, _pos, out value, out end, out error))
                    throw new FormatException(error);

                if (value.Length != 1)
                    throw new FormatException("Character literal must contain exactly one character");

                if (value[0] > 0xFF)
                    throw new FormatException("Character literal is outside the byte range");

                _pos = end;
                return value[0];
            }

            private long ParseNumber()
            {
                char c = _text[_pos];

                if (c == '$')
                {
                    _pos++;
                    if (_pos < _text.Length && AsmText.IsHexDigit(_text[_pos]))
                        return ParseHexDigits();
                    return _location;
                }

                if (c == '0' && _pos + 1 < _text.Length && (_text[_pos + 1] == 'x' || _text[_pos + 1] == 'X'))
                {
                    _pos += 2;
                    if (_pos >= _text.Length || !AsmText.IsHexDigit(_text[_pos]))
                        throw new FormatException("0x literal requires at least one hexadecimal digit");
                    return ParseHexDigits();
                }

                int start = _pos;
                while (_pos < _text.Length)
                {
                    char t = _text[_pos];
                    if ((t >= '0' && t <= '9') || (t >= 'A' && t <= 'Z') || (t >= 'a' && t <= 'z'))
                        _pos++;
                    else
                        break;
                }

                string token = _text.Substring(start, _pos - start);
                return ParseNumericToken(token);
            }

            private long ParseHexDigits()
            {
                long value = 0;
                int digits = 0;
                while (_pos < _text.Length && AsmText.IsHexDigit(_text[_pos]))
                {
                    if (value > 0x0FFFFFFFL)
                        throw new FormatException("Numeric literal is too large");
                    value = (value << 4) | (long)AsmText.HexValue(_text[_pos]);
                    digits++;
                    _pos++;
                }

                if (digits == 0)
                    throw new FormatException("Expected a hexadecimal digit");
                return value;
            }

            private static long ParseNumericToken(string token)
            {
                if (token.Length == 0)
                    throw new FormatException("Expected a number");

                bool allDigits = true;
                for (int i = 0; i < token.Length; i++)
                {
                    if (token[i] < '0' || token[i] > '9')
                    {
                        allDigits = false;
                        break;
                    }
                }

                if (allDigits)
                    return ParseRadix(token, 10);

                char suffix = char.ToLowerInvariant(token[token.Length - 1]);
                string body = token.Substring(0, token.Length - 1);
                if (body.Length == 0)
                    throw new FormatException("Invalid numeric literal '" + token + "'");

                if (suffix == 'h')
                    return ParseRadix(body, 16);
                if (suffix == 'o')
                    return ParseRadix(body, 8);
                if (suffix == 'd')
                    return ParseRadix(body, 10);
                if (suffix == 'b')
                    return ParseRadix(body, 2);

                throw new FormatException("Invalid numeric literal '" + token + "'");
            }

            private static long ParseRadix(string body, int radix)
            {
                long value = 0;
                for (int i = 0; i < body.Length; i++)
                {
                    char c = body[i];
                    int digit;
                    if (c >= '0' && c <= '9')
                        digit = c - '0';
                    else if (c >= 'a' && c <= 'f')
                        digit = c - 'a' + 10;
                    else if (c >= 'A' && c <= 'F')
                        digit = c - 'A' + 10;
                    else
                        throw new FormatException("Invalid numeric literal '" + body + "'");

                    if (digit >= radix)
                        throw new FormatException("Invalid numeric literal '" + body + "'");

                    if (value > 0x0FFFFFFFL)
                        throw new FormatException("Numeric literal is too large");
                    value = value * radix + digit;
                }

                return value;
            }

            private static long CheckedAdd(long a, long b)
            {
                try
                {
                    return checked(a + b);
                }
                catch (OverflowException)
                {
                    throw new FormatException("Expression overflow");
                }
            }

            private static long CheckedSubtract(long a, long b)
            {
                try
                {
                    return checked(a - b);
                }
                catch (OverflowException)
                {
                    throw new FormatException("Expression overflow");
                }
            }

            private static long CheckedMultiply(long a, long b)
            {
                try
                {
                    return checked(a * b);
                }
                catch (OverflowException)
                {
                    throw new FormatException("Expression overflow");
                }
            }

            private static long CheckedNegate(long value)
            {
                if (value == long.MinValue)
                    throw new FormatException("Expression overflow");
                return -value;
            }

            private static long ShiftLeft(long value, long count)
            {
                if (count < 0 || count > 31)
                    throw new FormatException("Shift count must be between 0 and 31");
                return value << (int)count;
            }

            private static long ShiftRight(long value, long count)
            {
                if (count < 0 || count > 31)
                    throw new FormatException("Shift count must be between 0 and 31");
                return value >> (int)count;
            }
        }
    }
}

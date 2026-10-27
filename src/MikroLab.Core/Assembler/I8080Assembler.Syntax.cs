using System;
using System.Collections.Generic;
using System.Text;

namespace MikroLab.Core.Assembler
{
    public static partial class I8080Assembler
    {
        private enum LineKind
        {
            Empty,
            Instruction,
            Org,
            Equ,
            Db,
            Dw,
            Ds,
            End,
            Ignored
        }

        private enum OperandForm
        {
            None,
            Implied,
            Mov,
            Imm8,
            Imm16,
            Rst
        }

        private sealed class ParsedLine
        {
            public int LineNumber;
            public string Source;
            public string Label;
            public string Mnemonic;
            public LineKind Kind;
            public OperandForm Form;
            public byte Opcode;
            public int Reg1;
            public int Reg2;
            public int Size;
            public int Address;
            public string Expression;
            public List<string> Operands = new List<string>();
            public bool Invalid;
        }

        private static string[] SplitSourceLines(string source)
        {
            if (source == null)
                source = string.Empty;

            source = source.Replace("\r\n", "\n").Replace('\r', '\n');
            if (source.Length == 0)
                return new string[0];

            List<string> lines = new List<string>(source.Split('\n'));
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            return lines.ToArray();
        }

        private static string StripComment(string line)
        {
            bool inSingle = false;
            bool inDouble = false;
            bool escaped = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if ((inSingle || inDouble) && c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    continue;
                }

                if (c == '"' && !inSingle)
                {
                    inDouble = !inDouble;
                    continue;
                }

                if (c == ';' && !inSingle && !inDouble)
                    return line.Substring(0, i);
            }

            return line;
        }

        private static int ReadTokenEnd(string text, int start)
        {
            int i = start;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != ':')
                i++;
            return i;
        }

        private static bool IsValidIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (!AsmText.IsIdentifierStart(name[0]))
                return false;

            for (int i = 1; i < name.Length; i++)
            {
                if (!AsmText.IsIdentifierPart(name[i]))
                    return false;
            }

            return true;
        }

        private static List<ParsedLine> ParseLines(string source, List<AssemblerError> errors)
        {
            string[] sourceLines = SplitSourceLines(source);
            List<ParsedLine> parsed = new List<ParsedLine>(sourceLines.Length);
            bool ended = false;

            for (int index = 0; index < sourceLines.Length; index++)
            {
                ParsedLine line = new ParsedLine();
                line.LineNumber = index + 1;
                line.Source = sourceLines[index];
                line.Operands = new List<string>();

                if (ended)
                {
                    line.Kind = LineKind.Ignored;
                    parsed.Add(line);
                    continue;
                }

                string text = StripComment(sourceLines[index]).Trim();
                if (text.Length == 0)
                {
                    line.Kind = LineKind.Empty;
                    parsed.Add(line);
                    continue;
                }

                string label = null;
                string rest = text;
                int firstEnd = ReadTokenEnd(text, 0);
                string firstToken = text.Substring(0, firstEnd);
                int afterFirst = firstEnd;
                while (afterFirst < text.Length && char.IsWhiteSpace(text[afterFirst]))
                    afterFirst++;

                if (afterFirst < text.Length && text[afterFirst] == ':')
                {
                    label = firstToken;
                    rest = text.Substring(afterFirst + 1).Trim();
                }
                else if (afterFirst < text.Length)
                {
                    int secondEnd = ReadTokenEnd(text, afterFirst);
                    string secondToken = text.Substring(afterFirst, secondEnd - afterFirst);
                    if (secondToken.Equals("EQU", StringComparison.OrdinalIgnoreCase) ||
                        secondToken.Equals(".EQU", StringComparison.OrdinalIgnoreCase))
                    {
                        label = firstToken;
                        rest = text.Substring(secondEnd).Trim();
                        line.Mnemonic = "EQU";
                        line.Kind = LineKind.Equ;

                        if (!IsValidIdentifier(label))
                        {
                            AddError(errors, line.LineNumber, "Invalid label name '" + label + "'");
                            line.Label = null;
                        }
                        else
                        {
                            line.Label = label;
                        }

                        string splitError;
                        line.Operands = SplitOperands(rest, out splitError);
                        if (splitError != null)
                        {
                            AddError(errors, line.LineNumber, splitError);
                            line.Invalid = true;
                        }

                        if (line.Operands.Count != 1)
                        {
                            AddError(errors, line.LineNumber, "EQU requires exactly one expression");
                            line.Invalid = true;
                        }
                        else
                        {
                            line.Expression = line.Operands[0];
                        }

                        parsed.Add(line);
                        continue;
                    }
                }

                if (label != null)
                {
                    if (!IsValidIdentifier(label))
                    {
                        AddError(errors, line.LineNumber, "Invalid label name '" + label + "'");
                    }
                    else
                    {
                        line.Label = label;
                    }
                }

                rest = rest.Trim();
                if (rest.Length == 0)
                {
                    line.Kind = LineKind.Empty;
                    parsed.Add(line);
                    continue;
                }

                int mnemonicEnd = ReadTokenEnd(rest, 0);
                string mnemonicToken = rest.Substring(0, mnemonicEnd);
                string operandText = rest.Substring(mnemonicEnd).Trim();
                line.Operands = SplitOperands(operandText, out string operandError);
                if (operandError != null)
                {
                    AddError(errors, line.LineNumber, operandError);
                    line.Invalid = true;
                }

                string directive;
                if (TryNormalizeDirective(mnemonicToken, out directive))
                {
                    line.Mnemonic = directive;
                    switch (directive)
                    {
                        case "ORG": line.Kind = LineKind.Org; break;
                        case "EQU": line.Kind = LineKind.Equ; break;
                        case "DB": line.Kind = LineKind.Db; break;
                        case "DW": line.Kind = LineKind.Dw; break;
                        case "DS": line.Kind = LineKind.Ds; break;
                        case "END": line.Kind = LineKind.End; ended = true; break;
                        default: line.Kind = LineKind.Instruction; break;
                    }
                }
                else
                {
                    line.Kind = LineKind.Instruction;
                    line.Mnemonic = mnemonicToken.ToUpperInvariant();
                }

                parsed.Add(line);
            }

            return parsed;
        }

        private static bool TryNormalizeDirective(string token, out string directive)
        {
            directive = token ?? string.Empty;
            if (directive.StartsWith(".", StringComparison.Ordinal))
                directive = directive.Substring(1);
            directive = directive.ToUpperInvariant();

            switch (directive)
            {
                case "ORG":
                case "EQU":
                case "DB":
                case "DW":
                case "DS":
                case "END":
                    return true;
                default:
                    directive = null;
                    return false;
            }
        }

        private static List<string> SplitOperands(string text, out string error)
        {
            error = null;
            List<string> result = new List<string>();
            if (text == null)
                text = string.Empty;
            text = text.Trim();
            if (text.Length == 0)
                return result;

            bool inSingle = false;
            bool inDouble = false;
            bool escaped = false;
            int depth = 0;
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if ((inSingle || inDouble) && c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    continue;
                }

                if (c == '"' && !inSingle)
                {
                    inDouble = !inDouble;
                    continue;
                }

                if (inSingle || inDouble)
                    continue;

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                    if (depth < 0)
                    {
                        if (error == null)
                            error = "Unbalanced parentheses in operand list";
                        depth = 0;
                    }
                }
                else if (c == ',' && depth == 0)
                {
                    string part = text.Substring(start, i - start).Trim();
                    if (part.Length == 0)
                    {
                        if (error == null)
                            error = "Empty operand";
                    }
                    else
                    {
                        result.Add(part);
                    }

                    start = i + 1;
                }
            }

            if (inSingle || inDouble)
            {
                if (error == null)
                    error = "Unterminated string literal";
            }

            if (depth != 0 && error == null)
                error = "Unbalanced parentheses in operand list";

            string last = text.Substring(start).Trim();
            if (last.Length == 0)
            {
                if (result.Count > 0 && error == null)
                    error = "Empty operand";
            }
            else
            {
                result.Add(last);
            }

            return result;
        }

        private static void AddError(List<AssemblerError> errors, int lineNumber, string message)
        {
            errors.Add(new AssemblerError(lineNumber, message));
        }
    }
}

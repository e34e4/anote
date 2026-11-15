// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Evaluates the infix expressions entered in the calculator tab. The supported grammar is a
    /// superset of a standard Android calculator and covers the scientific functions
    /// sin/cos/tan/cot (and their inverses), ln, log, exp, sqrt, power, factorial, percent, the
    /// constants π and e, named variables, hexadecimal/binary/octal literals, the programmer
    /// operators &amp; | xor &lt;&lt; &gt;&gt; ~ mod, brackets and a unary minus.
    /// </summary>
    public static class CalculatorEngine
    {
        /// <summary>
        /// Evaluates an expression and formats the result.
        /// </summary>
        /// <param name="expression">The expression to evaluate, using × ÷ π √ as symbols.</param>
        /// <param name="degrees">True to interpret trig functions in degrees, false for radians.</param>
        /// <param name="variables">Optional named variables which can be referenced.</param>
        /// <returns>The formatted result, or null if the expression could not be evaluated.</returns>
        public static string Evaluate(string expression, bool degrees, IReadOnlyDictionary<string, double> variables = null)
        {
            try
            {
                var parser = new Parser(expression ?? string.Empty, degrees, variables);
                double value = parser.Parse();
                return Format(value);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Evaluates an assignment like <c>tax = 0.21</c>. The variable is stored in the passed
        /// dictionary when the expression could be evaluated.
        /// </summary>
        /// <param name="text">The entered assignment or expression.</param>
        /// <param name="degrees">True to interpret trig functions in degrees, false for radians.</param>
        /// <param name="variables">Dictionary which receives the new variable value.</param>
        /// <param name="name">Receives the lower case variable name.</param>
        /// <param name="result">Receives the formatted value.</param>
        /// <returns>True if an assignment was detected and evaluated.</returns>
        public static bool TryEvaluateAssignment(
            string text,
            bool degrees,
            IDictionary<string, double> variables,
            out string name,
            out string result)
        {
            name = null;
            result = null;
            if (variables == null || string.IsNullOrWhiteSpace(text))
                return false;

            int equalsIndex = FindAssignmentOperator(text);
            if (equalsIndex <= 0)
                return false;

            string left = text.Substring(0, equalsIndex).Trim();
            string right = text.Substring(equalsIndex + 1).Trim();
            if (!IsValidIdentifier(left) || right.Length == 0)
                return false;

            double value;
            try
            {
                var scope = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, double> variable in variables)
                    scope[variable.Key] = variable.Value;
                var parser = new Parser(right, degrees, scope);
                value = parser.Parse();
            }
            catch (Exception)
            {
                return false;
            }

            name = left.ToLowerInvariant();
            variables[name] = value;
            result = Format(value);
            return true;
        }

        private static int FindAssignmentOperator(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '=')
                    continue;
                bool previousIsOperator = i > 0 && "!<>=+-*/×÷^&|".IndexOf(text[i - 1]) >= 0;
                bool nextIsEquals = i + 1 < text.Length && text[i + 1] == '=';
                if (!previousIsOperator && !nextIsEquals)
                    return i;
            }
            return -1;
        }

        private static bool IsValidIdentifier(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            if (!(char.IsLetter(text[0]) || text[0] == '_'))
                return false;
            foreach (char c in text)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Formats a value in the requested number base (2, 8, 10 or 16).
        /// Negative values are rendered as 64 bit two's complement, like a programmer calculator.
        /// </summary>
        public static string FormatInteger(double value, int numberBase)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return string.Empty;

            long signedValue = (long)Math.Truncate(value);
            if (numberBase == 10)
                return signedValue.ToString(CultureInfo.InvariantCulture);

            ulong bits = unchecked((ulong)signedValue);
            switch (numberBase)
            {
                case 2:
                    return Convert.ToString(unchecked((long)bits), 2);
                case 8:
                    return Convert.ToString(unchecked((long)bits), 8);
                case 16:
                    return bits.ToString("X", CultureInfo.InvariantCulture);
                default:
                    return signedValue.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Renders the value in decimal, hexadecimal, octal and binary for the programmer view.
        /// </summary>
        public static string FormatAllBases(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return string.Empty;

            return "HEX  " + FormatInteger(value, 16)
                + "\nDEC  " + FormatInteger(value, 10)
                + "\nOCT  " + FormatInteger(value, 8)
                + "\nBIN  " + FormatInteger(value, 2);
        }

        private static string Format(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return null;

            double rounded = Math.Round(value, 10, MidpointRounding.AwayFromZero);
            if (rounded == 0.0)
                return "0";

            double absolute = Math.Abs(rounded);
            if ((absolute >= 1e15) || (absolute < 1e-9))
                return rounded.ToString("0.##########E+0", CultureInfo.InvariantCulture);

            if (rounded == Math.Truncate(rounded))
                return ((long)rounded).ToString(CultureInfo.InvariantCulture);

            return rounded.ToString("0.##########", CultureInfo.InvariantCulture);
        }

        private static long ToInteger(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || (value > long.MaxValue) || (value < long.MinValue))
                throw new FormatException("Value is not an integer");
            return (long)Math.Truncate(value);
        }

        /// <summary>
        /// A small recursive descent parser for the calculator expressions.
        /// </summary>
        private class Parser
        {
            private readonly string _text;
            private readonly bool _degrees;
            private readonly IReadOnlyDictionary<string, double> _variables;
            private int _pos;

            public Parser(string text, bool degrees, IReadOnlyDictionary<string, double> variables = null)
            {
                _text = text;
                _degrees = degrees;
                _variables = variables;
            }

            public double Parse()
            {
                double value = ParseExpression();
                SkipSpaces();
                if (_pos < _text.Length)
                    throw new FormatException("Unexpected character");
                return value;
            }

            private double ParseExpression()
            {
                return ParseBitwiseOr();
            }

            private double ParseBitwiseOr()
            {
                double value = ParseBitwiseXor();
                while (true)
                {
                    SkipSpaces();
                    if (Match('|'))
                        value = ToInteger(value) | ToInteger(ParseBitwiseXor());
                    else
                        return value;
                }
            }

            private double ParseBitwiseXor()
            {
                double value = ParseBitwiseAnd();
                while (true)
                {
                    SkipSpaces();
                    if (MatchWord("xor"))
                        value = ToInteger(value) ^ ToInteger(ParseBitwiseAnd());
                    else
                        return value;
                }
            }

            private double ParseBitwiseAnd()
            {
                double value = ParseShift();
                while (true)
                {
                    SkipSpaces();
                    if (Match('&'))
                        value = ToInteger(value) & ToInteger(ParseShift());
                    else
                        return value;
                }
            }

            private double ParseShift()
            {
                double value = ParseAdditive();
                while (true)
                {
                    SkipSpaces();
                    if (MatchWord("<<"))
                        value = ToInteger(value) << ClampShift(ToInteger(ParseAdditive()));
                    else if (MatchWord(">>"))
                        value = ToInteger(value) >> ClampShift(ToInteger(ParseAdditive()));
                    else
                        return value;
                }
            }

            private double ParseAdditive()
            {
                double value = ParseTerm();
                while (true)
                {
                    SkipSpaces();
                    if (Match('+'))
                        value += ParseTerm();
                    else if (Match('-'))
                        value -= ParseTerm();
                    else
                        return value;
                }
            }

            private double ParseTerm()
            {
                double value = ParseUnary();
                while (true)
                {
                    SkipSpaces();
                    if (Match('×') || Match('*'))
                        value *= ParseUnary();
                    else if (Match('÷') || Match('/'))
                        value /= ParseUnary();
                    else if (MatchWord("mod"))
                        value = Modulo(ToInteger(value), ToInteger(ParseUnary()));
                    else
                        return value;
                }
            }

            private double ParseUnary()
            {
                SkipSpaces();
                if (Match('-'))
                    return -ParseUnary();
                if (Match('+'))
                    return ParseUnary();
                if (Match('~'))
                    return ~ToInteger(ParseUnary());
                return ParsePower();
            }

            private double ParsePower()
            {
                double value = ParsePostfix();
                SkipSpaces();
                if (Match('^'))
                    return Math.Pow(value, ParseUnary());
                return value;
            }

            private double ParsePostfix()
            {
                double value = ParsePrimary();
                while (true)
                {
                    SkipSpaces();
                    if (Match('%'))
                        value /= 100.0;
                    else if (Match('!'))
                        value = Factorial(value);
                    else
                        return value;
                }
            }

            private double ParsePrimary()
            {
                SkipSpaces();
                if (_pos >= _text.Length)
                    throw new FormatException("Unexpected end of expression");

                char current = _text[_pos];
                if (current == '(')
                {
                    _pos++;
                    double value = ParseExpression();
                    SkipSpaces();
                    if (!Match(')'))
                        throw new FormatException("Missing closing bracket");
                    return value;
                }
                if (current == 'π')
                {
                    _pos++;
                    return Math.PI;
                }
                if (current == '√')
                {
                    _pos++;
                    return Math.Sqrt(ParseArgument());
                }
                if (char.IsDigit(current) || (current == '.'))
                    return ParseNumber();
                if (char.IsLetter(current) || current == '_')
                    return ParseIdentifier();

                throw new FormatException("Unexpected character");
            }

            private double ParseIdentifier()
            {
                int start = _pos;
                while ((_pos < _text.Length) && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_'))
                    _pos++;
                string name = _text.Substring(start, _pos - start);

                if (string.Equals(name, "e", StringComparison.OrdinalIgnoreCase))
                    return Math.E;
                if (string.Equals(name, "pi", StringComparison.OrdinalIgnoreCase))
                    return Math.PI;

                if (IsFunction(name))
                {
                    SkipSpaces();
                    if (_pos < _text.Length && _text[_pos] == '(')
                        return ApplyFunction(name.ToLowerInvariant(), ParseArgument());
                }

                if (_variables != null && _variables.TryGetValue(name, out double variableValue))
                    return variableValue;

                throw new FormatException("Unknown variable");
            }

            private static bool IsFunction(string name)
            {
                switch (name.ToLowerInvariant())
                {
                    case "sin":
                    case "cos":
                    case "tan":
                    case "cot":
                    case "asin":
                    case "acos":
                    case "atan":
                    case "acot":
                    case "ln":
                    case "log":
                    case "exp":
                    case "abs":
                    case "sqrt":
                        return true;
                    default:
                        return false;
                }
            }

            private double ParseArgument()
            {
                SkipSpaces();
                if (!Match('('))
                    throw new FormatException("Missing opening bracket");
                double value = ParseExpression();
                SkipSpaces();
                if (!Match(')'))
                    throw new FormatException("Missing closing bracket");
                return value;
            }

            private double ParseNumber()
            {
                int start = _pos;
                if ((_text[_pos] == '0') && (_pos + 1 < _text.Length))
                {
                    char prefix = _text[_pos + 1];
                    if ((prefix == 'x') || (prefix == 'X'))
                        return ParseRadixNumber(ref start, 16, 2);
                    if ((prefix == 'b') || (prefix == 'B'))
                        return ParseRadixNumber(ref start, 2, 2);
                    if ((prefix == 'o') || (prefix == 'O'))
                        return ParseRadixNumber(ref start, 8, 2);
                }

                bool hasDot = false;
                while (_pos < _text.Length)
                {
                    char current = _text[_pos];
                    if (char.IsDigit(current))
                    {
                        _pos++;
                    }
                    else if ((current == '.') && !hasDot)
                    {
                        hasDot = true;
                        _pos++;
                    }
                    else
                    {
                        break;
                    }
                }

                string number = _text.Substring(start, _pos - start);
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw new FormatException("Invalid number");
                return value;
            }

            private double ParseRadixNumber(ref int start, int numberBase, int prefixLength)
            {
                _pos += prefixLength;
                int digitsStart = _pos;
                while (_pos < _text.Length)
                {
                    char c = _text[_pos];
                    if (char.IsLetterOrDigit(c) || c == '_')
                        _pos++;
                    else
                        break;
                }

                string digits = _text.Substring(digitsStart, _pos - digitsStart).Replace("_", string.Empty);
                if (digits.Length == 0)
                    throw new FormatException("Invalid number");

                BigInteger value = 0;
                foreach (char c in digits)
                {
                    int digit;
                    if (c >= '0' && c <= '9')
                        digit = c - '0';
                    else if (c >= 'a' && c <= 'f')
                        digit = 10 + (c - 'a');
                    else if (c >= 'A' && c <= 'F')
                        digit = 10 + (c - 'A');
                    else
                        throw new FormatException("Invalid digit");
                    if (digit >= numberBase)
                        throw new FormatException("Invalid digit");
                    value = value * numberBase + digit;
                }

                return (double)value;
            }

            private static double Modulo(long left, long right)
            {
                if (right == 0)
                    throw new FormatException("Division by zero");
                return left % right;
            }

            private static int ClampShift(long value)
            {
                return (int)(value & 63);
            }

            private double ApplyFunction(string name, double argument)
            {
                switch (name)
                {
                    case "sin": return Math.Sin(ToRadians(argument));
                    case "cos": return Math.Cos(ToRadians(argument));
                    case "tan": return Math.Tan(ToRadians(argument));
                    case "cot": return 1.0 / Math.Tan(ToRadians(argument));
                    case "asin": return FromRadians(Math.Asin(argument));
                    case "acos": return FromRadians(Math.Acos(argument));
                    case "atan": return FromRadians(Math.Atan(argument));
                    case "acot": return FromRadians((Math.PI / 2.0) - Math.Atan(argument));
                    case "ln": return Math.Log(argument);
                    case "log": return Math.Log10(argument);
                    case "exp": return Math.Exp(argument);
                    case "abs": return Math.Abs(argument);
                    case "sqrt": return Math.Sqrt(argument);
                    default: throw new FormatException("Unknown function");
                }
            }

            private double ToRadians(double value)
            {
                return _degrees ? (value * Math.PI / 180.0) : value;
            }

            private double FromRadians(double value)
            {
                return _degrees ? (value * 180.0 / Math.PI) : value;
            }

            private static double Factorial(double value)
            {
                if ((value < 0) || (value != Math.Floor(value)) || (value > 170))
                    throw new FormatException("Invalid factorial");

                double result = 1;
                for (int i = 2; i <= (int)value; i++)
                    result *= i;
                return result;
            }

            private bool Match(char expected)
            {
                if ((_pos < _text.Length) && (_text[_pos] == expected))
                {
                    _pos++;
                    return true;
                }
                return false;
            }

            private bool MatchWord(string expected)
            {
                if (_pos + expected.Length > _text.Length)
                    return false;
                for (int i = 0; i < expected.Length; i++)
                {
                    if (char.ToLowerInvariant(_text[_pos + i]) != expected[i])
                        return false;
                }

                int end = _pos + expected.Length;
                bool nextIsNameCharacter = end < _text.Length
                    && (char.IsLetterOrDigit(_text[end]) || _text[end] == '_');
                if (nextIsNameCharacter)
                    return false;

                _pos = end;
                return true;
            }

            private void SkipSpaces()
            {
                while ((_pos < _text.Length) && char.IsWhiteSpace(_text[_pos]))
                    _pos++;
            }
        }
    }
}

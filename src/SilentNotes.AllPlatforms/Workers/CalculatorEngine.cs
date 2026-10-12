// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Evaluates the infix expressions entered in the calculator tab. The supported grammar is a
    /// superset of a standard Android calculator and covers the scientific functions
    /// sin/cos/tan/cot (and their inverses), ln, log, exp, sqrt, power, factorial, percent, the
    /// constants π and e, brackets and a unary minus.
    /// </summary>
    public static class CalculatorEngine
    {
        /// <summary>
        /// Evaluates an expression and formats the result.
        /// </summary>
        /// <param name="expression">The expression to evaluate, using × ÷ π √ as symbols.</param>
        /// <param name="degrees">True to interpret trig functions in degrees, false for radians.</param>
        /// <returns>The formatted result, or null if the expression could not be evaluated.</returns>
        public static string Evaluate(string expression, bool degrees)
        {
            try
            {
                var parser = new Parser(expression ?? string.Empty, degrees);
                double value = parser.Parse();
                return Format(value);
            }
            catch (Exception)
            {
                return null;
            }
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

            return rounded.ToString("0.##########", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A small recursive descent parser for the calculator expressions.
        /// </summary>
        private class Parser
        {
            private readonly string _text;
            private readonly bool _degrees;
            private int _pos;

            public Parser(string text, bool degrees)
            {
                _text = text;
                _degrees = degrees;
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
                if (char.IsLetter(current))
                    return ParseIdentifier();

                throw new FormatException("Unexpected character");
            }

            private double ParseIdentifier()
            {
                int start = _pos;
                while ((_pos < _text.Length) && char.IsLetter(_text[_pos]))
                    _pos++;
                string name = _text.Substring(start, _pos - start).ToLowerInvariant();

                if (name == "e")
                    return Math.E;
                if (name == "pi")
                    return Math.PI;

                return ApplyFunction(name, ParseArgument());
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

            private void SkipSpaces()
            {
                while ((_pos < _text.Length) && (_text[_pos] == ' '))
                    _pos++;
            }
        }
    }
}

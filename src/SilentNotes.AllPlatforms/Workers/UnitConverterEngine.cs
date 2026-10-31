// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Text;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Conversion helpers for the Calc converters UI (physics, CS, networking, currency).
    /// </summary>
    public static class UnitConverterEngine
    {
        public enum ConverterCategory
        {
            Physics,
            ComputerScience,
            Networking,
            Currency,
        }

        public sealed class UnitOption
        {
            public string Id { get; init; }
            public string Label { get; init; }
            public double ToBase { get; init; }
        }

        public sealed class UnitGroup
        {
            public string Id { get; init; }
            public string Label { get; init; }
            public IReadOnlyList<UnitOption> Units { get; init; }
            public bool IsTemperature { get; init; }
        }

        /// <summary>Approximate mid-2024 USD-based rates for offline conversion.</summary>
        private static readonly Dictionary<string, double> CurrencyToUsd = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 1.0,
            ["EUR"] = 1.08,
            ["GBP"] = 1.27,
            ["JPY"] = 0.0067,
            ["CNY"] = 0.14,
            ["RUB"] = 0.011,
            ["INR"] = 0.012,
            ["KRW"] = 0.00074,
            ["CHF"] = 1.12,
            ["CAD"] = 0.73,
            ["AUD"] = 0.66,
            ["BRL"] = 0.18,
            ["TRY"] = 0.031,
            ["SEK"] = 0.094,
            ["NOK"] = 0.093,
            ["PLN"] = 0.25,
            ["CZK"] = 0.043,
            ["UAH"] = 0.025,
            ["KZT"] = 0.0021,
            ["AED"] = 0.27,
        };

        public static IReadOnlyList<UnitGroup> PhysicsGroups { get; } = new List<UnitGroup>
        {
            new UnitGroup
            {
                Id = "length",
                Label = "Length",
                Units = new[]
                {
                    U("m", "Meter (m)", 1),
                    U("km", "Kilometer (km)", 1000),
                    U("cm", "Centimeter (cm)", 0.01),
                    U("mm", "Millimeter (mm)", 0.001),
                    U("um", "Micrometer (µm)", 1e-6),
                    U("nm", "Nanometer (nm)", 1e-9),
                    U("in", "Inch (in)", 0.0254),
                    U("ft", "Foot (ft)", 0.3048),
                    U("yd", "Yard (yd)", 0.9144),
                    U("mi", "Mile (mi)", 1609.344),
                    U("nmi", "Nautical mile", 1852),
                },
            },
            new UnitGroup
            {
                Id = "mass",
                Label = "Mass",
                Units = new[]
                {
                    U("kg", "Kilogram (kg)", 1),
                    U("g", "Gram (g)", 0.001),
                    U("mg", "Milligram (mg)", 1e-6),
                    U("t", "Metric ton (t)", 1000),
                    U("lb", "Pound (lb)", 0.45359237),
                    U("oz", "Ounce (oz)", 0.028349523125),
                },
            },
            new UnitGroup
            {
                Id = "temp",
                Label = "Temperature",
                IsTemperature = true,
                Units = new[]
                {
                    U("C", "Celsius (°C)", 1),
                    U("F", "Fahrenheit (°F)", 1),
                    U("K", "Kelvin (K)", 1),
                },
            },
            new UnitGroup
            {
                Id = "speed",
                Label = "Speed",
                Units = new[]
                {
                    U("mps", "m/s", 1),
                    U("kmh", "km/h", 1 / 3.6),
                    U("mph", "mph", 0.44704),
                    U("knot", "Knot", 0.514444),
                },
            },
            new UnitGroup
            {
                Id = "energy",
                Label = "Energy",
                Units = new[]
                {
                    U("J", "Joule (J)", 1),
                    U("kJ", "Kilojoule (kJ)", 1000),
                    U("cal", "Calorie (cal)", 4.184),
                    U("kcal", "Kilocalorie (kcal)", 4184),
                    U("Wh", "Watt-hour (Wh)", 3600),
                    U("kWh", "Kilowatt-hour (kWh)", 3_600_000),
                    U("eV", "Electronvolt (eV)", 1.602176634e-19),
                },
            },
            new UnitGroup
            {
                Id = "pressure",
                Label = "Pressure",
                Units = new[]
                {
                    U("Pa", "Pascal (Pa)", 1),
                    U("kPa", "Kilopascal (kPa)", 1000),
                    U("bar", "Bar", 100000),
                    U("atm", "Atmosphere (atm)", 101325),
                    U("psi", "PSI", 6894.757293168),
                    U("mmHg", "mmHg / Torr", 133.322387415),
                },
            },
            new UnitGroup
            {
                Id = "time",
                Label = "Time",
                Units = new[]
                {
                    U("s", "Second (s)", 1),
                    U("ms", "Millisecond (ms)", 0.001),
                    U("min", "Minute", 60),
                    U("h", "Hour", 3600),
                    U("d", "Day", 86400),
                },
            },
        };

        public static IReadOnlyList<UnitGroup> ComputerScienceGroups { get; } = new List<UnitGroup>
        {
            new UnitGroup
            {
                Id = "datasize",
                Label = "Data size",
                Units = new[]
                {
                    U("B", "Byte (B)", 1),
                    U("KB", "Kilobyte (10³)", 1e3),
                    U("MB", "Megabyte (10⁶)", 1e6),
                    U("GB", "Gigabyte (10⁹)", 1e9),
                    U("TB", "Terabyte (10¹²)", 1e12),
                    U("KiB", "Kibibyte (2¹⁰)", 1024),
                    U("MiB", "Mebibyte (2²⁰)", 1024 * 1024),
                    U("GiB", "Gibibyte (2³⁰)", 1024d * 1024 * 1024),
                    U("TiB", "Tebibyte (2⁴⁰)", 1024d * 1024 * 1024 * 1024),
                    U("bit", "Bit", 0.125),
                },
            },
        };

        public static IReadOnlyList<string> CurrencyCodes => CurrencyToUsd.Keys.OrderBy(k => k).ToList();

        public static string ConvertLinear(double value, UnitOption from, UnitOption to)
        {
            if (from == null || to == null || from.ToBase == 0)
                return string.Empty;
            double baseValue = value * from.ToBase;
            double result = baseValue / to.ToBase;
            return FormatNumber(result);
        }

        public static string ConvertTemperature(double value, string fromId, string toId)
        {
            double celsius = fromId switch
            {
                "F" => (value - 32.0) * 5.0 / 9.0,
                "K" => value - 273.15,
                _ => value,
            };
            double result = toId switch
            {
                "F" => celsius * 9.0 / 5.0 + 32.0,
                "K" => celsius + 273.15,
                _ => celsius,
            };
            return FormatNumber(result);
        }

        public static string ConvertCurrency(double value, string fromCode, string toCode)
        {
            if (!CurrencyToUsd.TryGetValue(fromCode, out double fromRate)
                || !CurrencyToUsd.TryGetValue(toCode, out double toRate)
                || toRate == 0)
            {
                return string.Empty;
            }

            double usd = value * fromRate;
            return FormatNumber(usd / toRate);
        }

        public static string ConvertNumberBase(string input, int fromBase, int toBase)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;
            string trimmed = input.Trim().Replace(" ", string.Empty);
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                fromBase = 16;
                trimmed = trimmed.Substring(2);
            }
            else if (trimmed.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                fromBase = 2;
                trimmed = trimmed.Substring(2);
            }

            try
            {
                BigInteger value = (fromBase == 16)
                    ? BigInteger.Parse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    : ParseInBase(trimmed, fromBase);

                if (toBase == 10)
                    return value.ToString(CultureInfo.InvariantCulture);
                if (toBase == 16)
                    return value.ToString("X", CultureInfo.InvariantCulture);
                if (toBase == 2 || toBase == 8)
                    return FormatInBase(value, toBase);
                return value.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return "Invalid";
            }
        }

        public static string AnalyzeCidr(string cidr)
        {
            if (string.IsNullOrWhiteSpace(cidr))
                return string.Empty;

            string text = cidr.Trim();
            int slash = text.IndexOf('/');
            if (slash <= 0 || slash >= text.Length - 1)
                return "Use a.b.c.d/prefix";

            if (!IPAddress.TryParse(text.Substring(0, slash), out IPAddress address)
                || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return "IPv4 only";
            }

            if (!int.TryParse(text.Substring(slash + 1), out int prefix) || prefix < 0 || prefix > 32)
                return "Prefix must be 0–32";

            uint ip = ToUInt(address);
            uint mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
            uint network = ip & mask;
            uint broadcast = network | ~mask;
            uint hostCount = prefix >= 31 ? (prefix == 32 ? 1u : 2u) : (uint)Math.Pow(2, 32 - prefix) - 2;

            var sb = new StringBuilder();
            sb.AppendLine("Network: " + ToIp(network));
            sb.AppendLine("Mask: " + ToIp(mask));
            sb.AppendLine("Broadcast: " + ToIp(broadcast));
            if (prefix < 31)
            {
                sb.AppendLine("First host: " + ToIp(network + 1));
                sb.AppendLine("Last host: " + ToIp(broadcast - 1));
            }
            sb.Append("Hosts: " + hostCount.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static string AsciiToHex(string text)
        {
            if (text == null)
                return string.Empty;
            var bytes = Encoding.UTF8.GetBytes(text);
            return BitConverter.ToString(bytes).Replace("-", " ");
        }

        public static string HexToAscii(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return string.Empty;
            string cleaned = hex.Replace(" ", string.Empty).Replace("-", string.Empty).Trim();
            if (cleaned.Length % 2 != 0)
                return "Invalid hex";
            try
            {
                var bytes = new byte[cleaned.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] = byte.Parse(cleaned.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return "Invalid hex";
            }
        }

        private static UnitOption U(string id, string label, double toBase) =>
            new UnitOption { Id = id, Label = label, ToBase = toBase };

        private static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return string.Empty;
            if (Math.Abs(value) >= 1e10 || (Math.Abs(value) > 0 && Math.Abs(value) < 1e-6))
                return value.ToString("0.######E+0", CultureInfo.InvariantCulture);
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private static BigInteger ParseInBase(string text, int numberBase)
        {
            BigInteger result = 0;
            foreach (char c in text)
            {
                int digit;
                if (c >= '0' && c <= '9')
                    digit = c - '0';
                else if (c >= 'a' && c <= 'f')
                    digit = 10 + (c - 'a');
                else if (c >= 'A' && c <= 'F')
                    digit = 10 + (c - 'A');
                else
                    throw new FormatException();
                if (digit >= numberBase)
                    throw new FormatException();
                result = result * numberBase + digit;
            }
            return result;
        }

        private static string FormatInBase(BigInteger value, int numberBase)
        {
            if (value == 0)
                return "0";
            bool negative = value.Sign < 0;
            if (negative)
                value = -value;
            var chars = new Stack<char>();
            while (value > 0)
            {
                value = BigInteger.DivRem(value, numberBase, out BigInteger rem);
                int d = (int)rem;
                chars.Push((char)(d < 10 ? '0' + d : 'A' + (d - 10)));
            }
            string s = new string(chars.ToArray());
            return negative ? "-" + s : s;
        }

        private static uint ToUInt(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        private static string ToIp(uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return new IPAddress(bytes).ToString();
        }
    }
}

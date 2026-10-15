// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using DynamicExpresso;

namespace PcmHacking
{
    /// <summary>
    /// Works out the scale a parameter reads over, so a gauge or a chart has an axis before any data
    /// has arrived.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored. A parameter's range is already implied by its conversion and its
    /// storage width - a byte through "x * 100 / 255" can only ever produce 0 to 100 - so writing it
    /// into the definitions would duplicate what the file already says and leave two things to keep
    /// in step. Deriving it also means every parameter gets a scale, including ones added later,
    /// without anyone hand-editing XML.
    ///
    /// Full scale is not always the useful scale: engine speed derives to 0-16383 where a dial wants
    /// 0-8000, and a temperature that can read -40 to 215 is usually watched over a much narrower
    /// band. <see cref="Overrides"/> carries those few by parameter id; everything else takes what
    /// the maths gives.
    /// </remarks>
    public static class ConversionRange
    {
        /// <summary>
        /// Parameters whose full-scale range is technically correct but useless on a gauge, by
        /// parameter id. Deliberately short - it earns its keep on the ones people actually display.
        /// </summary>
        private static readonly Dictionary<string, (double Low, double High)> Overrides =
            new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase)
            {
                { "EngineSpeed", (0, 8000) },
                { "DesiredIdleSpeed", (0, 2000) },
                { "MAFSensor", (0, 400) },
                { "MAFHighPrecision", (0, 400) },
                { "VehicleSpeedSensor", (0, 260) },
                { "ECTSensor", (-40, 150) },
                { "IATSensor", (-40, 150) },
                { "StartUpECT", (-40, 150) },
                { "TransmissionOilTemperature", (-40, 150) },
                { "TransmissinonFluidTemperature", (-40, 150) },
                { "EngineLoad", (0, 100) },
                { "IgnitionTiming", (-20, 50) },
                { "KnockRetardDegrees", (0, 25) },
                { "Ignition1Signal", (8, 16) },
                { "EngineOilPressure", (0, 700) },
                { "CalculatedCatTemp", (0, 1000) },
                { "EngineRunTime", (0, 3600) },
            };

        /// <summary>
        /// The range a conversion produces over its parameter's storage, or null when it cannot be
        /// worked out. Bit-mapped parameters have no scale and return null.
        /// </summary>
        public static (double Low, double High)? Derive(
            string parameterId, string expression, string storageType, bool bitMapped)
        {
            if (bitMapped || string.IsNullOrWhiteSpace(expression))
            {
                return null;
            }

            if (Overrides.TryGetValue(parameterId ?? string.Empty, out (double Low, double High) known))
            {
                return known;
            }

            if (!TryGetStorageBounds(storageType, out double rawLow, out double rawHigh))
            {
                return null;
            }

            double? low = Evaluate(expression, rawLow);
            double? high = Evaluate(expression, rawHigh);
            if (low == null || high == null)
            {
                return null;
            }

            double lower = Math.Min(low.Value, high.Value);
            double upper = Math.Max(low.Value, high.Value);

            // A span of zero is a constant, and an enormous one means the conversion is not really a
            // linear scale. Neither makes a usable axis, so say nothing and let the display work it
            // out from the data.
            double span = upper - lower;
            if (span <= 0 || span > 1e6 || double.IsNaN(span) || double.IsInfinity(span))
            {
                return null;
            }

            return (Round(lower), Round(upper));
        }

        private static bool TryGetStorageBounds(string storageType, out double low, out double high)
        {
            switch (storageType)
            {
                case "uint8":
                    low = 0;
                    high = byte.MaxValue;
                    return true;

                case "int8":
                    low = sbyte.MinValue;
                    high = sbyte.MaxValue;
                    return true;

                case "uint16":
                    low = 0;
                    high = ushort.MaxValue;
                    return true;

                case "int16":
                    low = short.MinValue;
                    high = short.MaxValue;
                    return true;

                default:
                    low = 0;
                    high = 0;
                    return false;
            }
        }

        private static double? Evaluate(string expression, double x)
        {
            try
            {
                Interpreter interpreter = new Interpreter();
                interpreter.SetVariable("x", x);
                return interpreter.Eval<double>(expression);
            }
            catch (Exception)
            {
                // A conversion this cannot evaluate simply has no derived range; it is not an error,
                // and refusing to load the database over it would be far worse.
                return null;
            }
        }

        /// <summary>
        /// Trim the floating-point tail without moving the number: a derived end point is exact, and
        /// rounding 1.275 volts to 1.28 would put a wrong figure on the axis to save one character.
        /// </summary>
        private static double Round(double value)
        {
            return Math.Abs(value) >= 100 ? Math.Round(value) : Math.Round(value, 4);
        }
    }
}

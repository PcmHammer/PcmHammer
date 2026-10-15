// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace Tests
{
    [TestClass]
    public class LoggingTests
    {
        private Dictionary<uint, uint> GetAddress(uint address)
        {
            Dictionary<uint, uint> addresses = new Dictionary<uint, uint>();
            addresses[0] = address;
            return addresses;
        }

        [TestMethod]
        public void DecodeDpid()
        {
            Conversion conversion = new Conversion("units", "x*0.9", "0.00");
            Conversion[] conversions = new Conversion[] { conversion };

            Parameter signed8 =    new RamParameter("S8",  "signed 8-bit",    "", "int8",   false, conversions, GetAddress(0));
            Parameter unsigned8 =  new RamParameter("U8",  "unsigned 8-bit",  "", "uint8",  false, conversions, GetAddress(0));
            Parameter signed16 =   new RamParameter("U8",  "signed 16-bit",   "", "int16",  false, conversions, GetAddress(0));
            Parameter unsigned16 = new RamParameter("U16", "unsigned 16-bit", "", "uint16", false, conversions, GetAddress(0));

            LogColumn signed8Column = new LogColumn(signed8, conversion, false);
            LogColumn unsigned8Column = new LogColumn(unsigned8, conversion, false);
            LogColumn signed16Column = new LogColumn(signed16, conversion, false);
            LogColumn unsigned16Column = new LogColumn(unsigned16, conversion, false);

            ParameterGroup group = new ParameterGroup(0);

            group.TryAddLogColumn(signed8Column);
            group.TryAddLogColumn(unsigned8Column);
            group.TryAddLogColumn(signed16Column);
            group.TryAddLogColumn(unsigned16Column);

            DpidConfiguration dpid = new DpidConfiguration();
            dpid.TryAddGroup(group);
            LogRowParser parser = new LogRowParser(dpid);

            Int16 int16 = -3000;
            UInt16 uint16 = 50000;
            byte[] bytesOfSigned16 =   new byte[] { (byte) ((int16 & 0xFF00) >> 8), (byte) (int16 & 0xFF) };
            byte[] bytesOfUnsigned16 = new byte[] { (byte) ((uint16 & 0xFF00) >> 8), (byte) (uint16 & 0xFF) };

            byte signed8Byte;
            unchecked
            {
                signed8Byte = (byte)-20;
            }

            RawLogData data = new RawLogData(
                0,
                new byte[] 
                {
                    signed8Byte,
                    200,
                    bytesOfSigned16[0],
                    bytesOfSigned16[1],
                    bytesOfUnsigned16[0],
                    bytesOfUnsigned16[1]
                });

            parser.ParseData(data);
            PcmParameterValues values = parser.Evaluate();

            // Values are scaled here to match the conversion formula.
            Assert.AreEqual(-20 * 0.9, values[signed8Column].ValueAsDouble, "signed 8");
            Assert.AreEqual(200 * 0.9, values[unsigned8Column].ValueAsDouble, "unsigned 8");
            Assert.AreEqual(int16 * 0.9, values[signed16Column].ValueAsDouble, "signed 16");
            Assert.AreEqual(uint16 * 0.9, values[unsigned16Column].ValueAsDouble, "unsigned 16");
        }

        /// <summary>
        /// The SAE O2 PIDs (14, 15, 18, 19) return two bytes that are two different quantities:
        /// byte 1 is the sensor voltage at 0.005 V per count, byte 2 is short term fuel trim. The
        /// conversion has to take the high byte of the 16-bit read and ignore the low one.
        /// </summary>
        /// <remarks>
        /// Written because the obvious fix - declaring the parameter as one byte so only the voltage
        /// is requested - is rejected by the PCM, which answers 7F 2C 12 for a partial PID and aborts
        /// the whole logging session. The full width must be requested and the byte picked apart
        /// afterwards, which puts the arithmetic in an expression string that nothing else checks.
        /// </remarks>
        [TestMethod]
        public void O2ConversionTakesTheVoltageByte()
        {
            Conversion conversion = new Conversion("Volts", "((x - (x % 256)) / 256) * 0.005", "0.000");
            Conversion[] conversions = new Conversion[] { conversion };

            Parameter o2 = new RamParameter(
                "O2", "O2 sensor", "", "uint16", false, conversions, GetAddress(0));
            LogColumn column = new LogColumn(o2, conversion, false);

            ParameterGroup group = new ParameterGroup(0);
            group.TryAddLogColumn(column);

            DpidConfiguration dpid = new DpidConfiguration();
            dpid.TryAddGroup(group);
            LogRowParser parser = new LogRowParser(dpid);

            // 0x58 = 88 counts = 0.440 V; 0x80 = neutral fuel trim, and must not reach the result.
            parser.ParseData(new RawLogData(0, new byte[] { 0x58, 0x80 }));

            Assert.AreEqual(0.440, parser.Evaluate()[column].ValueAsDouble, 0.0001, "voltage");
        }

        /// <summary>
        /// A conversion's scale is derived from its expression and storage width, so a gauge has an
        /// axis without anyone writing ranges into the definitions by hand.
        /// </summary>
        [TestMethod]
        public void RangeIsDerivedFromExpressionAndStorage()
        {
            (double Low, double High)? percent =
                ConversionRange.Derive("Anything", "x * 100 / 255", "uint8", false);
            Assert.IsNotNull(percent, "percent derived");
            Assert.AreEqual(0, percent!.Value.Low, 0.01, "percent low");
            Assert.AreEqual(100, percent.Value.High, 0.01, "percent high");

            (double Low, double High)? volts =
                ConversionRange.Derive("Anything", "x * 0.005", "uint8", false);
            Assert.IsNotNull(volts, "volts derived");
            Assert.AreEqual(0, volts!.Value.Low, 0.0001, "volts low");
            Assert.AreEqual(1.275, volts.Value.High, 0.0001, "volts high");

            // Signed storage has to carry its negative end through the conversion.
            (double Low, double High)? signed =
                ConversionRange.Derive("Anything", "x", "int8", false);
            Assert.IsNotNull(signed, "signed derived");
            Assert.AreEqual(-128, signed!.Value.Low, 0.01, "signed low");
            Assert.AreEqual(127, signed.Value.High, 0.01, "signed high");
        }

        /// <summary>Things with no sensible axis say so, rather than returning a misleading one.</summary>
        [TestMethod]
        public void RangeIsAbsentWhereItWouldBeMeaningless()
        {
            Assert.IsNull(
                ConversionRange.Derive("Anything", "x", "uint8", bitMapped: true), "bit mapped");
            Assert.IsNull(
                ConversionRange.Derive("Anything", "x", "", false), "unknown storage");
            Assert.IsNull(
                ConversionRange.Derive("Anything", "17", "uint8", false), "constant");
            Assert.IsNull(
                ConversionRange.Derive("Anything", "not an expression", "uint8", false), "unparsable");
        }

        /// <summary>
        /// Full scale is correct but useless for a few parameters, so those are overridden by id.
        /// </summary>
        [TestMethod]
        public void RangeOverrideWinsForParametersWhereFullScaleIsUseless()
        {
            // 0 - 16383 is what the maths gives; nobody wants that on a tachometer.
            (double Low, double High)? rpm =
                ConversionRange.Derive("EngineSpeed", "x * 0.25", "uint16", false);

            Assert.IsNotNull(rpm);
            Assert.AreEqual(0, rpm!.Value.Low, 0.01, "low");
            Assert.AreEqual(8000, rpm.Value.High, 0.01, "high");
        }

        /// <summary>The low byte must not shift the reading, whatever fuel trim happens to be.</summary>
        [TestMethod]
        public void O2ConversionIgnoresTheFuelTrimByte()
        {
            Conversion conversion = new Conversion("Volts", "((x - (x % 256)) / 256) * 0.005", "0.000");
            Conversion[] conversions = new Conversion[] { conversion };

            Parameter o2 = new RamParameter(
                "O2", "O2 sensor", "", "uint16", false, conversions, GetAddress(0));

            foreach (byte trim in new byte[] { 0x00, 0x7F, 0x80, 0xFF })
            {
                LogColumn column = new LogColumn(o2, conversion, false);
                ParameterGroup group = new ParameterGroup(0);
                group.TryAddLogColumn(column);

                DpidConfiguration dpid = new DpidConfiguration();
                dpid.TryAddGroup(group);
                LogRowParser parser = new LogRowParser(dpid);

                parser.ParseData(new RawLogData(0, new byte[] { 0xFF, trim }));

                Assert.AreEqual(
                    1.275, parser.Evaluate()[column].ValueAsDouble, 0.0001, "trim 0x" + trim.ToString("X2"));
            }
        }
    }
}

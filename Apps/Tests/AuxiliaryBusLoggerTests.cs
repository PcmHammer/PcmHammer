// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace Tests
{
    /// <summary>
    /// Decoding broadcast messages into parameter values, and combining the ones that arrive between
    /// log rows.
    /// </summary>
    /// <remarks>
    /// These feed Record(messageId, payload) rather than raw adapter bytes. The logger no longer owns
    /// a port - a Device supplies frames already decoded - so the serial framing belongs to
    /// UsbCanAnalyzerProtocolTests and CanParserTests, and what is left here is the part this class
    /// is actually responsible for.
    /// </remarks>
    [TestClass]
    public class AuxiliaryBusLoggerTests
    {
        private static readonly ParameterDatabase ParameterDatabase =
            new ParameterDatabase(CreateMockParameters());

        private static Dictionary<UInt32, IEnumerable<BusParameter>> CreateMockParameters()
        {
            Conversion conversion = new Conversion("test", "x", "0.00");
            Conversion[] conversions = new Conversion[] { conversion };

            BusParameter param1 = new BusParameter(0x01, 0, 2, true, "1", "1", "1", conversions, Aggregation.Last);
            BusParameter param21 = new BusParameter(0x02, 0, 2, true, "21", "21", "21", conversions, Aggregation.Sum);
            BusParameter param22 = new BusParameter(0x02, 2, 2, true, "22", "22", "22", conversions, Aggregation.Average);

            return new Dictionary<UInt32, IEnumerable<BusParameter>>
            {
                { 0x01, new BusParameter[] { param1 } },
                { 0x02, new BusParameter[] { param21, param22 } },
            };
        }

        private static AuxiliaryBusLogger CreateLogger()
        {
            AuxiliaryBusLogger logger = new AuxiliaryBusLogger(ParameterDatabase, new MockLogger());
            logger.UseDatabaseKeys();
            return logger;
        }

        [TestMethod]
        public void UnknownMessageLeavesTheColumnsAlone()
        {
            AuxiliaryBusLogger logger = CreateLogger();
            logger.Record(0x1033021, new byte[] { 0x11, 0x22 });

            // Every row carries the same columns, so UseDatabaseKeys seeds each known parameter and a
            // row is always returned. An unknown message is ignored by leaving those seeded values
            // alone, not by returning nothing.
            IList<AuxiliaryBusLogger.ParameterAndValue> results = logger.GetParameterValues().ToArray();

            CollectionAssert.AreEqual(
                new[] { "1", "21", "22" },
                results.Select(x => x.Parameter.Id).ToArray(),
                "an unknown message must not add a parameter");

            foreach (AuxiliaryBusLogger.ParameterAndValue result in results)
            {
                Assert.AreEqual(0, result.ValueAsNumber, "valueAsNumber for " + result.Parameter.Id);
                Assert.AreEqual("0", result.ValueAsString, "valueAsString for " + result.Parameter.Id);
            }
        }

        [TestMethod]
        public void KnownMessageIsDecoded()
        {
            AuxiliaryBusLogger logger = CreateLogger();
            logger.Record(0x01, new byte[] { 0xFF, 0xFF });

            AuxiliaryBusLogger.ParameterAndValue result = logger.GetParameterValues().First();

            Assert.AreEqual("1", result.Parameter.Id, "Id");
            Assert.AreEqual(65535, result.ValueAsNumber, "valueAsNumber");
            Assert.AreEqual("65535.00", result.ValueAsString, "valueAsString");
            Assert.AreEqual("test", result.Units, "Units");
        }

        /// <summary>A parameter whose bytes run past the end of the message reads zero, not a crash.</summary>
        [TestMethod]
        public void ShortMessageDoesNotRunOffTheEnd()
        {
            AuxiliaryBusLogger logger = CreateLogger();
            logger.Record(0x01, new byte[] { 0xFF });

            Assert.AreEqual(0, logger.GetParameterValues().First().ValueAsNumber);
        }

        [TestMethod]
        public void LastWins()
        {
            AuxiliaryBusLogger logger = CreateLogger();
            logger.Record(0x01, new byte[] { 0xFF, 0xFF });
            logger.Record(0x01, new byte[] { 0x00, 0x00 });
            logger.Record(0x01, new byte[] { 0x00, 0xFF });

            AuxiliaryBusLogger.ParameterAndValue result = logger.GetParameterValues().First();

            Assert.AreEqual("1", result.Parameter.Id, "Id");
            Assert.AreEqual(255, result.ValueAsNumber, "valueAsNumber");
            Assert.AreEqual("255.00", result.ValueAsString, "valueAsString");
        }

        [TestMethod]
        public void SumAndAverage()
        {
            AuxiliaryBusLogger logger = CreateLogger();
            logger.Record(0x02, new byte[] { 0x00, 0x01, 0x00, 0x02 });
            logger.Record(0x02, new byte[] { 0x00, 0x02, 0x00, 0x04 });
            logger.Record(0x02, new byte[] { 0x00, 0x03, 0x00, 0x06 });

            IList<AuxiliaryBusLogger.ParameterAndValue> values = logger.GetParameterValues().ToArray();

            AuxiliaryBusLogger.ParameterAndValue summed = values.First(x => x.Parameter.Id == "21");
            Assert.AreEqual(6, summed.ValueAsNumber, "1 + 2 + 3");
            Assert.AreEqual("6.00", summed.ValueAsString, "summed as string");

            AuxiliaryBusLogger.ParameterAndValue averaged = values.First(x => x.Parameter.Id == "22");
            Assert.AreEqual(4, averaged.ValueAsNumber, "(2 + 4 + 6) / 3");
            Assert.AreEqual("4.00", averaged.ValueAsString, "averaged as string");
        }

        /// <summary>
        /// Values are consumed when a row is built, so the next row aggregates only what arrived
        /// after it rather than everything since the session started.
        /// </summary>
        [TestMethod]
        public void ValuesAreConsumedByBuildingARow()
        {
            AuxiliaryBusLogger logger = CreateLogger();
            logger.Record(0x02, new byte[] { 0x00, 0x05, 0x00, 0x05 });

            Assert.AreEqual(5, logger.GetParameterValues().First(x => x.Parameter.Id == "21").ValueAsNumber, "first row");

            // Nothing new arrived, so the previous reading stands rather than summing again.
            Assert.AreEqual(5, logger.GetParameterValues().First(x => x.Parameter.Id == "21").ValueAsNumber, "second row");

            logger.Record(0x02, new byte[] { 0x00, 0x02, 0x00, 0x02 });

            Assert.AreEqual(2, logger.GetParameterValues().First(x => x.Parameter.Id == "21").ValueAsNumber, "third row");
        }

        /// <summary>A little-endian parameter reads its bytes the other way round.</summary>
        [TestMethod]
        public void LowByteFirstIsHonoured()
        {
            Conversion conversion = new Conversion("test", "x", "0.00");
            BusParameter parameter = new BusParameter(
                0x03, 0, 2, false, "lo", "lo", "lo", new Conversion[] { conversion }, Aggregation.Last);

            ParameterDatabase database = new ParameterDatabase(
                new Dictionary<UInt32, IEnumerable<BusParameter>>
                {
                    { 0x03, new BusParameter[] { parameter } },
                });

            AuxiliaryBusLogger logger = new AuxiliaryBusLogger(database, new MockLogger());
            logger.UseDatabaseKeys();
            logger.Record(0x03, new byte[] { 0x01, 0x02 });

            Assert.AreEqual(0x0201, logger.GetParameterValues().First().ValueAsNumber);
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// Unit tests for the log file's headings and for reading one back. The writer and reader share
    /// one format, so these cover the round trip rather than either half alone.
    /// </summary>
    [TestClass]
    public class LogFileTests
    {
        private static readonly Conversion Rpm = new Conversion("RPM", "x", "0");

        private static PidParameter Pid(string id, string name, uint pid) =>
            new PidParameter(id, name, "", "uint16", false, new[] { Rpm }, pid, new uint[0]);

        [TestMethod]
        public void Heading_PutsThePidFirst()
        {
            Assert.AreEqual(
                "PID 000C: Engine Speed (RPM)",
                LogColumnHeading.For(Pid("EngineSpeed", "Engine Speed", 0x000C), "RPM"));
        }

        [TestMethod]
        public void Heading_UsesTheMessageIdForABroadcastParameter()
        {
            BusParameter parameter = new BusParameter(
                0x000A0301, 0, 2, true, "aem", "AEM Pressure", "", new[] { Rpm }, Aggregation.Last);

            Assert.AreEqual("MSG 000A0301: AEM Pressure (psi)", LogColumnHeading.For(parameter, "psi"));
        }

        [TestMethod]
        public void Heading_OmitsTheAddressWhenThereIsNone()
        {
            // Math is computed rather than read, so there is no address to record.
            MathParameter parameter = new MathParameter(
                "afr", "Air Fuel Ratio", "", new[] { Rpm },
                new LogColumn(Pid("a", "A", 1), Rpm, false),
                new LogColumn(Pid("b", "B", 2), Rpm, false));

            Assert.AreEqual("Air Fuel Ratio (AFR)", LogColumnHeading.For(parameter, "AFR"));
        }

        [TestMethod]
        public void ParseHeading_SplitsAddressNameAndUnits()
        {
            LoggedColumn column = LogFileReader.ParseHeading("PID 000C: Engine Speed (RPM)");

            Assert.AreEqual("PID 000C", column.Address);
            Assert.AreEqual("Engine Speed", column.Name);
            Assert.AreEqual("RPM", column.Units);
        }

        [TestMethod]
        public void ParseHeading_HandlesAColumnWithNoAddress()
        {
            LoggedColumn column = LogFileReader.ParseHeading("Air Fuel Ratio (AFR)");

            Assert.IsNull(column.Address);
            Assert.AreEqual("Air Fuel Ratio", column.Name);
            Assert.AreEqual("AFR", column.Units);
        }

        [TestMethod]
        public void ParseHeading_TakesTheLastBracketsAsUnits()
        {
            // A name of its own can contain brackets; the units are always the trailing group.
            LoggedColumn column = LogFileReader.ParseHeading("PID 1141: Battery (main) (Volts)");

            Assert.AreEqual("Battery (main)", column.Name);
            Assert.AreEqual("Volts", column.Units);
        }

        /// <summary>
        /// A log in the writer's shape, written by hand so the reader is tested against the format
        /// rather than against whatever the writer happens to do today.
        /// </summary>
        private static string WriteSampleLog()
        {
            StringWriter text = new StringWriter();
            text.WriteLine("Clock Time, Elapsed Time, PID 000C: Engine Speed (RPM), Air Fuel Ratio (AFR)");
            text.WriteLine("2026-10-03 11:22:33Z, 00:00:00.1000000, 800, 14.7");
            text.WriteLine("2026-10-03 11:22:34Z, 00:00:01.1000000, 1600, 14.6");
            return text.ToString();
        }

        [TestMethod]
        public void Read_ReturnsColumnsAndSamples()
        {
            LoggedData data = LogFileReader.Read(new StringReader(WriteSampleLog()));

            Assert.AreEqual(2, data.Columns.Count);
            Assert.AreEqual("PID 000C", data.Columns[0].Address);
            Assert.AreEqual("Air Fuel Ratio", data.Columns[1].Name);

            Assert.AreEqual(2, data.Samples.Count);
            Assert.AreEqual(800, data.Samples[0][0]);
            Assert.AreEqual(14.7, data.Samples[0][1], 0.0001);
            Assert.AreEqual(1600, data.Samples[1][0]);
            Assert.IsTrue(data.Timestamps[1] > data.Timestamps[0]);
        }

        [TestMethod]
        public void Read_SkipsRowsThatDoNotMatchTheHeader()
        {
            string log =
                "Clock Time, Elapsed Time, PID 000C: Engine Speed (RPM)\r\n" +
                "2026-10-03 11:22:33Z, 00:00:00.1000000, 800\r\n" +
                "2026-10-03 11:22:34Z, 00:00:01.1000000\r\n" +
                "2026-10-03 11:22:35Z, 00:00:02.1000000, 1600\r\n";

            LoggedData data = LogFileReader.Read(new StringReader(log));

            Assert.AreEqual(2, data.Samples.Count);
            Assert.AreEqual(1, data.MalformedRows);
        }

        [TestMethod]
        public void Read_GivesNaNForAFieldThatWillNotParse()
        {
            // A parameter that produced no value leaves an empty field rather than ending the row.
            string log =
                "Clock Time, Elapsed Time, PID 000C: Engine Speed (RPM)\r\n" +
                "2026-10-03 11:22:33Z, 00:00:00.1000000, \r\n";

            LoggedData data = LogFileReader.Read(new StringReader(log));

            Assert.AreEqual(1, data.Samples.Count);
            Assert.IsTrue(double.IsNaN(data.Samples[0][0]));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidDataException))]
        public void Read_RejectsAFileWithNoDataColumns()
        {
            LogFileReader.Read(new StringReader("Clock Time, Elapsed Time\r\n"));
        }

        [TestMethod]
        public void Read_FillsAHistoryTheMonitorsCanDraw()
        {
            LoggedData data = LogFileReader.Read(new StringReader(WriteSampleLog()));

            LogHistory history = new LogHistory(data.Columns.Select(c => c.Heading));
            for (int row = 0; row < data.Samples.Count; row++)
            {
                history.Append(data.Timestamps[row], data.Samples[row]);
            }

            Assert.AreEqual(2, history.Count);
            Assert.AreEqual(2, history.ColumnCount);
            Assert.AreEqual(1600, history.GetValue(1, 0));
        }
    }
}

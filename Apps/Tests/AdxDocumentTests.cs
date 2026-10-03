// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace PcmHacking.Tests
{
    /// <summary>
    /// Reading TunerPro ADX definitions.
    /// </summary>
    [TestClass]
    public class AdxDocumentTests
    {
        /// <summary>
        /// A cut-down ADX with one value, one bitmask, and one of each view. The shapes and the
        /// mixture of hex and decimal are taken from a real file.
        /// </summary>
        private const string SampleAdx = @"<ADXFORMAT version=""1.01"">
  <ADXHEADER>
    <guid>7b1b0aeb-bacd-467e-a060-467b79441385</guid>
    <userversion>1.12</userversion>
    <author>Someone</author>
    <desc>Sample</desc>
    <baud>8192</baud>
    <DEFAULTS datasizeinbits=""8"" sigdigits=""2"" outputtype=""3"" baud=""0"" signed=""0"" lsbfirst=""0"" float=""0"" />
  </ADXHEADER>
  <ADXCLISTENPACKET id=""Mode1Message0"" idhash=""0x55C678F5"" title=""Rx Mode1 Message0"">
    <listentimeout>500</listentimeout>
    <packetbodylength>60</packetbodylength>
    <packetoffsetinbody>3</packetoffsetinbody>
    <packetsize>57</packetsize>
  </ADXCLISTENPACKET>
  <ADXVALUE id=""1"" idhash=""0x86F3549B"" title=""Engine RPM"">
    <parentcmdidhash>0x55C678F5</parentcmdidhash>
    <units>RPM</units>
    <packetoffset>0x00</packetoffset>
    <sizeinbits>16</sizeinbits>
    <range low=""0.000000"" high=""9600.000000"" />
    <alarms low=""0.000000"" high=""5000.000000"" />
    <digcount>1</digcount>
    <MATH equation=""X*0.25""><VAR varID=""X"" type=""native"" /></MATH>
  </ADXVALUE>
  <ADXVALUE id=""2"" idhash=""0x00000002"" title=""Coolant"">
    <parentcmdidhash>0x55C678F5</parentcmdidhash>
    <units>DegC</units>
    <packetoffset>0x07</packetoffset>
  </ADXVALUE>
  <ADXVALUE id=""InjDC"" idhash=""0x00000003"" title=""Injector Duty Cycle"">
    <parentcmdidhash>0x55C678F5</parentcmdidhash>
    <units>%</units>
    <packetoffset>0x0A</packetoffset>
    <MATH equation=""((X / 65.536) / (2000 * (1/(Y/60)))) * 100"">
      <VAR varID=""X"" type=""native"" />
      <VAR varID=""Y"" type=""link"" linkIDHash=""0x86F3549B"" />
    </MATH>
  </ADXVALUE>
  <ADXVALUE id=""BaroMAP"" idhash=""0x00000004"" title=""Baro Compensated MAP"">
    <parentcmdidhash>0x55C678F5</parentcmdidhash>
    <units>kPa</units>
    <MATH equation=""((( X * 256)/ Y) * 0.313) + 20.0"">
      <VAR varID=""X"" type=""link"" linkIDHash=""0x00000003"" />
      <VAR varID=""Y"" type=""link"" linkIDHash=""0x00000002"" />
    </MATH>
  </ADXVALUE>
  <ADXBITMASK id=""C9"" idhash=""0xDE6B7930"" title=""Pin C9 Input"">
    <parentcmdidhash>0x55C678F5</parentcmdidhash>
    <truestring>Active</truestring>
    <falsestring>Clear</falsestring>
    <packetoffset>0x17</packetoffset>
    <operand>0x00000020</operand>
    <bitop>AND</bitop>
    <result>0x00000020</result>
  </ADXBITMASK>
  <ADXDASHBOARD id=""100"" idhash=""0xCD52093B"" title=""Complete Dash"">
    <entrycount>2</entrycount>
    <ADXDGENTRY gaugetype=""0"" itemidhash=""0x86F3549B"" left=""0"" top=""0"" right=""27"" bottom=""35"" arcmax=""300"" />
    <ADXDGENTRY gaugetype=""1"" itemidhash=""0xDE6B7930"" left=""27"" top=""0"" right=""52"" bottom=""35"" bordercolor=""0x00000000"" />
  </ADXDASHBOARD>
  <ADXMONITOR id=""monitor1"" idhash=""0xCEC4CE52"" title=""Monitor1"">
    <plotbkgcolor>0x00F0F0F0</plotbkgcolor>
    <ADXMONSERIES linecolor=""0x0000FF"" axiscolor=""0x0000FF"" itemidhash=""0x86F3549B"" />
  </ADXMONITOR>
  <ADXLISTVIEW id=""M1M0"" idhash=""0xAF28B859"" title=""Mode1Message0"">
    <ADXLVENTRY entrytype=""0"" itemidhash=""0x86F3549B"" />
    <ADXLVENTRY entrytype=""0"" itemidhash=""0xDE6B7930"" />
  </ADXLISTVIEW>
</ADXFORMAT>";

        private static AdxDocument LoadSample()
        {
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(SampleAdx)))
            {
                return AdxDocument.Load(stream);
            }
        }

        [TestMethod]
        public void HeaderIsRead()
        {
            AdxDocument document = LoadSample();

            Assert.AreEqual("1.12", document.UserVersion);
            Assert.AreEqual("Someone", document.Author);
            Assert.AreEqual(8192, document.Baud);
            Assert.AreEqual(8, document.DefaultDataSizeInBits);
            Assert.AreEqual(2, document.DefaultSignificantDigits);
        }

        [TestMethod]
        public void ValuesAndBitmasksBothBecomeParameters()
        {
            AdxDocument document = LoadSample();

            Assert.AreEqual(5, document.Parameters.Count);
            Assert.AreEqual(4, document.Parameters.Count(p => !p.IsBitMapped));
            Assert.AreEqual(1, document.Parameters.Count(p => p.IsBitMapped));
        }

        [TestMethod]
        public void NativeAndLinkedVariablesAreRead()
        {
            AdxParameter? injectorDutyCycle = LoadSample().FindParameter(0x00000003);

            Assert.IsNotNull(injectorDutyCycle);
            Assert.AreEqual(2, injectorDutyCycle!.Variables.Count);

            AdxVariable native = injectorDutyCycle.Variables.Single(v => !v.IsLink);
            Assert.AreEqual("X", native.VarId);

            AdxVariable linked = injectorDutyCycle.Variables.Single(v => v.IsLink);
            Assert.AreEqual("Y", linked.VarId);
            Assert.AreEqual(0x86F3549Bu, linked.LinkIdHash);
        }

        /// <summary>
        /// A parameter whose every variable is linked reads nothing from the packet - its packet
        /// offset is meaningless and it can only be computed once its sources are known.
        /// </summary>
        [TestMethod]
        public void FullyLinkedParameterIsDerived()
        {
            AdxDocument document = LoadSample();

            Assert.IsTrue(document.FindParameter(0x00000004)!.IsDerived, "Baro Compensated MAP");
            Assert.IsFalse(document.FindParameter(0x00000003)!.IsDerived, "Injector Duty Cycle");
            Assert.IsFalse(document.FindParameter(0x86F3549B)!.IsDerived, "Engine RPM");
        }

        [TestMethod]
        public void EvaluationOrderPutsSourcesBeforeDependents()
        {
            IReadOnlyList<AdxParameter> order = LoadSample().GetEvaluationOrder();

            Assert.AreEqual(5, order.Count, "every parameter is placed");

            int rpm = IndexOf(order, 0x86F3549B);
            int coolant = IndexOf(order, 0x00000002);
            int injectorDutyCycle = IndexOf(order, 0x00000003);
            int baroMap = IndexOf(order, 0x00000004);

            Assert.IsTrue(rpm < injectorDutyCycle, "RPM before the duty cycle that divides by it");
            Assert.IsTrue(injectorDutyCycle < baroMap, "duty cycle before the value derived from it");
            Assert.IsTrue(coolant < baroMap, "coolant before the value derived from it");
        }

        /// <summary>A cycle must not drop parameters or hang the sort.</summary>
        [TestMethod]
        public void DependencyCycleStillReturnsEveryParameter()
        {
            string cyclic = @"<ADXFORMAT version=""1.01"">
  <ADXHEADER><DEFAULTS datasizeinbits=""8"" /></ADXHEADER>
  <ADXVALUE id=""a"" idhash=""0x0A"" title=""A"">
    <MATH equation=""X""><VAR varID=""X"" type=""link"" linkIDHash=""0x0B"" /></MATH>
  </ADXVALUE>
  <ADXVALUE id=""b"" idhash=""0x0B"" title=""B"">
    <MATH equation=""X""><VAR varID=""X"" type=""link"" linkIDHash=""0x0A"" /></MATH>
  </ADXVALUE>
</ADXFORMAT>";

            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(cyclic)))
            {
                Assert.AreEqual(2, AdxDocument.Load(stream).GetEvaluationOrder().Count);
            }
        }

        private static int IndexOf(IReadOnlyList<AdxParameter> order, uint idHash)
        {
            for (int index = 0; index < order.Count; index++)
            {
                if (order[index].IdHash == idHash)
                {
                    return index;
                }
            }

            return -1;
        }

        [TestMethod]
        public void ValueFieldsAreRead()
        {
            AdxParameter? rpm = LoadSample().FindParameter(0x86F3549B);

            Assert.IsNotNull(rpm);
            Assert.AreEqual("Engine RPM", rpm!.Title);
            Assert.AreEqual("RPM", rpm.Units);
            Assert.AreEqual(0, rpm.PacketOffset);
            Assert.AreEqual(16, rpm.SizeInBits);
            Assert.AreEqual(0.0, rpm.RangeLow);
            Assert.AreEqual(9600.0, rpm.RangeHigh);
            Assert.IsTrue(rpm.HasAlarms);
            Assert.AreEqual(5000.0, rpm.AlarmHigh);
            Assert.AreEqual(1, rpm.DigitCount);
            Assert.AreEqual("X*0.25", rpm.Equation);
            Assert.AreEqual(0x55C678F5u, rpm.ParentCommandIdHash);
        }

        /// <summary>
        /// Most entries in a real file leave sizeinbits and digcount out, so the header's defaults
        /// are what most parameters actually use.
        /// </summary>
        [TestMethod]
        public void OmittedWidthAndDigitsComeFromHeaderDefaults()
        {
            AdxParameter? coolant = LoadSample().FindParameter(0x00000002);

            Assert.IsNotNull(coolant);
            Assert.AreEqual(8, coolant!.SizeInBits);
            Assert.AreEqual(2, coolant.DigitCount);
            Assert.AreEqual("X", coolant.Equation);
        }

        [TestMethod]
        public void BitmaskFieldsAreRead()
        {
            AdxParameter? pin = LoadSample().FindParameter(0xDE6B7930);

            Assert.IsNotNull(pin);
            Assert.IsTrue(pin!.IsBitMapped);
            Assert.AreEqual(0x17, pin.PacketOffset);
            Assert.AreEqual("AND", pin.BitOperation);
            Assert.AreEqual(0x20u, pin.BitOperand);
            Assert.AreEqual(0x20u, pin.BitResult);
            Assert.AreEqual("Active", pin.TrueString);
            Assert.AreEqual("Clear", pin.FalseString);
        }

        [TestMethod]
        public void DashboardGaugesAreRead()
        {
            AdxDocument document = LoadSample();

            Assert.AreEqual(1, document.Dashboards.Count);
            AdxDashboard dashboard = document.Dashboards[0];
            Assert.AreEqual("Complete Dash", dashboard.Title);
            Assert.AreEqual(2, dashboard.Gauges.Count);

            AdxGauge round = dashboard.Gauges[0];
            Assert.AreEqual(AdxGaugeType.Round, round.GaugeType);
            Assert.AreEqual(300, round.ArcMax);
            Assert.AreEqual(27, round.Width);
            Assert.AreEqual(35, round.Height);
            Assert.AreEqual(AdxGaugeType.Text, dashboard.Gauges[1].GaugeType);
        }

        [TestMethod]
        public void MonitorSeriesAreRead()
        {
            AdxDocument document = LoadSample();

            Assert.AreEqual(1, document.Monitors.Count);
            Assert.AreEqual("Monitor1", document.Monitors[0].Title);
            Assert.AreEqual(1, document.Monitors[0].Series.Count);
            Assert.AreEqual(0x0000FFu, document.Monitors[0].Series[0].LineColor);
        }

        [TestMethod]
        public void ListViewEntriesAreRead()
        {
            AdxDocument document = LoadSample();

            Assert.AreEqual(1, document.ListViews.Count);
            CollectionAssert.AreEqual(
                new[] { 0x86F3549Bu, 0xDE6B7930u },
                document.ListViews[0].ItemIdHashes.ToArray());
        }

        /// <summary>
        /// The packet definition is the only transport-specific part, and is what a CAN or VPW
        /// source would have to stand in for.
        /// </summary>
        [TestMethod]
        public void ListenPacketIsRead()
        {
            AdxDocument document = LoadSample();

            Assert.AreEqual(1, document.ListenPackets.Count);
            AdxListenPacket packet = document.ListenPackets[0];
            Assert.AreEqual(0x55C678F5u, packet.IdHash);
            Assert.AreEqual(60, packet.PacketBodyLength);
            Assert.AreEqual(3, packet.PacketOffsetInBody);
            Assert.AreEqual(57, packet.PacketSize);
        }

        /// <summary>
        /// Every view entry must resolve to a parameter, or the display has nothing to show.
        /// </summary>
        [TestMethod]
        public void EveryViewEntryResolvesToAParameter()
        {
            AdxDocument document = LoadSample();

            foreach (uint idHash in document.ListViews.SelectMany(v => v.ItemIdHashes))
            {
                Assert.IsNotNull(document.FindParameter(idHash), $"list entry 0x{idHash:X8}");
            }

            foreach (AdxGauge gauge in document.Dashboards.SelectMany(d => d.Gauges))
            {
                Assert.IsNotNull(document.FindParameter(gauge.ItemIdHash), $"gauge 0x{gauge.ItemIdHash:X8}");
            }

            foreach (AdxMonitorSeries series in document.Monitors.SelectMany(m => m.Series))
            {
                Assert.IsNotNull(document.FindParameter(series.ItemIdHash), $"series 0x{series.ItemIdHash:X8}");
            }
        }

        /// <summary>
        /// A real file has blank rows in its list view, written as an item hash of zero. They are
        /// kept in the model so a view keeps its layout, and are for the display to skip.
        /// </summary>
        [TestMethod]
        public void ZeroItemHashResolvesToNothing()
        {
            Assert.IsNull(LoadSample().FindParameter(0));
        }

        [TestMethod]
        public void NonAdxContentIsRejected()
        {
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes("<XDFFORMAT version=\"1.60\" />")))
            {
                Assert.ThrowsException<InvalidDataException>(() => AdxDocument.Load(stream));
            }
        }
    }
}

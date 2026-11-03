// SPDX-License-Identifier: GPL-3.0-only
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace PcmHacking.Tests
{
    /// <summary>
    /// Where a new dashboard gauge goes: taken from a neighbour, or into a gap.
    /// </summary>
    [TestClass]
    public class GaugePlacementTests
    {
        private static GaugeLayout Gauge(double left, double top, double width, double height) =>
            new GaugeLayout { Left = left, Top = top, Width = width, Height = height, Title = "g" };

        /// <summary>Two columns by two rows, edge to edge.</summary>
        private static DashboardLayout Grid()
        {
            DashboardLayout layout = new DashboardLayout();
            layout.Gauges.Add(Gauge(0, 0, 0.5, 0.5));
            layout.Gauges.Add(Gauge(0.5, 0, 0.5, 0.5));
            layout.Gauges.Add(Gauge(0, 0.5, 0.5, 0.5));
            layout.Gauges.Add(Gauge(0.5, 0.5, 0.5, 0.5));
            return layout;
        }

        [TestMethod]
        public void SplitHalvesAWideGaugeSideBySide()
        {
            GaugeLayout gauge = Gauge(0.1, 0.2, 0.4, 0.2);

            GaugeLayout? added = GaugePlacement.Split(gauge, 1.0);

            Assert.IsNotNull(added);
            Assert.AreEqual(0.2, gauge.Width, 1e-9);
            Assert.AreEqual(0.2, gauge.Height, 1e-9);
            Assert.AreEqual(0.3, added!.Left, 1e-9);
            Assert.AreEqual(0.2, added.Top, 1e-9);
            Assert.AreEqual(0.2, added.Width, 1e-9);
            Assert.AreEqual(0.2, added.Height, 1e-9);
        }

        [TestMethod]
        public void SplitHalvesATallGaugeOneAboveTheOther()
        {
            GaugeLayout gauge = Gauge(0.0, 0.0, 0.2, 0.6);

            GaugeLayout? added = GaugePlacement.Split(gauge, 1.0);

            Assert.IsNotNull(added);
            Assert.AreEqual(0.3, gauge.Height, 1e-9);
            Assert.AreEqual(0.3, added!.Top, 1e-9);
            Assert.AreEqual(0.3, added.Height, 1e-9);
            Assert.AreEqual(0.2, added.Width, 1e-9);
        }

        [TestMethod]
        public void SplitJudgesLongerSideOnScreenNotInFractions()
        {
            // 0.3 wide by 0.4 tall looks taller in fractions, but on a dashboard three times wider
            // than it is high the gauge is 0.9 by 0.4 on screen, so it should split side by side.
            GaugeLayout gauge = Gauge(0, 0, 0.3, 0.4);

            GaugeLayout? added = GaugePlacement.Split(gauge, 3.0);

            Assert.IsNotNull(added);
            Assert.AreEqual(0.15, gauge.Width, 1e-9);
            Assert.AreEqual(0.4, gauge.Height, 1e-9);
        }

        [TestMethod]
        public void SplitRefusesWhenTheHalvesWouldBeTooSmall()
        {
            GaugeLayout gauge = Gauge(0, 0, GaugePlacement.MinimumSize, GaugePlacement.MinimumSize);

            Assert.IsNull(GaugePlacement.Split(gauge, 1.0));
            Assert.AreEqual(GaugePlacement.MinimumSize, gauge.Width, 1e-9);
        }

        [TestMethod]
        public void SplitGivesAnUnboundGaugeThatLooksLikeTheOriginal()
        {
            GaugeLayout gauge = Gauge(0, 0, 0.4, 0.2);
            gauge.Kind = GaugeKind.Text;
            gauge.PidId = "EngineSpeed";
            gauge.Digits = 0;
            gauge.NormalColor = 0x00FF00;

            GaugeLayout? added = GaugePlacement.Split(gauge, 1.0);

            Assert.IsNotNull(added);
            Assert.IsFalse(added!.IsBound);
            Assert.AreEqual(GaugeKind.Text, added.Kind);
            Assert.AreEqual(0, added.Digits);
            Assert.AreEqual(0x00FF00, added.NormalColor);
            Assert.IsTrue(added.RangeHigh > added.RangeLow);
        }

        [TestMethod]
        public void DeletingASplitGaugeRestoresTheOriginalSize()
        {
            DashboardLayout layout = new DashboardLayout();
            GaugeLayout original = Gauge(0.2, 0.1, 0.4, 0.3);
            layout.Gauges.Add(original);

            GaugeLayout? added = GaugePlacement.Split(original, 1.0);
            Assert.IsNotNull(added);
            layout.Gauges.Insert(1, added!);

            GaugeLayout? grew = GaugePlacement.Absorb(layout, added!);

            Assert.AreSame(original, grew);
            Assert.AreEqual(0.2, original.Left, 1e-9);
            Assert.AreEqual(0.1, original.Top, 1e-9);
            Assert.AreEqual(0.4, original.Width, 1e-9);
            Assert.AreEqual(0.3, original.Height, 1e-9);
        }

        [TestMethod]
        public void DeletingTheOriginalOfASplitLetsTheNewGaugeGrow()
        {
            DashboardLayout layout = new DashboardLayout();
            GaugeLayout original = Gauge(0.0, 0.0, 0.4, 0.3);
            layout.Gauges.Add(original);
            GaugeLayout added = GaugePlacement.Split(original, 1.0)!;
            layout.Gauges.Insert(1, added);

            GaugeLayout? grew = GaugePlacement.Absorb(layout, original);

            Assert.AreSame(added, grew);
            Assert.AreEqual(0.0, added.Left, 1e-9);
            Assert.AreEqual(0.4, added.Width, 1e-9);
        }

        [TestMethod]
        public void DeletingTheOriginalGivesTheSpaceToItsSplitPartnerNotTheGaugeBeforeIt()
        {
            // A full-height gauge beside a cell that was split in two: the gauge before the
            // deleted one shares a whole edge with it too, but the half-sized partner should win.
            DashboardLayout layout = new DashboardLayout();
            GaugeLayout before = Gauge(0.0, 0.0, 0.3, 0.3);
            GaugeLayout original = Gauge(0.3, 0.0, 0.3, 0.3);
            layout.Gauges.Add(before);
            layout.Gauges.Add(original);
            GaugeLayout partner = GaugePlacement.Split(original, 1.0)!;
            layout.Gauges.Insert(2, partner);

            GaugeLayout? grew = GaugePlacement.Absorb(layout, original);

            Assert.AreSame(partner, grew);
            Assert.AreEqual(0.3, partner.Left, 1e-9);
            Assert.AreEqual(0.3, partner.Width, 1e-9);
            Assert.AreEqual(0.3, before.Width, 1e-9);
        }

        [TestMethod]
        public void DeletingAGridCellGivesItToTheGaugeBeforeIt()
        {
            DashboardLayout layout = Grid();
            GaugeLayout removed = layout.Gauges[1]; // top right; the gauge before it is top left

            GaugeLayout? grew = GaugePlacement.Absorb(layout, removed);

            Assert.AreSame(layout.Gauges[0], grew);
            Assert.AreEqual(0.0, grew!.Left, 1e-9);
            Assert.AreEqual(1.0, grew.Width, 1e-9);
            Assert.AreEqual(0.5, grew.Height, 1e-9);
        }

        [TestMethod]
        public void AbsorbLeavesAGapWhenNoNeighbourSharesAWholeEdge()
        {
            DashboardLayout layout = new DashboardLayout();
            GaugeLayout removed = Gauge(0.3, 0.3, 0.3, 0.3);
            layout.Gauges.Add(Gauge(0.0, 0.0, 0.3, 0.5)); // touches only part of its left side
            layout.Gauges.Add(removed);

            Assert.IsNull(GaugePlacement.Absorb(layout, removed));
            Assert.AreEqual(0.3, layout.Gauges[0].Width, 1e-9);
        }

        [TestMethod]
        public void FillFreeSpaceReturnsTheCellLeftByADeletedGauge()
        {
            DashboardLayout layout = Grid();
            layout.Gauges.RemoveAt(1); // top right

            GaugeLayout? added = GaugePlacement.FillFreeSpace(layout, 0.75, 0.25);

            Assert.IsNotNull(added);
            Assert.AreEqual(0.5, added!.Left, 1e-9);
            Assert.AreEqual(0.0, added.Top, 1e-9);
            Assert.AreEqual(0.5, added.Width, 1e-9);
            Assert.AreEqual(0.5, added.Height, 1e-9);
            Assert.IsFalse(added.IsBound);
        }

        [TestMethod]
        public void FillFreeSpaceSpansAWholeRowWhenSeveralAreDeleted()
        {
            DashboardLayout layout = Grid();
            layout.Gauges.RemoveRange(0, 2); // the whole top row

            GaugeLayout? added = GaugePlacement.FillFreeSpace(layout, 0.1, 0.1);

            Assert.IsNotNull(added);
            Assert.AreEqual(0.0, added!.Left, 1e-9);
            Assert.AreEqual(1.0, added.Width, 1e-9);
            Assert.AreEqual(0.0, added.Top, 1e-9);
            Assert.AreEqual(0.5, added.Height, 1e-9);
        }

        [TestMethod]
        public void FillFreeSpaceIsNullOnAGauge()
        {
            Assert.IsNull(GaugePlacement.FillFreeSpace(Grid(), 0.25, 0.25));
        }

        [TestMethod]
        public void FillFreeSpaceIsNullWhenTheGapIsTooSmall()
        {
            DashboardLayout layout = new DashboardLayout();
            layout.Gauges.Add(Gauge(0.0, 0.0, 0.48, 1.0));
            layout.Gauges.Add(Gauge(0.52, 0.0, 0.48, 1.0));

            Assert.IsNull(GaugePlacement.FillFreeSpace(layout, 0.5, 0.5));
        }

        [TestMethod]
        public void FillFreeSpaceNeverOverlapsAnExistingGauge()
        {
            DashboardLayout layout = Grid();
            layout.Gauges.RemoveAt(0);
            layout.Gauges.RemoveAt(2); // leaves top right and bottom left

            GaugeLayout? added = GaugePlacement.FillFreeSpace(layout, 0.25, 0.25);

            Assert.IsNotNull(added);
            foreach (GaugeLayout other in layout.Gauges)
            {
                bool overlaps = added!.Left < other.Left + other.Width - 1e-9
                    && added.Left + added.Width > other.Left + 1e-9
                    && added.Top < other.Top + other.Height - 1e-9
                    && added.Top + added.Height > other.Top + 1e-9;
                Assert.IsFalse(overlaps, "The new gauge must not cover " + other.Left + "," + other.Top);
            }
        }
    }
}

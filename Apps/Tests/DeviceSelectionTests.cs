// SPDX-License-Identifier: GPL-3.0-only
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace Tests
{
    /// <summary>
    /// The rules a device slot follows: what counts as selected, what is complete enough to open,
    /// and when two slots clash.
    /// </summary>
    [TestClass]
    public class DeviceSelectionTests
    {
        private static DeviceSelection Serial(string port, string type = "OBDX") =>
            new DeviceSelection(DeviceConstants.DeviceCategorySerial, string.Empty, port, type);

        private static DeviceSelection J2534(string name) =>
            new DeviceSelection(DeviceConstants.DeviceCategoryJ2534, name, string.Empty, string.Empty);

        [TestMethod]
        public void UnsetCategoryMeansNone()
        {
            Assert.IsFalse(new DeviceSelection(null, null, null, null).IsSelected, "null");
            Assert.IsFalse(new DeviceSelection("", "", "", "").IsSelected, "empty");
            Assert.IsFalse(DeviceSelection.None.IsSelected, "None");

            // First run and a device that has gone away both land here, so it has to be the default
            // rather than an error state.
            Assert.AreEqual(
                DeviceConstants.DeviceCategoryNone,
                new DeviceSelection(null, null, null, null).Category);
        }

        [TestMethod]
        public void UsableRequiresEnoughToIdentifyTheHardware()
        {
            Assert.IsTrue(Serial("COM3").IsUsable, "serial with port and type");
            Assert.IsFalse(Serial(string.Empty).IsUsable, "serial with no port");
            Assert.IsFalse(Serial("COM3", string.Empty).IsUsable, "serial with no device type");

            Assert.IsTrue(J2534("Tactrix").IsUsable, "j2534 with a device");
            Assert.IsFalse(J2534(string.Empty).IsUsable, "j2534 with no device");

            Assert.IsFalse(DeviceSelection.None.IsUsable, "none");
        }

        [TestMethod]
        public void TheSameInterfaceInTwoSlotsIsAConflict()
        {
            Assert.IsTrue(Serial("COM3").ConflictsWith(Serial("COM3")), "same port");
            Assert.IsTrue(J2534("Tactrix").ConflictsWith(J2534("Tactrix")), "same j2534 device");
        }

        [TestMethod]
        public void DifferentInterfacesDoNotConflict()
        {
            Assert.IsFalse(Serial("COM3").ConflictsWith(Serial("COM4")), "different ports");
            Assert.IsFalse(Serial("COM3").ConflictsWith(J2534("Tactrix")), "different categories");
            Assert.IsFalse(J2534("Tactrix").ConflictsWith(J2534("Mongoose")), "different j2534 devices");
        }

        /// <summary>Two empty slots are not a clash; there is nothing to clash over.</summary>
        [TestMethod]
        public void EmptySlotsNeverConflict()
        {
            Assert.IsFalse(DeviceSelection.None.ConflictsWith(DeviceSelection.None), "none vs none");
            Assert.IsFalse(DeviceSelection.None.ConflictsWith(Serial("COM3")), "none vs serial");
            Assert.IsFalse(Serial("COM3").ConflictsWith(DeviceSelection.None), "serial vs none");
            Assert.IsFalse(Serial("COM3").ConflictsWith(null), "serial vs nothing");
        }

        [TestMethod]
        public void ClaimingAnInterfaceTwiceIsRefusedUntilItIsReleased()
        {
            DeviceSelection selection = Serial("COM9");

            IDisposable? first = DeviceRegistry.TryClaim(selection);
            Assert.IsNotNull(first, "first claim");
            Assert.IsTrue(DeviceRegistry.IsInUse(selection), "in use after claiming");

            // An equal selection, not the same object: the hardware does not care which object
            // opened it.
            Assert.IsNull(DeviceRegistry.TryClaim(Serial("COM9")), "second claim refused");

            first!.Dispose();

            Assert.IsFalse(DeviceRegistry.IsInUse(selection), "released");

            IDisposable? third = DeviceRegistry.TryClaim(selection);
            Assert.IsNotNull(third, "claimable again");
            third!.Dispose();
        }

        [TestMethod]
        public void ClaimingNothingAlwaysSucceeds()
        {
            using (IDisposable? claim = DeviceRegistry.TryClaim(DeviceSelection.None))
            {
                Assert.IsNotNull(claim, "none is claimable");
                Assert.IsFalse(DeviceRegistry.IsInUse(DeviceSelection.None), "but reserves nothing");

                // So a second empty slot is not blocked by the first.
                using (IDisposable? second = DeviceRegistry.TryClaim(DeviceSelection.None))
                {
                    Assert.IsNotNull(second, "a second empty slot is fine");
                }
            }
        }
    }
}

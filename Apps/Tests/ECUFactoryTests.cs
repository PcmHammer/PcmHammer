using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;
using PcmHacking.ECU;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tests
{
    [TestClass]
    public class ECUFactoryTests
    {
        [TestMethod]
        public void Can_Modify_Flag_And_Get_New_Controller()
        {
            ECUBase firstP01 = ECUFactory.GetControllerByOSID(12212156);
            firstP01.IsSupported = false;
            ECUBase secondP01 = ECUFactory.GetControllerByOSID(12212156);
            Assert.IsTrue(secondP01.IsSupported);
        }

        [TestMethod]
        public void Can_Set_CurrentOS_Object()
        {
            ECUBase p01 = ECUFactory.GetControllerByOSID(12212156);
            Assert.IsNotNull(p01);
            Assert.AreEqual(p01.GetCurrentOSID(), 12212156u);
        }

        [TestMethod]
        public async Task Validate_OS_Lookups()
        {
            uint p01_id = 12212156;
            uint p59_id = 12619623;
            uint p04_id = 9382915;
            uint p04E_id = 16233470;
            uint p04E_512k_id = 24234036;
            uint p05_id = 12603217;
            uint p08_id = 16267097;
            uint p10_id = 12623317;
            uint p11_id = 12586586;
            uint p12_id = 12627883;
            uint p12_2M_id = 12613422;
            uint blackbox_id = 16265175;
            uint E54_id = 15186006;
            uint E60_id = 15087230;
            uint UnsupportedLS = 16238242;
            uint UnknownOS = 55555555;
            uint hpt_p01 = 1273003;
            uint hpt_p59 = 1273053;

            uint[] ECUs = [p01_id, p59_id, p04_id, p04E_id, p04E_512k_id, p05_id, p08_id, p10_id, p11_id, p12_id, p12_2M_id, blackbox_id, E54_id, E60_id, hpt_p01, hpt_p59, UnsupportedLS, UnknownOS];

            foreach (uint s in ECUs)
            {
                ECUBase a = ECUFactory.GetControllerByOSID(s);
                OSIDInfo b = new OSIDInfo(s);

                Assert.IsNotNull(a);
                Assert.IsNotNull(b);
                if (s == p01_id || s == hpt_p01)
                {
                    Assert.IsTrue(b.HardwareType == PcmTypeOld.P01);
                    Assert.IsTrue(a.BaseHardwareType == PcmType.P01);
                    Assert.IsTrue(a.HardwareType == PcmType.P01);
                }
                else if (s == p59_id || s == hpt_p59)
                {
                    Assert.IsTrue(b.HardwareType == PcmTypeOld.P59);
                    Assert.IsTrue(a.BaseHardwareType == PcmType.P01);
                    Assert.IsTrue(a.HardwareType == PcmType.P59);
                }
                else if (s == p04E_512k_id)
                {
                    Assert.IsTrue(b.HardwareType == PcmTypeOld.P04_Early);
                    Assert.IsTrue(a.BaseHardwareType == PcmType.P04_Early);
                    Assert.IsTrue(a.HardwareType == PcmType.P04_Early_512k);
                }
                else
                {
                    Debug.WriteLineIf(Enum.GetName(typeof(PcmType), a.BaseHardwareType) != Enum.GetName(typeof(PcmTypeOld), b.HardwareType), $"Assert failure on ECUBase type: {a.HardwareType}; Base: {a.BaseHardwareType}");
                    Assert.IsTrue(Enum.GetName(typeof(PcmType), a.BaseHardwareType) == Enum.GetName(typeof(PcmTypeOld), b.HardwareType));
                }
                Assert.IsTrue(a.GetCurrentOSID() == b.OSID);
                Assert.IsTrue(a.ChecksumSupport == b.ChecksumSupport);
                Assert.IsTrue(a.KeyAlgorithm == b.KeyAlgorithm);
                Assert.IsTrue(a.FlashCRCSupport == b.FlashCRCSupport);
                Assert.IsTrue(a.FlashIDSupport == b.FlashIDSupport);
                Assert.IsTrue(a.HardwareSlaveCPU == b.HardwareSlaveCPU);
                Assert.IsTrue(a.ImageBaseAddress == b.ImageBaseAddress);
                Assert.IsTrue(a.ImageSize == b.ImageSize);
                Assert.IsTrue(a.IsSupported == b.IsSupported);
                Assert.IsTrue(a.IsSupportedRead == b.IsSupportedRead);
                Assert.IsTrue(a.IsSupportedWrite == b.IsSupportedWrite);
                Assert.IsTrue(a.IsSupportedWriteBootSector == b.IsSupportedWriteBootSector);
                Assert.IsTrue(a.IsSupportedWriteBySegment == b.IsSupportedWriteBySegment);
                Assert.IsTrue(a.IsSupportedWriteSlaveCPU == b.IsSupportedWriteSlaveCPU);
                Assert.IsTrue(a.IsUnderDevelopment == b.IsUnderDevelopment);
                Assert.IsTrue(a.KernelBaseAddress == b.KernelBaseAddress);
                Assert.IsTrue(a.KernelFileName == b.KernelFileName);
                Assert.IsTrue(a.KernelMaxBlockSize == b.KernelMaxBlockSize);
                Assert.IsTrue(a.KernelVersionSupport == b.KernelVersionSupport);
                Assert.IsTrue(a.LoaderBaseAddress == b.LoaderBaseAddress);
                Assert.IsTrue(a.LoaderFileName == b.LoaderFileName);
                Assert.IsTrue(a.LoaderRequired == b.LoaderRequired);
            }

            foreach (ECUBase ecu in ECUFactory.StoredECUs)
            {
                if (ecu.HardwareType <= PcmType.Unsupported)
                {
                    continue;
                }
                PcmType type = ecu.BaseHardwareType;
                ECUBase testObj = ECUFactory.GetControllerOverride(type, 0);
                Assert.IsNotNull(testObj);
                Assert.IsTrue(ecu.BaseHardwareType == testObj.BaseHardwareType);
                if (Enum.TryParse(Enum.GetName(typeof(PcmType), type), true, out PcmTypeOld oldType))
                {
                    OSIDInfo crossTranslateTest = new OSIDInfo(oldType);
                    Assert.IsNotNull(crossTranslateTest);
                    Assert.IsTrue(Enum.GetName(typeof(PcmType), ecu.BaseHardwareType) == Enum.GetName(typeof(PcmTypeOld), crossTranslateTest.HardwareType));
                }
            }
            List<Task> activeTasks = new List<Task>();
            for (uint loopIndex = 100000; loopIndex < 20000000; loopIndex++)
            {
                OSIDInfo oldInfo = new OSIDInfo(loopIndex);
                if (oldInfo.HardwareType != PcmTypeOld.Undefined)
                {
                    ECUBase newInfo = ECUFactory.GetControllerByOSID(loopIndex);
                    bool test = Enum.GetName(typeof(PcmType), newInfo.HardwareType) == Enum.GetName(typeof(PcmTypeOld), oldInfo.HardwareType) || Enum.GetName(typeof(PcmType), newInfo.BaseHardwareType) == Enum.GetName(typeof(PcmTypeOld), oldInfo.HardwareType);
                    if (newInfo.IsCustomOS)
                    {
                        test = true;
                    }
                    Debug.WriteLineIf(!test, $"PCMInfo: ({oldInfo.HardwareType}) new(\"GM\", {loopIndex}, {oldInfo.ServiceNumber}, {oldInfo.KeyAlgorithm}), NewInfo: {newInfo.HardwareType}");
                    //Assert.IsTrue(test);
                }
            }
            Debug.WriteLine("All tasks have finished. Done!");
        }
    }
}

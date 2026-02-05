using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace PcmHammer.Tests
{
    [TestClass]
    public class FileIntegrityTests
    {
        private static string GetTestBinPath(string fileName)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "TestData", fileName);
        }

        [TestMethod]
        public void FileIntegrity_P01_DetectsType()
        {
            string path = GetTestBinPath("P01_12225074.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            OSIDInfo info = new OSIDInfo(osid);

            Assert.AreEqual(PcmType.P01_P59, info.HardwareType);
        }

        [TestMethod]
        public void FileIntegrity_P01_DetectsOsid_12225074()
        {
            string path = GetTestBinPath("P01_12225074.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            Assert.AreEqual((uint)12225074, osid);
        }

        [TestMethod]
        public void FileIntegrity_P01_CrcChecks_Pass()
        {
            string path = GetTestBinPath("P01_12225074.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            Assert.IsTrue(validator.IsValid());
        }

        [TestMethod]
        public void FileIntegrity_P04_Early_DetectsType()
        {
            string path = GetTestBinPath("P04_Early_16238517.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            OSIDInfo info = new OSIDInfo(osid);

            Assert.AreEqual(PcmType.P04_Early, info.HardwareType);
        }

        [TestMethod]
        public void FileIntegrity_P04_Early_DetectsOsid_16238517()
        {
            string path = GetTestBinPath("P04_Early_16238517.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            Assert.AreEqual((uint)16238517, osid);
        }

        [TestMethod]
        public void FileIntegrity_P04_Early_CrcChecks_Pass()
        {
            string path = GetTestBinPath("P04_Early_16238517.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            Assert.IsTrue(validator.IsValid());
        }

        [TestMethod]
        public void FileIntegrity_P04_DetectsType()
        {
            string path = GetTestBinPath("P04_12214427.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            OSIDInfo info = new OSIDInfo(osid);

            Assert.AreEqual(PcmType.P04, info.HardwareType);
        }

        [TestMethod]
        public void FileIntegrity_P04_DetectsOsid_12214427()
        {
            string path = GetTestBinPath("P04_12214427.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            Assert.AreEqual((uint)12214427, osid);
        }

        [TestMethod]
        public void FileIntegrity_P04_CrcChecks_Pass()
        {
            string path = GetTestBinPath("P04_12214427.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            Assert.IsTrue(validator.IsValid());
        }

        [TestMethod]
        public void FileIntegrity_P05_DetectsType()
        {
            string path = GetTestBinPath("P05_12584057.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            OSIDInfo info = new OSIDInfo(osid);

            Assert.AreEqual(PcmType.P05, info.HardwareType);
        }

        [TestMethod]
        public void FileIntegrity_P05_DetectsOsid_12584057()
        {
            string path = GetTestBinPath("P05_12584057.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            Assert.AreEqual((uint)12584057, osid);
        }

        [TestMethod]
        public void FileIntegrity_P05_CrcChecks_Pass()
        {
            string path = GetTestBinPath("P05_12584057.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            Assert.IsTrue(validator.IsValid());
        }

        [TestMethod]
        public void FileIntegrity_P05_NoParamBlock_DetectsType()
        {
            string path = GetTestBinPath("P05_12612937_NoParamBlock.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            OSIDInfo info = new OSIDInfo(osid);

            Assert.AreEqual(PcmType.P05, info.HardwareType);
        }

        [TestMethod]
        public void FileIntegrity_P05_NoParamBlock_DetectsOsid_12612937()
        {
            string path = GetTestBinPath("P05_12612937_NoParamBlock.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            Assert.AreEqual((uint)12612937, osid);
        }

        [TestMethod]
        public void FileIntegrity_P05_NoParamBlock_CrcChecks_Fail()
        {
            string path = GetTestBinPath("P05_12612937_NoParamBlock.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            Assert.IsFalse(validator.IsValid());
        }

        [TestMethod]
        public void FileIntegrity_P08_DetectsType()
        {
            string path = GetTestBinPath("P08_12206029.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            OSIDInfo info = new OSIDInfo(osid);

            Assert.AreEqual(PcmType.P08, info.HardwareType);
        }

        [TestMethod]
        public void FileIntegrity_P08_DetectsOsid_12206029()
        {
            string path = GetTestBinPath("P08_12206029.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            Assert.AreEqual((uint)12206029, osid);
        }

        [TestMethod]
        public void FileIntegrity_P08_CrcChecks_Pass()
        {
            string path = GetTestBinPath("P08_12206029.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            Assert.IsTrue(validator.IsValid());
        }

        [TestMethod]
        public void FileIntegrity_P10_DetectsType()
        {
            string path = GetTestBinPath("P10_12597031.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            OSIDInfo info = new OSIDInfo(osid);

            Assert.AreEqual(PcmType.P10, info.HardwareType);
        }

        [TestMethod]
        public void FileIntegrity_P10_DetectsOsid_12597031()
        {
            string path = GetTestBinPath("P10_12597031.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            Assert.AreEqual((uint)12597031, osid);
        }

        [TestMethod]
        public void FileIntegrity_P10_CrcChecks_Pass()
        {
            string path = GetTestBinPath("P10_12597031.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            Assert.IsTrue(validator.IsValid());
        }

        [TestMethod]
        public void FileIntegrity_P11_DetectsType()
        {
            string path = GetTestBinPath("P11_12586586.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            OSIDInfo info = new OSIDInfo(osid);

            Assert.AreEqual(PcmType.P11, info.HardwareType);
        }

        [TestMethod]
        public void FileIntegrity_P11_DetectsOsid_12586586()
        {
            string path = GetTestBinPath("P11_12586586.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            Assert.AreEqual((uint)12586586, osid);
        }

        [TestMethod]
        public void FileIntegrity_P11_CrcChecks_Pass()
        {
            string path = GetTestBinPath("P11_12586586.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            Assert.IsTrue(validator.IsValid());
        }

        [TestMethod]
        public void FileIntegrity_P12_DetectsType()
        {
            string path = GetTestBinPath("P12_12627883.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            OSIDInfo info = new OSIDInfo(osid);

            Assert.AreEqual(PcmType.P12, info.HardwareType);
        }

        [TestMethod]
        public void FileIntegrity_P12_DetectsOsid_12627883()
        {
            string path = GetTestBinPath("P12_12627883.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            Assert.AreEqual((uint)12627883, osid);
        }

        [TestMethod]
        public void FileIntegrity_P12_CrcChecks_Pass()
        {
            string path = GetTestBinPath("P12_12627883.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            Assert.IsTrue(validator.IsValid());
        }

        [TestMethod]
        public void FileIntegrity_P12b_DetectsType()
        {
            string path = GetTestBinPath("P12b_12613422.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            OSIDInfo info = new OSIDInfo(osid);

            Assert.AreEqual(PcmType.P12, info.HardwareType);
        }

        [TestMethod]
        public void FileIntegrity_P12b_DetectsOsid_12613422()
        {
            string path = GetTestBinPath("P12b_12613422.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            Assert.AreEqual((uint)12613422, osid);
        }

        [TestMethod]
        public void FileIntegrity_P12b_CrcChecks_Pass()
        {
            string path = GetTestBinPath("P12b_12613422.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            Assert.IsTrue(validator.IsValid());
        }

        [TestMethod]
        public void FileIntegrity_P59_DetectsType()
        {
            string path = GetTestBinPath("P59_12592433.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            OSIDInfo info = new OSIDInfo(osid);

            Assert.AreEqual(PcmType.P01_P59, info.HardwareType);
        }

        [TestMethod]
        public void FileIntegrity_P59_DetectsOsid_12592433()
        {
            string path = GetTestBinPath("P59_12592433.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            Assert.AreEqual((uint)12592433, osid);
        }

        [TestMethod]
        public void FileIntegrity_P59_CrcChecks_Pass()
        {
            string path = GetTestBinPath("P59_12592433.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            Assert.IsTrue(validator.IsValid());
        }

        [TestMethod]
        public void FileIntegrity_E54_DetectsType()
        {
            string path = GetTestBinPath("E54_15189044.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            OSIDInfo info = new OSIDInfo(osid);

            Assert.AreEqual(PcmType.E54, info.HardwareType);
        }

        [TestMethod]
        public void FileIntegrity_E54_DetectsOsid_15189044()
        {
            string path = GetTestBinPath("E54_15189044.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            Assert.AreEqual((uint)15189044, osid);
        }

        [TestMethod]
        public void FileIntegrity_E54_CrcChecks_Pass()
        {
            string path = GetTestBinPath("E54_15189044.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            Assert.IsTrue(validator.IsValid());
        }

        [TestMethod]
        public void FileIntegrity_BlackBox_DetectsType()
        {
            string path = GetTestBinPath("BlackBox_9365085.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            OSIDInfo info = new OSIDInfo(osid);

            Assert.AreEqual(PcmType.BlackBox, info.HardwareType);
        }

        [TestMethod]
        public void FileIntegrity_BlackBox_DetectsOsid_9365085()
        {
            string path = GetTestBinPath("BlackBox_9365085.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            uint osid = validator.GetOsidFromImage();
            Assert.AreEqual((uint)9365085, osid);
        }

        [TestMethod]
        public void FileIntegrity_BlackBox_CrcChecks_Pass()
        {
            string path = GetTestBinPath("BlackBox_9365085.bin");
            Assert.IsTrue(File.Exists(path), $"Missing test bin at {path}");

            byte[] image = File.ReadAllBytes(path);
            TestLogger logger = new TestLogger();
            FileValidator validator = new FileValidator(image, logger);

            Assert.IsTrue(validator.IsValid());
        }
    }
}

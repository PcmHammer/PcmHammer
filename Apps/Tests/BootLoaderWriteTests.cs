// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace Tests
{
    /// <summary>
    /// The boot loader download: how a master image is split into flash modules, and the message
    /// sequence those modules turn into. The E92 expectations come from captured vendor writes, the E38
    /// ones lock in the sequence that is already known to work on hardware.
    /// </summary>
    [TestClass]
    public class BootLoaderWriteTests
    {
        // The E92 flash segments, as the pointer table in a known-good 4 MiB image describes them.
        private static readonly (string Name, int Start, int End)[] E92Segments =
        {
            ("OS",         0x0C0000, 0x3FFFFF),
            ("System",     0x040000, 0x042FFF),
            ("Fuel",       0x043000, 0x047FFF),
            ("Speedo",     0x048000, 0x049FFF),
            ("EngineDiag", 0x04A000, 0x05FEFF),
            ("Engine",     0x05FF00, 0x0BFFFF),
        };

        // The OS segment's header record sits 0x100 in, and the module leads with 0x800 bytes of it.
        private const int E92OsHeaderOffset = 0x100;

        [TestMethod]
        public void E92_IsWrittenThroughItsBootLoader()
        {
            var e92 = new OSIDInfo(PcmType.E92);

            Assert.IsTrue(e92.IsSupportedWrite, "Write must be offered, or the UI greys it out.");
            Assert.IsTrue(e92.IsSupportedWriteBySegment, "The boot loader programs one segment per module.");
            Assert.IsTrue(e92.IsSupportedWriteSlaveCPU, "The boot loader reaches the slave CPU.");
            Assert.IsTrue(e92.IsSupportedBootLoaderWrite);
            Assert.IsTrue(e92.IsUnderDevelopment, "Untested on hardware: the brick-risk prompt must still appear.");
            Assert.IsFalse(e92.HasParameterBlocks, "There is no parameter block in E92 flash to write.");

            // The kernel reads and CRCs but cannot program, so a destructive write must never reach the
            // kernel writer. The E38 has a write kernel and uses the boot loader only for its slave.
            Assert.IsTrue(e92.RequiresBootLoaderWrite);
            Assert.IsFalse(new OSIDInfo(PcmType.E38).RequiresBootLoaderWrite);
        }

        /// <summary>
        /// A separately writable parameter block is the reason segment write exists, so the two agree
        /// unless a PCM states otherwise.
        /// </summary>
        [TestMethod]
        public void ParameterBlocksFollowSegmentWrite_ExceptOnTheE92()
        {
            foreach (PcmType type in Enum.GetValues(typeof(PcmType)))
            {
                var info = new OSIDInfo(type);
                if (type == PcmType.E92)
                {
                    Assert.IsTrue(info.IsSupportedWriteBySegment, "E92 writes by segment.");
                    Assert.IsFalse(info.HasParameterBlocks, "E92 has no parameter block in flash.");
                    continue;
                }

                Assert.AreEqual(
                    info.IsSupportedWriteBySegment, info.HasParameterBlocks,
                    type + ": parameter blocks and segment write must agree.");
            }

            // Spot checks of the two sides of the rule, so a wholesale flip cannot pass the loop above.
            Assert.IsTrue(new OSIDInfo(PcmType.P01).HasParameterBlocks, "P01 writes by segment.");
            Assert.IsFalse(new OSIDInfo(PcmType.E38).HasParameterBlocks, "The E38 is clone only.");
        }

        [TestMethod]
        public void E92_Segments_MatchThePointerTable()
        {
            List<FlashSegment> segments = FileValidator.GetE92MasterSegments(LoadE92Image());

            Assert.AreEqual(E92Segments.Length, segments.Count);
            for (int i = 0; i < segments.Count; i++)
            {
                Assert.AreEqual(E92Segments[i].Name, segments[i].Name);
                Assert.AreEqual(E92Segments[i].Start, segments[i].Start, segments[i].Name + " start");
                Assert.AreEqual(E92Segments[i].End, segments[i].End, segments[i].Name + " end");
            }

            Assert.AreEqual(E92OsHeaderOffset, segments[0].HeaderOffset, "OS header offset");
            Assert.IsTrue(segments.Skip(1).All(s => s.HeaderOffset == 0), "Calibration segments lead with their header.");
        }

        [TestMethod]
        public void E92_OsModule_IsTheSegmentLedByACopyOfItsHeader()
        {
            byte[] image = LoadE92Image();
            var pcmInfo = new OSIDInfo(PcmType.E92);
            FlashModule os = FlashModuleBuilder.Build(image, pcmInfo)[0];

            Assert.AreEqual("OS", os.Name);
            Assert.AreEqual(pcmInfo.BootLoaderMasterHeaderLength, os.HeaderLength);
            Assert.AreEqual(Gmlan.DataFormatUncompressed, os.DataFormat);

            // 0x800 header + the whole segment: the size the vendor's own OS module files have.
            Assert.AreEqual(0x800 + 0x340000, os.Data.Length);
            CollectionAssert.AreEqual(
                Slice(image, 0x0C0000 + E92OsHeaderOffset, 0x800), os.Data.Take(0x800).ToArray(), "OS module header");
            CollectionAssert.AreEqual(
                Slice(image, 0x0C0000, 0x340000), os.Data.Skip(0x800).ToArray(), "OS module body");
        }

        [TestMethod]
        public void E92_CalibrationModules_AreCompressedAndDecodeBackToTheSegment()
        {
            byte[] image = LoadE92Image();
            List<FlashModule> modules = FlashModuleBuilder.Build(image, new OSIDInfo(PcmType.E92));

            Assert.AreEqual(E92Segments.Length, modules.Count);
            for (int i = 1; i < modules.Count; i++)
            {
                FlashModule module = modules[i];
                (string name, int start, int end) = E92Segments[i];

                Assert.AreEqual(name, module.Name);
                Assert.AreEqual(0, module.HeaderLength, name + " carries no header");
                Assert.AreEqual(Gmlan.DataFormatCompressed, module.DataFormat, name + " data format");
                Assert.IsTrue(E92ModuleCodec.IsWrapped(module.Data), name + " is wrapped");
                Assert.IsTrue(module.Data.Length < end - start + 1, name + " is smaller than the segment");

                CollectionAssert.AreEqual(
                    Slice(image, start, end - start + 1),
                    E92ModuleCodec.Decompress(E92ModuleCodec.Unwrap(module.Data)),
                    name + " decodes back to the segment");
            }

            // Capture fidelity: the vendor's own System module for this image is 5541 bytes. The coding
            // is not unique, so a change here is not automatically wrong - but it is worth knowing.
            Assert.AreEqual(5541, modules[1].Data.Length, "System module size differs from the captured vendor write.");
        }

        [TestMethod]
        public void E92_CalibrationWrite_LeavesTheOsAlone()
        {
            List<FlashModule> modules = FlashModuleBuilder.Build(
                LoadE92Image(), new OSIDInfo(PcmType.E92), includeOperatingSystem: false);

            Assert.AreEqual(E92Segments.Length - 1, modules.Count);
            Assert.IsFalse(modules.Any(m => m.Name == "OS"));
            Assert.IsTrue(modules.All(m => m.DataFormat == Gmlan.DataFormatCompressed));
        }

        [TestMethod]
        public void E92_Phases_MatchTheCapturedVendorWrite()
        {
            var pcmInfo = new OSIDInfo(PcmType.E92);
            byte[] library = Filler(17952);
            List<FlashModule> master = FlashModuleBuilder.Build(LoadE92Image(), pcmInfo);
            List<FlashModule> slave = FlashModuleBuilder.BuildSlaveModules(
                new[] { Filler(11392), E92ModuleCodec.Wrap(E92ModuleCodec.Compress(Filler(512))) }, pcmInfo);

            List<CanBootLoaderWriter.DownloadPhase> phases =
                CanBootLoaderWriter.BuildPhases(pcmInfo, library, master, null, slave);

            // No SRAM parameter mirror on this PCM, so the download opens with the flash routines.
            Assert.AreEqual(4, phases.Count);

            List<byte[]> upload = Bytes(phases[0]);
            CollectionAssert.AreEqual(new byte[] { 0x34, 0x00, 0x00, 0x46, 0x20 }, upload[0], "RequestDownload for the flash routines");
            Assert.AreEqual(0x40000400u, AddressOf(upload[1]));
            Assert.AreEqual(0x400013F8u, AddressOf(upload[2]), "The flash routines stream to a rising address.");

            // The master burn needs no handshake: it opens with the OS module's RequestDownload.
            List<byte[]> masterMessages = Bytes(phases[1]);
            CollectionAssert.AreEqual(new byte[] { 0x34, 0x00, 0x0F, 0xFE }, masterMessages[0], "OS module is streamed as-is");
            Assert.AreEqual(0x800, DataLengthOf(masterMessages[1]), "The OS header is a message of its own.");
            Assert.AreEqual(0x40007000u, AddressOf(masterMessages[1]));
            Assert.AreEqual(0x40007000u, AddressOf(masterMessages[2]), "Every block stages at one address.");

            byte[][] calibrationDownloads = masterMessages
                .Where(m => m[0] == 0x34)
                .Skip(1)
                .ToArray();
            Assert.AreEqual(5, calibrationDownloads.Length);
            foreach (byte[] request in calibrationDownloads)
            {
                CollectionAssert.AreEqual(new byte[] { 0x34, 0x10, 0x0F, 0xFE }, request, "Calibration modules are compressed");
            }

            // The slave answers for both its modules, then the modules stream; there is no driver upload.
            List<byte[]> slaveMessages = Bytes(phases[2]);
            CollectionAssert.AreEqual(new byte[] { 0x1A, 0xC9 }, slaveMessages[0]);
            CollectionAssert.AreEqual(new byte[] { 0x1A, 0xCA }, slaveMessages[1]);
            CollectionAssert.AreEqual(new byte[] { 0x34, 0x00, 0x0F, 0xFE }, slaveMessages[2], "Slave OS is streamed as-is");
            Assert.AreEqual(0x80, DataLengthOf(slaveMessages[3]), "The slave OS header is a message of its own.");
            Assert.IsTrue(
                slaveMessages.Any(m => m.Length == 4 && m[0] == 0x34 && m[1] == 0x10),
                "The slave calibration is compressed.");

            AssertFinalize(phases[3]);
        }

        [TestMethod]
        public void E92_ModuleBlocks_NeverExceedTheBootLoaderBlockSize()
        {
            var pcmInfo = new OSIDInfo(PcmType.E92);
            List<CanBootLoaderWriter.DownloadPhase> phases = CanBootLoaderWriter.BuildPhases(
                pcmInfo, Filler(17952), FlashModuleBuilder.Build(LoadE92Image(), pcmInfo), null, null);

            foreach (CanBootLoaderWriter.DownloadPhase phase in phases)
            {
                foreach (byte[] message in Bytes(phase))
                {
                    Assert.IsTrue(
                        message.Length <= pcmInfo.BootLoaderBlockSize,
                        "Message of " + message.Length + " bytes exceeds the block size.");
                }
            }
        }

        [TestMethod]
        public void E38_Phases_AreUnchanged()
        {
            var pcmInfo = new OSIDInfo(PcmType.E38);
            var master = new List<FlashModule>
            {
                new FlashModule("Operating System", Filler(0x800 + 0x4000), pcmInfo.BootLoaderMasterHeaderLength),
                new FlashModule("Engine Operations", Filler(0x2000)),
            };
            var slave = FlashModuleBuilder.BuildSlaveModules(new[] { Filler(26752), Filler(1536) }, pcmInfo);

            List<CanBootLoaderWriter.DownloadPhase> phases =
                CanBootLoaderWriter.BuildPhases(pcmInfo, Filler(5440), master, Filler(416), slave);

            // Mirror fill, flash routines, master, slave, finalize.
            Assert.AreEqual(5, phases.Count);
            Assert.AreEqual(0x003F8000u, AddressOf(Bytes(phases[0])[1]), "The SRAM mirror fill still runs first.");
            CollectionAssert.AreEqual(new byte[] { 0x34, 0x00, 0x00, 0x15, 0x40 }, Bytes(phases[1])[0]);
            Assert.AreEqual(0x003FC430u, AddressOf(Bytes(phases[1])[1]));

            List<byte[]> masterMessages = Bytes(phases[2]);
            CollectionAssert.AreEqual(new byte[] { 0x1A, 0xC1 }, masterMessages[0], "The master handshake starts the burn.");
            CollectionAssert.AreEqual(new byte[] { 0x34, 0x00, 0x0F, 0xFE }, masterMessages[1]);
            Assert.AreEqual(0x800, DataLengthOf(masterMessages[2]), "The OS header is a message of its own.");

            // The slave handshake still sits between the driver's RequestDownload and its data.
            List<byte[]> slaveMessages = Bytes(phases[3]);
            CollectionAssert.AreEqual(new byte[] { 0x34, 0x00, 0x00, 0x01, 0xA0 }, slaveMessages[0]);
            CollectionAssert.AreEqual(new byte[] { 0x1A, 0xC9 }, slaveMessages[1]);
            Assert.AreEqual(0x003FC430u, AddressOf(slaveMessages[2]), "The slave driver streams to a rising address.");
            Assert.AreEqual(0x80, DataLengthOf(slaveMessages[4]), "The slave OS header is a message of its own.");

            AssertFinalize(phases[4]);
        }

        /// <summary>
        /// The download ends with ReturnToNormal and then the DeviceControl. Sent on its own straight
        /// after the last burn, the DeviceControl is ignored until the PCM has finished with the module
        /// it was given, and the download stalls until the retries run out.
        /// </summary>
        private static void AssertFinalize(CanBootLoaderWriter.DownloadPhase phase)
        {
            List<byte[]> messages = Bytes(phase);
            Assert.AreEqual(2, messages.Count, "Finalize must be ReturnToNormal then DeviceControl.");
            CollectionAssert.AreEqual(new byte[] { 0x20 }, messages[0]);
            CollectionAssert.AreEqual(new byte[] { 0xAE, 0x28, 0x80 }, messages[1]);
        }

        [TestMethod]
        public void BuildSlaveModules_ReadsTheFormFromTheImage()
        {
            var pcmInfo = new OSIDInfo(PcmType.E92);
            byte[] wrapped = E92ModuleCodec.Wrap(E92ModuleCodec.Compress(Filler(512)));
            List<FlashModule> modules = FlashModuleBuilder.BuildSlaveModules(new[] { Filler(11392), wrapped }, pcmInfo);

            Assert.AreEqual("slave-os", modules[0].Name);
            Assert.AreEqual(Gmlan.DataFormatUncompressed, modules[0].DataFormat);
            Assert.AreEqual(pcmInfo.BootLoaderSlaveHeaderLength, modules[0].HeaderLength);

            Assert.AreEqual("slave-calibration", modules[1].Name);
            Assert.AreEqual(Gmlan.DataFormatCompressed, modules[1].DataFormat);
            Assert.AreEqual(0, modules[1].HeaderLength);
        }

        private static List<byte[]> Bytes(CanBootLoaderWriter.DownloadPhase phase) =>
            phase.Messages.Select(m => m.GetBytes()).ToList();

        private static uint AddressOf(byte[] transferData)
        {
            Assert.AreEqual(0x36, transferData[0], "Not a TransferData message.");
            return ((uint)transferData[2] << 24) | ((uint)transferData[3] << 16) | ((uint)transferData[4] << 8) | transferData[5];
        }

        private static int DataLengthOf(byte[] transferData)
        {
            Assert.AreEqual(0x36, transferData[0], "Not a TransferData message.");
            return transferData.Length - 6;
        }

        private static byte[] Slice(byte[] image, int start, int length) =>
            new ArraySegment<byte>(image, start, length).ToArray();

        // Distinct bytes, so a misplaced block shows up as a mismatch rather than passing by luck.
        private static byte[] Filler(int length) =>
            Enumerable.Range(0, length).Select(i => (byte)(i * 7)).ToArray();

        private static byte[] LoadE92Image()
        {
            for (DirectoryInfo? d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string candidate = Path.Combine(d.FullName, "TestData", "E92_4096KiB_12691156.bin");
                if (File.Exists(candidate))
                {
                    return File.ReadAllBytes(candidate);
                }
            }

            throw new InvalidOperationException("E92 test image not found above " + AppContext.BaseDirectory);
        }
    }
}

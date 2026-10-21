// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace Tests
{
    [TestClass]
    public class FlashSafetyTests
    {
        private static OSIDInfo E38 => new OSIDInfo(PcmType.E38);
        private static byte[] Image(string name) => SlaveLibrary.Resolve(name)!;
        private static PackageController Controller() => new PackageController
        {
            ModuleType = "E38",
            Images = {
                PackageImage.Reference("slave-os", "12625892.bin", 12625892),
                PackageImage.Reference("slave-calibration", "12629150.bin", 12629150)
            }
        };

        [TestMethod]
        public void E38PartNumbersMatchBothHeaderRepresentations()
        {
            foreach (uint number in new uint[] {12605899,12608447,12609080,12617980,12625892,12629150})
            {
                Assert.IsTrue(FlashModuleBuilder.TryGetSlaveModulePartNumber(Image(number + ".bin"), out uint actual));
                Assert.AreEqual(number, actual);
            }
        }

        [TestMethod]
        public void CustomMainCalibrationRemainsAcceptedWithValidChecksums()
        {
            // Offline synthetic edit, never a tune to flash: prove the factory-byte restriction
            // applies to slave images only, while the main image retains checksum validation.
            byte[] main = LoadMaster();
            FlashSegment cal = FileValidator.GetE38MasterSegments(main)
                .First(s => s.BlockType == BlockType.Calibration);
            main[cal.Start + 0x40] ^= 1;
            uint total = 0;
            for (int i = cal.Start + 2; i <= cal.End; i += 2)
                total += (uint)((main[i] << 8) | main[i + 1]);
            ushort checksum = unchecked((ushort)(0u - total));
            main[cal.Start] = (byte)(checksum >> 8);
            main[cal.Start + 1] = (byte)checksum;
            Assert.IsTrue(new FileValidator(main, new MockLogger()).IdentifyAndValidate());
            var controller = Controller();
            controller.Images.Add(new PackageImage {Target="main", FileName="custom.bin", Data=main});
            using (var stream = new MemoryStream())
            {
                var format = new PhzFormat();
                format.Save(stream, new PcmPackage {Controllers={controller}});
                stream.Position = 0;
                var loaded = format.Load(stream, "custom.phz");
                CollectionAssert.AreEqual(main, loaded.Controllers[0].Image("main")!.Data!);
                Assert.AreEqual(2, SlaveWriteValidation.Resolve(loaded.Controllers[0], E38).Count);
            }
        }

        [TestMethod]
        public void CompleteFactorySetResolvesInTargetOrder()
        {
            var controller = Controller();
            controller.Images.Reverse();
            var resolved = SlaveWriteValidation.Resolve(controller, E38);
            CollectionAssert.AreEqual(Image("12625892.bin"), resolved[0]);
            CollectionAssert.AreEqual(Image("12629150.bin"), resolved[1]);
        }

        [TestMethod]
        public void MissingAndDuplicateTargetsAreRejected()
        {
            var controller = Controller();
            controller.Images.RemoveAt(1);
            Assert.ThrowsException<PackageException>(() => SlaveWriteValidation.Resolve(controller, E38));
            controller = Controller();
            controller.Images[1].Target = "slave-os";
            Assert.ThrowsException<PackageException>(() => SlaveWriteValidation.Resolve(controller, E38));
        }

        [TestMethod]
        public void UnknownSlaveTargetIsRejected()
        {
            var controller = Controller();
            controller.Images[1].Target = "slave-extra";
            Assert.ThrowsException<PackageException>(() => SlaveWriteValidation.Resolve(controller, E38));
        }

        [TestMethod]
        public void ReferencePartNumberAndFileNameMustMatchBytes()
        {
            var controller = Controller();
            controller.Images[0].PartNumber = 12617980;
            Assert.ThrowsException<PackageException>(() => SlaveWriteValidation.Resolve(controller, E38));
            controller = Controller();
            controller.Images[0].Data = Image("12617980.bin");
            controller.Images[0].PartNumber = null;
            Assert.ThrowsException<PackageException>(() => SlaveWriteValidation.Resolve(controller, E38));
        }

        [TestMethod]
        public void CorruptedPayloadWithIntactPartNumberIsRejected()
        {
            var controller = Controller();
            byte[] corrupt = (byte[])Image("12625892.bin").Clone();
            corrupt[corrupt.Length - 1] ^= 1;
            controller.Images[0].Data = corrupt;
            Assert.ThrowsException<InvalidOperationException>(() => SlaveWriteValidation.Resolve(controller, E38));
        }

        [TestMethod]
        public void ResolvingAFileCannotChangeTheFactoryIntegrityBaseline()
        {
            byte[] resolved = Image("12625892.bin");
            resolved[resolved.Length - 1] ^= 1;
            Assert.IsFalse(SlaveLibrary.IsKnownFactoryImage(12625892, resolved));
            Assert.IsTrue(SlaveLibrary.IsKnownFactoryImage(12625892, Image("12625892.bin")));
        }

        [TestMethod]
        public void TruncatedAndSwappedSlaveModulesAreRejected()
        {
            Assert.ThrowsException<InvalidOperationException>(() => FlashModuleBuilder.BuildSlaveModules(
                new[] { new byte[] { 1 }, Image("12629150.bin") }, E38));
            Assert.ThrowsException<InvalidOperationException>(() => FlashModuleBuilder.BuildSlaveModules(
                new[] { Image("12629150.bin"), Image("12625892.bin") }, E38));
        }

        [TestMethod]
        public async Task FullE38PackageRejectsDifferentConnectedEcuBeforeProgramming()
        {
            var controller = Controller();
            controller.Images.Add(new PackageImage { Target="main", Data=LoadMaster() });
            var package = new PcmPackage {Controllers = {controller}};
            var device = new ScriptDevice();
            var logger = new MockLogger();
            using (var vehicle = new Vehicle(device, new Protocol(), logger, null!, AppContext.BaseDirectory))
            {
                var writer = new WriteManager(logger, vehicle, WriteType.Full,
                    (m,t) => Task.CompletedTask, (m,t) => Task.FromResult(false), CancellationToken.None);
                Assert.IsFalse(await writer.Write(package));
                Assert.IsTrue(device.Sent.Count > 0, "Must actually identify the connected PCM.");
                Assert.IsTrue(device.Sent.All(b => b[0] == 0x1A), "Only identification requests may be sent.");
            }
        }

        [TestMethod]
        public async Task PackagePlatformMismatchStopsBeforeAccessingVehicle()
        {
            var controller = Controller();
            controller.Images.Add(new PackageImage {Target="main", Data=LoadMaster()});
            var writer = new WriteManager(new MockLogger(), null!, WriteType.Full,
                (m,t) => Task.CompletedTask, (m,t) => Task.FromResult(false), CancellationToken.None);
            // The selected platform is incompatible with the package's main image.
            Assert.IsFalse(await writer.Write(new PcmPackage {Controllers={controller}}, PcmType.E92));
        }

        [TestMethod]
        public async Task BootLoaderWaitsPastUnrelatedRepliesForActualAck()
        {
            var device = new ScriptDevice { Replies = new[] {
                new byte[] {0x5A,0xC1,1,2,3,4}, new byte[] {0x7F,0x1A,0x31}, new byte[] {0x76} } };
            var commands = new CanCommands(device, new MockLogger());
            var response = await commands.SendBootLoaderMessage(new Message(new byte[] {0x36,0,0,0,0,0,1}), CancellationToken.None);
            Assert.AreEqual(ResponseStatus.Success, response.Status);
            CollectionAssert.AreEqual(new byte[] {0x76}, response.Value);
            Assert.AreEqual(1, device.Sent.Count, "Unrelated replies must not cause a retransmission.");
        }

        [TestMethod]
        public void BootLoaderReplyPolicyRejectsPendingMalformedAndWrongDid()
        {
            Assert.IsFalse(BootLoaderReplyPolicy.Matches(new byte[] {0x36}, new byte[] {0x7F,0x36,0x78}));
            Assert.IsFalse(BootLoaderReplyPolicy.Matches(new byte[] {0x36}, new byte[] {0x7F}));
            Assert.IsFalse(BootLoaderReplyPolicy.Matches(new byte[] {0x1A,0xC9}, new byte[] {0x5A,0xCA}));
            Assert.IsFalse(BootLoaderReplyPolicy.Matches(new byte[] {0x36}, Array.Empty<byte>()));
            Assert.IsTrue(BootLoaderReplyPolicy.Matches(new byte[] {0x1A,0xC1}, new byte[] {0x7F,0x1A,0x31}), "Recovery's master-handshake exception must reach the writer.");
            Assert.IsTrue(BootLoaderReplyPolicy.Matches(new byte[] {0x34}, new byte[] {0x74}));
            Assert.IsTrue(BootLoaderReplyPolicy.Matches(new byte[] {0xAE,0x28,0x80}, new byte[] {0xEE}));
        }

        private static byte[] LoadMaster()
        {
            for (DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "TestData", "E38_2048KiB_12607218.bin");
                if (File.Exists(path)) return File.ReadAllBytes(path);
            }
            throw new FileNotFoundException("E38 test image was not deployed.");
        }

        [TestMethod]
        public async Task ShortCanParameterReplyDoesNotInventASensorValue()
        {
            var database = new ParameterDatabase(AppContext.BaseDirectory);
            database.LoadDatabase();
            Assert.IsTrue(database.TryGetParameter<PidParameter>("EngineSpeed", out var rpm));
            var log = new MockLogger();
            var device = new ScriptDevice {Replies = new[] {new byte[] {0x62,0x00,0x0C,0x0C,0x80}}};
            using (var vehicle = new Vehicle(device,new Protocol(),log,null!,AppContext.BaseDirectory))
            {
                var logger = Logger.Create(vehicle,12607218,new[] {new LogColumn(rpm,rpm.Conversions.First(),false)},
                    false,false,BusProtocol.Can500k,log);
                Assert.IsTrue(await logger.StartLogging());
                device.Replies = new[] {new byte[] {0x62,0x00,0x0C,0x0C}};
                Assert.AreEqual(0,(await logger.GetNextRowV2()).Count());
            }
        }

        [TestMethod]
        public async Task CancelledParameterReadDoesNotSendAnotherRequest()
        {
            var device = new ScriptDevice();
            using (var vehicle = new Vehicle(device,new Protocol(),new MockLogger(),null!,AppContext.BaseDirectory))
            using (var stop = new CancellationTokenSource())
            {
                stop.Cancel();
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => vehicle.ReadCanParameter(0x000C,stop.Token));
                Assert.AreEqual(0,device.Sent.Count);
            }
        }

        private sealed class ScriptDevice : Device
        {
            public List<byte[]> Sent {get;} = new List<byte[]>();
            public byte[][]? Replies {get;set;}
            public ScriptDevice() : base(new MockLogger()) { }
            public override Task<bool> SetProtocol(BusProtocol p) => Task.FromResult(p == BusProtocol.Can500k);
            public override Task<bool> Initialize() => Task.FromResult(true);
            public override Task<TimeoutScenario> SetTimeout(TimeoutScenario t) => Task.FromResult(t);
            public override Task<byte?> ReadBroadcastState(byte b) => Task.FromResult<byte?>(null);
            public override string GetDeviceType() => "Scripted regression test";
            public override Task<bool> CheckDeviceConnection() => Task.FromResult(true);
            public override Task<bool> SendMessage(Message message)
            {
                byte[] b = message.GetBytes(); Sent.Add(b);
                if (Replies != null) {foreach (byte[] reply in Replies) this.Enqueue(new Message(reply));}
                else if (b.Length >= 2 && b[0] == 0x1A) {
                    uint osid = 12691156; // A known E92, deliberately different from the file's E38.
                    this.Enqueue(new Message(new byte[] {0x5A,b[1],(byte)(osid>>24),(byte)(osid>>16),(byte)(osid>>8),(byte)osid}));
                }
                return Task.FromResult(true);
            }
            public override void ClearMessageBuffer() { }
            protected override Task<bool> SetVPWSpeedInternal(VPWSpeed s) => Task.FromResult(false);
            protected override void Dispose(bool disposing) { }
            protected override Task Receive() => Task.CompletedTask;
        }
    }
}

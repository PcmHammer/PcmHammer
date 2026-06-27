// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// Unit tests for the GMLAN dialect message builders and parsers (Messages/Gmlan.cs). These
    /// cover the byte layout of each request and the positive/negative/mismatch paths of each
    /// parser, so the wire format is verified without a PCM.
    /// </summary>
    [TestClass]
    public class GmlanTests
    {
        private static Message Msg(params byte[] bytes) => new Message(bytes);

        // ---- ReadDataByIdentifier (0x1A / 0x5A) ----

        [TestMethod]
        public void ReadById_Request_IsServiceThenDid()
        {
            CollectionAssert.AreEqual(new byte[] { 0x1A, 0xC9 }, new Gmlan().CreateReadByIdRequest(0xC9).GetBytes());
        }

        [TestMethod]
        public void ReadById_Response_ReturnsDataAfterSidAndDid()
        {
            var r = new Gmlan().ParseReadByIdResponse(Msg(0x5A, 0xC9, 0x00, 0xC0, 0x9F, 0xE4), 0xC9);
            Assert.AreEqual(ResponseStatus.Success, r.Status);
            CollectionAssert.AreEqual(new byte[] { 0x00, 0xC0, 0x9F, 0xE4 }, r.Value);
        }

        [TestMethod]
        public void ReadById_NegativeResponse_IsError()
        {
            Assert.AreEqual(ResponseStatus.Error, new Gmlan().ParseReadByIdResponse(Msg(0x7F, 0x1A, 0x31), 0xC9).Status);
        }

        [TestMethod]
        public void ReadById_WrongDid_IsUnexpected()
        {
            Assert.AreEqual(ResponseStatus.UnexpectedResponse, new Gmlan().ParseReadByIdResponse(Msg(0x5A, 0x01, 0x00), 0xC9).Status);
        }

        // ---- Security access (0x27) ----

        [TestMethod]
        public void Unlock_Request_CarriesKeyBigEndian()
        {
            CollectionAssert.AreEqual(new byte[] { 0x27, 0x02, 0xAB, 0xCD }, new Gmlan().CreateUnlockRequest(0xABCD).GetBytes());
        }

        [TestMethod]
        public void ParseSeed_ReturnsBigEndianSeed()
        {
            var r = new Gmlan().ParseSeed(Msg(0x67, 0x01, 0x12, 0x34));
            Assert.AreEqual(ResponseStatus.Success, r.Status);
            Assert.AreEqual((ushort)0x1234, r.Value);
        }

        [TestMethod]
        public void ParseUnlock_Positive_IsSuccess()
        {
            Assert.AreEqual(ResponseStatus.Success, new Gmlan().ParseUnlockResponse(Msg(0x67, 0x02)).Status);
        }

        [TestMethod]
        public void ParseUnlock_Negative_IsError()
        {
            Assert.AreEqual(ResponseStatus.Error, new Gmlan().ParseUnlockResponse(Msg(0x7F, 0x27, 0x35)).Status);
        }

        [TestMethod]
        public void IsSecurityDelayActive_TrueOnlyForNrc37()
        {
            var gmlan = new Gmlan();
            Assert.IsTrue(gmlan.IsSecurityDelayActive(Msg(0x7F, 0x27, 0x37)));
            Assert.IsFalse(gmlan.IsSecurityDelayActive(Msg(0x7F, 0x27, 0x35)));
            Assert.IsFalse(gmlan.IsSecurityDelayActive(Msg(0x67, 0x02)));
        }

        // ---- Programming mode (0xA5) ----

        [TestMethod]
        public void ProgrammingMode_Request_IsA5_01()
        {
            CollectionAssert.AreEqual(new byte[] { 0xA5, 0x01 }, new Gmlan().CreateProgrammingModeRequest().GetBytes());
        }

        [TestMethod]
        public void ParseProgrammingMode_MatchesOnResponseSidAlone()
        {
            var gmlan = new Gmlan();
            Assert.AreEqual(ResponseStatus.Success, gmlan.ParseProgrammingModeResponse(Msg(0xE5)).Status);
            Assert.AreEqual(ResponseStatus.Error, gmlan.ParseProgrammingModeResponse(Msg(0x7F, 0xA5, 0x22)).Status);
        }

        // ---- RequestDownload (0x34) ----

        [TestMethod]
        public void RequestDownload_Request_CarriesThreeByteSize()
        {
            CollectionAssert.AreEqual(new byte[] { 0x34, 0x00, 0x01, 0x00, 0x00 }, new Gmlan().CreateRequestDownloadRequest(0x10000).GetBytes());
        }

        [TestMethod]
        public void RequestDownload_Request_CarriesTwoByteSize()
        {
            // P05c-style 16-bit length.
            CollectionAssert.AreEqual(new byte[] { 0x34, 0x00, 0x14, 0x60 }, new Gmlan().CreateRequestDownloadRequest(0x1460, sizeBytes: 2).GetBytes());
        }

        [TestMethod]
        public void ParseRequestDownload_74_IsSuccess()
        {
            var gmlan = new Gmlan();
            Assert.AreEqual(ResponseStatus.Success, gmlan.ParseRequestDownloadResponse(Msg(0x74)).Status);
            Assert.AreEqual(ResponseStatus.Refused, gmlan.ParseRequestDownloadResponse(Msg(0x12)).Status);
        }

        // ---- Transfer chunks (0x36) ----

        [TestMethod]
        public void NonExecChunk_LayoutIsSidModeLoadAddrThenCode()
        {
            var msg = new Gmlan().CreateNonExecChunkMessage(new byte[] { 0xDE, 0xAD }, 0x00112233).GetBytes();
            CollectionAssert.AreEqual(new byte[] { 0x36, 0x00, 0x00, 0x11, 0x22, 0x33, 0xDE, 0xAD }, msg);
        }

        [TestMethod]
        public void ExecChunk_LayoutCarriesLoadThenRunAddr()
        {
            var msg = new Gmlan().CreateExecChunkMessage(new byte[] { 0xBE, 0xEF }, 0x003FC430, 0x003FC434).GetBytes();
            CollectionAssert.AreEqual(
                new byte[] { 0x36, 0x80, 0x00, 0x3F, 0xC4, 0x30, 0x00, 0x3F, 0xC4, 0x34, 0xBE, 0xEF },
                msg);
        }

        [TestMethod]
        public void ExecuteMessage_IsBareSidModeAddr_NoRunOrCode()
        {
            // P05c bare execute frame: 0x36 0x80 + address only.
            CollectionAssert.AreEqual(
                new byte[] { 0x36, 0x80, 0x00, 0xFF, 0x60, 0x00 },
                new Gmlan().CreateExecuteMessage(0x00FF6000).GetBytes());
        }

        [TestMethod]
        public void IsChunkAck_DistinguishesExecFromNonExec()
        {
            var gmlan = new Gmlan();
            Assert.IsTrue(gmlan.IsChunkAck(Msg(0x99), isExec: true));
            Assert.IsFalse(gmlan.IsChunkAck(Msg(0x76), isExec: true));
            Assert.IsTrue(gmlan.IsChunkAck(Msg(0x76), isExec: false));
            Assert.IsFalse(gmlan.IsChunkAck(Msg(0x99), isExec: false));
        }

        // ---- Kernel memory read (0x35 / 0x36) ----

        [TestMethod]
        public void MemoryRead_Request_IsFixed0x400BlockAtAddress()
        {
            CollectionAssert.AreEqual(
                new byte[] { 0x35, 0x00, 0x04, 0x00, 0x12, 0x34, 0x56 },
                new Gmlan().CreateMemoryReadRequest(0x123456).GetBytes());
        }

        [TestMethod]
        public void MemoryRead_Ack_Is75()
        {
            var gmlan = new Gmlan();
            Assert.IsTrue(gmlan.IsMemoryReadAck(Msg(0x75)));
            Assert.IsFalse(gmlan.IsMemoryReadAck(Msg(0x36)));
        }

        [TestMethod]
        public void ParseMemoryBlock_ReturnsThe0x400DataBytes()
        {
            byte[] frame = new byte[5 + Gmlan.KernelBlockSize];
            frame[0] = 0x36; frame[1] = 0x00; frame[2] = 0x12; frame[3] = 0x34; frame[4] = 0x56;
            for (int i = 0; i < Gmlan.KernelBlockSize; i++) frame[5 + i] = (byte)(i & 0xFF);

            var r = new Gmlan().ParseMemoryBlock(Msg(frame));
            Assert.AreEqual(ResponseStatus.Success, r.Status);
            Assert.AreEqual(Gmlan.KernelBlockSize, r.Value.Length);
            Assert.AreEqual(0x00, r.Value[0]);
            Assert.AreEqual((byte)((Gmlan.KernelBlockSize - 1) & 0xFF), r.Value[Gmlan.KernelBlockSize - 1]);
        }

        [TestMethod]
        public void ParseMemoryBlock_ShortFrame_IsError()
        {
            Assert.AreEqual(ResponseStatus.Error, new Gmlan().ParseMemoryBlock(Msg(0x36, 0x00, 0x12, 0x34, 0x56)).Status);
        }

        // ---- Kernel queries (0x3D / 0x7D) ----

        [TestMethod]
        public void Crc_Request_IsSizeThenAddress()
        {
            CollectionAssert.AreEqual(
                new byte[] { 0x3D, 0x02, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00 },
                new Gmlan().CreateCrcRequest(0x000000, 0x100000).GetBytes());
        }

        [TestMethod]
        public void Mode3D_Parsers_MatchSubFunctionAndReadUInt32()
        {
            var gmlan = new Gmlan();
            Assert.AreEqual(0xAABBCCDDu, gmlan.ParseCrcResponse(Msg(0x7D, 0x02, 0xAA, 0xBB, 0xCC, 0xDD)).Value);
            Assert.AreEqual(0x12345678u, gmlan.ParseKernelVersionResponse(Msg(0x7D, 0x00, 0x12, 0x34, 0x56, 0x78)).Value);
            // Wrong sub-function is rejected, not silently accepted.
            Assert.AreEqual(ResponseStatus.Refused, gmlan.ParseFlashIdResponse(Msg(0x7D, 0x00, 0x00, 0x00, 0x00, 0x00)).Status);
        }

        // ---- CRC32 ----

        [TestMethod]
        public void ComputeCrc32_EmptyRange_IsZero()
        {
            Assert.AreEqual(0u, Gmlan.ComputeCrc32(new byte[] { 1, 2, 3 }, 0, 0));
        }

        [TestMethod]
        public void ComputeCrc32_IsDeterministicAndSensitiveToData()
        {
            byte[] a = { 0x00, 0x11, 0x22, 0x33 };
            byte[] b = { 0x00, 0x11, 0x22, 0x34 };
            Assert.AreEqual(Gmlan.ComputeCrc32(a, 0, 4), Gmlan.ComputeCrc32(a, 0, 4));
            Assert.AreNotEqual(Gmlan.ComputeCrc32(a, 0, 4), Gmlan.ComputeCrc32(b, 0, 4));
        }

        // ---- CAN seed/key (KeyAlgorithm.GetCanKey) ----

        [TestMethod]
        public void GetCanKey_BoundaryCases()
        {
            Assert.AreEqual((ushort)0x0000, KeyAlgorithm.GetCanKey(-1, 0x1234), "algo out of range -> 0.");
            Assert.AreEqual((ushort)0x0000, KeyAlgorithm.GetCanKey(256, 0x1234), "algo out of range -> 0.");
            Assert.AreEqual((ushort)0xFFFF, KeyAlgorithm.GetCanKey(5, 0xFFFF), "seed 0xFFFF -> 0xFFFF.");
        }

        [TestMethod]
        public void GetCanKey_IsDeterministic()
        {
            Assert.AreEqual(KeyAlgorithm.GetCanKey(0x0A, 0x1234), KeyAlgorithm.GetCanKey(0x0A, 0x1234));
        }

        [TestMethod]
        public void GetKey_ByProtocol_SelectsTheRightTable()
        {
            ushort seed = 0x1234;
            for (int algo = 0; algo <= 8; algo++)
            {
                Assert.AreEqual(KeyAlgorithm.GetCanKey(algo, seed), KeyAlgorithm.GetKey(BusProtocol.Can500k, algo, seed), "Can500k -> CAN table.");
                Assert.AreEqual(KeyAlgorithm.GetKey(algo, seed), KeyAlgorithm.GetKey(BusProtocol.Vpw, algo, seed), "Vpw -> VPW table.");
            }
        }

        [TestMethod]
        public void GetCanKey_UsesCanTable_NotVpwAlgorithm()
        {
            // The CAN table has a different opcode set to the VPW table, so for a representative
            // algo/seed the two key functions must not coincide - proves we are running the CAN path.
            ushort seed = 0x1234;
            bool anyDifferent = false;
            for (int algo = 0; algo <= 8; algo++)
            {
                if (KeyAlgorithm.GetCanKey(algo, seed) != KeyAlgorithm.GetKey(algo, seed))
                {
                    anyDifferent = true;
                    break;
                }
            }
            Assert.IsTrue(anyDifferent, "CAN keys should differ from VPW keys for at least one algorithm.");
        }
    }
}

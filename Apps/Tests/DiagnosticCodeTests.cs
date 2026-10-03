// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// Unit tests for decoding trouble codes and reading their descriptions. The encoding is fixed,
    /// so all of this is testable without a vehicle.
    /// </summary>
    [TestClass]
    public class DiagnosticCodeTests
    {
        [TestMethod]
        public void Decode_ReadsTheSystemLetterFromTheTopTwoBits()
        {
            Assert.AreEqual("P0143", DiagnosticCode.Decode(0x0143));
            Assert.AreEqual("C0271", DiagnosticCode.Decode(0x4271));
            Assert.AreEqual("B1000", DiagnosticCode.Decode(0x9000));
            Assert.AreEqual("U2100", DiagnosticCode.Decode(0xE100));
        }

        [TestMethod]
        public void Decode_ReadsTheFirstDigitFromTheNextTwoBits()
        {
            Assert.AreEqual("P0300", DiagnosticCode.Decode(0x0300));
            Assert.AreEqual("P1300", DiagnosticCode.Decode(0x1300));
            Assert.AreEqual("P2300", DiagnosticCode.Decode(0x2300));
            Assert.AreEqual("P3400", DiagnosticCode.Decode(0x3400));
        }

        [TestMethod]
        public void Decode_KeepsHexDigitsInTheLastThreePositions()
        {
            // P122A and P1A00 are not typos: the low twelve bits are written as hex.
            Assert.AreEqual("P122A", DiagnosticCode.Decode(0x122A));
            Assert.AreEqual("P1A00", DiagnosticCode.Decode(0x1A00));
            Assert.AreEqual("P2A11", DiagnosticCode.Decode(0x2A11));
        }

        [TestMethod]
        public void Definitions_ReadCodeAndDescription()
        {
            DiagnosticCodeDefinitions definitions = new DiagnosticCodeDefinitions();
            definitions.Read(new StringReader(
                "# a comment\r\n" +
                "\r\n" +
                "P0143,O2 Sensor Circuit Low Voltage Bank 1 Sensor 3\r\n" +
                "P1870,Transmission Component Slipping\r\n"));

            Assert.AreEqual(2, definitions.Count);
            Assert.AreEqual("Transmission Component Slipping", definitions.DescriptionFor("P1870"));
        }

        [TestMethod]
        public void Definitions_SplitOnTheFirstCommaOnly()
        {
            // The code never contains a comma; a description might.
            DiagnosticCodeDefinitions definitions = new DiagnosticCodeDefinitions();
            definitions.Read(new StringReader("P0300,Misfire detected, cylinder unknown\r\n"));

            Assert.AreEqual("Misfire detected, cylinder unknown", definitions.DescriptionFor("P0300"));
        }

        [TestMethod]
        public void Describe_LeavesAnUnknownCodeWithoutADescription()
        {
            // Better than a guess: a wrong description sends someone to the wrong part.
            DiagnosticCodeDefinitions definitions = new DiagnosticCodeDefinitions();
            DiagnosticCode code = definitions.Describe(0x0143, DiagnosticCodeKind.Stored);

            Assert.AreEqual("P0143", code.Name);
            Assert.IsNull(code.Description);
            Assert.AreEqual("P0143", code.ToString());
        }

        [TestMethod]
        public void ReadCodePairs_SkipsThePaddingPairs()
        {
            // A module with one code to report still fills the message with zeroes.
            List<ushort> codes = new List<ushort>();
            Protocol.ReadCodePairs(new byte[] { 0x43, 0x01, 0x43, 0x00, 0x00, 0x00, 0x00 }, 1, codes);

            CollectionAssert.AreEqual(new[] { (ushort)0x0143 }, codes);
        }

        [TestMethod]
        public void VPW_RequestIsAddressedToTheEmissionsModules()
        {
            // 0x68 functional to 0x6A, not 0x6C physical to the PCM: asked physically, a PCM that
            // supports these services answers "service not supported".
            CollectionAssert.AreEqual(
                new byte[] { 0x68, 0x6A, 0xF0, 0x03 },
                new Protocol().CreateTroubleCodeRequest(DiagnosticCodeKind.Stored).GetBytes());
        }

        [TestMethod]
        public void VPW_ReadsCodesFromAFunctionallyAddressedReply()
        {
            // 48 6B 10 43 then three code pairs, zero-padded. The reply goes to the emissions
            // address rather than back to the tool, which is why it must not be matched on 0xF0.
            Protocol protocol = new Protocol();

            TroubleCodeReply reply = protocol.ParseTroubleCodes(
                new Message(new byte[] { 0x48, 0x6B, 0x10, 0x43, 0x01, 0x43, 0x18, 0x70, 0x00, 0x00 }),
                DiagnosticCodeKind.Stored,
                out byte source,
                out List<ushort> codes);

            Assert.AreEqual(TroubleCodeReply.Codes, reply);
            Assert.AreEqual(0x10, source);
            CollectionAssert.AreEqual(new[] { (ushort)0x0143, (ushort)0x1870 }, codes);
        }

        [TestMethod]
        public void VPW_AnEmptyReplyIsNoCodesRatherThanNoAnswer()
        {
            Protocol protocol = new Protocol();

            TroubleCodeReply reply = protocol.ParseTroubleCodes(
                new Message(new byte[] { 0x48, 0x6B, 0x10, 0x47, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }),
                DiagnosticCodeKind.Pending,
                out byte _,
                out List<ushort> codes);

            Assert.AreEqual(TroubleCodeReply.Codes, reply);
            Assert.AreEqual(0, codes.Count);
        }

        [TestMethod]
        public void VPW_RecognisesTheRefusalSeenOnHardware()
        {
            // 6C F0 10 7F 03 11 - service not supported, which is definitive and must not be
            // waited out: each pointless retry costs a full receive timeout.
            Protocol protocol = new Protocol();

            Assert.AreEqual(
                TroubleCodeReply.Refused,
                protocol.ParseTroubleCodes(
                    new Message(new byte[] { 0x6C, 0xF0, 0x10, 0x7F, 0x03, 0x11 }),
                    DiagnosticCodeKind.Stored,
                    out byte source,
                    out List<ushort> _));

            Assert.AreEqual(0x10, source);
        }

        [TestMethod]
        public void VPW_IgnoresTrafficThatIsNotAnAnswer()
        {
            // A streaming log row, which is what used to be read instead of the reply.
            Protocol protocol = new Protocol();

            Assert.AreEqual(
                TroubleCodeReply.Unrelated,
                protocol.ParseTroubleCodes(
                    new Message(new byte[] { 0x6C, 0xF0, 0x10, 0x6A, 0xFD, 0x59, 0x80, 0x01 }),
                    DiagnosticCodeKind.Stored,
                    out byte _,
                    out List<ushort> _));
        }

        [TestMethod]
        public void StatusMask_RequestsAreAddressedToThePcm()
        {
            Protocol protocol = new Protocol();

            // Trailing FF 00 selects the list. FF FF would ask for a count, and anything else is
            // refused as an unsupported subfunction.
            CollectionAssert.AreEqual(
                new byte[] { 0x6C, 0x10, 0xF0, 0x19, 0xDA, 0xFF, 0x00 },
                protocol.CreateTroubleCodeListRequest().GetBytes());
        }

        [TestMethod]
        public void StatusMask_ExcludesTheBitsThatAreNotFailures()
        {
            // Bit 0 is set on every enabled fault, so a mask containing it reports the whole
            // supported-code table. Bits 2 and 5 mean a monitor has not run, which is not a fault.
            Assert.AreEqual(0x00, Protocol.TroubleCodeFailureMask & 0x25);
        }

        [TestMethod]
        public void StatusMask_SpellsOutTheStatusByte()
        {
            // Captured from a bench PCM: a monitor that has never run, which is not a failure.
            Assert.AreEqual("not run since last clear, not run this key-on",
                Protocol.DescribeTroubleCodeStatus(0x25));

            // Bit 0 alone says only that the calibration enables the fault.
            Assert.AreEqual("enabled", Protocol.DescribeTroubleCodeStatus(0x01));

            Assert.AreEqual("failing now, lamp on, history, failed this key-on",
                Protocol.DescribeTroubleCodeStatus(0xD2));

            // The bit this mask includes and other tools leave out, on its own: an immature
            // failure, and it has to read as one rather than as a live fault.
            Assert.AreEqual("failed since last clear", Protocol.DescribeTroubleCodeStatus(0x09));
        }

        [TestMethod]
        public void StatusMask_ReadsACodeAndStopsAtTheTerminator()
        {
            Protocol protocol = new Protocol();

            Assert.IsTrue(protocol.TryParseTroubleCodeByStatus(
                new Message(new byte[] { 0x6C, 0xF0, 0x10, 0x59, 0x01, 0x43, 0x8F }),
                out ushort code,
                out byte status));

            Assert.AreEqual(0x0143, code);
            Assert.AreEqual(0x8F, status);

            // The module ends the list with a code of zero.
            Assert.IsFalse(protocol.TryParseTroubleCodeByStatus(
                new Message(new byte[] { 0x6C, 0xF0, 0x10, 0x59, 0x00, 0x00, 0xFF }),
                out ushort _,
                out byte _));
        }

        [TestMethod]
        public void StatusMask_ReadsACaptureFromTheBench()
        {
            Protocol protocol = new Protocol();

            Assert.IsTrue(protocol.TryParseTroubleCodeByStatus(
                new Message(new byte[] { 0x6C, 0xF0, 0x10, 0x59, 0x02, 0x30, 0x25 }),
                out ushort code,
                out byte status));

            Assert.AreEqual("P0230", DiagnosticCode.Decode(code));
            Assert.AreEqual(0x25, status);
        }

        [TestMethod]
        public void StatusMask_RecognisesARefusal()
        {
            // So the generic services are tried instead of the module being called empty.
            Assert.IsTrue(new Protocol().IsTroubleCodeStatusRefusal(
                new Message(new byte[] { 0x6C, 0xF0, 0x10, 0x7F, 0x19, 0x12 })));

            Assert.IsFalse(new Protocol().IsTroubleCodeStatusRefusal(
                new Message(new byte[] { 0x6C, 0xF0, 0x10, 0x7F, 0x03, 0x11 })));
        }

        [TestMethod]
        public void TroubleCodeMode_MapsEachListToItsService()
        {
            Assert.AreEqual(0x03, Protocol.TroubleCodeMode(DiagnosticCodeKind.Stored));
            Assert.AreEqual(0x07, Protocol.TroubleCodeMode(DiagnosticCodeKind.Pending));
            Assert.AreEqual(0x0A, Protocol.TroubleCodeMode(DiagnosticCodeKind.Permanent));
        }

        [TestMethod]
        public void Gmlan_ParsesCodesAndRejectsARefusal()
        {
            Gmlan gmlan = new Gmlan();

            Response<List<ushort>> parsed = gmlan.ParseTroubleCodes(
                new Message(new byte[] { 0x43, 0x02, 0x01, 0x43, 0x18, 0x70 }),
                DiagnosticCodeKind.Stored);

            Assert.AreEqual(ResponseStatus.Success, parsed.Status);
            CollectionAssert.AreEqual(new[] { (ushort)0x0143, (ushort)0x1870 }, parsed.Value);

            Assert.AreEqual(
                ResponseStatus.Error,
                gmlan.ParseTroubleCodes(
                    new Message(new byte[] { 0x7F, 0x03, 0x11 }), DiagnosticCodeKind.Stored).Status);
        }

        [TestMethod]
        public void Gmlan_ParsesTheRepliesSeenOnHardware()
        {
            // Captured from an E39a: a count of zero for stored and pending, three permanent codes.
            Gmlan gmlan = new Gmlan();

            Assert.AreEqual(
                0,
                gmlan.ParseTroubleCodes(
                    new Message(new byte[] { 0x43, 0x00 }), DiagnosticCodeKind.Stored).Value.Count);

            Assert.AreEqual(
                0,
                gmlan.ParseTroubleCodes(
                    new Message(new byte[] { 0x47, 0x00 }), DiagnosticCodeKind.Pending).Value.Count);

            Response<List<ushort>> permanent = gmlan.ParseTroubleCodes(
                new Message(new byte[] { 0x4A, 0x03, 0x30, 0x5F, 0x26, 0x46, 0x05, 0x8D }),
                DiagnosticCodeKind.Permanent);

            Assert.AreEqual(ResponseStatus.Success, permanent.Status);
            CollectionAssert.AreEqual(
                new[] { (ushort)0x305F, (ushort)0x2646, (ushort)0x058D }, permanent.Value);

            Assert.AreEqual("P305F", DiagnosticCode.Decode(permanent.Value[0]));
            Assert.AreEqual("P2646", DiagnosticCode.Decode(permanent.Value[1]));
            Assert.AreEqual("P058D", DiagnosticCode.Decode(permanent.Value[2]));
        }

        [TestMethod]
        public void ShippedDefinitions_LoadAndDescribeAKnownCode()
        {
            // The file ships beside the logger; skip rather than fail where it is not deployed.
            string path = Path.Combine(AppContext.BaseDirectory, DiagnosticCodeDefinitions.FileName);
            if (!File.Exists(path))
            {
                Assert.Inconclusive(DiagnosticCodeDefinitions.FileName + " is not in the test output.");
            }

            DiagnosticCodeDefinitions definitions =
                DiagnosticCodeDefinitions.Load(AppContext.BaseDirectory);

            Assert.IsTrue(definitions.Count > 1000, "Expected the full code list.");
            Assert.AreEqual("Transmission Component Slipping", definitions.DescriptionFor("P1870"));
        }
    }
}

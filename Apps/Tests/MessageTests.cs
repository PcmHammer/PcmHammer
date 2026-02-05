using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace PcmHammer.Tests
{
    [TestClass]
    public class MessageTests
    {
        [TestMethod]
        public void Constructor_SetsLengthAndIndexer()
        {
            byte[] bytes = { 0x01, 0xAB, 0x00, 0xFF };
            Message message = new Message(bytes);

            Assert.AreEqual(4, message.Length);
            Assert.AreEqual(0x01, message[0]);
            Assert.AreEqual(0xAB, message[1]);
            Assert.AreEqual(0x00, message[2]);
            Assert.AreEqual(0xFF, message[3]);
        }

        [TestMethod]
        public void Constructor_WithTimestampAndError_SetsProperties()
        {
            byte[] bytes = { 0x10, 0x20 };
            Message message = new Message(bytes, 1234, 77);

            Assert.AreEqual((ulong)1234, message.TimeStamp);
            Assert.AreEqual((ulong)77, message.Error);
        }

        [TestMethod]
        public void Properties_CanBeUpdated()
        {
            Message message = new Message(new byte[] { 0x01 });
            message.TimeStamp = 99;
            message.Error = 42;

            Assert.AreEqual((ulong)99, message.TimeStamp);
            Assert.AreEqual((ulong)42, message.Error);
        }

        [TestMethod]
        public void GetBytes_ReturnsUnderlyingArray()
        {
            byte[] bytes = { 0xDE, 0xAD, 0xBE, 0xEF };
            Message message = new Message(bytes);

            byte[] returned = message.GetBytes();

            Assert.AreSame(bytes, returned);
        }

        [TestMethod]
        public void ToString_FormatsAsHexBytes()
        {
            byte[] bytes = { 0x00, 0x0A, 0x10, 0xFF };
            Message message = new Message(bytes);

            Assert.AreEqual("00 0A 10 FF", message.ToString());
        }

        [TestMethod]
        public void ToString_TruncatedMessage_FormatsShorterPayload()
        {
            byte[] bytes = { 0x12, 0x34 };
            Message message = new Message(bytes);

            Assert.AreEqual("12 34", message.ToString());
        }

        [TestMethod]
        public void ToString_CorruptedMessage_ReflectsMutatedBytes()
        {
            byte[] bytes = { 0x01, 0x02, 0x03 };
            Message message = new Message(bytes);

            bytes[1] = 0xFF; // simulate corruption after construction

            Assert.AreEqual("01 FF 03", message.ToString());
        }

        [TestMethod]
        [ExpectedException(typeof(IndexOutOfRangeException))]
        public void Indexer_OutOfRange_Throws()
        {
            Message message = new Message(new byte[] { 0x01 });
            _ = message[1];
        }
    }
}

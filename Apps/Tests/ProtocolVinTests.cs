using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace PcmHammer.Tests
{
    [TestClass]
    public class ProtocolVinTests
    {
        private static byte[] BuildVinResponse(byte blockId, string payload)
        {
            byte[] data = Encoding.ASCII.GetBytes(payload);
            int extraLengthByte = (blockId == BlockId.Vin1) ? 1 : 0;
            int length = 5 + extraLengthByte + data.Length + 1; // header + optional length + data + crc
            byte[] response = new byte[length];

            response[0] = Priority.Physical0;
            response[1] = DeviceId.Tool;
            response[2] = DeviceId.Pcm;
            response[3] = (byte)(Mode.ReadBlock + Mode.Response);
            response[4] = blockId;

            int index = 5;
            if (extraLengthByte == 1)
            {
                response[index++] = 0x00; // length placeholder used by PCM for VIN1
            }

            Buffer.BlockCopy(data, 0, response, index, data.Length);

            response[response.Length - 1] = 0x00; // crc placeholder
            return response;
        }

        [TestMethod]
        public void ParseVinResponses_Good_ReturnsVin()
        {
            Protocol protocol = new Protocol();

            byte[] response1 = BuildVinResponse(BlockId.Vin1, "12345");
            byte[] response2 = BuildVinResponse(BlockId.Vin2, "ABCDEF");
            byte[] response3 = BuildVinResponse(BlockId.Vin3, "123456");

            Response<string> result = protocol.ParseVinResponses(response1, response2, response3);

            Assert.AreEqual(ResponseStatus.Success, result.Status);
            Assert.AreEqual("12345ABCDEF123456", result.Value);
        }

        [TestMethod]
        public void ParseVinResponses_MalformedHeader_ReturnsUnexpectedResponse()
        {
            Protocol protocol = new Protocol();

            byte[] response1 = BuildVinResponse(BlockId.Vin1, "12345");
            byte[] response2 = BuildVinResponse(BlockId.Vin2, "ABCDEF");
            byte[] response3 = BuildVinResponse(BlockId.Vin3, "123456");

            response2[4] = 0xFF; // wrong block id

            Response<string> result = protocol.ParseVinResponses(response1, response2, response3);

            Assert.AreEqual(ResponseStatus.UnexpectedResponse, result.Status);
        }

        [TestMethod]
        public void ParseVinResponses_TruncatedHeader_ReturnsTruncated()
        {
            Protocol protocol = new Protocol();

            byte[] response1 = new byte[] { Priority.Physical0, DeviceId.Tool, DeviceId.Pcm, (byte)(Mode.ReadBlock + Mode.Response) };
            byte[] response2 = BuildVinResponse(BlockId.Vin2, "ABCDEF");
            byte[] response3 = BuildVinResponse(BlockId.Vin3, "123456");

            Response<string> result = protocol.ParseVinResponses(response1, response2, response3);

            Assert.AreEqual(ResponseStatus.Truncated, result.Status);
        }

        [TestMethod]
        public void ParseVinResponses_CorruptedPayload_ReturnsCorruptedVin()
        {
            Protocol protocol = new Protocol();

            byte[] response1 = BuildVinResponse(BlockId.Vin1, "12345");
            byte[] response2 = BuildVinResponse(BlockId.Vin2, "ABC?EF");
            byte[] response3 = BuildVinResponse(BlockId.Vin3, "123456");

            Response<string> result = protocol.ParseVinResponses(response1, response2, response3);

            Assert.AreEqual(ResponseStatus.Success, result.Status);
            Assert.AreEqual("12345ABC?EF123456", result.Value);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ParseVinResponses_TruncatedPayload_Throws()
        {
            Protocol protocol = new Protocol();

            // Valid headers but payloads too short to satisfy BlockCopy lengths.
            byte[] response1 = new byte[] { Priority.Physical0, DeviceId.Tool, DeviceId.Pcm, (byte)(Mode.ReadBlock + Mode.Response), BlockId.Vin1, 0x00, 0x41 };
            byte[] response2 = new byte[] { Priority.Physical0, DeviceId.Tool, DeviceId.Pcm, (byte)(Mode.ReadBlock + Mode.Response), BlockId.Vin2, 0x42 };
            byte[] response3 = new byte[] { Priority.Physical0, DeviceId.Tool, DeviceId.Pcm, (byte)(Mode.ReadBlock + Mode.Response), BlockId.Vin3, 0x43 };

            _ = protocol.ParseVinResponses(response1, response2, response3);
        }
    }
}

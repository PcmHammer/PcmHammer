using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace PcmHammer.Tests
{
    [TestClass]
    public class CrcTests
    {
        [TestMethod]
        public void Crc32_SequentialBytes_512Kb()
        {
            const int length = 512 * 1024;
            byte[] buffer = new byte[length];
            for (int i = 0; i < length; i++)
            {
                buffer[i] = (byte)(i % 256);
            }

            Crc crc = new Crc();
            uint actual = crc.GetCrc(buffer, 0, (uint)length);

            // Precomputed for 512KB buffer of 0x00..0xFF repeating.
            const uint expected = 0x6BFCE89E;
            Assert.AreEqual(expected, actual);
        }
    }
}

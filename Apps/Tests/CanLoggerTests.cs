using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tests
{
    [TestClass]
    public class CanLoggerTests
    {
        private static ParameterDatabase parameterDatabase = new ParameterDatabase(CanLoggerTests.CreateMockCanParameters());

        private static Dictionary<UInt32, IEnumerable<CanParameter>> CreateMockCanParameters()
        {
            Conversion simpleConversion = new Conversion("test", "x", "0.00");
            Conversion[] conversionArray = new Conversion[] { simpleConversion };

            CanParameter param1 = new CanParameter(0x01, 0, 2, true, "1", "1", "1", conversionArray, Aggregation.LastWins);
            CanParameter param21 = new CanParameter(0x02, 0, 2, true, "21", "21", "21", conversionArray, Aggregation.Sum);
            CanParameter param22 = new CanParameter(0x02, 2, 2, true, "22", "22", "22", conversionArray, Aggregation.Average);

            Dictionary<UInt32, IEnumerable<CanParameter>> result = new Dictionary<UInt32, IEnumerable<CanParameter>>();
            result.Add(0x01, new CanParameter[] { param1 });
            result.Add(0x02, new CanParameter[] {  param21, param22 });
            return result;
        }

        [TestMethod]
        public void SimpleMessage_Unknown()
        {
            byte[] data = { 0xAA, 0xE2, 0x21, 0x30, 0x03, 0x01, 0x11, 0x22, 0x55 };
            CanLogger logger = new CanLogger(parameterDatabase, new MockLogger());
            logger.UseDatabaseKeys();
            logger.DataReceived(data, data.Length);

            IEnumerable<CanLogger.ParameterAndValue> results = logger.GetParameterValues();
            Assert.IsNotNull(results);
            CanLogger.ParameterAndValue result = results.FirstOrDefault();
            Assert.IsNull(result);

            // If we wanted to log unexpected messages, we could... not sure it's a good idea though.
        }

        [TestMethod]
        public void SimpleMessage_Known()
        {
            byte[] data = { 0xAA, 0xE2, 0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x55 };
            CanLogger logger = new CanLogger(parameterDatabase, new MockLogger());
            logger.UseDatabaseKeys();
            logger.DataReceived(data, data.Length);

            IEnumerable<CanLogger.ParameterAndValue> results = logger.GetParameterValues();
            Assert.IsNotNull(results);
            CanLogger.ParameterAndValue result = results.FirstOrDefault();
            Assert.IsNotNull(result);
            Assert.AreEqual("1", result.Parameter.Id, "Id");
            Assert.AreEqual(65535, result.ValueAsNumber, "valueAsNumber");
            Assert.AreEqual("65535.00", result.ValueAsString, "valueAsString");
            Assert.AreEqual("test", result.Units, "Units");
        }

        [TestMethod]
        public void LastWins()
        {
            byte[] data1 = { 0xAA, 0xE2, 0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x55 };
            byte[] data2 = { 0xAA, 0xE2, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x55 };
            byte[] data3 = { 0xAA, 0xE2, 0x01, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x55 };

            CanLogger logger = new CanLogger(parameterDatabase, new MockLogger());
            logger.UseDatabaseKeys();
            logger.DataReceived(data1, data1.Length);
            logger.DataReceived(data2, data2.Length);
            logger.DataReceived(data3, data3.Length);

            IEnumerable<CanLogger.ParameterAndValue> results = logger.GetParameterValues();
            Assert.IsNotNull(results);
            CanLogger.ParameterAndValue result = results.FirstOrDefault();
            Assert.IsNotNull(result);
            Assert.AreEqual("1", result.Parameter.Id, "Id");
            Assert.AreEqual(255, result.ValueAsNumber, "valueAsNumber");
            Assert.AreEqual("255.00", result.ValueAsString, "valueAsString");
            Assert.AreEqual("test", result.Units, "Units");
        }

        [TestMethod]
        public void SumAndAverage()
        {
            byte[] data1 = { 0xAA, 0xE4, 0x02, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x02, 0x55 };
            byte[] data2 = { 0xAA, 0xE4, 0x02, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x04, 0x55 };
            byte[] data3 = { 0xAA, 0xE4, 0x02, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x06, 0x55 };

            CanLogger logger = new CanLogger(parameterDatabase, new MockLogger());
            logger.UseDatabaseKeys();
            logger.DataReceived(data1, data1.Length);
            logger.DataReceived(data2, data2.Length);
            logger.DataReceived(data3, data3.Length);

            IEnumerable<CanLogger.ParameterAndValue> temp = logger.GetParameterValues();
            IList<CanLogger.ParameterAndValue> array = temp.ToArray();
            
            CanLogger.ParameterAndValue result21 = array.FirstOrDefault(x => x.Parameter.Id == "21");
            CanLogger.ParameterAndValue result22 = array.FirstOrDefault(x => x.Parameter.Id == "22");

            Assert.IsNotNull(result21);
            Assert.AreEqual("21", result21.Parameter.Id, "Id");
            Assert.AreEqual(6, result21.ValueAsNumber, "valueAsNumber");
            Assert.AreEqual("6.00", result21.ValueAsString, "valueAsString");
            Assert.AreEqual("test", result21.Units, "Units");

            Assert.IsNotNull(result22);
            Assert.AreEqual("22", result22.Parameter.Id, "Id");
            Assert.AreEqual(4, result22.ValueAsNumber, "valueAsNumber");
            Assert.AreEqual("4.00", result22.ValueAsString, "valueAsString");
            Assert.AreEqual("test", result22.Units, "Units");
        }
    }
}

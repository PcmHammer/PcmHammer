// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace Tests
{
    [TestClass]
    public class MathTests
    {
        [TestMethod]
        public void MathValueTest()
        {
            Conversion rpmConversion = new Conversion("RPM", "x", "0");
            LogColumn rpm = new LogColumn(
                new PidParameter("EngSpeed", "Engine Speed", "", "uint16", false, 
                    new Conversion[] { rpmConversion }, 0x3456, new List<uint>()),
                rpmConversion,
                false);

            Conversion mafConversion = new Conversion("RPM", "x", "0");
            LogColumn maf = new LogColumn(
                new PidParameter("MAF", "Mass Air Flow", "", "uint16", false,
                    new Conversion[] { mafConversion }, 0x1234, new List<uint>()),
                mafConversion,
                false);

            MathParameter load = new MathParameter(
                "id",
                "Load",
                "",
                new Conversion[] { new Conversion("g/cyl", "(y*60)/x", "0.00") },
                rpm,
                maf);

            LogColumn mathColumn = new LogColumn(load, load.Conversions.First(), false);

            DpidConfiguration profile = new DpidConfiguration();
            profile.ParameterGroups.Add(new ParameterGroup(0xFE));
            profile.ParameterGroups[0].LogColumns.Add(rpm);
            profile.ParameterGroups[0].LogColumns.Add(maf);

            //MockDevice mockDevice = new MockDevice();
            //MockLogger mockLogger = new MockLogger();
            //Vehicle vehicle = new Vehicle(
            //    new MockDevice(),
            //    new Protocol(),
            //    mockLogger,
            //    new ToolPresentNotifier(mockDevice, mockLogger));
            //Logger logger = new Logger(vehicle, profile, mathValueConfiguration);

            //MathValueConfigurationLoader loader = new MathValueConfigurationLoader();
            //loader.Initialize();


            PcmParameterValues dpidValues = new PcmParameterValues();
            dpidValues.Add(rpm, new PcmParameterValue() { ValueAsDouble = 1000 });
            dpidValues.Add(maf, new PcmParameterValue() { ValueAsDouble = 100 });

            MathColumnAndDependencies dependencies = new MathColumnAndDependencies(mathColumn, rpm, maf);
            
            MathValueProcessor processor = new MathValueProcessor(profile, new MathColumnAndDependencies[] { dependencies });
            IEnumerable<string> mathValues = processor.GetMathValues(dpidValues);

            Assert.AreEqual(1, mathValues.Count(), "Number of math values.");
            string loadValue = mathValues.First();
            Assert.AreEqual("6.00", loadValue, "Load value.");
        }

        /// <summary>
        /// A math column whose input the PCM refused is dropped rather than left to fail per row.
        /// </summary>
        /// <remarks>
        /// Refused parameters are taken out of their DPID group as logging starts, which used to
        /// leave the math column looking up a value nobody was reading. That surfaced against the
        /// parameter as "the given key was not present in the dictionary", once per row.
        /// </remarks>
        [TestMethod]
        public void MathColumnIsDroppedWhenAnInputIsNotLogged()
        {
            MathFixture fixture = new MathFixture();

            // As ConfigureDpids leaves it when the module refuses mass airflow.
            fixture.Profile.ParameterGroups[0].LogColumns.Remove(fixture.Maf);

            IReadOnlyList<Parameter> dropped = fixture.Processor.RemoveColumnsWithMissingDependencies();

            Assert.AreEqual(1, dropped.Count);
            Assert.AreEqual("Load", dropped[0].Name);
            Assert.AreEqual(0, fixture.Processor.GetMathColumns().Count());
            Assert.AreEqual(0, fixture.Processor.GetHeaderNames().Count());
        }

        [TestMethod]
        public void MathColumnIsKeptWhenBothInputsAreLogged()
        {
            MathFixture fixture = new MathFixture();

            Assert.AreEqual(0, fixture.Processor.RemoveColumnsWithMissingDependencies().Count);
            Assert.AreEqual(1, fixture.Processor.GetMathColumns().Count());
        }

        /// <summary>
        /// The guard behind the drop: a row missing an input reads as blank, not as an exception
        /// message in the column where a number belongs.
        /// </summary>
        [TestMethod]
        public void MissingInputReadsAsBlankRatherThanAnError()
        {
            MathFixture fixture = new MathFixture();

            PcmParameterValues row = new PcmParameterValues();
            row.Add(fixture.Rpm, new PcmParameterValue() { ValueAsDouble = 1000 });

            string value = fixture.Processor.GetMathValues(row).Single();
            Assert.AreEqual(string.Empty, value);

            LogRowElement element = fixture.Processor.GetMathValuesV2(row).Single();
            Assert.AreEqual(string.Empty, element.ValueAsString);
            Assert.IsTrue(double.IsNaN(element.ValueAsNumber));
        }

        /// <summary>One math column over two PCM columns, all three in one DPID group.</summary>
        private class MathFixture
        {
            internal MathFixture()
            {
                Conversion rpmConversion = new Conversion("RPM", "x", "0");
                this.Rpm = new LogColumn(
                    new PidParameter("EngSpeed", "Engine Speed", "", "uint16", false,
                        new[] { rpmConversion }, 0x3456, new List<uint>()),
                    rpmConversion,
                    false);

                Conversion mafConversion = new Conversion("g/s", "x", "0");
                this.Maf = new LogColumn(
                    new PidParameter("MAF", "Mass Air Flow", "", "uint16", false,
                        new[] { mafConversion }, 0x1234, new List<uint>()),
                    mafConversion,
                    false);

                MathParameter load = new MathParameter(
                    "id", "Load", "",
                    new[] { new Conversion("g/cyl", "(y*60)/x", "0.00") },
                    this.Rpm,
                    this.Maf);

                this.Profile = new DpidConfiguration();
                this.Profile.ParameterGroups.Add(new ParameterGroup(0xFE));
                this.Profile.ParameterGroups[0].LogColumns.Add(this.Rpm);
                this.Profile.ParameterGroups[0].LogColumns.Add(this.Maf);

                MathColumnAndDependencies dependencies = new MathColumnAndDependencies(
                    new LogColumn(load, load.Conversions.First(), false), this.Rpm, this.Maf);

                this.Processor = new MathValueProcessor(this.Profile, new[] { dependencies });
            }

            internal LogColumn Rpm { get; }

            internal LogColumn Maf { get; }

            internal DpidConfiguration Profile { get; }

            internal MathValueProcessor Processor { get; }
        }
    }
}

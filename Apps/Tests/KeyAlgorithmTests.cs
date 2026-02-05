using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace PcmHammer.Tests
{
    [TestClass]
    public class KeyAlgorithmTests
    {
        private class AlgoCase
        {
            public int Algo { get; set; }
            public ushort Seed { get; set; }
            public ushort Expected { get; set; }
            public string Description { get; set; }
        }

        [TestMethod]
        public void GetKey_KnownAlgorithms_MatchExpected()
        {
            // Seed/key pairs generated using KeyAlgorithm.GetKey with the current logic.
            // Each case includes the algorithm's description comment from KeyAlgorithm.bytearray1.
            // This selection should excersize most or all of the algo mechenism
            List<AlgoCase> cases = new List<AlgoCase>
            {
                // #6 P04 Early
                new AlgoCase { Algo = 6, Seed = 0x0001, Expected = 0xF406, Description = "P04 Early" },
                new AlgoCase { Algo = 6, Seed = 0x1234, Expected = 0x2BBE, Description = "P04 Early" },
                new AlgoCase { Algo = 6, Seed = 0xBEEF, Expected = 0x3D0B, Description = "P04 Early" },

                // #14 P04 Late
                new AlgoCase { Algo = 14, Seed = 0x0001, Expected = 0x394F, Description = "P04 Late" },
                new AlgoCase { Algo = 14, Seed = 0x1234, Expected = 0xD1C0, Description = "P04 Late" },
                new AlgoCase { Algo = 14, Seed = 0xBEEF, Expected = 0xAF26, Description = "P04 Late" },

                // #16 Vortec Black Box
                new AlgoCase { Algo = 16, Seed = 0x0001, Expected = 0x30C1, Description = "Vortec Black Box" },
                new AlgoCase { Algo = 16, Seed = 0x1234, Expected = 0x53F2, Description = "Vortec Black Box" },
                new AlgoCase { Algo = 16, Seed = 0xBEEF, Expected = 0x1FAD, Description = "Vortec Black Box" },

                // #40 P01/P59
                new AlgoCase { Algo = 40, Seed = 0x0001, Expected = 0x924D, Description = "P01/P59" },
                new AlgoCase { Algo = 40, Seed = 0x1234, Expected = 0x5F3B, Description = "P01/P59" },
                new AlgoCase { Algo = 40, Seed = 0xBEEF, Expected = 0xA48F, Description = "P01/P59" },

                // #53 P05
                new AlgoCase { Algo = 53, Seed = 0x0001, Expected = 0xBAE5, Description = "P05" },
                new AlgoCase { Algo = 53, Seed = 0x1234, Expected = 0xDC04, Description = "P05" },
                new AlgoCase { Algo = 53, Seed = 0xBEEF, Expected = 0x96D0, Description = "P05" },

                // #54 E54
                new AlgoCase { Algo = 54, Seed = 0x0001, Expected = 0x8058, Description = "E54" },
                new AlgoCase { Algo = 54, Seed = 0x1234, Expected = 0x4F39, Description = "E54" },
                new AlgoCase { Algo = 54, Seed = 0xBEEF, Expected = 0x946D, Description = "E54" },

                // #66 P10
                new AlgoCase { Algo = 66, Seed = 0x0001, Expected = 0xAF44, Description = "P10" },
                new AlgoCase { Algo = 66, Seed = 0x1234, Expected = 0x4921, Description = "P10" },
                new AlgoCase { Algo = 66, Seed = 0xBEEF, Expected = 0xD1C8, Description = "P10" },

                // #91 P12
                new AlgoCase { Algo = 91, Seed = 0x0001, Expected = 0x611E, Description = "P12" },
                new AlgoCase { Algo = 91, Seed = 0x1234, Expected = 0x7BA8, Description = "P12" },
                new AlgoCase { Algo = 91, Seed = 0xBEEF, Expected = 0xD8FE, Description = "P12" },
            };

            foreach (AlgoCase testCase in cases)
            {
                ushort actual = KeyAlgorithm.GetKey(testCase.Algo, testCase.Seed);
                Assert.AreEqual(
                    testCase.Expected,
                    actual,
                    $"{testCase.Description} algo #{testCase.Algo} seed 0x{testCase.Seed:X4}");
            }
        }

        [TestMethod]
        // This test is to handle the case where a param block is erased. If seed is FFFF it is
        // likely the whole block is FFFF and so the key will be FFFF as well. This allows recovery
        public void GetKey_NonStandardSeed_ReturnsFFFF()
        {
            ushort actual = KeyAlgorithm.GetKey(6, 0xFFFF);
            Assert.AreEqual((ushort)0xFFFF, actual);
        }
    }
}

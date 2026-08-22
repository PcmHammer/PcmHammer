// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// Round-trip and integrity coverage for the package format library (PcmPackage / PackageStore /
    /// Phz + Bin formats). Pure file I/O - no hardware - so it runs in the normal unit-test suite.
    /// </summary>
    [TestClass]
    public class PackageFormatTests
    {
        private string dir = null!;

        [TestInitialize]
        public void Setup()
        {
            this.dir = Path.Combine(Path.GetTempPath(), "phztest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.dir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(this.dir, true); } catch { }
        }

        private static byte[] Pattern(int size, byte seed)
        {
            var b = new byte[size];
            for (int i = 0; i < size; i++) b[i] = (byte)((i * 31 + seed) & 0xFF);
            return b;
        }

        private static PcmPackage BuildTwoController()
        {
            return new PcmPackage
            {
                Generator = "tests",
                Created = DateTime.UtcNow.ToString("o"),
                Vehicle = new PackageVehicle { Description = "2009 Corvette", Vin = "1G1YY26W895000000" },
                Notes = "round-trip fixture",
                Controllers =
                {
                    new PackageController
                    {
                        Id = 1, Type = "PCM", ModuleType = "E38", BusId = "0x7E0",
                        Images =
                        {
                            new PackageImage { Target = "main",              FileName = "main.bin",     Data = Pattern(65536, 1), Osid = 12628990 },
                            new PackageImage { Target = "slave-os",          FileName = "12625892.bin", Data = Pattern(26752, 2), PartNumber = 12625892 },
                            new PackageImage { Target = "slave-calibration", FileName = "12629150.bin", Data = Pattern(1536, 3),  PartNumber = 12629150 },
                        }
                    },
                    new PackageController
                    {
                        Id = 2, Type = "TCM", ModuleType = "T42", BusId = "0x7E1",
                        Images = { new PackageImage { Target = "main", FileName = "main.bin", Data = Pattern(32768, 4) } }
                    }
                }
            };
        }

        [TestMethod]
        public void PhzRoundTrip_PreservesEverything()
        {
            string phz = Path.Combine(this.dir, "vehicle.phz");
            PcmPackage original = BuildTwoController();
            PackageStore.Save(phz, original);

            PcmPackage loaded = PackageStore.Load(phz);
            Assert.AreEqual("pcmhammer/package", loaded.Format);
            Assert.AreEqual(1, loaded.FormatVersion);
            Assert.AreEqual("1G1YY26W895000000", loaded.Vehicle!.Vin);
            Assert.AreEqual("round-trip fixture", loaded.Notes);
            Assert.AreEqual(2, loaded.Controllers.Count);

            PackageController e38 = loaded.Controllers.First(c => c.Id == 1);
            Assert.AreEqual("E38", e38.ModuleType);
            Assert.AreEqual("PCM", e38.Type);
            Assert.AreEqual("0x7E0", e38.BusId);
            Assert.AreEqual(3, e38.Images.Count);
            Assert.AreEqual((uint)12628990, e38.Image("main")!.Osid);
            Assert.AreEqual((uint)12625892, e38.Image("slave-os")!.PartNumber);
            Assert.AreEqual((uint)12629150, e38.Image("slave-calibration")!.PartNumber);

            foreach (PackageController oc in original.Controllers)
            {
                PackageController lc = loaded.Controllers.First(c => c.Id == oc.Id);
                foreach (PackageImage oi in oc.Images)
                {
                    PackageImage? li = lc.Image(oi.Target!);
                    Assert.IsNotNull(li, oi.Target);
                    CollectionAssert.AreEqual(oi.Data, li!.Data, "data for " + oc.Id + "/" + oi.Target);
                    Assert.AreEqual(oi.Data!.Length, li.Size);
                    Assert.IsFalse(string.IsNullOrEmpty(li.Sha256));
                }
            }
        }

        [TestMethod]
        public void PhzArchive_UsesIdSlashFileLayout()
        {
            string phz = Path.Combine(this.dir, "layout.phz");
            PackageStore.Save(phz, BuildTwoController());
            using (ZipArchive za = ZipFile.OpenRead(phz))
            {
                var names = za.Entries.Select(e => e.FullName).ToList();
                CollectionAssert.Contains(names, "manifest.json");
                CollectionAssert.Contains(names, "1/main.bin");
                CollectionAssert.Contains(names, "1/12625892.bin");
                CollectionAssert.Contains(names, "2/main.bin");
            }
        }

        [TestMethod]
        public void BinRoundTrip_LegacySingleImage()
        {
            string bin = Path.Combine(this.dir, "master.bin");
            byte[] data = Pattern(131072, 9);
            var single = new PcmPackage
            {
                Controllers = { new PackageController { Id = 1, Images = { new PackageImage { Target = "main", FileName = "main.bin", Data = data } } } }
            };
            PackageStore.Save(bin, single);
            Assert.AreEqual(data.Length, new FileInfo(bin).Length, "raw bin has no wrapper");

            PcmPackage loaded = PackageStore.Load(bin);
            Assert.AreEqual(1, loaded.Controllers.Count);
            Assert.AreEqual(1, loaded.Controllers[0].Images.Count);
            CollectionAssert.AreEqual(data, loaded.Controllers[0].Image("main")!.Data);
        }

        [TestMethod]
        [ExpectedException(typeof(PackageException))]
        public void BinSave_RefusesToDropSlaveData()
        {
            var e38Only = new PcmPackage { Controllers = { BuildTwoController().Controllers.First(c => c.Id == 1) } };
            PackageStore.Save(Path.Combine(this.dir, "lossy.bin"), e38Only);
        }

        [TestMethod]
        public void BinSave_AllowsDroppingSlaveReferences()
        {
            // A fresh E38 read is master bytes + slave *references* (no bytes). Saving that as .bin is
            // allowed: it writes the master and drops the reference metadata (which is what choosing .bin
            // over .phz means). Only a non-main image that carries actual bytes would be refused.
            byte[] master = Pattern(131072, 7);
            var package = new PcmPackage
            {
                Controllers =
                {
                    new PackageController
                    {
                        Id = 1, ModuleType = "E38",
                        Images =
                        {
                            new PackageImage { Target = "main", FileName = "main.bin", Data = master },
                            PackageImage.Reference("slave-os", "12625892.bin", 12625892),
                            PackageImage.Reference("slave-calibration", "12629150.bin", 12629150),
                        }
                    }
                }
            };
            string bin = Path.Combine(this.dir, "master-only.bin");
            PackageStore.Save(bin, package);
            Assert.AreEqual(master.Length, new FileInfo(bin).Length, "raw bin is just the master image");
            CollectionAssert.AreEqual(master, PackageStore.Load(bin).Controllers[0].Image("main")!.Data);
        }

        [TestMethod]
        [ExpectedException(typeof(PackageException))]
        public void BinSave_RefusesMultipleControllers()
        {
            PackageStore.Save(Path.Combine(this.dir, "multi.bin"), BuildTwoController());
        }

        [TestMethod]
        [ExpectedException(typeof(PackageException))]
        public void Load_TamperedImage_Throws()
        {
            string phz = Path.Combine(this.dir, "tampered.phz");
            PackageStore.Save(phz, BuildTwoController());
            using (ZipArchive za = ZipFile.Open(phz, ZipArchiveMode.Update))
            {
                ZipArchiveEntry entry = za.GetEntry("1/main.bin");
                byte[] b;
                using (var s = entry.Open()) { using (var ms = new MemoryStream()) { s.CopyTo(ms); b = ms.ToArray(); } }
                b[100] ^= 0xFF; // same length, different content -> hash mismatch vs manifest
                using (var s = entry.Open()) { s.SetLength(0); s.Write(b, 0, b.Length); }
            }
            PackageStore.Load(phz); // must throw
        }

        [TestMethod]
        [ExpectedException(typeof(PackageException))]
        public void Verify_WrongSize_Throws()
        {
            var img = new PackageImage { Target = "main", FileName = "main.bin", Data = Pattern(100, 0) };
            img.RefreshHash();
            img.Size = 999;
            img.Verify(1);
        }

        [TestMethod]
        [ExpectedException(typeof(PackageException))]
        public void Load_UnknownExtension_Throws()
        {
            string path = Path.Combine(this.dir, "foo.txt");
            File.WriteAllText(path, "not a package");
            PackageStore.Load(path);
        }

        [TestMethod]
        public void PhzRoundTrip_ReferenceImages_DeclaredButNotStored()
        {
            string phz = Path.Combine(this.dir, "refs.phz");
            var package = new PcmPackage
            {
                Controllers =
                {
                    new PackageController
                    {
                        Id = 1, Type = "PCM", ModuleType = "E38",
                        Images =
                        {
                            new PackageImage { Target = "main", FileName = "main.bin", Data = Pattern(65536, 1), Osid = 12628990 },
                            PackageImage.Reference("slave-os", "12625892.bin", 12625892),
                            PackageImage.Reference("slave-calibration", "12629150.bin", 12629150),
                        }
                    }
                }
            };
            PackageStore.Save(phz, package);

            // The reference files are NOT in the archive; the main image is.
            using (ZipArchive za = ZipFile.OpenRead(phz))
            {
                var names = za.Entries.Select(e => e.FullName).ToList();
                CollectionAssert.Contains(names, "1/main.bin");
                CollectionAssert.DoesNotContain(names, "1/12625892.bin");
                CollectionAssert.DoesNotContain(names, "1/12629150.bin");

                // The manifest declares references by name/part number.
                ZipArchiveEntry manifest = za.GetEntry("manifest.json");
                string json;
                using (var reader = new StreamReader(manifest.Open())) { json = reader.ReadToEnd(); }
                StringAssert.Contains(json, "12625892.bin");
            }

            PcmPackage loaded = PackageStore.Load(phz);
            PackageController c = loaded.Controllers[0];
            Assert.IsFalse(c.Image("main")!.IsReference, "main is embedded");
            PackageImage slaveOs = c.Image("slave-os")!;
            Assert.IsTrue(slaveOs.IsReference, "slave-os is a reference");
            Assert.IsNull(slaveOs.Data);
            Assert.IsNull(slaveOs.Sha256, "reference carries no checksum");
            Assert.AreEqual(0, slaveOs.Size, "reference carries no size");
            Assert.AreEqual((uint)12625892, slaveOs.PartNumber);
            Assert.AreEqual("12625892.bin", slaveOs.FileName);
        }

        [TestMethod]
        public void SlaveLibrary_ResolvesReferencesFromDirectory()
        {
            // A local library directory with one of the two slave files present.
            string lib = Path.Combine(this.dir, "lib");
            Directory.CreateDirectory(lib);
            byte[] osBytes = Pattern(26752, 7);
            File.WriteAllBytes(Path.Combine(lib, "12625892.bin"), osBytes);
            SlaveLibrary.AddSearchDirectory(lib);

            Assert.IsNotNull(SlaveLibrary.Find("12625892.bin"));
            CollectionAssert.AreEqual(osBytes, SlaveLibrary.Resolve("12625892.bin")!);
            Assert.IsNull(SlaveLibrary.Find("99999999.bin"));

            var package = new PcmPackage
            {
                Controllers =
                {
                    new PackageController
                    {
                        Id = 1, ModuleType = "E38",
                        Images =
                        {
                            new PackageImage { Target = "main", FileName = "main.bin", Data = Pattern(1024, 1) },
                            PackageImage.Reference("slave-os", "12625892.bin", 12625892),          // present
                            // Not a shipped file: every real name now resolves from the archive
                            // embedded in PcmLibrary, so only an unknown one is genuinely absent.
                            PackageImage.Reference("slave-calibration", "99999999.bin", 99999999), // absent
                        }
                    }
                }
            };

            IList<PackageImage> missing = SlaveLibrary.ResolveReferences(package);
            Assert.AreEqual(1, missing.Count, "one reference is not in the library");
            Assert.AreEqual("99999999.bin", missing[0].FileName);
            CollectionAssert.AreEqual(osBytes, package.Controllers[0].Image("slave-os")!.Data, "resolved reference got its bytes");
            Assert.IsNull(package.Controllers[0].Image("slave-calibration")!.Data, "unresolved reference stays empty");
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace Tests
{
    /// <summary>
    /// Runs FileValidator over a corpus of real PCM images (Apps/Tests/TestData), each named
    /// [PcmType]_[size]_[OSID].bin - the file name IS the expected result. The four checks run over the
    /// whole corpus in order: (1) type identification, (2) OSID, (3) checksum, (4) CVN (only where it
    /// applies, i.e. E38). This is the regression guard for detection: a new file type whose signature is
    /// too loose will grab one of these known files first and fail Check 1. Add a variant by dropping a
    /// correctly-named .bin into TestData - no code change needed.
    /// </summary>
    [TestClass]
    public class FileValidatorTests
    {
        // Order within a size class, matching the order DetectFileType tries the types, so a run reads
        // like the validator's own decision path.
        private static readonly string[] DetectionOrder =
        {
            "E54", "BlackBox", "P01", "P04", "P04_Early", "P10", "P11", "P08",
            "P59", "P05c", "P05b", "P05", "P12", "E38",
        };

        private sealed class Sample
        {
            public string Name = string.Empty;
            public byte[] Bytes = Array.Empty<byte>();
            public PcmType Type;
            public uint Osid;
        }

        private static List<Sample> LoadCorpus()
        {
            string dir = FindTestDataDir();
            var samples = new List<Sample>();
            foreach (string path in Directory.GetFiles(dir, "*.bin"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                // [Type]_[size]_[OSID]; Type may contain '_' (P04_Early), so read the OSID + size from the end.
                string[] parts = name.Split('_');
                Assert.IsTrue(parts.Length >= 3, "Bad test file name (expected [Type]_[size]_[OSID].bin): " + name);
                uint osid = uint.Parse(parts[parts.Length - 1]);
                string typeName = string.Join("_", parts.Take(parts.Length - 2));
                PcmType type = (PcmType)Enum.Parse(typeof(PcmType), typeName, ignoreCase: true);
                samples.Add(new Sample { Name = name, Bytes = File.ReadAllBytes(path), Type = type, Osid = osid });
            }

            Assert.IsTrue(samples.Count > 0, "No .bin files in TestData: " + dir);
            return samples
                .OrderBy(s => s.Bytes.Length)
                .ThenBy(s => Array.IndexOf(DetectionOrder, s.Type.ToString()))
                .ToList();
        }

        private static string FindTestDataDir()
        {
            for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string candidate = Path.Combine(d.FullName, "TestData");
                if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.bin").Length > 0)
                {
                    return candidate;
                }
            }
            throw new InvalidOperationException("TestData folder with .bin files not found above " + AppContext.BaseDirectory);
        }

        [TestMethod]
        public void Check_1_FileTypeIdentification()
        {
            var failures = new List<string>();
            foreach (Sample s in LoadCorpus())
            {
                PcmType detected = new FileValidator(s.Bytes, new MockLogger()).GetFileType();
                if (detected != s.Type)
                {
                    failures.Add(string.Format("{0}: expected {1}, detected {2}", s.Name, s.Type, detected));
                }
            }
            Assert.IsTrue(failures.Count == 0, "Type misidentification:\n" + string.Join("\n", failures));
        }

        [TestMethod]
        public void Check_2_OsidIdentification()
        {
            var failures = new List<string>();
            foreach (Sample s in LoadCorpus())
            {
                uint osid = new FileValidator(s.Bytes, new MockLogger()).GetOsidFromImage();
                if (osid != s.Osid)
                {
                    failures.Add(string.Format("{0}: expected OSID {1}, got {2}", s.Name, s.Osid, osid));
                }
            }
            Assert.IsTrue(failures.Count == 0, "OSID mismatch:\n" + string.Join("\n", failures));
        }

        [TestMethod]
        public void Check_3_Checksums()
        {
            var failures = new List<string>();
            foreach (Sample s in LoadCorpus())
            {
                // Force the known type so this is purely a checksum check, independent of detection (Check 1).
                bool ok = new FileValidator(s.Bytes, new MockLogger(), s.Type).IdentifyAndValidate();
                if (!ok)
                {
                    failures.Add(string.Format("{0}: checksum validation failed", s.Name));
                }
            }
            Assert.IsTrue(failures.Count == 0, "Checksum failures:\n" + string.Join("\n", failures));
        }

        [TestMethod]
        public void Check_4_Cvn()
        {
            var failures = new List<string>();
            foreach (Sample s in LoadCorpus())
            {
                FileValidator.SumCvnVerdict verdict = new FileValidator(s.Bytes, new MockLogger()).ValidateSumAndCvn();
                if (verdict == FileValidator.SumCvnVerdict.NotApplicable)
                {
                    continue; // CVN only applies to types that carry one (E38).
                }
                if (verdict != FileValidator.SumCvnVerdict.Good)
                {
                    failures.Add(string.Format("{0}: CVN verdict {1} (expected Good)", s.Name, verdict));
                }
            }
            Assert.IsTrue(failures.Count == 0, "CVN failures:\n" + string.Join("\n", failures));
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;

using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// The slave library is packed into one deflate stream by PcmLibrary.csproj and unpacked by
    /// SlaveLibrary. Nothing else exercises that round trip, and a packing bug would only surface as a
    /// failed E38 write against real hardware.
    /// </summary>
    [TestClass]
    public class SlaveLibraryArchiveTests
    {
        /// <summary>The repo's SlaveLibrary folder, found by walking up from the test binaries.</summary>
        private static string? SourceDirectory
        {
            get
            {
                DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "SlaveLibrary");
                    if (Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
                return null;
            }
        }

        private static string[] SourceFiles =>
            SourceDirectory == null ? Array.Empty<string>() : Directory.GetFiles(SourceDirectory, "*.bin");

        [TestMethod]
        public void EveryFileRoundTripsByteForByte()
        {
            string? source = SourceDirectory;
            if (source == null)
            {
                Assert.Inconclusive("SlaveLibrary folder not found above " + AppContext.BaseDirectory);
            }

            string[] files = SourceFiles;
            Assert.IsTrue(files.Length > 0, "No slave library files to check.");

            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                byte[]? unpacked = SlaveLibrary.Resolve(name);

                Assert.IsNotNull(unpacked, name + " did not resolve from the embedded archive.");
                CollectionAssert.AreEqual(File.ReadAllBytes(file), unpacked, name + " came back with different bytes.");
            }
        }

        [TestMethod]
        public void NamesAreCaseInsensitive()
        {
            string[] files = SourceFiles;
            if (files.Length == 0)
            {
                Assert.Inconclusive("SlaveLibrary folder not found.");
            }

            string name = Path.GetFileName(files[0]);
            Assert.IsNotNull(SlaveLibrary.Resolve(name.ToUpperInvariant()));
            Assert.IsNotNull(SlaveLibrary.Resolve(name.ToLowerInvariant()));
        }

        [TestMethod]
        public void UnknownNameResolvesToNull()
        {
            Assert.IsNull(SlaveLibrary.Resolve("00000000.bin"));
        }

        [TestMethod]
        public void BlankNameResolvesToNull()
        {
            Assert.IsNull(SlaveLibrary.Resolve(null));
            Assert.IsNull(SlaveLibrary.Resolve(string.Empty));
            Assert.IsNull(SlaveLibrary.Resolve("   "));
        }

        [TestMethod]
        public void RepeatedResolvesReturnTheSameBytes()
        {
            string[] files = SourceFiles;
            if (files.Length == 0)
            {
                Assert.Inconclusive("SlaveLibrary folder not found.");
            }

            string name = Path.GetFileName(files[0]);
            CollectionAssert.AreEqual(SlaveLibrary.Resolve(name), SlaveLibrary.Resolve(name));
        }

        /// <summary>The boot libraries moved to the kernel build, so they must not still be here.</summary>
        [TestMethod]
        public void BootLibrariesAreNotInTheSlaveLibrary()
        {
            Assert.IsNull(SlaveLibrary.Resolve("e38-master.bin"));
            Assert.IsNull(SlaveLibrary.Resolve("e38-slave.bin"));
            Assert.IsNull(SlaveLibrary.Resolve("BootLib-E38-Master.bin"));
            Assert.IsNull(SlaveLibrary.Resolve("BootLib-E38-Slave.bin"));
        }
    }
}

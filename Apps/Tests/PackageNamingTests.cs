// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;

using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// Coverage for the default save-name rule (PackageStore.DefaultBaseName). It lives in the library
    /// so every UI suggests the same file name; before that each front end invented its own, and one of
    /// them handed its window-title text to the save dialog and produced "Untitled (unsaved read).phz".
    /// </summary>
    [TestClass]
    public class PackageNamingTests
    {
        private string dir = null!;

        [TestInitialize]
        public void CreateTempDirectory()
        {
            this.dir = Path.Combine(Path.GetTempPath(), "PcmHammerNaming_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.dir);
        }

        [TestCleanup]
        public void RemoveTempDirectory()
        {
            try
            {
                if (Directory.Exists(this.dir)) Directory.Delete(this.dir, true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test run over.
            }
        }

        private static string Today => DateTime.Now.ToString("yyyyMMdd");

        private static PcmPackage PackageWith(string? moduleType, string? type, uint? osid)
        {
            var controller = new PackageController { Id = 1, Type = type, ModuleType = moduleType };
            controller.Images.Add(new PackageImage { Target = "main", FileName = "main.bin", Osid = osid });
            return new PcmPackage { Controllers = { controller } };
        }

        private static PcmPackage E38 => PackageWith("E38", "PCM", 12628990);

        private void Touch(string fileName) => File.WriteAllBytes(Path.Combine(this.dir, fileName), new byte[] { 0 });

        // ---- Name shape ----

        [TestMethod]
        public void UnsavedRead_UsesModuleOsidDateAndSequence()
        {
            Assert.AreEqual("E38_12628990_" + Today + "_1", PackageStore.DefaultBaseName(E38, null, this.dir));
        }

        [TestMethod]
        public void AlreadySaved_KeepsExistingNameWithoutExtension()
        {
            // An existing document keeps its own name; it must not start a new sequence.
            Assert.AreEqual("my read", PackageStore.DefaultBaseName(E38, Path.Combine(this.dir, "my read.phz"), this.dir));
        }

        [TestMethod]
        public void NoOsid_FallsBackToModuleAndDate()
        {
            Assert.AreEqual("E38_" + Today + "_1", PackageStore.DefaultBaseName(PackageWith("E38", "PCM", null), null, this.dir));
        }

        [TestMethod]
        public void NoModuleType_FallsBackToControllerType()
        {
            Assert.AreEqual("PCM_12628990_" + Today + "_1", PackageStore.DefaultBaseName(PackageWith(null, "PCM", 12628990), null, this.dir));
        }

        [TestMethod]
        public void NoPackageAtAll_StillProducesAUsableName()
        {
            // The Uno read picker runs before the read, so it has nothing to name the file after.
            Assert.AreEqual("PCM_" + Today + "_1", PackageStore.DefaultBaseName(null, null, this.dir));
        }

        [TestMethod]
        public void ModuleTypeWithPathCharacters_IsSanitized()
        {
            // Module type comes from the package manifest, so it is not guaranteed to be a legal name.
            string name = PackageStore.DefaultBaseName(PackageWith(@"E38/bad:name", "PCM", 1), null, this.dir);
            StringAssert.StartsWith(name, "E38_bad_name_1_");
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                Assert.IsFalse(name.IndexOf(c) >= 0, "Name still contains an invalid character.");
            }
        }

        [TestMethod]
        public void BlankPathIsTreatedAsUnsaved()
        {
            Assert.AreEqual("E38_12628990_" + Today + "_1", PackageStore.DefaultBaseName(E38, "   ", this.dir));
        }

        // ---- Sequence ----

        [TestMethod]
        public void SequenceSkipsNamesAlreadyOnDisk()
        {
            string stem = "E38_12628990_" + Today;
            this.Touch(stem + "_1.phz");
            Assert.AreEqual(stem + "_2", PackageStore.DefaultBaseName(E38, null, this.dir));

            this.Touch(stem + "_2.phz");
            Assert.AreEqual(stem + "_3", PackageStore.DefaultBaseName(E38, null, this.dir));
        }

        [TestMethod]
        public void SequenceIsClaimedAcrossEverySupportedExtension()
        {
            // A .bin at sequence 1 must push a .phz save to 2, so one sequence number means one read
            // whichever format it was saved in.
            string stem = "E38_12628990_" + Today;
            this.Touch(stem + "_1.bin");
            Assert.AreEqual(stem + "_2", PackageStore.DefaultBaseName(E38, null, this.dir));
        }

        [TestMethod]
        public void SequenceFillsTheFirstGap()
        {
            string stem = "E38_12628990_" + Today;
            this.Touch(stem + "_1.phz");
            this.Touch(stem + "_3.phz");
            Assert.AreEqual(stem + "_2", PackageStore.DefaultBaseName(E38, null, this.dir));
        }

        [TestMethod]
        public void UnrelatedFilesDoNotAdvanceTheSequence()
        {
            this.Touch("something else.phz");
            this.Touch("E38_99999999_" + Today + "_1.phz");
            Assert.AreEqual("E38_12628990_" + Today + "_1", PackageStore.DefaultBaseName(E38, null, this.dir));
        }

        [TestMethod]
        public void UnknownDirectory_StillSequencesFromOne()
        {
            Assert.AreEqual("E38_12628990_" + Today + "_1", PackageStore.DefaultBaseName(E38, null, null));
        }

        [TestMethod]
        public void MissingDirectory_DoesNotThrow()
        {
            string missing = Path.Combine(this.dir, "no such folder");
            Assert.AreEqual("E38_12628990_" + Today + "_1", PackageStore.DefaultBaseName(E38, null, missing));
        }

        [TestMethod]
        public void MalformedDirectory_DoesNotThrow()
        {
            // A bad setting must not be able to break saving.
            Assert.AreEqual("E38_12628990_" + Today + "_1", PackageStore.DefaultBaseName(E38, null, "::::"));
        }

        // ---- Sequencing follows where reads are actually saved ----

        [TestMethod]
        public void SaveDialogFolderAndSequencedNameAlwaysAgree()
        {
            // The bug this guards: the dialog opened in the folder the user actually saves to while the
            // name was sequenced against a different (or blank) configured folder, so it suggested "_1"
            // over the top of a read already sitting there. The folder a front end opens and the folder
            // the sequence is counted against must be the same one, whatever the caller passes.
            string stem = "E38_12628990_" + Today;
            PackageStore.Save(Path.Combine(this.dir, stem + "_1.phz"), E38);

            foreach (string? configured in new[] { null, "", "::::", Path.GetTempPath() })
            {
                Assert.AreEqual(this.dir, PackageStore.DefaultSaveDirectory(configured),
                    "Dialog must open in the folder packages were last saved to.");
                Assert.AreEqual(stem + "_2", PackageStore.DefaultBaseName(E38, null, configured),
                    "Name must be sequenced against that same folder.");
            }
        }

        [TestMethod]
        public void AfterSaving_SequencesAgainstTheSavedFolderNotThePassedOne()
        {
            // The bug this guards: a UI passes its configured bin folder, but the user saves reads
            // somewhere else. The sequence must follow the real save location so the next read does not
            // reuse "_1" and overwrite the last one. PackageStore.Save records that folder; DefaultBaseName
            // sequences against it in preference to the passed (configured) folder.
            string stem = "E38_12628990_" + Today;
            PackageStore.Save(Path.Combine(this.dir, stem + "_1.phz"), E38);

            string empty = Path.Combine(Path.GetTempPath(), "PcmHammerNamingCfg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(empty);
            try
            {
                Assert.AreEqual(stem + "_2", PackageStore.DefaultBaseName(E38, null, empty));
            }
            finally
            {
                if (Directory.Exists(empty)) Directory.Delete(empty, true);
            }
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using System;

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
        private static string Today => DateTime.Now.ToString("yyyyMMdd");

        private static PcmPackage PackageWith(string? moduleType, string? type, uint? osid)
        {
            var controller = new PackageController { Id = 1, Type = type, ModuleType = moduleType };
            controller.Images.Add(new PackageImage { Target = "main", FileName = "main.bin", Osid = osid });
            return new PcmPackage { Controllers = { controller } };
        }

        [TestMethod]
        public void UnsavedRead_UsesModuleOsidAndDate()
        {
            Assert.AreEqual(
                "E38_12628990_" + Today,
                PackageStore.DefaultBaseName(PackageWith("E38", "PCM", 12628990), null));
        }

        [TestMethod]
        public void AlreadySaved_KeepsExistingNameWithoutExtension()
        {
            Assert.AreEqual(
                "my read",
                PackageStore.DefaultBaseName(PackageWith("E38", "PCM", 12628990), @"C:\bins\my read.phz"));
        }

        [TestMethod]
        public void NoOsid_FallsBackToModuleAndDate()
        {
            Assert.AreEqual(
                "E38_" + Today,
                PackageStore.DefaultBaseName(PackageWith("E38", "PCM", null), null));
        }

        [TestMethod]
        public void NoModuleType_FallsBackToControllerType()
        {
            Assert.AreEqual(
                "PCM_12628990_" + Today,
                PackageStore.DefaultBaseName(PackageWith(null, "PCM", 12628990), null));
        }

        [TestMethod]
        public void NoPackageAtAll_StillProducesAUsableName()
        {
            // The Uno read picker runs before the read, so it has nothing to name the file after.
            Assert.AreEqual("PCM_" + Today, PackageStore.DefaultBaseName(null, null));
        }

        [TestMethod]
        public void ModuleTypeWithPathCharacters_IsSanitized()
        {
            // Module type comes from the package manifest, so it is not guaranteed to be a legal name.
            string name = PackageStore.DefaultBaseName(PackageWith(@"E38/bad:name", "PCM", 1), null);
            StringAssert.StartsWith(name, "E38_bad_name_1_");
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                Assert.IsFalse(name.IndexOf(c) >= 0, "Name still contains an invalid character.");
            }
        }

        [TestMethod]
        public void BlankPathIsTreatedAsUnsaved()
        {
            Assert.AreEqual(
                "E38_12628990_" + Today,
                PackageStore.DefaultBaseName(PackageWith("E38", "PCM", 12628990), "   "));
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;

namespace PcmHacking
{
    /// <summary>
    /// The .phz format: a ZIP of manifest.json plus one folder per controller (named by its id). Every
    /// image is SHA-256-verified on load, so a partial or altered file is rejected. See
    /// Docs/pcmhammer-file-format.md.
    /// </summary>
    public class PhzFormat : IPackageFormat
    {
        private const string ManifestEntryName = "manifest.json";

        public string Name => "PcmHammer package";
        public string Extension => ".phz";

        public bool CanLoad(string path) =>
            string.Equals(Path.GetExtension(path), this.Extension, StringComparison.OrdinalIgnoreCase);

        public PcmPackage Load(Stream stream, string path)
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                ZipArchiveEntry manifestEntry = archive.GetEntry(ManifestEntryName)
                    ?? throw new PackageException("This is not a PcmHammer package - it has no manifest.json.");

                PcmPackage package;
                using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();
                    package = JsonConvert.DeserializeObject<PcmPackage>(json)
                        ?? throw new PackageException("The package manifest is empty or unreadable.");
                }

                foreach (PackageController controller in package.Controllers)
                {
                    foreach (PackageImage image in controller.Images)
                    {
                        string entryPath = ImageEntryPath(controller, image);
                        ZipArchiveEntry? entry = archive.GetEntry(entryPath);

                        if (entry == null)
                        {
                            // No file in the archive. If the manifest recorded a checksum, the bytes were
                            // meant to be here and have gone missing (corrupt package). Otherwise this is a
                            // reference - declared by name/part number, to be resolved from the local
                            // library on write - so leave Data null and move on.
                            if (!string.IsNullOrEmpty(image.Sha256))
                            {
                                throw new PackageException(string.Format(
                                    "The package manifest lists \"{0}\" with a checksum but the file is missing - the package is corrupt.", entryPath));
                            }
                            image.Data = null;
                            continue;
                        }

                        using (var entryStream = entry.Open())
                        using (var memory = new MemoryStream())
                        {
                            entryStream.CopyTo(memory);
                            image.Data = memory.ToArray();
                        }

                        image.Verify(controller.Id);
                    }
                }

                return package;
            }
        }

        public void Save(Stream stream, PcmPackage package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            // Recompute size/hash from the actual bytes so the manifest always matches what we store.
            // Reference images have no bytes (resolved from the library on write); they are declared in
            // the manifest but not stored, and carry no size/checksum.
            foreach (PackageImage image in package.AllImages)
            {
                if (!image.IsReference)
                {
                    image.RefreshHash();
                }
            }

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                string json = JsonConvert.SerializeObject(package, Formatting.Indented);
                WriteTextEntry(archive, ManifestEntryName, json);
                WriteTextEntry(archive, "readme.txt", BuildReadme(package));

                foreach (PackageController controller in package.Controllers)
                {
                    foreach (PackageImage image in controller.Images)
                    {
                        if (image.IsReference)
                        {
                            continue; // declared in the manifest only; no file stored
                        }

                        byte[] data = image.Data!;
                        ZipArchiveEntry entry = archive.CreateEntry(ImageEntryPath(controller, image), CompressionLevel.Optimal);
                        using (var entryStream = entry.Open())
                        {
                            entryStream.Write(data, 0, data.Length);
                        }
                    }
                }
            }
        }

        /// <summary>Archive path for an image: "<controller.Id>/<file>" (e.g. "1/main.bin").</summary>
        private static string ImageEntryPath(PackageController controller, PackageImage image)
        {
            if (string.IsNullOrEmpty(image.FileName))
            {
                throw new PackageException(string.Format(
                    "Controller {0} has an image with no file name.", controller.Id));
            }
            return controller.Id + "/" + image.FileName;
        }

        private static void WriteTextEntry(ZipArchive archive, string name, string text)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(text);
            }
        }

        private static string BuildReadme(PcmPackage package)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PcmHammer package (.phz)");
            sb.AppendLine("========================");
            sb.AppendLine();
            sb.AppendLine("This is a ZIP. manifest.json describes the controllers and images below.");
            sb.AppendLine("Keep the whole file together - the images are checksummed and only valid as a set.");
            sb.AppendLine("Open it with PcmHammer, not by extracting individual files.");
            sb.AppendLine();
            if (package.Vehicle != null && !string.IsNullOrEmpty(package.Vehicle.Description))
            {
                sb.AppendLine("Vehicle: " + package.Vehicle.Description);
            }
            foreach (PackageController controller in package.Controllers)
            {
                sb.AppendLine(string.Format("Controller {0}: {1}", controller.Id, controller.ModuleType ?? controller.Type ?? "?"));
                foreach (PackageImage image in controller.Images)
                {
                    string detail = image.IsReference ? "from local library" : string.Format("{0:N0} bytes", image.Size);
                    sb.AppendLine(string.Format("  {0,-18} {1} ({2})", image.Target, image.FileName, detail));
                }
            }
            return sb.ToString();
        }
    }
}

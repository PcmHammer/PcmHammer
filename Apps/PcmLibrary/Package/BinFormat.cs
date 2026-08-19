// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Linq;

namespace PcmHacking
{
    /// <summary>
    /// The legacy raw .bin: the master image only, mapped to a one-controller/one-"main"-image package.
    /// <see cref="Save"/> refuses a package that would lose slave bytes or a second controller (use .phz).
    /// </summary>
    public class BinFormat : IPackageFormat
    {
        public string Name => "Raw binary";
        public string Extension => ".bin";

        public bool CanLoad(string path) =>
            string.Equals(Path.GetExtension(path), this.Extension, StringComparison.OrdinalIgnoreCase);

        public PcmPackage Load(Stream stream, string path)
        {
            byte[] data;
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                data = memory.ToArray();
            }

            var image = new PackageImage { Target = "main", FileName = "main.bin", Data = data };
            image.RefreshHash();

            return new PcmPackage
            {
                Controllers =
                {
                    new PackageController
                    {
                        Id = 1,
                        // Type/ModuleType are unknown from a raw bin; the write path resolves them from
                        // the connected PCM (or an explicit override), as it always has.
                        Images = { image }
                    }
                }
            };
        }

        public void Save(Stream stream, PcmPackage package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            if (package.Controllers.Count != 1)
            {
                throw new PackageException(
                    ".bin can hold only one controller. Save as .phz to keep every controller.");
            }

            PackageController controller = package.Controllers[0];
            PackageImage? main = controller.Image("main");
            if (main == null)
            {
                throw new PackageException("This package has no \"main\" image to write as a .bin.");
            }

            // A .bin is the master image only, so refuse when another image carries flash data that would
            // be lost. A reference carries no data, and dropping it loses only metadata, which is what
            // choosing .bin over .phz means.
            PackageImage? extraWithData = controller.Images.FirstOrDefault(
                i => !string.Equals(i.Target, "main", StringComparison.OrdinalIgnoreCase) && i.Data != null);
            if (extraWithData != null)
            {
                throw new PackageException(string.Format(
                    ".bin can hold only the main image; \"{0}\" carries data that would be lost. Save as .phz instead.",
                    extraWithData.Target));
            }

            byte[] data = main.Data ?? throw new PackageException("The main image has no data.");
            stream.Write(data, 0, data.Length);
        }
    }
}

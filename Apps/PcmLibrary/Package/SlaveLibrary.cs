// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PcmHacking
{
    /// <summary>
    /// Resolves slave-CPU images a package references by name but does not embed (an E38 slave can be
    /// identified but not read). Bytes come from a "SlaveLibrary" folder next to the app, plus any
    /// directories added via <see cref="AddSearchDirectory"/>.
    /// </summary>
    public static class SlaveLibrary
    {
        private static readonly List<string> extraDirectories = new List<string>();

        /// <summary>The library folder shipped/managed with the app (next to the executable).</summary>
        public static string DefaultDirectory =>
            Path.Combine(AppContext.BaseDirectory, "SlaveLibrary");

        /// <summary>Search this directory first (e.g. a user-configured library path). Null/blank is ignored.</summary>
        public static void AddSearchDirectory(string? directory)
        {
            if (!string.IsNullOrWhiteSpace(directory) && !extraDirectories.Contains(directory!))
            {
                extraDirectories.Insert(0, directory!);
            }
        }

        /// <summary>Every directory searched, in order.</summary>
        public static IEnumerable<string> SearchDirectories =>
            extraDirectories.Concat(new[] { DefaultDirectory });

        /// <summary>Full path of a library file by name (e.g. "12625892.bin"), or null if not present.</summary>
        public static string? Find(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            foreach (string dir in SearchDirectories)
            {
                string candidate = Path.Combine(dir, fileName!);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>
        /// Read a library file's bytes by name, or null if not present. A loose file on disk takes
        /// precedence over a copy embedded in the executable.
        /// </summary>
        public static byte[]? Resolve(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            string? path = Find(fileName);
            return path != null ? File.ReadAllBytes(path) : TryLoadEmbedded(fileName!);
        }

        // Mirrors the kernel loader's embedded-resource fallback: match a resource whose name is the file
        // name, or ends with "." and the file name.
        private static byte[]? TryLoadEmbedded(string fileName)
        {
            System.Reflection.Assembly? assembly = System.Reflection.Assembly.GetEntryAssembly();
            if (assembly == null) return null;

            foreach (string resourceName in assembly.GetManifestResourceNames())
            {
                bool match = string.Equals(resourceName, fileName, StringComparison.OrdinalIgnoreCase)
                    || (resourceName.Length > fileName.Length
                        && resourceName[resourceName.Length - fileName.Length - 1] == '.'
                        && resourceName.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
                if (!match) continue;

                using (Stream? stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    using (MemoryStream buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        return buffer.ToArray();
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Fill in every reference image in the package from the library. Returns the images that could
        /// not be found (empty when all resolved); resolved images get their <see cref="PackageImage.Data"/>.
        /// </summary>
        public static IList<PackageImage> ResolveReferences(PcmPackage package)
        {
            var missing = new List<PackageImage>();
            foreach (PackageImage image in package.AllImages.Where(i => i.IsReference))
            {
                byte[]? data = Resolve(image.FileName);
                if (data == null)
                {
                    missing.Add(image);
                }
                else
                {
                    image.Data = data;
                }
            }
            return missing;
        }
    }
}

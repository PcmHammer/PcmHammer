// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

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

        private const string ArchiveResourceName = "SlaveLibrary.archive";

        private static readonly object archiveLock = new object();
        private static Dictionary<string, byte[]>? archive;

        private static byte[]? TryLoadEmbedded(string fileName)
        {
            LoadArchive();
            return archive != null && archive.TryGetValue(fileName, out byte[] data) ? data : null;
        }

        /// <summary>
        /// Unpacks the archive PcmLibrary.csproj embeds in this assembly: a deflate stream holding a
        /// count, then each name and length, then the blobs in the same order. It lives in this
        /// assembly rather than the entry assembly so every front end gets it from the one reference.
        /// </summary>
        private static void LoadArchive()
        {
            if (archive != null) return;

            lock (archiveLock)
            {
                if (archive != null) return;

                var loaded = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    System.Reflection.Assembly assembly = typeof(SlaveLibrary).Assembly;
                    using (Stream? resource = assembly.GetManifestResourceStream(ArchiveResourceName))
                    {
                        if (resource != null)
                        {
                            using (var deflate = new DeflateStream(resource, CompressionMode.Decompress))
                            using (var raw = new MemoryStream())
                            {
                                deflate.CopyTo(raw);
                                raw.Position = 0;
                                ReadEntries(raw, loaded);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // A corrupt archive must not take the app down; the file simply reads as missing.
                }

                archive = loaded;
            }
        }

        private static void ReadEntries(Stream raw, Dictionary<string, byte[]> into)
        {
            var names = new List<string>();
            var lengths = new List<int>();

            using (var reader = new BinaryReader(raw, Encoding.UTF8, true))
            {
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    names.Add(reader.ReadString());
                    lengths.Add(reader.ReadInt32());
                }

                for (int i = 0; i < count; i++)
                {
                    into[names[i]] = reader.ReadBytes(lengths[i]);
                }
            }
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

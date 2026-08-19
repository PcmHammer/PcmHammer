// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PcmHacking
{
    /// <summary>
    /// The single load/save entry point: a registry of <see cref="IPackageFormat"/> handlers dispatched
    /// by extension (add a format in one spot, <see cref="Register"/>). Pure I/O, shared by every UI.
    /// </summary>
    public static class PackageStore
    {
        private static readonly List<IPackageFormat> Formats = new List<IPackageFormat>();

        static PackageStore()
        {
            Register(new PhzFormat());
            Register(new BinFormat());
        }

        /// <summary>Add (or replace, by extension) a format handler. Extension point for future formats.</summary>
        public static void Register(IPackageFormat format)
        {
            if (format == null) throw new ArgumentNullException(nameof(format));
            Formats.RemoveAll(f => string.Equals(f.Extension, format.Extension, StringComparison.OrdinalIgnoreCase));
            Formats.Add(format);
        }

        /// <summary>Extensions the app can open/save, e.g. for building a file-dialog filter (".phz", ".bin").</summary>
        public static IReadOnlyList<string> SupportedExtensions =>
            Formats.Select(f => f.Extension).ToList();

        /// <summary>Load a package from a file. Dispatches on the file's extension.</summary>
        public static PcmPackage Load(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            IPackageFormat format = FormatFor(path);
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    return format.Load(stream, path);
                }
            }
            catch (PackageException)
            {
                throw; // already has a clear, user-facing message
            }
            catch (Exception ex)
            {
                throw new PackageException(string.Format(
                    "Could not read \"{0}\" as a {1}: {2}", Path.GetFileName(path), format.Name, ex.Message), ex);
            }
        }

        /// <summary>
        /// Load a file (.phz or .bin) and return its main (master flash) image bytes - what a checksum or
        /// identify examines. Extracts the master from a .phz; a .bin is returned as-is. Null when the
        /// package carries no main image with data.
        /// </summary>
        public static byte[]? LoadMainImage(string path)
        {
            PcmPackage package = Load(path);
            foreach (PackageController controller in package.Controllers)
            {
                PackageImage? main = controller.Image("main");
                if (main?.Data != null)
                {
                    return main.Data;
                }
            }
            return null;
        }

        /// <summary>Save a package to a file. Dispatches on the file's extension.</summary>
        public static void Save(string path, PcmPackage package)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (package == null) throw new ArgumentNullException(nameof(package));
            IPackageFormat format = FormatFor(path);
            try
            {
                // Write to a temp file first, then move into place, so an interrupted save never leaves a
                // truncated package where a good one used to be.
                string temp = path + ".tmp";
                using (var stream = File.Create(temp))
                {
                    format.Save(stream, package);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (PackageException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PackageException(string.Format(
                    "Could not write \"{0}\" as a {1}: {2}", Path.GetFileName(path), format.Name, ex.Message), ex);
            }
        }

        /// <summary>
        /// Save a package to an already-open stream, choosing the format from the extension of
        /// <paramref name="nameForExtension"/> (e.g. "read.phz"). For platforms that hand back a writable
        /// stream rather than a path (sandboxed file pickers).
        /// </summary>
        public static void Save(Stream stream, PcmPackage package, string nameForExtension)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (package == null) throw new ArgumentNullException(nameof(package));
            IPackageFormat format = FormatFor(nameForExtension);
            try
            {
                format.Save(stream, package);
            }
            catch (PackageException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PackageException(string.Format(
                    "Could not write \"{0}\" as a {1}: {2}", Path.GetFileName(nameForExtension), format.Name, ex.Message), ex);
            }
        }

        private static IPackageFormat FormatFor(string path)
        {
            IPackageFormat format = Formats.FirstOrDefault(f => f.CanLoad(path));
            if (format == null)
            {
                throw new PackageException(string.Format(
                    "Unsupported file type \"{0}\". Supported: {1}.",
                    Path.GetExtension(path), string.Join(", ", SupportedExtensions)));
            }
            return format;
        }
    }
}

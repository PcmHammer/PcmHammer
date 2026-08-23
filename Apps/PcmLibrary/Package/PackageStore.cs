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

        // Where the last saved package went, remembered across sessions. In-memory value plus the file
        // it is cached in; loaded lazily so the disk is touched at most once per run.
        private static string? lastSaveDirectory;
        private static bool lastSaveDirectoryLoaded;

        private static readonly string LastSaveDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PcmHammer",
            "last-save-directory.txt");

        /// <summary>
        /// The folder the most recently saved package was written to, remembered across sessions, or
        /// null if nothing has been saved yet. Recorded by <see cref="Save(string, PcmPackage)"/>.
        /// </summary>
        public static string? LastSaveDirectory
        {
            get
            {
                if (!lastSaveDirectoryLoaded)
                {
                    lastSaveDirectoryLoaded = true;
                    try
                    {
                        if (File.Exists(LastSaveDirectoryPath))
                        {
                            lastSaveDirectory = File.ReadAllText(LastSaveDirectoryPath).Trim();
                        }
                    }
                    catch (Exception)
                    {
                        // Not worth failing a save over; the caller's directory is used instead.
                    }
                }

                return lastSaveDirectory;
            }
        }

        /// <summary>
        /// Where a package save dialog should open, and the folder the "_N" sequence in
        /// <see cref="DefaultBaseName"/> is counted against: the folder packages were last saved to,
        /// falling back to the caller's configured folder.
        /// </summary>
        /// <remarks>
        /// A front end MUST use this for its dialog's initial directory. The suggested name's sequence
        /// number is only correct for the folder it was counted against, so a dialog that opens anywhere
        /// else can offer a name that silently overwrites an existing read.
        /// </remarks>
        public static string? DefaultSaveDirectory(string? configuredDirectory)
            => DirectoryIsUsable(LastSaveDirectory) ? LastSaveDirectory : configuredDirectory;

        /// <summary>
        /// The base file name (no extension) to suggest when saving a document: the name it already
        /// has, or for a document that has never been saved, one built from what was read -
        /// <c>[PCM]_[OSID]_[Date]_[Seq]</c>, e.g. "E38_12628990_20260821_1".
        /// </summary>
        /// <param name="package">The working document. May be null.</param>
        /// <param name="currentPath">
        /// Where the document currently lives, or null for a document that has never been saved (a
        /// fresh read). Pass null to have the name built from the package.
        /// </param>
        /// <param name="directory">
        /// Where the save dialog will open, so the sequence number can skip names already on disk.
        /// Pass null when the location is not known yet; the name is then simply sequence 1.
        /// </param>
        /// <remarks>
        /// Lives here so the naming rule exists once for every UI. A front end that substituted its own
        /// display text produced files called "Untitled (unsaved read).phz"; the callers now ask for
        /// this instead of inventing a name.
        /// <para>
        /// The sequence number is always present, so several reads of the same PCM on the same day are
        /// distinct files rather than one repeatedly overwritten. It is claimed against every supported
        /// extension, not just the one being saved: a stem is skipped if either "name.phz" or
        /// "name.bin" exists, so sequence N always refers to one read regardless of the format chosen.
        /// This only picks the suggested name - the dialog still prompts before overwriting if the user
        /// types over it.
        /// </para>
        /// </remarks>
        public static string DefaultBaseName(PcmPackage? package, string? currentPath, string? directory)
        {
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                // Already has a name of its own; saving again should keep it, not start a new sequence.
                return Path.GetFileNameWithoutExtension(currentPath);
            }

            PackageController? controller = package?.Controllers?.FirstOrDefault();
            string module = FirstNonBlank(controller?.ModuleType, controller?.Type) ?? "PCM";
            uint? osid = controller?.Image("main")?.Osid;
            string date = DateTime.Now.ToString("yyyyMMdd");

            string name = osid != null
                ? module + "_" + osid + "_" + date
                : module + "_" + date;

            // Count the sequence against the folder the dialog will actually open in, so the suggested
            // name cannot collide with a read already sitting there.
            return NextFreeInSequence(Sanitize(name), DefaultSaveDirectory(directory));
        }

        /// <summary>
        /// Append the lowest sequence number whose name is not already taken in
        /// <paramref name="directory"/>. Numbering starts at 1 and the suffix is always added.
        /// </summary>
        private static string NextFreeInSequence(string stem, string? directory)
        {
            // Nothing to collide with (unknown or missing directory): sequence 1.
            if (!DirectoryIsUsable(directory))
            {
                return stem + "_1";
            }

            // Bounded so a directory in a strange state can never spin here. A thousand reads of one
            // PCM in one day into one folder is not a real case; falling back to a time-stamped
            // suffix keeps the name unique rather than returning one that is known to be taken.
            for (int sequence = 1; sequence <= 1000; sequence++)
            {
                string candidate = stem + "_" + sequence;
                if (!AnySupportedFileExists(directory!, candidate))
                {
                    return candidate;
                }
            }

            return stem + "_" + DateTime.Now.ToString("HHmmss");
        }

        /// <summary>
        /// Record where packages are being saved, so the next suggested name is sequenced against that
        /// folder and the next save dialog opens there - including after a restart. Best effort: if it
        /// cannot be cached, naming falls back to the caller's configured folder.
        /// </summary>
        private static void RememberSaveDirectory(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            lastSaveDirectory = directory;
            lastSaveDirectoryLoaded = true;

            try
            {
                string? cacheDirectory = Path.GetDirectoryName(LastSaveDirectoryPath);
                if (!string.IsNullOrEmpty(cacheDirectory))
                {
                    Directory.CreateDirectory(cacheDirectory);
                }

                File.WriteAllText(LastSaveDirectoryPath, directory);
            }
            catch (Exception)
            {
                // The save itself already succeeded; only the convenience of remembering is lost.
            }
        }

        private static bool DirectoryIsUsable(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            try
            {
                return Directory.Exists(directory);
            }
            catch (Exception)
            {
                // A malformed path is not worth failing a save over; treat it as "cannot check".
                return false;
            }
        }

        private static bool AnySupportedFileExists(string directory, string baseName)
        {
            foreach (IPackageFormat format in Formats)
            {
                try
                {
                    if (File.Exists(Path.Combine(directory, baseName + format.Extension)))
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // Unreadable path: stop probing and let the caller use this name.
                    return false;
                }
            }

            return false;
        }

        private static string? FirstNonBlank(string? first, string? second) =>
            !string.IsNullOrWhiteSpace(first) ? first
            : !string.IsNullOrWhiteSpace(second) ? second
            : null;

        /// <summary>
        /// Strip anything the host file system would reject. A module type comes from the package's
        /// manifest, so it is not guaranteed to be a legal file name.
        /// </summary>
        private static string Sanitize(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var clean = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                clean.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            string result = clean.ToString().Trim();
            return result.Length > 0 ? result : "PCM";
        }

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

                RememberSaveDirectory(Path.GetDirectoryName(path));
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

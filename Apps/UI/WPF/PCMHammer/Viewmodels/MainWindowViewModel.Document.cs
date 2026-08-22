using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using PCMHammer.Views.DialogBoxes;
using System.IO;
using System.Linq;
using System.Windows;

namespace PCMHammer.Viewmodels
{
    /// <summary>
    /// The "loaded file" document model, mirroring the WinForms app: the app holds one working document
    /// (a <see cref="PcmPackage"/>) in memory. A Read produces it, Load File opens a .phz or .bin into it,
    /// and Write / Test Write / Verify / Export all act on it - no file is picked at operation time. Save
    /// writes it back out (as .phz when complete, otherwise .bin).
    /// </summary>
    public partial class MainWindowViewModel
    {
        #region Document state

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasDocument))]
        [NotifyPropertyChangedFor(nameof(CanWriteDocument))]
        [NotifyPropertyChangedFor(nameof(CanUseDocument))]
        [NotifyPropertyChangedFor(nameof(LoadedFileText))]
        [NotifyPropertyChangedFor(nameof(WindowTitle))]
        public partial PcmPackage? LoadedPackage { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LoadedFileText))]
        [NotifyPropertyChangedFor(nameof(WindowTitle))]
        public partial string? LoadedPackagePath { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LoadedFileText))]
        [NotifyPropertyChangedFor(nameof(WindowTitle))]
        public partial bool DocumentDirty { get; set; }

        /// <summary>True when a working document is loaded.</summary>
        public bool HasDocument => LoadedPackage is not null;

        /// <summary>
        /// True when the loaded document may be flashed/verified: a document is loaded, a device is
        /// connected, and nothing is running. Write / Test Write / Verify bind to this.
        /// </summary>
        public bool CanWriteDocument => HasDocument && CanStartOperation;

        /// <summary>
        /// True when a document operation that needs no device (Save, Export, Import) is allowed: a
        /// document is loaded and nothing is running. WinForms locks the whole File box while the bus
        /// monitor runs, so the monitor counts as "running" here too.
        /// </summary>
        public bool CanUseDocument => HasDocument && !IsOperationRunning && !IsBusMonitorRunning;

        /// <summary>True when the (hidden by default) Import Bin feature is enabled.</summary>
        public bool IsModuleImportAllowed => RuntimeSettings.AllowModuleImport;

        /// <summary>The window title, showing the loaded file and a "*" when it has unsaved changes.</summary>
        public string WindowTitle =>
            HasDocument ? "PCM Hammer - " + DocumentDisplayName() + (DocumentDirty ? " *" : string.Empty) : "PCM Hammer";

        /// <summary>The two-line "loaded file" panel text: the file name (with dirty mark) and a summary.</summary>
        public string LoadedFileText =>
            LoadedPackage == null
                ? "No file loaded"
                : DocumentDisplayName() + (DocumentDirty ? " *" : string.Empty) + Environment.NewLine + DescribeDocument(LoadedPackage);

        #endregion

        #region Document commands

        [RelayCommand(CanExecute = nameof(IsDeviceControlEnabled))]
        public void LoadFile()
        {
            if (IsOperationRunning)
            {
                return;
            }

            if (!ConfirmDiscardIfDirty())
            {
                return;
            }

            string? path = _fileDialogService.OpenPackageFileDialog();
            if (path == null)
            {
                return;
            }

            try
            {
                PcmPackage package = PackageStore.Load(path);

                // A raw .bin has no recorded type/OSID; identify its main image now, and reject anything
                // that isn't a recognized master (e.g. a lone slave module bin, which is not usable alone).
                if (!IdentifyLoadedMain(package))
                {
                    return;
                }

                SetLoadedPackage(package, path, dirty: false);
                _logger.AddUserMessage("Loaded " + path);
                _logger.AddUserMessage("  " + DescribeDocument(package));
                WarnIfIncomplete(package);
                StatusText = "File loaded.";
            }
            catch (PackageException exception)
            {
                _logger.AddUserMessage("Unable to load file: " + exception.Message);
                MessageBox.Show(exception.Message, "Could not load file", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand(CanExecute = nameof(CanUseDocument))]
        public void SaveFile() => SaveDocument(forcePrompt: true);

        [RelayCommand(CanExecute = nameof(CanUseDocument))]
        public void SaveFileAs() => SaveDocument(forcePrompt: true);

        [RelayCommand(CanExecute = nameof(CanUseDocument))]
        public void ImportModule()
        {
            if (IsOperationRunning || LoadedPackage == null || !RuntimeSettings.AllowModuleImport)
            {
                return;
            }

            ModuleImportDialog dialog = new(LoadedPackage, _logger) { Owner = _parentWindow };
            dialog.ShowDialog();
            if (dialog.Changed)
            {
                MarkDocumentDirty();
            }
        }

        [RelayCommand(CanExecute = nameof(CanUseDocument))]
        public void ExportBin()
        {
            if (IsOperationRunning || LoadedPackage == null)
            {
                return;
            }

            ExportBinDialogBox dialog = new(LoadedPackage) { Owner = _parentWindow };
            if (!dialog.HasExportableImages)
            {
                MessageBox.Show("This file has no image data to export.", "Export Bin", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (dialog.ShowDialog() != true || dialog.SelectedImages.Count == 0)
            {
                return;
            }

            string? basePath = _fileDialogService.ExportBinBaseDialog(DefaultSaveName());
            if (basePath == null)
            {
                return;
            }

            string dir = Path.GetDirectoryName(basePath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(basePath);

            foreach (ExportBinSelection item in dialog.SelectedImages)
            {
                string pcmType = item.Controller.ModuleType ?? item.Controller.Type ?? "PCM";
                string target = item.Image.Target ?? "image";
                string fileName = string.Format("{0}_{1}_{2}.bin", baseName, pcmType, target);
                string full = Path.Combine(dir, fileName);

                // Embedded images carry their own bytes; a reference (e.g. an E38 slave) is copied out of
                // the local slave library.
                byte[]? bytes = item.Image.Data ?? SlaveLibrary.Resolve(item.Image.FileName);
                if (bytes == null)
                {
                    _logger.AddUserMessage(string.Format(
                        "Skipped {0}: \"{1}\" is not in the local library.", target, item.Image.FileName));
                    continue;
                }

                try
                {
                    File.WriteAllBytes(full, bytes);
                    _logger.AddUserMessage("Exported " + full);
                }
                catch (Exception exception)
                {
                    _logger.AddUserMessage("Failed to export " + fileName + ": " + exception.Message);
                }
            }
        }

        #endregion

        #region Document helpers

        /// <summary>
        /// Called when the window is closing: if the working document has unsaved changes, offer to save,
        /// discard, or cancel. Returns false only when the user cancels (the close should be aborted).
        /// </summary>
        public bool ConfirmDiscardOnExit() => ConfirmDiscardIfDirty();

        /// <summary>Set (or clear) the working document and refresh the title and the file panel.</summary>
        private void SetLoadedPackage(PcmPackage? package, string? path, bool dirty)
        {
            LoadedPackage = package;
            LoadedPackagePath = path;
            DocumentDirty = package != null && dirty;
        }

        /// <summary>Mark the working document changed (e.g. after Import) and refresh the display.</summary>
        private void MarkDocumentDirty()
        {
            if (LoadedPackage == null)
            {
                return;
            }

            DocumentDirty = true;
        }

        /// <summary>
        /// If the working document has unsaved changes, ask whether to save, discard, or cancel. Returns
        /// false only when the user cancels (the caller should abort whatever would discard the document).
        /// </summary>
        private bool ConfirmDiscardIfDirty()
        {
            if (LoadedPackage == null || !DocumentDirty)
            {
                return true;
            }

            MessageBoxResult choice = MessageBox.Show(
                "The loaded file has unsaved changes. Save them first?",
                "Unsaved changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            return choice switch
            {
                MessageBoxResult.Yes => SaveDocument(forcePrompt: false),
                MessageBoxResult.No => true,
                _ => false,
            };
        }

        /// <summary>
        /// After a read, go straight to the save dialog. Practically everyone wants to keep a fresh
        /// read, so a "save it now?" confirmation was only ever an extra click on the way to the same
        /// place. Cancelling the dialog costs nothing: the read stays in the working document, where
        /// Save File and Write can still reach it.
        /// </summary>
        private void SaveAfterRead()
        {
            SaveDocument(forcePrompt: true);
        }

        /// <summary>
        /// Save the working document. A complete package may be written as .phz or .bin; an incomplete one
        /// may only be saved as .bin - the .phz option is refused so we never write a package with missing
        /// modules. Returns true on a successful save.
        /// </summary>
        private bool SaveDocument(bool forcePrompt)
        {
            if (LoadedPackage == null)
            {
                return false;
            }

            bool complete = PackageCompleteness.IsComplete(LoadedPackage, out string reason);
            string? path = forcePrompt ? null : LoadedPackagePath;

            // A remembered path is only reusable if it still satisfies the completeness rule for its type.
            if (path != null && !complete && path.EndsWith(".phz", StringComparison.OrdinalIgnoreCase))
            {
                path = null;
            }

            if (path == null)
            {
                path = _fileDialogService.SavePackageFileDialog(complete, DefaultSaveName());
                if (path == null)
                {
                    return false;
                }
            }

            if (!complete && path.EndsWith(".phz", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "This file cannot be saved as a package (.phz): " + reason + Environment.NewLine +
                    "Save it as a .bin instead.",
                    "Incomplete package", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                PackageStore.Save(path, LoadedPackage);
                LoadedPackagePath = path;
                DocumentDirty = false;
                _logger.AddUserMessage("Saved " + path);
                StatusText = "File saved.";
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is PackageException)
            {
                _logger.AddUserMessage("Unable to save file: " + exception.Message);
                MessageBox.Show(exception.Message, "Could not save file", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Identify the main image of a just-loaded package. A .phz already carries its module type and
        /// OSID, so only a raw .bin is identified here: if it is not a recognized main image, the load is
        /// rejected. On success the detected type and OSID are filled in. Returns false when rejected.
        /// </summary>
        private bool IdentifyLoadedMain(PcmPackage package)
        {
            foreach (PackageController controller in package.Controllers)
            {
                PackageImage? main = controller.Image("main");
                if (main?.Data == null)
                {
                    continue;
                }

                // Already identified (a .phz records both); trust it and skip re-identification.
                if (!string.IsNullOrEmpty(controller.ModuleType) && main.Osid != null)
                {
                    continue;
                }

                FileValidator validator = new(main.Data, _logger);
                PcmType type = validator.GetFileType();
                if (type == PcmType.Undefined)
                {
                    string message =
                        "This file is not a recognized PCM image, so it cannot be loaded on its own." + Environment.NewLine +
                        "A slave module can only be brought in with File -> Import Bin, into a package read from a PCM.";
                    _logger.AddUserMessage(message);
                    MessageBox.Show(message, "Unrecognized file", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                if (string.IsNullOrEmpty(controller.ModuleType))
                {
                    controller.ModuleType = type.ToString();
                }

                if (main.Osid == null)
                {
                    uint osid = validator.GetOsidFromImage();
                    if (osid != 0)
                    {
                        main.Osid = osid;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// After loading, note when the document is a master image for a platform that has a slave but no
        /// slave data (e.g. a downloaded E38 .bin): it can be flashed master-only, but it isn't a complete
        /// package and can only be saved as a .bin.
        /// </summary>
        private void WarnIfIncomplete(PcmPackage package)
        {
            if (!PackageCompleteness.IsComplete(package, out string reason))
            {
                _logger.AddUserMessage("Note: " + reason + " It can be flashed as a master-only image and saved as a .bin.");
            }
        }

        /// <summary>The display file name for the working document (falls back to an unsaved-read label).</summary>
        /// <remarks>
        /// Window/status text only. Do NOT use it to seed a save dialog - that is what produced files
        /// called "Untitled (unsaved read).phz". Use <see cref="DefaultSaveName"/> for that.
        /// </remarks>
        private string DocumentDisplayName() =>
            LoadedPackagePath != null ? Path.GetFileName(LoadedPackagePath) : "Untitled (unsaved read)";

        /// <summary>
        /// The base file name to suggest when saving. The rule lives in the library so every UI
        /// suggests the same thing; a null path is what asks it to build one from the package.
        /// </summary>
        private string DefaultSaveName() =>
            PackageStore.DefaultBaseName(
                LoadedPackage,
                LoadedPackagePath,
                // The folder the save dialogs open in, so the sequence number skips names already there.
                Properties.Settings.Default.BinDirectory);

        /// <summary>One-line summary of a package: module type, OSID, and whether it carries the slave.</summary>
        private static string DescribeDocument(PcmPackage package)
        {
            PackageController? controller = package.Controllers.FirstOrDefault();
            if (controller == null)
            {
                return "empty";
            }

            string module = controller.ModuleType ?? controller.Type ?? "PCM";
            PackageImage? main = controller.Image("main");
            string osid = main?.Osid != null ? ", OSID " + main.Osid : string.Empty;
            bool hasSlave = controller.Images.Any(
                i => i.Target != null && i.Target.StartsWith("slave", StringComparison.OrdinalIgnoreCase));
            bool complete = PackageCompleteness.IsComplete(package, out _);
            string shape = hasSlave ? "master+slave" : (complete ? "master" : "master only");
            return module + osid + ", " + shape;
        }

        #endregion
    }
}

using PcmHacking;

namespace PCMHammer.Services
{
    public class PcmReader(Vehicle vehicle, ILogger logger)
    {
        // Parameter order is (message, title), matching the library's promptForYesNo/alert delegates
        // and the WinForms implementation.
        public Task Alert(string message, string title)
        {
            logger.AddUserMessage($"ALERT [{title}]: {message}");
            return Task.CompletedTask;
        }

        public static Task<bool> PromptForYesNo(string message, string title) => ShowYesNo(message, title);

        /// <summary>
        /// Show a Yes/No dialog and return the answer as a COMPLETED task. This must run synchronously:
        /// the library calls it fire-and-forget through its UI-thread marshaller, and an async gap (as an
        /// awaited Dispatcher.InvokeAsync introduces) would let the caller read the default before the
        /// user answers - which showed up as every warning being treated as "No" / abort.
        /// </summary>
        internal static Task<bool> ShowYesNo(string message, string title)
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            bool Ask() => System.Windows.MessageBox.Show(
                message, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question)
                == System.Windows.MessageBoxResult.Yes;

            bool result = dispatcher.CheckAccess() ? Ask() : dispatcher.Invoke(Ask);
            return Task.FromResult(result);
        }

        // These fallbacks map to ReadManager expectations if it encounters complex/deep OS query scenarios
        private Task<string> DummyPromptForFile() => Task.FromResult(string.Empty);
        private Task<PcmType> DummyPromptForOsId() => Task.FromResult(PcmType.Undefined);

        /// <summary>
        /// Read the PCM into an in-memory working document (a <see cref="PcmPackage"/>), the way the
        /// WinForms app does: the read produces a package the UI holds and can then save or flash, with
        /// no intermediate file on disk. Returns null if nothing was read.
        /// </summary>
        public async Task<PcmPackage?> ReadToPackageAsync(
            bool useAutoPcmType = true,
            PcmType selectedPcmType = PcmType.Undefined,
            CancellationToken cancellationToken = default)
        {
            using (new AwayMode())
            {
                try
                {
                    if (vehicle == null)
                    {
                        logger.AddUserMessage("Error: No vehicle interface connected.");
                        return null;
                    }

                    PcmType forcedPcmType = useAutoPcmType ? PcmType.Undefined : selectedPcmType;

                    ReadManager reader = new(
                        logger,
                        vehicle,
                        (action) => {
                            System.Windows.Application.Current.Dispatcher.Invoke(action);
                            return Task.CompletedTask;
                        },
                        DummyPromptForFile!,
                        DummyPromptForOsId!,
                        Alert,
                        PromptForYesNo,
                        cancellationToken);

                    return await reader.ReadToPackage(forcedPcmType);
                }
                catch (Exception exception)
                {
                    logger.AddUserMessage($"Read failed: {exception.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Read a PCM that is in recovery mode into an in-memory document, using the user-selected PCM
        /// type. All the recovery rules live in the library (see PcmHacking.RecoveryMode).
        /// </summary>
        public async Task<PcmPackage?> RecoveryReadAsync(
            PcmType pcmType,
            CancellationToken cancellationToken = default)
        {
            using (new AwayMode())
            {
                try
                {
                    if (vehicle == null)
                    {
                        logger.AddUserMessage("Error: No vehicle interface connected.");
                        return null;
                    }

                    ReadManager reader = new(
                        logger,
                        vehicle,
                        (action) => {
                            System.Windows.Application.Current.Dispatcher.Invoke(action);
                            return Task.CompletedTask;
                        },
                        DummyPromptForFile!,
                        DummyPromptForOsId!,
                        Alert,
                        PromptForYesNo,
                        cancellationToken);

                    return await reader.RecoveryRead(pcmType);
                }
                catch (Exception exception)
                {
                    logger.AddUserMessage($"Recovery read failed: {exception.Message}");
                    return null;
                }
            }
        }

        public async Task<bool> ReadPcmAsync(
            string path,
            bool useAutoPcmType = true,
            PcmType selectedPcmType = PcmType.Undefined,
            CancellationToken cancellationToken = default
            )
        {
            using (new AwayMode())
            {
                try
                {
                    if (vehicle == null)
                    {
                        logger.AddUserMessage("Error: No vehicle interface connected.");
                        return false;
                    }

                    logger.AddUserMessage($"Reading to: {path}");

                    PcmType forcedPcmType = useAutoPcmType ? PcmType.Undefined : selectedPcmType;

                    // Match WinForms ReadManager initialization
                    ReadManager reader = new(
                        logger,
                        vehicle,
                        (action) => { 
                            System.Windows.Application.Current.Dispatcher.Invoke(action); 
                            return Task.CompletedTask; 
                        },
                        DummyPromptForFile!,
                        DummyPromptForOsId!,
                        Alert,
                        PromptForYesNo,
                        cancellationToken);

                    bool success = await reader.Read(path, forcedPcmType);
                    return success;
                }
                catch (Exception exception)
                {
                    logger.AddUserMessage($"Read failed: {exception.Message}");
                    return false;
                }
            }
        }
    }
}
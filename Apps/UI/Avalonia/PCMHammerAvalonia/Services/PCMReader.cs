using Avalonia.Threading;
using PcmHacking;
using PCMHammerAvalonia.Views;

namespace PCMHammerAvalonia.Services
{
    public class PcmReader(Vehicle vehicle, ILogger logger)
    {
        public Task Alert(string title, string message)
        {
            logger.AddUserMessage($"ALERT [{title}]: {message}");
            return Task.CompletedTask;
        }

        public static async Task<bool> PromptForYesNo(string title, string message)
        {
            bool userResult = false;
            var result = await MessageBox.ShowAsync(message, title, MessageBoxButton.YesNo);
            userResult = (result == MessageBoxResult.Yes);
            return userResult;
        }

        // These fallbacks map to ReadManager expectations if it encounters complex/deep OS query scenarios
        private Task<string> DummyPromptForFile() => Task.FromResult(string.Empty);
        private Task<PcmType> DummyPromptForOsId() => Task.FromResult(PcmType.Undefined);

        public async Task<bool> ReadPcmAsync(
            string path,
            bool useAutoPcmType = true,
            PcmType selectedPcmType = PcmType.Undefined,
            CancellationToken cancellationToken = default
        ){
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

                    // Updated for Avalonia UI Thread Dispatching
                    ReadManager reader = new(
                        logger,
                        vehicle,
                        async (action) =>
                        {
                            if (Dispatcher.UIThread.CheckAccess())
                            {
                                action();
                            }
                            else
                            {
                                await Dispatcher.UIThread.InvokeAsync(action);
                            }
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
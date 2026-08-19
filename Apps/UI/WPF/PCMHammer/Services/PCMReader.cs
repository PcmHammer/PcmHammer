using PcmHacking;

namespace PCMHammer.Services
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
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var result = System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                userResult = (result == System.Windows.MessageBoxResult.Yes);
            });
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
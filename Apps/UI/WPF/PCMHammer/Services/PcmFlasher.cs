using PcmHacking;
using System.IO;

namespace PCMHammer.Services
{
    public class PcmFlasher(Vehicle vehicle, ILogger logger)
    {
        // Track the current operations state locally
        private WriteType _currentWriteType = WriteType.None;

        public Task Alert(string title, string message)
        {
            logger.AddUserMessage($"ALERT [{title}]: {message}");
            return Task.CompletedTask;
        }

        public async Task<bool> PromptForYesNo(string title, string message)
        {
            bool userResult = false;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var result = System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                userResult = (result == System.Windows.MessageBoxResult.Yes);
            });
            return userResult;
        }

        public async Task<bool> WritePcmAsync(
            WriteType writeType,
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
                    _currentWriteType = writeType;

                    if (vehicle == null)
                    {
                        logger.AddUserMessage("Error: No vehicle interface connected.");
                        return false;
                    }

                    logger.AddUserMessage(path);

                    PcmType forcedPcmType = useAutoPcmType ? PcmType.Undefined : selectedPcmType;

                    WriteManager writer = new(
                        logger,
                        vehicle,
                        writeType,
                        Alert,
                        PromptForYesNo,
                        cancellationToken
                    );

                    bool success = await writer.Write(path, forcedPcmType);
                    
                    return success;
                }
                catch (IOException exception)
                {
                    logger.AddUserMessage(exception.ToString());
                    return false;
                }
                finally
                {
                    _currentWriteType = WriteType.None;
                }
            }
        }
    }
}
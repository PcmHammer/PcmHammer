using PcmHacking;
using PCMHammerAvalonia.Views;
using System.IO;

namespace PCMHammerAvalonia.Services
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

        public static async Task<bool> PromptForYesNo(string title, string message)
        {
            var result = await MessageBox.ShowAsync(
                message, 
                title, 
                MessageBoxButton.YesNo
            );
            return result == MessageBoxResult.Yes;
        }

        public async Task<bool> WritePcmAsync(
            WriteType writeType,
            string path,
            bool useAutoPcmType = true,
            PcmType selectedPcmType = PcmType.Undefined,
            bool suppressOSIDWarning = false,
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
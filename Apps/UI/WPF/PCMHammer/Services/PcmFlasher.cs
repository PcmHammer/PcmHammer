using PcmHacking;
using System.IO;

namespace PCMHammer.Services
{
    public class PcmFlasher(Vehicle vehicle, ILogger logger)
    {
        // Track the current operations state locally
        private WriteType _currentWriteType = WriteType.None;

        // Parameter order is (message, title), matching the library's promptForYesNo/alert delegates
        // and the WinForms implementation.
        public Task Alert(string message, string title)
        {
            logger.AddUserMessage($"ALERT [{title}]: {message}");
            return Task.CompletedTask;
        }

        // Delegates to the shared, synchronous implementation. It must complete synchronously because the
        // library calls it fire-and-forget through its UI-thread marshaller; an awaited async gap would
        // let the caller read the default before the user answers (every warning read as "No" / abort).
        public Task<bool> PromptForYesNo(string message, string title) => PcmReader.ShowYesNo(message, title);

        /// <summary>
        /// Flash the in-memory working document (a <see cref="PcmPackage"/>) instead of a file on disk.
        /// The bytes come from the loaded package; this is the WinForms "write the loaded file" path,
        /// used by Write / Test Write / Verify once a document is loaded or freshly read.
        /// </summary>
        public async Task<bool> WritePackageAsync(
            WriteType writeType,
            PcmPackage package,
            bool useAutoPcmType = true,
            PcmType selectedPcmType = PcmType.Undefined,
            CancellationToken cancellationToken = default)
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

                    PcmType forcedPcmType = useAutoPcmType ? PcmType.Undefined : selectedPcmType;

                    WriteManager writer = new(
                        logger,
                        vehicle,
                        writeType,
                        Alert,
                        PromptForYesNo,
                        cancellationToken);

                    return await writer.Write(package, forcedPcmType);
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

        /// <summary>
        /// Write the loaded document to a PCM that is in recovery mode, using the user-selected PCM
        /// type. All the recovery rules live in the library (see PcmHacking.RecoveryMode).
        /// </summary>
        public async Task<bool> RecoveryWriteAsync(
            PcmPackage package,
            PcmType pcmType,
            CancellationToken cancellationToken = default)
        {
            using (new AwayMode())
            {
                try
                {
                    _currentWriteType = WriteType.Full;

                    if (vehicle == null)
                    {
                        logger.AddUserMessage("Error: No vehicle interface connected.");
                        return false;
                    }

                    WriteManager writer = new(
                        logger,
                        vehicle,
                        WriteType.Full,
                        Alert,
                        PromptForYesNo,
                        cancellationToken);

                    return await writer.RecoveryWrite(package, pcmType);
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
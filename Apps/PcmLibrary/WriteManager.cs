using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Contains flash-writing code shared by the WinForms and Uno user interfaces.
    /// </summary>
    public class WriteManager
    {
        private ILogger logger;
        private Vehicle vehicle;
        private Func<Action, object> invoke;
        private Func<Task<string>> promptForFilePath;
        private Func<Task<UInt32>> promptForOperatingSystemId;
        private CancellationToken cancellationToken;

        public WriteManager(
            ILogger logger,
            Vehicle vehicle,
            Func<Action, object> invoke,
            Func<Task<string>> promptForFilePath,
            Func<Task<UInt32>> promptForOperatingSystemId,
            CancellationToken cancellationToken)
        {
            this.logger = logger;
            this.vehicle = vehicle;
            this.invoke = invoke;
            this.promptForFilePath = promptForFilePath;
            this.promptForOperatingSystemId = promptForOperatingSystemId;
            this.cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Contains cross-platform code to handle user interactions to write the PCM's flash memory.
        /// </summary>
        /// <remarks>
        /// The return value should be used to suppress future warnings about using an unproven connection.
        /// </remarks>
        /// <returns>True if the write was successful, fales if failed or aborted.</returns>
        public Task<bool> Write()
        {
            return Task.FromResult(false);
        }
    }
}

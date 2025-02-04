using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking.UnoUI.Utilities
{
    /// <summary>
    /// This interface and the class that implements it are a workaround for a bug in the Uno Platform code generator.
    /// </summary>
    /// <remarks>
    /// Uno's code generator requires all public methods in a Model class to return Task.
    /// https://github.com/unoplatform/uno/issues/19344
    /// In order to work around that bug, we need to create an interface that
    /// has methods that return Task, and then create a class that implements
    /// that interface.
    /// 
    /// But the WinForms version of PCM Hammer has over 100 places where we 
    /// call AddUserMessage without awaiting the result, because it returns
    /// void. So we're not going to change that codoe.
    /// 
    /// This interface as the class below allow bridge the gap between what
    /// Uno requires and what the rest of the code expects.
    /// </remarks>
    interface IAsyncLogger
    {
        Task AddUserMessage(string message);
        Task AddDebugMessage(string message);
        Task StatusUpdateActivity(string activity);
        Task StatusUpdateTimeRemaining(string remaining);
        Task StatusUpdatePercentDone(string percent);
        Task StatusUpdateRetryCount(string retries);
        Task StatusUpdateProgressBar(double completed, bool visible);
        Task StatusUpdateKbps(string Kbps);
        Task StatusUpdateReset();
    }

    /// <summary>
    /// See the description of IAsyncLogger.
    /// </summary>
    class LoggerAdapter : PcmHacking.ILogger
    {
        private readonly IAsyncLogger logger;

        public LoggerAdapter(IAsyncLogger logger)
        {
            this.logger = logger;
        }

        public void AddUserMessage(string message)
        {
            this.logger.AddUserMessage(message);
        }

        public void AddDebugMessage(string message)
        {
            this.logger.AddDebugMessage(message);
        }

        public void StatusUpdateActivity(string activity)
        {
            this.logger.StatusUpdateActivity(activity);
        }

        public void StatusUpdateTimeRemaining(string remaining)
        {
            this.logger.StatusUpdateTimeRemaining(remaining);
        }

        public void StatusUpdatePercentDone(string percent)
        {
            this.logger.StatusUpdatePercentDone(percent);
        }

        public void StatusUpdateRetryCount(string retries)
        {
            this.logger.StatusUpdateRetryCount(retries);
        }

        public void StatusUpdateProgressBar(double completed, bool visible)
        {
            this.logger.StatusUpdateProgressBar(completed, visible);
        }

        public void StatusUpdateKbps(string Kbps)
        {
            this.logger.StatusUpdateKbps(Kbps);
        }

        public void StatusUpdateReset()
        {
            this.logger.StatusUpdateReset();
        }
    }
}

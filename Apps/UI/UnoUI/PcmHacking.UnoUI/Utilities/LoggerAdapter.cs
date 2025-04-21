using PcmHacking.UnoUI.Services;
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
    public interface IAsyncLogger
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
    public class LoggerAdapter : PcmHacking.ILogger
    {
        private ILogBuffer buffer;

        public IAsyncLogger? Logger { get; set; }

        public LoggerAdapter(ILogBuffer buffer)
        {
            this.buffer = buffer;
        }

        private string ExpandTabs(string message)
        {
            StringBuilder builder = new();
            for (int index = 0; index < message.Length; index++)
            {
                char current = message[index];
                if (current != '\t')
                {
                    builder.Append(current);
                    continue;
                }

                int spaces = 4 - (builder.Length % 4);
                for (int spaceIndex = 0; spaceIndex < spaces; spaceIndex++)
                {
                    builder.Append(' ');
                }
            }

            return builder.ToString();
        }

        public void AddUserMessage(string message)
        {
            if (!this.buffer.Enabled)
            {
                return;
            }

            message = this.ExpandTabs(message);

            this.buffer.Add(LogType.User, message);

            if (this.Logger != null)
            {
                this.Logger.AddUserMessage(message);
            }
            else
            {
                Console.WriteLine($"UserMessage: {message}");
            }
        }

        public void AddDebugMessage(string message)
        {
            if (!this.buffer.Enabled)
            {
                return;
            }

            message = this.ExpandTabs(message);

            this.buffer.Add(LogType.Debug, message);

            if (this.Logger != null)
            {
                this.Logger.AddDebugMessage(message);
            }
            else
            {
                Console.WriteLine($"DebugMessage: {message}");
            }
        }

        public void StatusUpdateActivity(string activity)
        {
            this.buffer.Add(LogType.User, activity);

            if (this.Logger != null)
            {
                this.Logger.StatusUpdateActivity(activity);
            }
            else
            {
                Console.WriteLine($"Activity: {activity}");
            }
        }

        public void StatusUpdateTimeRemaining(string remaining)
        {
            if (this.Logger != null)
            {
                this.Logger.StatusUpdateTimeRemaining(remaining);
            }
            else
            {
                Console.WriteLine($"TimeRemaining: {remaining}");
            }
        }

        public void StatusUpdatePercentDone(string percent)
        {
            if (this.Logger != null)
            {
                this.Logger.StatusUpdatePercentDone(percent);
            }
            else
            {
                Console.WriteLine($"PercentDone: {percent}");
            }
        }

        public void StatusUpdateRetryCount(string retries)
        {
            if (this.Logger != null)
            {
                this.Logger.StatusUpdateRetryCount(retries);
            }
            else
            {
                Console.WriteLine($"RetryCount: {retries}");
            }
        }

        public void StatusUpdateProgressBar(double completed, bool visible)
        {
            if (this.Logger != null)
            {
                this.Logger.StatusUpdateProgressBar(completed, visible);
            }
            else
            {
                Console.WriteLine($"ProgressBar: Completed={completed}, Visible={visible}");
            }
        }

        public void StatusUpdateKbps(string Kbps)
        {
            if (this.Logger != null)
            {
                this.Logger.StatusUpdateKbps(Kbps);
            }
            else
            {
                Console.WriteLine($"Kbps: {Kbps}");
            }
        }

        public void StatusUpdateReset()
        {
            if (this.Logger != null)
            {
                this.Logger.StatusUpdateReset();
            }
            else
            {
                Console.WriteLine("Reset");
            }
        }
    }

    public class LogInterceptor : IDisposable
    {
        private LoggerAdapter loggerAdapter;

        public LogInterceptor(LoggerAdapter loggerAdapter, IAsyncLogger logger)
        {
            this.loggerAdapter = loggerAdapter;
            this.loggerAdapter.Logger = logger;
        }
        public void Dispose()
        {
            this.loggerAdapter.Logger = null;
        }
    }

}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking.UnoUI.Utilities
{
    // https://devblogs.microsoft.com/oldnewthing/20220505-00/?p=106585
    class TimeoutUtilities
    {
        static async Task<T> DelayedTimeoutExceptionTask<T>(TimeSpan delay)
        {
            await Task.Delay(delay);
            throw new TimeoutException();
        }

        public static async Task<T> TaskWithTimeoutAndException<T>(
            Task<T> task,
            TimeSpan timeout)
        {
            return await await Task.WhenAny(
                task, DelayedTimeoutExceptionTask<T>(timeout));
        }

        static async Task<T> DelayedResultTask<T>(TimeSpan delay, Func<T> fallbackMaker)
        {
            await Task.Delay(delay);
            return fallbackMaker();
        }

        public static async Task<T> TaskWithTimeoutAndFallback<T>(
            Task<T> task,
            TimeSpan timeout,
            Func<T> fallbackMaker)
        {
            return await await Task.WhenAny(
                task, DelayedResultTask<T>(timeout, fallbackMaker));
        }
    }
}

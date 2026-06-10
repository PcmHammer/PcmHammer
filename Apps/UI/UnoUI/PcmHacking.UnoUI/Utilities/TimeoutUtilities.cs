// SPDX-License-Identifier: GPL-3.0-only
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
        private static async Task<T> DelayedTimeoutExceptionTask<T>(TimeSpan delay, CancellationToken token)
        {
            await Task.Delay(delay);
            if (!token.IsCancellationRequested)
            {
                throw new TimeoutException();
            }

            return default!;
        }

        public static async Task<T> TaskWithTimeoutAndException<T>(
            Task<T> task,
            TimeSpan timeout)
        {
            CancellationTokenSource source = new CancellationTokenSource();
            Task<T> next = await Task.WhenAny(task, DelayedTimeoutExceptionTask<T>(timeout, source.Token));
            source.Cancel();
            return await next; 
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

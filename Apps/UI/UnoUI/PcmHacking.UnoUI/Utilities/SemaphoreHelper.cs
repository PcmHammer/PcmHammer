// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking.UnoUI.Utilities
{
    internal class SemaphoreHelper : IDisposable
    {
        private SemaphoreSlim? semaphore;

        public static async Task<SemaphoreHelper> WaitAsync(SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            return new SemaphoreHelper(semaphore);
        }

        private SemaphoreHelper(SemaphoreSlim semaphore)
        {
            this.semaphore = semaphore;
        }

        public void Dispose()
        {
            if (this.semaphore != null)
            {
                this.semaphore.Release();
                this.semaphore = null;
            }
        }
    }
}

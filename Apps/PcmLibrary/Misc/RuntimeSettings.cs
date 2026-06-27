// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking
{
    /// <summary>
    /// Process-wide flags that intentionally do NOT persist. They reset to their
    /// defaults on every launch so a dangerous option can never be left enabled
    /// across sessions.
    /// </summary>
    public static class RuntimeSettings
    {
        /// <summary>
        /// When true, the file-vs-PCM compatibility checks are relaxed so an
        /// incompatible image can be written (advanced recovery / developer use,
        /// e.g. recovering a P05c that was BDM-flashed with a P05b bin). This can
        /// brick a PCM. Always starts false; it is never saved to disk.
        /// </summary>
        public static bool AllowCrossFlashing { get; set; }

        /// <summary>
        /// When true, every in-scope flash sector is erased and rewritten even when
        /// its on-device CRC already matches the image, so the whole chip (incl.
        /// boot) is written at least once. Slower and adds flash wear; useful for
        /// recovery and diagnosing what is actually eraseable/writable. Always
        /// starts false; it is never saved to disk.
        /// </summary>
        public static bool ForceWriteAllSectors { get; set; }
    }
}

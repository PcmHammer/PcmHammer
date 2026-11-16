// SPDX-License-Identifier: GPL-3.0-only
namespace PCMHammerAvalonia.Services;

/// <summary>
/// Cross-platform resource locations. On Android there is no directory beside the executable, so the
/// Android head copies the bundled kernel binaries (packed as APK assets) into the app's private files
/// directory at startup and records it here; the Vehicle is then constructed with this as its base path
/// so <c>LoadKernelFromFile</c> finds the kernels. On desktop this stays null and the library falls back
/// to <c>AppContext.BaseDirectory</c> (the kernels are copied next to the exe), so nothing changes there.
/// </summary>
public static class PlatformPaths
{
    public static string? KernelDirectory { get; set; }
}

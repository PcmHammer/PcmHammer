// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PcmHacking
{
    /// <summary>
    /// The parts of dark mode that belong to Windows rather than to the app.
    /// </summary>
    /// <remarks>
    /// Controls drawn by WinForms itself take their colours from a palette, and
    /// <see cref="AppTheme"/> handles those. What is left is drawn by Windows and reads none of
    /// them: scroll bars, drop-down lists, context menus, tool tips, the caret and the common
    /// dialogs. This is how those are reached.
    ///
    /// Two different levels of support:
    ///
    /// SetWindowTheme is a documented, public call. Only the theme name "DarkMode_Explorer" is
    /// undocumented, and an unrecognised name does nothing at all, so that part is safe.
    ///
    /// The uxtheme entry points are not documented and are exported by ordinal with no names. They
    /// are what every dark-mode Win32 app uses, and the ordinals have been stable since the feature
    /// appeared, but they are unsupported and a future Windows could move them. Every call is
    /// guarded: when any of this fails the app keeps the colours it set itself, which is the state
    /// it was in before any of this ran.
    /// </remarks>
    internal static class NativeDarkMode
    {
        /// <summary>Dark mode first appeared in Windows 10 1809.</summary>
        private const int FirstSupportedBuild = 17763;

        /// <summary>1903 replaced AllowDarkModeForApp with SetPreferredAppMode at the same ordinal.</summary>
        private const int PreferredAppModeBuild = 18362;

        private enum PreferredAppMode
        {
            Default = 0,
            AllowDark = 1,
            ForceDark = 2,
            ForceLight = 3,
        }

        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern int SetPreferredAppMode(PreferredAppMode mode);

        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern bool AllowDarkModeForApp(bool allow);

        [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true)]
        private static extern bool AllowDarkModeForWindow(IntPtr window, bool allow);

        [DllImport("uxtheme.dll", EntryPoint = "#104", SetLastError = true)]
        private static extern void RefreshImmersiveColorPolicyState();

        [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
        private static extern void FlushMenuThemes();

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window, string? subApp, string? subId);

        private static readonly int WindowsBuild = ReadWindowsBuild();

        /// <summary>Whether this Windows knows about dark mode at all.</summary>
        internal static bool IsSupported => WindowsBuild >= FirstSupportedBuild;

        /// <summary>
        /// Tell Windows which way the whole process leans, which is what reaches menus and dialogs.
        /// </summary>
        internal static void SetAppMode(bool dark)
        {
            if (!IsSupported)
            {
                return;
            }

            try
            {
                if (WindowsBuild >= PreferredAppModeBuild)
                {
                    SetPreferredAppMode(dark ? PreferredAppMode.ForceDark : PreferredAppMode.ForceLight);
                }
                else
                {
                    AllowDarkModeForApp(dark);
                }

                RefreshImmersiveColorPolicyState();
                FlushMenuThemes();
            }
            catch (Exception)
            {
                // Undocumented and therefore allowed to vanish. The app keeps its own colours.
            }
        }

        /// <summary>Let one window follow the app mode, for its non-client parts.</summary>
        internal static void AllowForWindow(IntPtr window, bool dark)
        {
            if (!IsSupported || window == IntPtr.Zero)
            {
                return;
            }

            try
            {
                AllowDarkModeForWindow(window, dark);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>The theme class a control should be put on to get its dark parts.</summary>
        /// <remarks>
        /// Which class to ask for depends on the control, and getting it wrong is the same as not
        /// asking: Explorer covers lists, trees and scroll bars, while edits and combo boxes answer
        /// to the common file dialog's class instead.
        /// </remarks>
        internal const string Explorer = "DarkMode_Explorer";

        /// <summary>The class edits and combo boxes answer to.</summary>
        internal const string CommonFileDialog = "DarkMode_CFD";

        /// <summary>
        /// Put a control on one of the dark theme classes.
        /// </summary>
        /// <remarks>
        /// The sub-id matters as much as the class. A scroll bar belongs to the window's non-client
        /// area rather than being a child of it, so naming "ScrollBar" is what reaches it - asking
        /// for the class alone leaves the bar exactly as it was.
        ///
        /// Passing nothing puts the control back on its default theme, so this reverts cleanly when
        /// the light theme is chosen.
        /// </remarks>
        internal static void UseTheme(IntPtr window, bool dark, string themeClass, string? subId = null)
        {
            if (!IsSupported || window == IntPtr.Zero)
            {
                return;
            }

            try
            {
                SetWindowTheme(window, dark ? themeClass : null, dark ? subId : null);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Take a control off visual styles altogether, so it stops being drawn by the theme.
        /// </summary>
        /// <remarks>
        /// An empty theme name is the documented way to say "no visual styles here", and it is what
        /// makes a tab control stop painting its own strip over the colours set on it. Not gated on
        /// the Windows build: this has worked since visual styles existed.
        /// </remarks>
        internal static void DisableVisualStyles(IntPtr window, bool disable)
        {
            if (window == IntPtr.Zero)
            {
                return;
            }

            try
            {
                SetWindowTheme(window, disable ? string.Empty : null, disable ? string.Empty : null);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// The real build number.
        /// </summary>
        /// <remarks>
        /// Read from the registry rather than from Environment.OSVersion, which reports Windows 8
        /// on a .NET Framework app without a compatibility manifest and would hide every version
        /// this cares about.
        /// </remarks>
        private static int ReadWindowsBuild()
        {
            try
            {
                object? value = Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                    "CurrentBuildNumber",
                    null);

                return int.TryParse(value?.ToString(), out int build) ? build : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}

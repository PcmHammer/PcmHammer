using System;
using System.IO;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using PCMLoggerAvalonia.Services;

namespace PCMLoggerAvalonia.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            base.OnCreate();

            // The data files (parameter XMLs, DiagnosticCodes.txt, the default LogProfile) are packed as
            // APK assets; the shared loaders read them from the filesystem, so copy them into the app's
            // private files dir and point the resource directory there before any view model loads them.
            CopyBundledDataAssets();
        }

        private void CopyBundledDataAssets()
        {
            try
            {
                string targetDir = this.FilesDir?.AbsolutePath ?? AppContext.BaseDirectory;
                string[] assetNames = this.Assets?.List(string.Empty) ?? Array.Empty<string>();

                foreach (string name in assetNames)
                {
                    bool isDataFile =
                        name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".LogProfile", StringComparison.OrdinalIgnoreCase);
                    if (!isDataFile)
                    {
                        continue;
                    }

                    using Stream source = this.Assets!.Open(name);
                    using FileStream destination = File.Create(Path.Combine(targetDir, name));
                    source.CopyTo(destination);
                }

                PlatformPaths.ResourceDirectory = targetDir;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine("CopyBundledDataAssets failed: " + exception);
            }
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
            .WithInterFont();
        }
    }
}

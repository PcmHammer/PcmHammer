using System;
using System.IO;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using PCMHammerAvalonia.Services;

namespace PCMHammerAvalonia.Android
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

            // The PCM kernels/loaders/boot libraries (Kernel-*.bin, Loader-*.bin, BootLib-*.bin) are
            // packed as APK assets. The shared kernel loader reads them from the filesystem, so copy
            // them into the app's private files dir and point PlatformPaths.KernelDirectory there before
            // any read/write builds a Vehicle. Without this, LoadKernelFromFile finds nothing on Android
            // and read/write/flash fails at kernel upload.
            CopyBundledKernels();
        }

        private void CopyBundledKernels()
        {
            try
            {
                string targetDir = this.FilesDir?.AbsolutePath ?? AppContext.BaseDirectory;
                string[] assetNames = this.Assets?.List(string.Empty) ?? Array.Empty<string>();

                foreach (string name in assetNames)
                {
                    if (!name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using Stream source = this.Assets!.Open(name);
                    using FileStream destination = File.Create(Path.Combine(targetDir, name));
                    source.CopyTo(destination);
                }

                PlatformPaths.KernelDirectory = targetDir;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine("CopyBundledKernels failed: " + exception);
            }
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
            .WithInterFont();
        }
    }
}

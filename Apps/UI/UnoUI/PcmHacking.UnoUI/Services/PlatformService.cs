using System;
using System.Collections.Generic;
using System.Text;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PcmHacking.UnoUI
{
    public interface IPlatformService
    {
        void PrepareChildWindow(object target);
        Task<StorageFile?> PromptForFileOpenPath();
        Task<StorageFile?> PromptForFileSavePath();
    }

    public class PlatformService : IPlatformService
    {
        public void PrepareChildWindow(object target)
        {
#if WINDOWS
            nint handle = WindowNative.GetWindowHandle(App.StaticMainWindow);
            InitializeWithWindow.Initialize(target, handle);
#endif
        }

        public async Task<StorageFile?> PromptForFileOpenPath()
        {
            // Use the standard open-file dialog to get the file path
            // TODO: find/create a touch-friendly file picker
            FileOpenPicker openPicker = new FileOpenPicker();
            PrepareChildWindow(openPicker);
            openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            openPicker.FileTypeFilter.Add(".bin");
            StorageFile file = await openPicker.PickSingleFileAsync();
            if (file == null)
            {
                return null;
            }
            return file;
        }

        public async Task<StorageFile?> PromptForFileSavePath()
        {
            // Open a Save-As dialog to get the file path
            FileSavePicker savePicker = new FileSavePicker();
            PrepareChildWindow(savePicker);
            savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("Binary", new List<string>() { ".bin" });
            savePicker.SuggestedFileName = "Untitled.bin";
            StorageFile file = await savePicker.PickSaveFileAsync();
            if (file == null)
            {
                return null;
            }
            return file;
        }


    }
}

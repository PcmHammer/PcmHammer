using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Text;
using Windows.Networking.NetworkOperators;

namespace PcmHacking.UnoUI.Services
{
    public static class DialogService
    {
        private static IDispatcher _dispatcher;

        public static void SetDispatcher(IDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public async static Task ShowAlertPrompt(string title, string message)
        {
            await _dispatcher.ExecuteAsync(async (ct) => { 
                AlertPrompt prompt = new(title, message);
                return await prompt.ShowAsync(); 
            });
        }

        public async static Task<bool> ShowBinaryPrompt(string title, string message, string acceptText = "Okay", string cancelText = "Cancel", PrimaryButton primarySelection = PrimaryButton.Close)
        {
            ContentDialogResult result = await _dispatcher.ExecuteAsync(async (ct) => { 
            BinaryPrompt prompt = new(title, message, acceptText, cancelText, primarySelection);
                return await prompt.ShowAsync();
            });
            return result == ContentDialogResult.Primary;
        }
    }
}

using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking.UnoUI.Services
{
    public struct NoticeData
    {
        public required string HelpButtonText { get; init; }
        public required string NoticeText { get; init; }
        public required string LinkText { get; init; }
        public required string LinkUrl { get; init; }
    }

    public interface INoticeService
    {
        IState<NoticeData> NoticeData { get; }
        Task GetNotice();
    }

    public class NoticeService : INoticeService
    {
        private const string Branch = "nsfw/net8-uno-ui";
        private const string NoticeUrl = $"https://raw.githubusercontent.com/PcmHammer/PcmHammer/refs/heads/{Branch}/Apps/UI/UnoUI/PcmHacking.UnoUI/notice.json";
        private readonly NoticeData emptyNotice = new NoticeData
        {
            HelpButtonText = String.Empty,
            NoticeText = String.Empty,
            LinkText = String.Empty,
            LinkUrl = String.Empty
        };

        public IState<NoticeData> NoticeData => State<NoticeData>.Value(this, () => emptyNotice);

        public NoticeService()
        {
        }

        /// <summary>
        /// Fetch a JSON file that will be used to display a notice in the app.
        /// </summary>
        /// <remarks>
        /// This feature was added to help notify users of important changes or updates to the app.
        /// It gives us a way to customize the following:
        /// * The text on the Help button on the front page of the app.
        /// * A message that will be displayed on the help page.
        /// * The text and URL of a link that will be displayed on the help page.
        /// </remarks>
        public async Task GetNotice()
        {
            try
            {
                using var client = new HttpClient();
                string json = await client.GetStringAsync(NoticeUrl);
                var data = System.Text.Json.JsonSerializer.Deserialize<NoticeData>(json, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                await this.NoticeData.SetAsync(data);
            }
            catch (Exception)
            {
                await this.NoticeData.SetAsync(
                    new NoticeData
                    { 
                        HelpButtonText = "Help", 
                        NoticeText = String.Empty, 
                        LinkText = String.Empty, 
                        LinkUrl = String.Empty
                    });
            }
        }
    }
}

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
        private const string NoticeUrl = $"https://github.com/PcmHammer/PcmHammer/blob/{Branch}/Apps/UI/UnoUI/notice.txt";
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

        public async Task GetNotice()
        {
            try
            {
                using var client = new HttpClient();
                string json = await client.GetStringAsync(NoticeUrl);

                // set json to mock data for testing purposes
                // string json = "{\"HelpButtonText\":\"Help\",\"NoticeText\":\"This is a test notice.\",\"LinkText\":\"Visit the wiki.\",\"LinkUrl\":\"https://pcmhacking.net/wiki/\"}";

                // Convert the JSON string to a NoticeData object
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

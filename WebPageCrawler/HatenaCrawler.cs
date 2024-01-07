using System;
using System.Net;
using HtmlAgilityPack;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using Azure.Identity;
using System.Diagnostics;
using System.Xml.Linq;

namespace WebPageCrawler
{
    public class HatenaCrawler
    {
        private static readonly HttpClient httpClient = new();
        private readonly ILogger logger;
        private readonly string hatenaID;

        public HatenaCrawler(ILoggerFactory loggerFactory)
        {
            logger = loggerFactory.CreateLogger<HatenaCrawler>();

            if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HATENA_ID")))
                throw new Exception("HATENA_ID is not set.");

            hatenaID = Environment.GetEnvironmentVariable("HATENA_ID");
        }

        [Function("HatenaCrawler")]
        public async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo myTimer)
        {
            logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            if (myTimer.ScheduleStatus is not null)
            {
                logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");
            }

            var entries = await HatenaBlogScraper($"https://{hatenaID}.hatenablog.jp/");
            await SaveHtmlToAzureBlob(entries);

            Console.WriteLine("Completed!");
        }

        public class HatenaBlogEntry
        {
            public HatenaBlogEntry(string title, string url, DateTimeOffset lastUpdated)
            {
                Title = title;
                Url = url;
                LastUpdated = lastUpdated;
            }
            public string Title { get; }
            public string Url { get; }
            public DateTimeOffset LastUpdated { get; }
        }

        async Task<List<HatenaBlogEntry>> HatenaBlogScraper(string blogRootUrl)
        {
            var continueLoop = true;
            var url = blogRootUrl;
            var entries = new List<HatenaBlogEntry>();

            while (continueLoop)
            {
                continueLoop = false;
                var html = await httpClient.GetStringAsync(url);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                //TODO:更新日確認

                foreach (var node in doc.DocumentNode.SelectNodes("//a[@class='entry-title-link bookmark']"))
                {
                    Console.WriteLine(node.GetAttributeValue("href", ""));
                    Console.WriteLine(node.InnerHtml);
                    // Console.WriteLine(node.InnerHtml + "," + node.GetAttributeValue("href", ""));  

                    var entry = new HatenaBlogEntry(
                        title: node.InnerHtml,
                        url: node.GetAttributeValue("href", ""),
                        lastUpdated: DateTimeOffset.UtcNow
                        );
                    entries.Add(entry);
                }

                var nextPageNode = doc.DocumentNode.SelectSingleNode("//span[@class='pager-next']/a");
                if (nextPageNode != null)
                {
                    url = nextPageNode.GetAttributeValue("href", "");
                    continueLoop = true;
                }
            }
            return entries;
        }

        async Task SaveHtmlToAzureBlob(List<HatenaBlogEntry> entries)
        {
            foreach (var entry in entries)
            {
                var blobName = entry.Url.Replace($"https://{hatenaID}.hatenablog.jp/entry/", "").Replace("/", "-") + ".html";
                var blobClient = new BlobClient
                (
                       Environment.GetEnvironmentVariable("BLOB_CONNECTION_STRING"),
                        "scraped-hatena",
                        blobName
                    );
                var blobContent = await httpClient.GetStreamAsync(entry.Url);
                //TODO:上書き確認
                await blobClient.UploadAsync(blobContent, true);
            }
        }

    }
}

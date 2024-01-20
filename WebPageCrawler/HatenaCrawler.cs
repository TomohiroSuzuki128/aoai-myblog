using System;
using System.Net.Http;
using AngleSharp;
using AngleSharp.Html.Parser;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using Azure.Identity;
using System.Diagnostics;
using System.Xml.Linq;
using Azure.Storage.Blobs.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HATENA_ID")))
                throw new Exception("HATENA_ID is not set.");

            hatenaID = Environment.GetEnvironmentVariable("HATENA_ID");
        }

        [Function("HatenaCrawler")]
        public async Task Run([TimerTrigger("0 */3 * * * *")] TimerInfo myTimer)
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
            var parser = new HtmlParser();

            while (continueLoop)
            {
                continueLoop = false;
                var html = await httpClient.GetStringAsync(url);
                var document = await parser.ParseDocumentAsync(html);

                foreach (var node in document.QuerySelectorAll("a.entry-title-link.bookmark"))
                {
                    var entryUrl = node.GetAttribute("href");
                    Console.WriteLine(entryUrl);
                    Console.WriteLine(node.TextContent);

                    var entryHtml = await httpClient.GetStringAsync(entryUrl);
                    var entryDocument = await parser.ParseDocumentAsync(entryHtml);
                    var publishedTimeNode = entryDocument.QuerySelector("meta[property='article:published_time']");
                    var publishedUnixEpochTicks = publishedTimeNode.GetAttribute("content");
                    var publishedDateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(long.Parse(publishedUnixEpochTicks));

                    var entry = new HatenaBlogEntry(
                       title: node.TextContent,
                       url: entryUrl,
                       lastUpdated: publishedDateTimeOffset.ToOffset(TimeSpan.FromHours(9))
                       );
                    entries.Add(entry);
                }

                var nextPageNode = document.QuerySelector("span.pager-next > a");
                if (nextPageNode != null)
                {
                    url = nextPageNode.GetAttribute("href");
                    continueLoop = true;
                }
            }
            return entries;
        }

        async Task SaveHtmlToAzureBlob(List<HatenaBlogEntry> entries)
        {
            foreach (var item in entries.Select((entry, index) => new { entry, index }))
            {
                var blobName = item.entry.Url.Replace($"https://{hatenaID}.hatenablog.jp/entry/", "").Replace("/", "-") + ".html";
                var blobClient = new BlobClient
                (
                       Environment.GetEnvironmentVariable("BLOB_CONNECTION_STRING"),
                        "scraped-hatena",
                        blobName
                    );
                var htmlStream = await httpClient.GetStreamAsync(item.entry.Url);
                var options = new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = "text/html",
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        { "url", item.entry.Url },
                        { "lastUpdated", item.entry.LastUpdated.ToString()}
                    }
                    // TODO:更新条件実装
                };
                Console.WriteLine($"Uploading : {item.index + 1}/{entries.Count.ToString("000000")} {item.entry.Url}");
                await blobClient.UploadAsync(htmlStream, options);
            }
        }
    }
}
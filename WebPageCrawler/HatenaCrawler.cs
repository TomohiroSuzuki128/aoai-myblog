using AngleSharp.Html.Parser;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace WebPageCrawler
{
    public class HatenaCrawler
    {
        static readonly HttpClient httpClient = new();
        readonly ILogger logger;
        readonly string hatenaID;

        public HatenaCrawler(ILoggerFactory loggerFactory)
        {
            logger = loggerFactory.CreateLogger<HatenaCrawler>();

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HATENA_ID")))
                throw new Exception("HATENA_ID is not set.");

            hatenaID = Environment.GetEnvironmentVariable("HATENA_ID") ?? string.Empty;
        }

        [Function("HatenaCrawler")]
        public async Task Run([TimerTrigger("0 */3 * * * *")] TimerInfo myTimer)
        {
            logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            if (myTimer.ScheduleStatus is not null)
                logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");

            var entries = await HatenaBlogScraper($"https://{hatenaID}.hatenablog.jp/");
            await SaveHtmlToAzureBlob(entries);

            Console.WriteLine("Function \"HatenaCrawler\" is Completed!");
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

        async ValueTask<List<HatenaBlogEntry>> HatenaBlogScraper(string blogRootUrl)
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
                    var entryUrl = node.GetAttribute("href") ?? throw new InvalidOperationException("href is not found.");
                    Console.WriteLine($"URL : {entryUrl}");
                    Console.WriteLine($"TextContent : {node.TextContent}");

                    var entryHtml = await httpClient.GetStringAsync(entryUrl);
                    var entryDocument = await parser.ParseDocumentAsync(entryHtml);
                    var publishedTimeNode = entryDocument.QuerySelector("meta[property='article:published_time']");
                    var publishedUnixEpochTicks = publishedTimeNode?.GetAttribute("content") ?? throw new InvalidOperationException("content is not found.");
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

        async ValueTask SaveHtmlToAzureBlob(List<HatenaBlogEntry> entries)
        {
            foreach (var (entry, index) in entries.Select((entry, index) => (entry, index)))
            {
                var blobName = entry.Url.Replace($"https://{hatenaID}.hatenablog.jp/entry/", "").Replace("/", "-") + ".html";
                var blobClient = new BlobClient
                (
                       Environment.GetEnvironmentVariable("BLOB_CONNECTION_STRING"),
                       Environment.GetEnvironmentVariable("SOURCE_BLOB_CONTAINER"),
                        blobName
                    );
                var htmlStream = await httpClient.GetStreamAsync(entry.Url);
                var options = new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = "text/html",
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        { "url", entry.Url },
                        { "lastUpdated", entry.LastUpdated.ToString()}
                    }
                    // TODO:更新条件実装
                };
                Console.WriteLine($"Uploading : {(index + 1).ToString("000000")}/{entries.Count.ToString("000000")} {entry.Url}");
                await blobClient.UploadAsync(htmlStream, options);
            }
        }
    }
}
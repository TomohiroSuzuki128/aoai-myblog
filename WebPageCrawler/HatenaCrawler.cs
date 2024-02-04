using AngleSharp.Html.Parser;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WebPageCrawler
{
    public class HatenaCrawler
    {
        static readonly HttpClient httpClient = new();
        readonly ILogger logger;
        readonly string hatenaID;
        readonly string blobConnecionString;
        readonly string blobContainer;
        readonly BlobContainerClient blobContainerClient;

        public HatenaCrawler(ILoggerFactory loggerFactory)
        {
            logger = loggerFactory.CreateLogger<HatenaCrawler>();

            hatenaID = Environment.GetEnvironmentVariable("HATENA_ID") ?? throw new InvalidOperationException("HATENA_ID is not set.");
            blobConnecionString = Environment.GetEnvironmentVariable("BLOB_CONNECTION_STRING") ?? throw new InvalidOperationException("BLOB_CONNECTION_STRING is not set.");
            blobContainer = Environment.GetEnvironmentVariable("SOURCE_BLOB_CONTAINER") ?? throw new InvalidOperationException("SOURCE_BLOB_CONTAINER is not set.");

            blobContainerClient = new BlobContainerClient(blobConnecionString, blobContainer);
        }

        [Function("HatenaCrawler")]
#if RELEASE
        public async Task Run([TimerTrigger("0 */30 * * * *")] TimerInfo myTimer)
#　else
        public async Task Run([TimerTrigger("0 */3 * * * *")] TimerInfo myTimer)
#endif
        {
            logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            if (myTimer.ScheduleStatus is not null)
                logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");

            var entries = await CrawlHatenaBlogArticles($"https://{hatenaID}.hatenablog.jp/");
            await UpdateAzureBlobContainerItems(entries);

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

        async ValueTask<List<HatenaBlogEntry>> CrawlHatenaBlogArticles(string blogRootUrl)
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

                    var jsonNode = entryDocument.QuerySelector("script[type='application/ld+json']");
                    var jsonString = jsonNode?.TextContent ?? throw new InvalidOperationException("json is not found.");
                    var articleInfo = JsonNode.Parse(jsonString);

                    if(articleInfo == null) 
                        throw new InvalidOperationException("json is not parsed.");

                    string datePublished = articleInfo["datePublished"]?.GetValue<string>() ?? throw new InvalidOperationException("Key \"datePublished\" is not found.");
                    string dateModified = articleInfo["dateModified"]?.GetValue<string>() ?? throw new InvalidOperationException("Key \"dateModified\" is not found.");
      
                    var publishedDateTimeOffset = DateTimeOffset.Parse(datePublished);
                    var lastUpdatedDateTimeOffset = DateTimeOffset.Parse(dateModified);

                    var entry = new HatenaBlogEntry(
                       title: node.TextContent,
                       url: entryUrl,
                       lastUpdated: lastUpdatedDateTimeOffset.ToOffset(TimeSpan.FromHours(9))
                       );
                    entries.Add(entry);
                }

                var nextPageNode = document.QuerySelector("span.pager-next > a");
                if (nextPageNode != null)
                {
                    url = nextPageNode.GetAttribute("href");
                    continueLoop = true;
#if DEBUG
                    continueLoop = false;
#endif
                }

            }
            return entries;
        }

        async ValueTask UpdateAzureBlobContainerItems(List<HatenaBlogEntry> entries)
        {
            foreach (var (entry, index) in entries.Select((entry, index) => (entry, index)))
            {
                var blobName = entry.Url.Replace($"https://{hatenaID}.hatenablog.jp/entry/", "").Replace("/", "-") + ".html";
                var blobClient = blobContainerClient.GetBlobClient(blobName);
                bool exists = await blobClient.ExistsAsync();
                if (!exists)
                {
                    Console.WriteLine($"Upload : {(index + 1).ToString("000000")}/{entries.Count.ToString("000000")} {entry.Url}");
                    await UploadOrUpdateAzureBlobItem(entry, blobClient);
                }
                else
                {
                    var response = await blobClient.DownloadAsync();
                    if (!string.IsNullOrEmpty(response.Value.Details.Metadata["lastUpdated"]))
                    {
                        var azureBlogLastUpdated = DateTimeOffset.Parse(response.Value.Details.Metadata["lastUpdated"]);

                        // 面倒なのでタイムスタンプが一致しているときのみスキップ
                        // つまり、はてな側のの更新日が古くても上書きする
                        // 理由：齟齬があるのは間違いなく、はてな側を正とすべきなのは何ら変わらないから
                        if (entry.LastUpdated == azureBlogLastUpdated)
                        {
                            Console.WriteLine($"Skip   : {(index + 1).ToString("000000")}/{entries.Count.ToString("000000")} {entry.Url}");
                            continue;
                        }
                    }

                    Console.WriteLine($"Upload : {(index + 1).ToString("000000")}/{entries.Count.ToString("000000")} {entry.Url}");
                    await UploadOrUpdateAzureBlobItem(entry, blobClient);
                }
            }
        }

        async ValueTask UploadOrUpdateAzureBlobItem(HatenaBlogEntry entry, BlobClient blobClient)
        {
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
            };
            await blobClient.UploadAsync(htmlStream, options);
        }
    }
}
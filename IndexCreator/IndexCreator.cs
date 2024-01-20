using System.IO;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SearchIndexClientBuilder;

namespace IndexCreator
{
    public class IndexCreator
    {
        private readonly ILogger<IndexCreator> _logger;

        public IndexCreator(ILogger<IndexCreator> logger)
        {
            _logger = logger;
        }

        [Function(nameof(IndexCreator))]
        public async Task Run([BlobTrigger("scraped-hatena/{name}",
                                Source = BlobTriggerSource.EventGrid,
                                Connection = "BLOB_CONNECTION_STRING")]
                                BlobClient myBlob,
                                string name, 
                                FunctionContext executionContext)
        {
            var contentResponse = await myBlob.DownloadContentAsync();
            string content = contentResponse.Value.Content.ToString() ?? string.Empty;
            var propertiesResponse = await myBlob.GetPropertiesAsync();
            string url = propertiesResponse.Value.Metadata["url"] ?? string.Empty;
            string lastUpdated = propertiesResponse.Value.Metadata["lastUpdated"] ?? string.Empty;


            _logger.LogInformation($"C# Blob trigger function Processed blob\n url: {url} \n Data: {content.Substring(0, 10)}");


            var parser = new HtmlParser();
            var document = await parser.ParseDocumentAsync(content);

            var title = string.Empty;
            var bodyText = string.Empty;
            if (document is IHtmlDocument)
            {
                var htmlDocument = document as IHtmlDocument;
                title = htmlDocument.Title;
                bodyText = htmlDocument.Body?.Text();
            }
            else
            {
                title = document.Title;
            }

            string indexeName = Environment.GetEnvironmentVariable("AI_SEARCH_INDEX_NAME", EnvironmentVariableTarget.Process) ?? string.Empty;
            string serviceName = Environment.GetEnvironmentVariable("AI_SEARCH_SEARVICE_NAME", EnvironmentVariableTarget.Process) ?? string.Empty;
            string apiKey = Environment.GetEnvironmentVariable("AI_SEARCH_API_KEY", EnvironmentVariableTarget.Process) ?? string.Empty;
            var searchIndexClient = IndexClientBuilder.GetClient(indexeName, serviceName, apiKey);


        }
    }
}

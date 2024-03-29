﻿using AIClient;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace IndexCreator
{
    public class IndexCreator
    {
        readonly ILogger<IndexCreator> _logger;

        public IndexCreator(ILogger<IndexCreator> logger)
        {
            _logger = logger;
        }

        [Function(nameof(IndexCreator))]
        public async Task Run([BlobTrigger("%SOURCE_BLOB_CONTAINER%/{name}",
                                Source = BlobTriggerSource.EventGrid,
                                Connection = "BLOB_CONNECTION_STRING")]
                                BlobClient myBlob,
                                string name,
                                FunctionContext executionContext)
        {
            string aiSerchIndexeName = Environment.GetEnvironmentVariable("AI_SEARCH_INDEX_NAME", EnvironmentVariableTarget.Process) ?? throw new InvalidOperationException("AI_SEARCH_INDEX_NAME is not set.");
            string aiSerchServiceName = Environment.GetEnvironmentVariable("AI_SEARCH_SERVICE_NAME", EnvironmentVariableTarget.Process) ?? throw new InvalidOperationException("AI_SEARCH_SERVICE_NAME is not set.");
            string aiSerchAdminApiKey = Environment.GetEnvironmentVariable("AI_SEARCH_ADMIN_KEY", EnvironmentVariableTarget.Process) ?? throw new InvalidOperationException("AI_SEARCH_ADMIN_KEY is not set.");
            var searchIndexClient = AIClientBuilder.GetSearchIndexClient(aiSerchIndexeName, aiSerchServiceName, aiSerchAdminApiKey);

            string openAIApiKey = Environment.GetEnvironmentVariable("OPEN_AI_API_KEY", EnvironmentVariableTarget.Process) ?? throw new InvalidOperationException("OPEN_AI_API_KEY is not set.");
            string openAIServiceName = Environment.GetEnvironmentVariable("OPEN_AI_SERVICE_NAME", EnvironmentVariableTarget.Process) ?? throw new InvalidOperationException("OPEN_AI_SERVICE_NAME is not set.");
            string openAIEmbeddingsDeproymentName = Environment.GetEnvironmentVariable("OPEN_AI_EMBEDDINGS_DEPROYMENTNAME", EnvironmentVariableTarget.Process) ?? throw new InvalidOperationException("OPEN_AI_EMBEDDINGS_DEPROYMENTNAME is not set.");

            var openAIClient = AIClientBuilder.GetOpenAIClient(openAIServiceName, openAIApiKey);


            var contentResponse = await myBlob.DownloadContentAsync();
            string content = contentResponse.Value.Content.ToString() ?? throw new InvalidOperationException("content is null."); ;
            var propertiesResponse = await myBlob.GetPropertiesAsync();
            string url = propertiesResponse.Value.Metadata["url"] ?? throw new InvalidOperationException("metadata.url is null.");
            string lastUpdated = propertiesResponse.Value.Metadata["lastUpdated"] ?? throw new InvalidOperationException("metadata.lastUpdated is null.");

            _logger.LogInformation($"C# Blob trigger function Processed blob\n url: {url} \n Data: {content.Substring(0, 10)}");

            HtmlParser parser = new();
            var document = await parser.ParseDocumentAsync(content);

            TextCleanser cleanser = new();

            string title;
            string bodyText;
            if (document is IHtmlDocument)
            {
                var htmlDocument = document as IHtmlDocument;
                title = htmlDocument.Title ?? string.Empty;
                bodyText = htmlDocument.Body?.Text() ?? throw new InvalidOperationException("Body text is null.");

                if (string.IsNullOrEmpty(title))
                    (bodyText, title) = cleanser.Cleanse(bodyText, name);
                else
                    (bodyText, _) = cleanser.Cleanse(bodyText);
            }
            else
            {
                throw new InvalidOperationException("document is not html.");
            }

            BobyContentChunker chunker = new();
            var chunks = chunker.ChunkText(bodyText);

            List<SearchDocument> documents = new();
            foreach (var (chunk, index) in chunks.Select((chunk, index) => (chunk, index)))
            {
                var id = Guid.NewGuid().ToString();
                var lastUpdatedText = DateTimeOffset.Now.ToString();
                var embedings = await GenerateEmbeddingsAsync(openAIClient, openAIEmbeddingsDeproymentName, chunk);
                
                documents.Add(new SearchDocument
                {
                    ["id"] = id,
                    ["lastUpdated"] = lastUpdatedText,
                    ["content"] = chunk,
                    ["title"] = title,
                    ["filePath"] = url,
                    ["url"] = url,
                    ["metadata"] = "{\"chunk_id\": \"" + id + "\"}",
                    ["contentVector"] = embedings
                });

                //Console.WriteLine($"Uploading documents : {(index + 1).ToString("000000")}/{chunks.Count.ToString("000000")}");
            }

            var searchClient = searchIndexClient.GetSearchClient(aiSerchIndexeName);
            await DeleteDocumentByUrlAsync(searchClient, url);
            await searchClient.UploadDocumentsAsync(documents);
        }

        async ValueTask<float[]> GenerateEmbeddingsAsync(OpenAIClient openAIClient, string deproymentName, string text)
        {
            var result = await openAIClient.GetEmbeddingsAsync(
                new EmbeddingsOptions(deproymentName, new List<string>() { text }));
            return result.Value.Data[0].Embedding.ToArray();
        }

        // url フィールドでインデックスデータを取得し、id フィールドで削除する
        async Task DeleteDocumentByUrlAsync(SearchClient searchClient, string url)
        {
            SearchOptions options = new SearchOptions()
            {
                Filter = $"url eq '{url}'",
                Size = 100
            };

            var response = searchClient.Search<SearchDocument>("*", options);

            foreach (var result in response.Value.GetResults())
            {
                var id = result.Document["id"].ToString() ?? string.Empty;
                await searchClient.DeleteDocumentsAsync("id", new List<string>() { id });
            }
        }
    }
}

using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;

namespace AIClient
{
    public static class AIClientBuilder
    {
        public static SearchIndexClient GetSearchIndexClient(string indexName, string serviceName, string apiKey)
        {
            var searchServiceEndPoint = $"https://{serviceName}.search.windows.net/";
            var options = new SearchClientOptions(SearchClientOptions.ServiceVersion.V2023_11_01);
            var indexClient = new SearchIndexClient(new Uri(searchServiceEndPoint), new AzureKeyCredential(apiKey), options);
            return indexClient;
        }

        public static OpenAIClient GetOpenAIClient(string serviceName, string apiKey)
        {
            var openAIClient = new OpenAIClient(new Uri($"https://{serviceName}.openai.azure.com/"), new AzureKeyCredential(apiKey));
            return openAIClient;
        }

    }
}

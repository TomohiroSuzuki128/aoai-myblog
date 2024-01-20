using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;

namespace SearchIndexClientBuilder
{
    public static class IndexClientBuilder
    {
        public static SearchIndexClient GetClient(string indexName, string serviceName, string apiKey)
        {
            var searchServiceEndPoint = $"https://{serviceName}.search.windows.net/";
            var options = new SearchClientOptions(SearchClientOptions.ServiceVersion.V2023_11_01);
            var indexClient = new SearchIndexClient(new Uri(searchServiceEndPoint), new AzureKeyCredential(apiKey), options);
            return indexClient;
        }
    }
}

using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace AzureAISearchIndexInitializer
{
    public static class IndexInitializer
    {
        public static async Task Initialize(string indexName, string serviceName, string apiKey)
        {
            var searchServiceEndPoint = $"https://{serviceName}.search.windows.net/";
            var options = new SearchClientOptions(SearchClientOptions.ServiceVersion.V2023_11_01);
            var indexClient = new SearchIndexClient(new Uri(searchServiceEndPoint), new AzureKeyCredential(apiKey), options);


            //"fields": [
            //             {
            //    "name": "id",
            //            "type": "Edm.String",
            //            "searchable": True,
            //            "key": True,
            //        },
            //        {
            //    "name": "content",
            //            "type": "Edm.String",
            //            "searchable": True,
            //            "sortable": False,
            //            "facetable": False,
            //            "filterable": False,
            //            "analyzer": f"{language}.lucene" if language else None,
            //        },
            //        {
            //    "name": "title",
            //            "type": "Edm.String",
            //            "searchable": True,
            //            "sortable": False,
            //            "facetable": False,
            //            "filterable": False,
            //            "analyzer": f"{language}.lucene" if language else None,
            //        },
            //        {
            //    "name": "filepath",
            //            "type": "Edm.String",
            //            "searchable": True,
            //            "sortable": False,
            //            "facetable": False,
            //            "filterable": False,
            //        },
            //        {
            //    "name": "url",
            //            "type": "Edm.String",
            //            "searchable": True,
            //        },
            //        {
            //    "name": "metadata",
            //            "type": "Edm.String",
            //            "searchable": True,
            //        },
            //    ],
            //    "suggesters": [],
            //    "scoringProfiles": [],
            //    "semantic": {
            //            "configurations": [
            //                {
            //                "name": semantic_config_name,
            //                "prioritizedFields": {
            //                    "titleField": { "fieldName": "title"},
            //                    "prioritizedContentFields": [{ "fieldName": "content"}],
            //                    "prioritizedKeywordsFields": [],
            //                },
            //            }
            //        ]
            //    },
            //}

            //if vector_config_name:
            //    body["fields"].append({
            //        "name": "contentVector",
            //        "type": "Collection(Edm.Single)",
            //        "searchable": True,
            //        "retrievable": True,
            //        "dimensions": 1536,
            //        "vectorSearchConfiguration": vector_config_name
            //    })

            //    body["vectorSearch"] = {
            //        "algorithmConfigurations": [
            //            {
            //                "name": vector_config_name,
            //                "kind": "hnsw"
            //            }
            //        ]
            //    }



            var semanticConfigName = "default";
            var vectorConfigName = "default";
            const string algorithmConfigName = "hnsw";

            var vectorSearch = new VectorSearch();
            vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration(algorithmConfigName));
            vectorSearch.Profiles.Add(new VectorSearchProfile(vectorConfigName, algorithmConfigName));

            var semanticPrioritizedFields = new SemanticPrioritizedFields()
            {
                TitleField = new SemanticField("title"),
            };
            semanticPrioritizedFields.ContentFields.Add(new SemanticField("content"));
            var semanticSearch = new SemanticSearch();
            semanticSearch.Configurations.Add(new SemanticConfiguration(semanticConfigName, semanticPrioritizedFields));

            var defaulIindex = new SearchIndex(indexName)
            {
                VectorSearch = vectorSearch,
                //SemanticSearch = semanticSearch,
                Fields =
                {
                    new SearchField("id", SearchFieldDataType.String)
                    {
                        IsKey = true,
                        IsSearchable = true
                    },
                    new SearchField("content", SearchFieldDataType.String)
                    {
                        IsSearchable = true,
                        IsSortable = false,
                        IsFacetable = false,
                        IsFilterable = false,
                        AnalyzerName = LexicalAnalyzerName.JaMicrosoft
                    },
                    new SearchField("title", SearchFieldDataType.String)
                    {
                        IsSearchable = true,
                        IsSortable = false,
                        IsFacetable = false,
                        IsFilterable = false,
                        AnalyzerName = LexicalAnalyzerName.JaMicrosoft
                    },
                    new SearchField("filePath", SearchFieldDataType.String)
                    {
                        IsSearchable = true,
                        IsSortable = false,
                        IsFacetable = false,
                        IsFilterable = false
                    },
                    new SearchField("url", SearchFieldDataType.String)
                    {
                        IsSearchable = true
                    },
                    new SearchField("metadata", SearchFieldDataType.String)
                    {
                        IsSearchable = true
                    },
                    new SearchField("contentVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                    {
                        IsSearchable = true,
                        VectorSearchDimensions = 1536,
                        VectorSearchProfileName = vectorConfigName
                    },
                },
            };

            try
            {
                var searchIndex = await indexClient.GetIndexAsync(indexName);
                if (searchIndex != null)
                {
                    var response = await indexClient.DeleteIndexAsync(indexName);
                    Console.WriteLine($"DeleteIndexAsync : {response.Status} {response.ReasonPhrase}");
                }
            }

            catch (RequestFailedException e) when (e.Status == 404)
            {
                Console.WriteLine("The index doesn't exist. No deletion occurred.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                return;
            }

            try
            {
                var response = await indexClient.CreateIndexAsync(defaulIindex);
                var rawResponse = response.GetRawResponse();
                Console.WriteLine($"CreateOrUpdateIndexAsync : {rawResponse.Status} {rawResponse.ReasonPhrase}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                return;
            }

        }
    }
}
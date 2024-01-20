using Azure;
using Azure.AI.FormRecognizer;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.DeepDev;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace IndexCreator
{
    public class DataUtils
    {
        public static Dictionary<string, string> FILE_FORMAT_DICT = new Dictionary<string, string>
        {
            { "md", "markdown" },
            { "txt", "text" },
            { "html", "html" },
            { "shtml", "html" },
            { "htm", "html" },
            { "py", "python" },
            { "pdf", "pdf" },
            { "docx", "docx" },
            { "pptx", "pptx" }
        };

        public Dictionary<string, string> PDF_HEADERS = new Dictionary<string, string>
        {
            { "title", "h1" },
            { "sectionHeading", "h2" }
        };


        public static readonly string[] SENTENCE_ENDINGS = { ".", "!", "?" };

        public static List<string> WORDS_BREAKS = new List<string>(new string[] { ",", ";", ":", " ", "(", ")", "[", "]", "{", "}", "\t", "\n" }.Reverse());

        public Dictionary<string, string> HTML_TABLE_TAGS = new Dictionary<string, string>
        {
            { "table_open", "<table>" },
            { "table_close", "</table>" },
            { "row_open", "<tr>" }
        };




        public static async ValueTask<string> GetEmbedding(string text, string embeddingModelEndpoint, string embeddingModelKey, DefaultAzureCredential defaultAzureCredential)
        {
            string endpoint = embeddingModelEndpoint ?? Environment.GetEnvironmentVariable("EMBEDDING_MODEL_ENDPOINT");
            string key = embeddingModelKey ?? Environment.GetEnvironmentVariable("EMBEDDING_MODEL_KEY");

            if (defaultAzureCredential == null && (endpoint == null || key == null))
            {
                throw new ArgumentNullException("EMBEDDING_MODEL_ENDPOINT and EMBEDDING_MODEL_KEY are required for embedding");
            }

            try
            {
                string[] endpointParts = endpoint.Split("/openai/deployments/");
                string baseUrl = endpointParts[0];
                string deploymentId = endpointParts[1].Split("/embeddings")[0];

                var options = new OpenAIClientOptions(OpenAIClientOptions.ServiceVersion.V2023_05_15);

                var apiKey = string.Empty;
                var apiType = string.Empty;
                AccessToken defaultToken;

                if (defaultAzureCredential != null)
                {
                    defaultToken = await defaultAzureCredential.GetTokenAsync(new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }));
                    apiType = "azure_ad";
                }
                else
                {
                    apiType = "azure";
                    apiKey = key;
                }

                var keyCredential = new AzureKeyCredential(apiKey);

                // DefaultAzureCredentialで認証をとって Token を取得する
                //var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { /* VSを除外したりとオプション指定 */ });
                //  var defaultTokenCredential = new Azure.Core.TokenCredentials(defaultToken.Token);

                // Token をさすことで認証が取れる
                // var cred = new AzureCredentials(defaultTokenCredential, defaultTokenCredential, null,  AzureEnvironment.AzureGlobalCloud);


                var openaiClient = new OpenAIClient(new Uri(embeddingModelEndpoint), keyCredential);
                //openaiClient.api_base = baseUrl;

                var embeddingOptions = new EmbeddingsOptions(deploymentId, new List<string>() { text });

                var embeddings = await openaiClient.GetEmbeddingsAsync(embeddingOptions);
                return embeddings.Value.Data[0].ToString() ?? "";
            }
            catch (Exception e)
            {
                throw new Exception($"Error getting embeddings with endpoint={endpoint} with error={e}");
            }
        }



        //Chunks the given directory recursively
        //Args:
        //    directory_path (str): The directory to chunk.
        //    ignore_errors (bool): If true, ignores errors and returns None.
        //    num_tokens (int): The number of tokens to use for chunking.
        //    min_chunk_size (int): The minimum chunk size.
        //    url_prefix (str): The url prefix to use for the files. If None, the url will be None. If not None, the url will be url_prefix + relpath. 
        //                        For example, if the directory path is /home/user/data and the url_prefix is https://example.com/data, 
        //                        then the url for the file /home/user/data/file1.txt will be https://example.com/data/file1.txt
        //    token_overlap (int): The number of tokens to overlap between chunks.
        //    extensions_to_process (List[str]): The list of extensions to process. 
        //    form_recognizer_client: Optional form recognizer client to use for pdf files.
        //    use_layout (bool): If true, uses Layout model for pdf files. Otherwise, uses Read.
        //    add_embeddings (bool): If true, adds a vector embedding to each chunk using the embedding model endpoint and key.

        //Returns:
        //    List[Document]: List of chunked documents.

        //public static async Task<List<Document>> ChunkDirectory(string directoryPath, bool ignoreErrors = true, int numTokens = 1024, int minChunkSize = 10, string urlPrefix = null, int tokenOverlap = 0, List<string> extensionsToProcess = null, FormRecognizerClient formRecognizerClient = null, bool useLayout = false, int njobs = 4, bool addEmbeddings = false, DefaultAzureCredential azureCredential = null, string embeddingEndpoint = null)
        //{
        //    List<Document> chunks = new List<Document>();

        //    var totalFiles = 0;
        //    var numUnsupportedFormatFiles = 0;
        //    var numFilesWithErrors = 0;
        //    var skippedChunks = 0;

        //    List<string> allFilesDirectory = GetFilesRecursively(directoryPath);
        //    List<string> filesToProcess = allFilesDirectory.Where(file => File.Exists(file)).ToList();
        //    Console.WriteLine($"Total files to process={filesToProcess.Count} out of total directory size={allFilesDirectory.Count}");

        //    if (njobs == 1)
        //    {
        //        Console.WriteLine("Single process to chunk and parse the files. --njobs > 1 can help performance.");
        //        foreach (string filePath in filesToProcess)
        //        {
        //            totalFiles++;
        //            var (result, isError) = ProcessFile(filePath, directoryPath, ignoreErrors, numTokens, minChunkSize, urlPrefix, tokenOverlap, extensionsToProcess, formRecognizerClient, useLayout, addEmbeddings, azureCredential, embeddingEndpoint);
        //            if (isError)
        //            {
        //                numFilesWithErrors++;
        //                continue;
        //            }
        //            chunks.AddRange(result.Chunks);
        //            numUnsupportedFormatFiles += result.NumUnsupportedFormatFiles;
        //            numFilesWithErrors += result.NumFilesWithErrors;
        //            skippedChunks += result.SkippedChunks;
        //        }
        //    }
        //    else if (njobs > 1)
        //    {
        //        Console.WriteLine($"Multiprocessing with njobs={njobs}");
        //        var processFilePartial = new Func<string, (ChunkingResult, bool)>(filePath =>
        //        {
        //            return ProcessFile(filePath, directoryPath, ignoreErrors, numTokens, minChunkSize, urlPrefix, tokenOverlap, extensionsToProcess, null, useLayout, addEmbeddings, azureCredential, embeddingEndpoint);
        //        });
        //        using (var executor = new ProcessPoolExecutor(njobs))
        //        {
        //            var futures = executor.Map(processFilePartial, filesToProcess);
        //            foreach (var (result, isError) in futures)
        //            {
        //                totalFiles++;
        //                if (isError)
        //                {
        //                    numFilesWithErrors++;
        //                    continue;
        //                }
        //                chunks.AddRange(result.Chunks);
        //                numUnsupportedFormatFiles += result.NumUnsupportedFormatFiles;
        //                numFilesWithErrors += result.NumFilesWithErrors;
        //                skippedChunks += result.SkippedChunks;
        //            }
        //        }
        //    }

        //    return new ChunkingResult
        //    {
        //        Chunks = chunks,
        //        TotalFiles = totalFiles,
        //        NumUnsupportedFormatFiles = numUnsupportedFormatFiles,
        //        NumFilesWithErrors = numFilesWithErrors,
        //        SkippedChunks = skippedChunks
        //    };
        //}

        public async Task<string> ExtractPdfContent(string filePath, DocumentAnalysisClient documentAnalysisClient, bool useLayout = false)
        {
            int offset = 0;
            var pageMap = new List<(int, int, string)>();
            string model = useLayout ? "prebuilt-layout" : "prebuilt-read";

            using var stream = new FileStream(filePath, FileMode.Open);
            var analyzeDocumentOperation = await documentAnalysisClient.AnalyzeDocumentAsync(WaitUntil.Started, model, stream);
            var formRecognizerResults = await analyzeDocumentOperation.WaitForCompletionAsync();

            var rolesStart = new Dictionary<int, string>();
            var rolesEnd = new Dictionary<int, string>();
            foreach (var paragraph in formRecognizerResults.Value.Paragraphs)
            {
                if (paragraph.Role != null)
                {
                    int paraStart = paragraph.Spans[0].Offset;
                    int paraEnd = paragraph.Spans[0].Offset + paragraph.Spans[0].Length;
                    rolesStart[paraStart] = paragraph.Role;
                    rolesEnd[paraEnd] = paragraph.Role;
                }
            }

            for (int pageNum = 0; pageNum < formRecognizerResults.Value.Pages.Count; pageNum++)
            {
                var page = formRecognizerResults.Value.Pages[pageNum];
                var tablesOnPage = formRecognizerResults.Value.Tables.Where(table => table.BoundingRegions[0].PageNumber == pageNum + 1).ToList();

                int pageOffset = page.Spans[0].Offset;
                int pageLength = page.Spans[0].Length;
                var tableChars = Enumerable.Repeat(-1, pageLength).ToArray();
                for (int tableId = 0; tableId < tablesOnPage.Count; tableId++)
                {
                    var table = tablesOnPage[tableId];
                    foreach (var span in table.Spans)
                    {
                        for (int i = 0; i < span.Length; i++)
                        {
                            int idx = span.Offset - pageOffset + i;
                            if (idx >= 0 && idx < pageLength)
                            {
                                tableChars[idx] = tableId;
                            }
                        }
                    }
                }

                var pageText = new StringBuilder();
                var addedTables = new HashSet<int>();
                for (int idx = 0; idx < tableChars.Length; idx++)
                {
                    int tableId = tableChars[idx];
                    if (tableId == -1)
                    {
                        int position = pageOffset + idx;
                        if (rolesStart.ContainsKey(position))
                        {
                            string role = rolesStart[position];
                            if (PDF_HEADERS.ContainsKey(role))
                            {
                                pageText.Append($"<{PDF_HEADERS[role]}>");
                            }
                        }
                        if (rolesEnd.ContainsKey(position))
                        {
                            string role = rolesEnd[position];
                            if (PDF_HEADERS.ContainsKey(role))
                            {
                                pageText.Append($"</{PDF_HEADERS[role]}>");
                            }
                        }

                        pageText.Append(formRecognizerResults.Value.Content[pageOffset + idx]);
                    }
                    else if (!addedTables.Contains(tableId))
                    {
                        pageText.Append(TableToHtml(tablesOnPage[tableId]));
                        addedTables.Add(tableId);
                    }
                }

                pageText.Append(" ");
                pageMap.Add((pageNum, offset, pageText.ToString()));
                offset += pageText.Length;
            }

            string fullText = string.Join("", pageMap.Select(x => x.Item3));
            return fullText;
        }


        public (string, string, string) ExtractStorageDetailsFromUrl(string url)
        {
            var regex = new Regex(@"https:\/\/([^\/.]*)\.blob\.core\.windows\.net\/([^\/]*)\/(.*)");
            var match = regex.Match(url);

            if (!match.Success)
            {
                throw new Exception($"Not a valid blob storage URL: {url}");
            }

            return (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
        }

        public async Task DownloadBlobUrlToLocalFolder(string blobUrl, string localFolder, string credential)
        {
            var (storageAccount, containerName, path) = ExtractStorageDetailsFromUrl(blobUrl);
            var containerUrl = $"https://{storageAccount}.blob.core.windows.net/{containerName}";
            var blobServiceClient = new BlobServiceClient(containerUrl, new StorageSharedKeyCredential(storageAccount, credential));

            if (!string.IsNullOrEmpty(path) && !path.EndsWith("/"))
            {
                path += "/";
            }

            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            await foreach (var blobItem in containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, path))
            {
                var relativePath = blobItem.Name.Substring(path.Length);
                var destinationPath = Path.Combine(localFolder, relativePath);
                var destinationFolder = Path.GetDirectoryName(destinationPath);

                if (!Directory.Exists(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }

                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                var response = await blobClient.DownloadAsync();

                using (var fileStream = File.OpenWrite(destinationPath))
                {
                    await response.Value.Content.CopyToAsync(fileStream);
                }
            }
        }

        //Gets all files in the given directory recursively.
        //Args:
        //    directoryPath(str) : The directory to get files from.
        //Returns:
        //    List<string>: List of file paths.
        public static List<string> GetFilesRecursively(string directoryPath)
        {
            return Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();
        }


        public string TableToHtml(Table table)
        {
            var tableHtml = new StringBuilder("<table>");
            var rows = Enumerable.Range(0, table.RowCount)
                .Select(i => table.Cells.Where(cell => cell.RowIndex == i)
                .OrderBy(cell => cell.ColumnIndex)
                .ToList())
                .ToList();

            foreach (var rowCells in rows)
            {
                tableHtml.Append("<tr>");
                foreach (var cell in rowCells)
                {
                    var tag = (cell.Kind == "columnHeader" || cell.Kind == "rowHeader") ? "th" : "td";
                    var cellSpans = "";
                    if (cell.ColumnSpan > 1) cellSpans += $" colSpan={cell.ColumnSpan}";
                    if (cell.RowSpan > 1) cellSpans += $" rowSpan={cell.RowSpan}";
                    tableHtml.Append($"<{tag}{cellSpans}>{WebUtility.HtmlEncode(cell.Content)}</{tag}>");
                }
                tableHtml.Append("</tr>");
            }
            tableHtml.Append("</table>");
            return tableHtml.ToString();
        }



    }

    public class SingletonFormRecognizerClient
    {
        private static DocumentAnalysisClient instance;

        static SingletonFormRecognizerClient()
        {
            Console.WriteLine("SingletonFormRecognizerClient: Creating instance of Form recognizer per process");
            string url = Environment.GetEnvironmentVariable("FORM_RECOGNIZER_ENDPOINT") ?? "";
            string key = Environment.GetEnvironmentVariable("FORM_RECOGNIZER_KEY") ?? "";
            if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(key))
            {
                instance = new DocumentAnalysisClient(new Uri(url), new AzureKeyCredential(key));
            }
            else
            {
                Console.WriteLine("SingletonFormRecognizerClient: Skipping since credentials not provided. Assuming NO form recognizer extensions(like .pdf) in directory");
            }
        }

        public static SingletonFormRecognizerClient Instance
        {
            get { return instance; }
        }
    }

    public class ChunkingResult
    {
        public List<Document> Chunks { get; set; }
        public int TotalFiles { get; set; }
        public int NumUnsupportedFormatFiles { get; set; }
        public int NumFilesWithErrors { get; set; }
        public int SkippedChunks { get; set; }
    }
}
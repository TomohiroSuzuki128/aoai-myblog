using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Security.KeyVault.Secrets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using static System.Reflection.Metadata.BlobBuilder;
using System.Xml.Linq;
using Newtonsoft.Json;
using static IndexCreator.DataUtils;

namespace IndexCreator
{
    public class ChunkDocuments
    {

        //        $schema: https://azuremlschemas.azureedge.net/latest/commandComponent.schema.json
        //type: command

        //name: chunk_documents
        //display_name: Crack and Chunk Documents
        //version: 1

        //inputs:
        //  input_data_path:
        //    type: uri_folder
        //  config_file:
        //    type: uri_file

        //outputs:
        //  document_chunks_file_path:
        //    type: uri_file

        //code: .

        //environment: 
        //  conda_file: conda.yml
        //  image: mcr.microsoft.com/azureml/curated/python-sdk-v2:latest

        //command: >-
        //  python chunk_documents.py --input_data_path ${ { inputs.input_data_path} } --config_file ${{inputs.config_file
        //    }
        //}
        //--output_file_path ${ { outputs.document_chunks_file_path} }

        static readonly int RETRY_COUNT = 5;

        public ChunkDocuments(string indputDataPath, string outputFilePath, string configFilePath)
        {
            var configJson = File.ReadAllText(configFilePath);
            var configNode = JsonNode.Parse(File.ReadAllText(configJson));

            if (configNode == null)
                throw new ArgumentNullException("Failed to parse config file");

            string secretName = configNode.AsArray().Select(s => s["document_intelligence_secret_name"].ToString()).FirstOrDefault() ?? "";

            var credential = new DefaultAzureCredential();

            // Keyvault Secret Client
            string keyVaultUrl = configNode.AsArray().Select(s => s["keyvault_url"].ToString()).FirstOrDefault() ?? "";

            SecretClient secretClient;
            if (string.IsNullOrEmpty(keyVaultUrl))
            {
                Console.WriteLine("No keyvault url provided in config file. Secret client will not be set up.");
                throw new ArgumentNullException("No keyvault url provided in config file. Secret client will not be set up.");
            }
            else
                secretClient = new SecretClient(new Uri(keyVaultUrl), credential);

            // Optional client for cracking documents
            var documentIntelligenceClient = GetDocumentIntelligenceClient(configJson, secretClient);

            // Crack and chunk documents
            Console.WriteLine("Cracking and chunking documents...");

            var chunkingResult = ChunkDirectory(
                directoryPath: indputDataPath,
                numTokens: int.Parse(configNode.AsArray().Select(s => s["chunk_size"].ToString()).FirstOrDefault() ?? "1024"),
                tokenOverlap: int.Parse(configNode.AsArray().Select(s => s["token_overlap"].ToString()).FirstOrDefault() ?? "128"),
                formRecognizerClient: documentIntelligenceClient,
                useLayout: bool.Parse(configNode.AsArray().Select(s => s["use_layout"].ToString()).FirstOrDefault() ?? "false"),
                njobs: 1);

            Console.WriteLine("Processed " + chunkingResult.TotalFiles + " files");
            Console.WriteLine("Unsupported formats: " + chunkingResult.NumUnsupportedFormatFiles + " files");
            Console.WriteLine("Files with errors: " + chunkingResult.NumFilesWithErrors + " files");
            Console.WriteLine("Found " + chunkingResult.Chunks.Count + " chunks");

            Console.WriteLine("Writing chunking result to " + outputFilePath + "...");
            using (var file = new StreamWriter(outputFilePath))
            {
                int id = 0;
                foreach (var chunk in chunkingResult.Chunks)
                {
                    var d = ToDictionary(chunk);
                    // add id to documents
                    d.Add("id", id.ToString());
                    file.WriteLine(JsonConvert.SerializeObject(d));
                    id++;
                }
            }
            Console.WriteLine("Chunking result written to " + outputFilePath + ".");

        }

        public DocumentAnalysisClient GetDocumentIntelligenceClient(string config, SecretClient secretClient)
        {
            Console.WriteLine("Setting up Document Intelligence client...");
            var node = JsonNode.Parse(config);
            var secretName = node.AsArray().Select(s => s["document_intelligence_secret_name"].ToString()).FirstOrDefault();

            if (secretClient == null || string.IsNullOrEmpty(secretName))
            {
                Console.WriteLine("No keyvault url or secret name provided in config file. Document Intelligence client will not be set up.");
                return null;
            }

            string endpoint = node.AsArray().Select(s => s["document_intelligence_endpoint"].ToString()).FirstOrDefault();
            if (string.IsNullOrEmpty(endpoint))
            {
                Console.WriteLine("No endpoint provided in config file. Document Intelligence client will not be set up.");
                return null;
            }

            try
            {
                var documentIntelligenceSecret = secretClient.GetSecret(secretName);
                Environment.SetEnvironmentVariable("FORM_RECOGNIZER_ENDPOINT", endpoint);
                Environment.SetEnvironmentVariable("FORM_RECOGNIZER_KEY", documentIntelligenceSecret.Value.Value);

                var documentIntelligenceCredential = new AzureKeyCredential(documentIntelligenceSecret.Value.Value);

                var client = new DocumentAnalysisClient(new Uri(endpoint), documentIntelligenceCredential);
                Console.WriteLine("Document Intelligence client set up.");
                return client;
            }
            catch (Exception e)
            {
                Console.WriteLine("Error setting up Document Intelligence client: " + e.Message);
                return null;
            }
        }


        public ChunkingResult ChunkDirectory(
            string directoryPath,
            bool ignoreErrors = true,
            int numTokens = 1024,
            int minChunkSize = 10,
            string urlPrefix,
            int tokenOverlap = 0,
            List<string> extensionsToProcess, // This needs to be initialized with the keys of FILE_FORMAT_DICT
            SingletonFormRecognizerClient formRecognizerClient, // Replace object with the actual type
            bool useLayout = false,
            int njobs = 4,
            bool addEmbeddings = false,
            object azureCredential = null, // Replace object with the actual type
            string embeddingEndpoint
            )
        {
            var chunks = new List<string>();
            var totalFiles = 0;
            var numUnsupportedFormatFiles = 0;
            var numFilesWithErrors = 0;
            var skippedChunks = 0;

            var allFilesDirectory = DataUtils.GetFilesRecursively(directoryPath); // This needs to be implemented
            var filesToProcess = allFilesDirectory.Where(filePath => File.Exists(filePath)).ToList();

            Console.WriteLine($"Total files to process={filesToProcess.Count} out of total directory size={allFilesDirectory.Count}");


            Console.WriteLine("Single process to chunk and parse the files. --njobs > 1 can help performance.");
            foreach (var filePath in filesToProcess) // Assuming filesToProcess is a List<string>
            {
                totalFiles += 1;
                var result = ProcessFile(filePath: filePath,
                                directoryPath: directoryPath,
                                ignoreErrors: ignoreErrors,
                                numTokens: numTokens,
                                minChunkSize: minChunkSize,
                                formRecognizerClient: formRecognizerClient,
                                urlPrefix: urlPrefix,
                                tokenOverlap: tokenOverlap,
                                extensionsToProcess: extensionsToProcess,

                                useLayout: useLayout,
                                addEmbeddings: addEmbeddings,
                                azureCredential: azureCredential,
                                embeddingEndpoint: embeddingEndpoint
                                );
                // Assuming ProcessFile is a method that takes these parameters and returns a Result
                if (result.IsError)
                {
                    numFilesWithErrors += 1;
                    continue;
                }
                chunks.AddRange(result.Chunks);
                numUnsupportedFormatFiles += result.NumUnsupportedFormatFiles;
                numFilesWithErrors += result.NumFilesWithErrors;
                skippedChunks += result.SkippedChunks;
            }


            return new ChunkingResult
            {
                Chunks = chunks,
                TotalFiles = totalFiles,
                NumUnsupportedFormatFiles = numUnsupportedFormatFiles,
                NumFilesWithErrors = numFilesWithErrors,
                SkippedChunks = skippedChunks
            };
        }

        public ChunkingResult ChunkFile(
            string filePath,
            bool ignoreErrors = true,
            int numTokens = 256,
            int minChunkSize = 10,
            string url,
            int tokenOverlap = 0,
            IEnumerable<string> extensionsToProcess = null, // Assuming FILE_FORMAT_DICT.keys() is a collection of strings
            object formRecognizerClient = null, // Replace object with the actual type of formRecognizerClient
            bool useLayout = false,
            bool addEmbeddings = false,
            object azureCredential = null, // Replace object with the actual type of azureCredential
            string embeddingEndpoint = null
        )
        {
            string fileName = Path.GetFileName(filePath);
            string fileFormat = GetFileFormat(fileName, extensionsToProcess); // Assuming GetFileFormat is a method that takes a file name and a collection of extensions and returns a string
            if (string.IsNullOrEmpty(fileFormat))
            {
                if (ignoreErrors)
                {
                    return new ChunkingResult
                    {
                        Chunks = new List<Document>(), // Assuming Document is a class
                        TotalFiles = 1,
                        NumUnsupportedFormatFiles = 1
                    };
                }
                else
                {
                    throw new UnsupportedFormatError($"{fileName} is not supported"); // Assuming UnsupportedFormatError is an exception class
                }
            }

            bool crackedPdf = false;
            string content;
            if (new[] { "pdf", "docx", "pptx" }.Contains(fileFormat))
            {
                if (formRecognizerClient == null)
                {
                    throw new UnsupportedFormatError("formRecognizerClient is required for pdf files");
                }
                content = ExtractPdfContent(filePath, formRecognizerClient, useLayout); // Assuming ExtractPdfContent is a method that takes a file path, a formRecognizerClient, and a boolean and returns a string
                crackedPdf = true;
            }
            else
            {
                try
                {
                    content = File.ReadAllText(filePath, Encoding.UTF8);
                }
                catch (DecoderFallbackException)
                {
                    byte[] binaryContent = File.ReadAllBytes(filePath);
                    //string encoding = Detect(binaryContent); // Assuming Detect is a method that takes a byte array and returns a string
                    content = Encoding.UTF8.GetString(binaryContent);
                }
            }

            return ChunkContent(
                content,
                fileName,
                url,
                azureCredential,
                embeddingEndpoint,
                extensionsToProcess,
                ignoreErrors,
                numTokens,
                minChunkSize,
                Math.Max(0, tokenOverlap),
                crackedPdf,
                useLayout,
                addEmbeddings
            ); // Assuming ChunkContent is a method that takes these parameters and returns a ChunkingResult
        }


        public ChunkingResult ChunkContent(
            string content,
            string fileName,
            string url,
            object azureCredential, // Replace object with the actual type of azureCredential
            string embeddingEndpoint,
            IEnumerable<string> extensionsToProcess, // Assuming FILE_FORMAT_DICT.keys() is a collection of strings
            bool ignoreErrors = true,
            int numTokens = 256,
            int minChunkSize = 10,
            int tokenOverlap = 0,
            bool crackedPdf = false,
            bool useLayout = false,
            bool addEmbeddings = false
        )
        {
            try
            {
                string fileFormat;
                if (fileName == null || (crackedPdf && !useLayout))
                {
                    fileFormat = "text";
                }
                else if (crackedPdf)
                {
                    fileFormat = "html_pdf"; // differentiate it from native html
                }
                else
                {
                    fileFormat = GetFileFormat(fileName, extensionsToProcess); // Assuming GetFileFormat is a method that takes a file name and a collection of extensions and returns a string
                    if (fileFormat == null)
                    {
                        throw new Exception($"{fileName} is not supported");
                    }
                }

                var chunkedContext = ChunkContentHelper(
                    content,
                    fileName,
                    fileFormat,
                    numTokens,
                    tokenOverlap
                ); // Assuming ChunkContentHelper is a method that takes these parameters and returns an IEnumerable of tuples

                var chunks = new List<Document>(); // Assuming Document is a class
                int skippedChunks = 0;
                foreach (var (chunk, chunkSize, doc) in chunkedContext)
                {
                    if (chunkSize >= minChunkSize)
                    {
                        if (addEmbeddings)
                        {
                            for (int i = 0; i < RETRY_COUNT; i++) // Assuming RETRY_COUNT is a constant
                            {
                                try
                                {
                                    doc.ContentVector = DataUtils.GetEmbedding(chunk, azureCredential, embeddingEndpoint);
                                    break;
                                }
                                catch
                                {
                                    Thread.Sleep(30000);
                                }
                            }
                            if (doc.ContentVector == null)
                            {
                                throw new Exception($"Error getting embedding for chunk={chunk}");
                            }
                        }

                        chunks.Add(
                            new Document
                            {
                                Content = chunk,
                                Title = doc.Title,
                                Url = url,
                                ContentVector = doc.ContentVector
                            }
                        );
                    }
                    else
                    {
                        skippedChunks++;
                    }
                }

                return new ChunkingResult
                {
                    Chunks = chunks,
                    TotalFiles = 1,
                    SkippedChunks = skippedChunks
                };
            }
            catch (UnsupportedFormatError e) // Assuming UnsupportedFormatError is an exception class
            {
                if (ignoreErrors)
                {
                    return new ChunkingResult
                    {
                        Chunks = new List<Document>(),
                        TotalFiles = 1,
                        NumUnsupportedFormatFiles = 1
                    };
                }
                else
                {
                    throw e;
                }
            }
            catch (Exception e)
            {
                if (ignoreErrors)
                {
                    return new ChunkingResult
                    {
                        Chunks = new List<Document>(),
                        TotalFiles = 1,
                        NumFilesWithErrors = 1
                    };
                }
                else
                {
                    throw e;
                }
            }
        }

        public IEnumerable<(string, int, Document)> ChunkContentHelper(
            string content, string fileFormat, string fileName = null,
            int tokenOverlap = 0,
            int numTokens = 256
        )
        {
            if (numTokens == 0)
            {
                numTokens = 1000000000;
            }

            var parser = ParserFactory(fileFormat.Split("_pdf")[0]); // Assuming ParserFactory is a method that takes a string and returns a Parser
            var doc = parser.Parse(content, fileName); // Assuming Parse is a method that takes a string and an optional string and returns a Document
                                                       // if the original doc after parsing is < numTokens return as it is
            var docContentSize = DataUtils.TOKEN_ESTIMATOR.EstimateTokens(doc.Content); // Assuming TOKEN_ESTIMATOR is an object with a method EstimateTokens that takes a string and returns an int
            if (docContentSize < numTokens)
            {
                yield return (doc.Content, docContentSize, doc);
            }
            else
            {
                if (fileFormat == "markdown")
                {
                    //    var splitter = MarkdownTextSplitter.FromTiktokenEncoder(
                    //        numTokens, tokenOverlap); // Assuming FromTiktokenEncoder is a method that takes two ints and returns a MarkdownTextSplitter
                    //    var chunkedContentList = splitter.SplitText(
                    //        content);  // chunk the original content
                    //    foreach (var (chunkedContent, chunkSize) in MergeChunksSerially(chunkedContentList, numTokens)) // Assuming MergeChunksSerially is a method that takes a list of strings and an int and returns an IEnumerable of tuples
                    //    {
                    //        var chunkDoc = parser.Parse(chunkedContent, fileName);
                    //        chunkDoc.Title = doc.Title;
                    //        yield return (chunkDoc.Content, chunkSize, chunkDoc);
                    //    }
                }
                else
                {
                    if (fileFormat == "python")
                    {
                        //splitter = PythonCodeTextSplitter.FromTiktokenEncoder(
                        //    numTokens, tokenOverlap); // Assuming FromTiktokenEncoder is a method that takes two ints and returns a PythonCodeTextSplitter
                    }
                    else
                    {
                        if (fileFormat == "html_pdf") // cracked pdf converted to html
                        {
                            var splitter = new PdfTextSplitter(SENTENCE_ENDINGS + WORDS_BREAKS, numTokens, tokenOverlap); // Assuming SENTENCE_ENDINGS and WORDS_BREAKS are strings
                        }
                        else
                        {
                            var splitter = RecursiveCharacterTextSplitter.FromTiktokenEncoder(
                                    SENTENCE_ENDINGS + WORDS_BREAKS,
                                    numTokens, tokenOverlap); // Assuming FromTiktokenEncoder is a method that takes a string and two ints and returns a RecursiveCharacterTextSplitter
                        }
                    }
                    var chunkedContentList = splitter.SplitText(doc.Content);
                    foreach (var chunkedContent in chunkedContentList)
                    {
                        var chunkSize = TOKEN_ESTIMATOR.EstimateTokens(chunkedContent);
                        yield return (chunkedContent, chunkSize, doc);
                    }
                }
            }
        }

        public IEnumerable<(string, int)> MergeChunksSerially(List<string> chunkedContentList, int numTokens)
        {
            string currentChunk = "";
            int totalSize = 0;
            foreach (var chunkedContent in chunkedContentList)
            {
                int chunkSize = TOKEN_ESTIMATOR.EstimateTokens(chunkedContent);
                if (totalSize > 0)
                {
                    int newSize = totalSize + chunkSize;
                    if (newSize > numTokens)
                    {
                        yield return (currentChunk, totalSize);
                        currentChunk = "";
                        totalSize = 0;
                    }
                }
                totalSize += chunkSize;
                currentChunk += chunkedContent;
            }
            if (totalSize > 0)
            {
                yield return (currentChunk, totalSize);
            }
        }

        public Tuple<ChunkingResult, bool> ProcessFile(
            string filePath,
            string directoryPath,
            object azureCredential,
            List<string> extensionsToProcess,
            SingletonFormRecognizerClient formRecognizerClient,
            bool ignoreErrors = true,
            int numTokens = 1024,
            int minChunkSize = 10,
            string urlPrefix = "",
            int tokenOverlap = 0,
            bool useLayout = false,
            bool addEmbeddings = false,
            string embeddingEndpoint = ""
            )
        {
            if (formRecognizerClient == null)
            {
                formRecognizerClient = new SingletonFormRecognizerClient();
            }

            bool isError = false;
            ChunkingResult result;

            try
            {
                var urlPath = string.Empty;
                string relFilePath = Path.GetRelativePath(directoryPath, filePath);
                if (!string.IsNullOrEmpty(urlPrefix))
                {
                    urlPath = urlPrefix + relFilePath;
                    urlPath = ConvertEscapedToPosix(urlPath);
                }

                result = ChunkFile(
                    filePath,
                    ignoreErrors,
                    numTokens,
                    minChunkSize,
                    urlPath,
                    tokenOverlap,
                    extensionsToProcess,
                    formRecognizerClient,
                    useLayout,
                    addEmbeddings,
                    azureCredential,
                    embeddingEndpoint
                );

                foreach (var chunk in result.Chunks)
                {
                    chunk.FilePath = relFilePath;
                    chunk.Metadata = JsonConvert.SerializeObject(new { chunk_id = chunk.Id });
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                if (!ignoreErrors)
                {
                    throw;
                }
                Console.WriteLine($"File ({filePath}) failed with ", e);
                isError = true;
            }

            return Tuple.Create(result, isError);
        }


        public static string ConvertEscapedToPosix(string escapedPath)
        {
            var windowsPath = escapedPath.Replace(@"\\", @"\");
            var posixPath = windowsPath.Replace(@"\", "/");
            return posixPath;
        }

        public string GetFileFormat(string fileName, IEnumerable<string> extensionsToProcess)
        {
            // In case the caller gives us a file path
            fileName = Path.GetFileName(fileName);
            var fileExtension = Path.GetExtension(fileName).TrimStart('.');

            if (!extensionsToProcess.Contains(fileExtension))
            {
                return string.Empty;
            }

            var fileFormat = DataUtils.FILE_FORMAT_DICT[fileExtension];
            return fileFormat;
        }

        public static IDictionary<string, object> ToDictionary(object obj)
        {
            return obj.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(obj));
        }

        static void DetectEncoding()
        {
            byte[] byteArray = new byte[] { /* バイト配列のデータ */ };

            if (IsEncoding(byteArray, "UTF-8"))
            {
                Console.WriteLine("This byte array is UTF-8 encoded.");
            }
            else if (IsEncoding(byteArray, "ASCII"))
            {
                Console.WriteLine("This byte array is ASCII encoded.");
            }
            else if (IsEncoding(byteArray, "Unicode"))
            {
                Console.WriteLine("This byte array is Unicode (UTF-16) encoded.");
            }
            else if (IsEncoding(byteArray, "Shift_JIS"))
            {
                Console.WriteLine("This byte array is Shift_JIS encoded.");
            }
            else
            {
                Console.WriteLine("This byte array's encoding could not be determined.");
            }
        }

        static bool IsEncoding(byte[] data, string encodingName)
        {
            var encoding = Encoding.GetEncoding(encodingName, new EncoderExceptionFallback(), new DecoderExceptionFallback());
            try
            {
                encoding.GetString(data);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }
    }

}

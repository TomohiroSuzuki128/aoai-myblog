// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.Configuration;
using AzureAISearchIndexInitializer;

var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? string.Empty;
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{env}.json", true)
    .Build();

string indexName = configuration["indexName"] ?? throw new InvalidOperationException("indexName is not set.");
string serviceName = configuration["serviceName"] ?? throw new InvalidOperationException("serviceName is not set.");
string apiKey = configuration["apiKey"] ?? throw new InvalidOperationException("apiKey is not set.");

IndexInitializer.Initialize(indexName, serviceName, apiKey).Wait();

Console.WriteLine("Initializing index is completed.");
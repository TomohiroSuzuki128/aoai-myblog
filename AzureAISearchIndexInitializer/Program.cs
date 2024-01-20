// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.Configuration;
using AzureAISearchIndexInitializer;


var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? string.Empty;
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{env}.json", true)
    .Build();

string indexName = configuration["indexName"] ?? "";
string serviceName = configuration["serviceName"] ?? "";
string apiKey = configuration["apiKey"] ?? "";

IndexInitializer.Initialize(indexName, serviceName, apiKey).Wait();
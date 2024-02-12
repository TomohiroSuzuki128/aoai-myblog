// See https://aka.ms/new-console-template for more information
using IndexCreator;
using Microsoft.SemanticKernel.Text;

Console.WriteLine("処理開始");

var maxTokensPerLine = 30;
var maxTokensPerParagraphs = 100;
var overlapTokens = 20;

var tokenEstimator = new TokenEstimator();

var text = string.Empty;

foreach (var x in Enumerable.Range(0, 10))
{
    text += $"{x + 1}行目。";
    foreach (var y in Enumerable.Range(0, 10))
    {
        text += "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをん" + Environment.NewLine;
    }
    text += Environment.NewLine;
}

#pragma warning disable SKEXP0055
var lines = TextChunker.SplitPlainTextLines(text, maxTokensPerLine, tokenEstimator.EstimateTokens);

List<string> newLines = new();
foreach (var line in lines)
{
    newLines.Add(line + "*");
}

//var chunks = TextChunker.SplitPlainTextParagraphs(newLines, maxTokensPerParagraphs, overlapTokens, "", tokenEstimator.EstimateTokens);

var chunks = TextChunker.SplitPlainTextParagraphs(new List<string> { text }, maxTokensPerParagraphs, overlapTokens, "", tokenEstimator.EstimateTokens);

#pragma warning restore SKEXP0055

foreach (var chunk in chunks)
{
    Console.WriteLine(chunk); 
    Console.WriteLine("chunk");
}


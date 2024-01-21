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
using Microsoft.DeepDev;
using static IndexCreator.DataUtils;
using Microsoft.SemanticKernel.Text;

namespace IndexCreator
{
#pragma warning disable SKEXP0055
    public class BobyContentChunker
    {
        readonly int maxTokensPerLine;
        readonly int overlapTokens;
        readonly TokenEstimator tokenEstimator;

        public BobyContentChunker(int maxTokensPerLine = 1024, int overlapTokens = 128)
        {
            this.maxTokensPerLine = maxTokensPerLine;
            this.overlapTokens = overlapTokens;
            tokenEstimator = new TokenEstimator();
        }

        public List<string> ChunkText(string text)
        {
            var lines = TextChunker.SplitPlainTextLines(text, maxTokensPerLine, tokenEstimator.EstimateTokens);
            var chunks = TextChunker.SplitPlainTextParagraphs(lines, maxTokensPerLine, overlapTokens, "", tokenEstimator.EstimateTokens);
            return chunks;
        }

    }
#pragma warning restore SKEXP0055 
}

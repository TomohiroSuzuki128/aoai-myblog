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

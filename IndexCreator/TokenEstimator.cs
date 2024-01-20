using Microsoft.DeepDev;

namespace IndexCreator
{
    public class TokenEstimator
    {
        ITokenizer tokenizer;

        public TokenEstimator(string modelToEncoding = "text-embedding-ada-002")
        {
            BuildTokenizer(modelToEncoding).Wait();
            if (tokenizer == null)
                throw new Exception("Tokenizer not built");
        }

        private async Task BuildTokenizer(string modelToEncoding)
        {
            tokenizer = await TokenizerBuilder.CreateByModelNameAsync(modelToEncoding);
        }

        public int EstimateTokens(string text)
        {
            return tokenizer.Encode(text).Count;
        }

        public string ConstructTokensWithSize(string tokens, int numberOfTokens)
        {
            var encodedTokens = tokenizer.Encode(tokens, Array.Empty<string>());
            var newTokens = tokenizer.Decode(encodedTokens.Take(numberOfTokens).ToArray());
            return newTokens;
        }
    }
}

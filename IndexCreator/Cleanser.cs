using AngleSharp;
using AngleSharp.Dom;
using System.Text.RegularExpressions;

namespace IndexCreator
{
    public abstract class CleanserBase
    {
        public abstract (string, string) Cleanse(string content, string fileName = "");

        public static string CleanupContent(string content)
        {
            var output = content;
            //output = Regex.Replace(output, @"\n{2,}", "\n", RegexOptions.Multiline);
            output = Regex.Replace(output, @"[^\S\n]{2,}", " ", RegexOptions.Multiline);
            output = Regex.Replace(output, @"-{2,}", "--", RegexOptions.Multiline);
            output = Regex.Replace(output, @"^\s$\n", "\n", RegexOptions.Multiline);
            output = Regex.Replace(output, @"\n{2,}", "\n", RegexOptions.Multiline);

            return output.Trim();
        }

        string GetFirstLineWithProperty(string content, string property = "title: ")
        {
            foreach (string line in content.Split(Environment.NewLine))
            {
                if (line.StartsWith(property))
                    return line.Substring(property.Length).Trim();
            }
            return string.Empty;
        }

        string GetFirstAlphanumLine(string content)
        {
            foreach (string line in content.Split(Environment.NewLine))
            {
                if (line.Any(char.IsLetterOrDigit))
                    return line.Trim();
            }
            return string.Empty;
        }
    }

    public class TextCleanser : CleanserBase
    {
        public TextCleanser() : base()
        {
        }

        string GetFirstAlphanumLine(string content)
        {
            foreach (string line in content.Split(Environment.NewLine))
            {
                if (line.Any(c => Char.IsLetterOrDigit(c)))
                    return line.Trim();
            }
            return string.Empty;
        }

        private string GetFirstLineWithProperty(string content, string property = "title: ")
        {
            foreach (string line in content.Split(Environment.NewLine))
            {
                if (line.StartsWith(property))
                    return line.Substring(property.Length).Trim();
            }
            return string.Empty;
        }

        public override (string, string) Cleanse(string content, string fileName = "")
        {
            string title = GetFirstLineWithProperty(content) ?? GetFirstAlphanumLine(content);
            return (CleanupContent(content), title ?? fileName);
        }
    }

    public class HtmlCleanser : CleanserBase
    {
        private const int TITLE_MAX_TOKENS = 128;
        private const string NEWLINE_TEMPL = "<NEWLINE_TEXT>";
        private TokenEstimator tokenEstimator;

        public HtmlCleanser() : base()
        {
            tokenEstimator = new TokenEstimator();
        }

        public override (string, string) Cleanse(string content, string fileName = "")
        {
            var title = ExtractTitle(content, fileName);
            return (CleanupContent(content), title);
        }

        string ExtractTitle(string content, string fileName)
        {
            var context = BrowsingContext.New(Configuration.Default);
            var document = context.OpenAsync(req => req.Content(content)).Result;

            var titleElement = document.QuerySelector("title");
            if (titleElement != null)
            {
                return titleElement.TextContent;
            }

            var h1Element = document.QuerySelector("h1");
            if (h1Element != null)
            {
                return h1Element.TextContent;
            }

            var h2Element = document.QuerySelector("h2");
            if (h2Element != null)
            {
                return h2Element.TextContent;
            }

            //if title is still not found, guess using the next string
            var title = GetNextStrippedString(document);
            title = tokenEstimator.ConstructTokensWithSize(title, TITLE_MAX_TOKENS);

            if (string.IsNullOrEmpty(title))
                title = fileName;

            return title;
        }

        public string GetNextStrippedString(IDocument document)
        {
            var allTextNodes = document.All
                .Where(n => n.NodeType == NodeType.Text && n.TextContent.Trim() != "")
                .Select(n => n.TextContent.Trim());
            // Get the first non-empty string
            var nextStrippedString = allTextNodes.FirstOrDefault() ?? "";
            return nextStrippedString;
        }
    }

}
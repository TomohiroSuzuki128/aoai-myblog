using Azure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using static IndexCreator.DataUtils;
using HtmlAgilityPack;
using System.Net.Http;
using AngleSharp.Dom;
using AngleSharp;

namespace IndexCreator
{
    public abstract class ParserBase
    {
        public abstract Document Parse(string content, string fileName = null);

        public Document ParseFile(string filePath)
        {
            using (StreamReader reader = new StreamReader(filePath))
            {
                return Parse(reader.ReadToEnd(), Path.GetFileName(filePath));
            }
        }

        public List<Document> ParseDirectory(string directoryPath)
        {
            List<Document> documents = new List<Document>();
            foreach (string fileName in Directory.EnumerateFiles(directoryPath))
            {
                documents.Add(ParseFile(fileName));
            }
            return documents;
        }
    }

    public class Document(
        string content = "", string id = "", string title = "", string filepath = "", string url = "")
    {
        public string Content { get; set; } = content;
        public string Id { get; set; } = id;
        public string Title { get; set; } = title;
        public string Filepath { get; set; } = filepath;
        public string Url { get; set; } = url;
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
        public List<float> ContentVector { get; set; } = new List<float>();

        //Cleans up the given content using regexes
        //Args:
        //    content(str) : The content to clean up.
        //Returns:
        //    str: The cleaned up content.
        public static string CleanupContent(string content)
        {
            string output = Regex.Replace(content, @"\n{2,}", "\n");
            output = Regex.Replace(output, @"[^\S\n]{2,}", " ");
            output = Regex.Replace(output, @"-{2,}", "--");

            return output.Trim();
        }

        private string GetFirstLineWithProperty(string content, string property = "title: ")
        {
            foreach (string line in content.Split(Environment.NewLine))
            {
                if (line.StartsWith(property))
                {
                    return line.Substring(property.Length).Trim();
                }
            }
            return string.Empty;
        }

        private string GetFirstAlphanumLine(string content)
        {
            foreach (string line in content.Split(Environment.NewLine))
            {
                if (line.Any(c => Char.IsLetterOrDigit(c)))
                {
                    return line.Trim();
                }
            }
            return string.Empty;
        }


        public class TextParser : ParserBase
        {
            public TextParser() : base()
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

            string GetFirstLineWithProperty(string content, string property = "title: ")
            {
                foreach (string line in content.Split(Environment.NewLine))
                {
                    if (line.StartsWith(property))
                        return line.Substring(property.Length).Trim();
                }
                return string.Empty;
            }

            public override Document Parse(string content, string fileName = "")
            {
                string title = GetFirstLineWithProperty(content) ?? GetFirstAlphanumLine(content);
                return new Document(content: CleanupContent(content), title: title ?? fileName);
            }
        }


        public class HtmlParser : ParserBase
        {
            private const int TITLE_MAX_TOKENS = 128;
            private const string NEWLINE_TEMPL = "<NEWLINE_TEXT>";
            private TokenEstimator tokenEstimator;

            public HtmlParser() : base()
            {
                tokenEstimator = new TokenEstimator();
            }

            public override Document Parse(string content, string fileName = "")
            {
                var title = ExtractTitle(content, fileName);
                return new Document(content: CleanupContent(content), title: title);
            }

            private string ExtractTitle(string content, string fileName)
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
}
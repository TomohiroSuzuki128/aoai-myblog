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

namespace IndexCreator
{
    public abstract class BaseParser
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
            return null;
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
            return null;
        }


        public class TextParser : BaseParser
        {
            public TextParser() : base()
            {
            }

            private string GetFirstAlphanumLine(string content)
            {
                foreach (string line in content.Split(Environment.NewLine))
                {
                    if (line.Any(c => Char.IsLetterOrDigit(c)))
                        return line.Trim();
                }
                return null;
            }

            private string GetFirstLineWithProperty(string content, string property = "title: ")
            {
                foreach (string line in content.Split(Environment.NewLine))
                {
                    if (line.StartsWith(property))
                        return line.Substring(property.Length).Trim();
                }
                return null;
            }

            public override Document Parse(string content, string fileName = "")
            {
                string title = GetFirstLineWithProperty(content) ?? GetFirstAlphanumLine(content);
                return new Document(content: CleanupContent(content), title: title ?? fileName);
            }
        }


        public class HTMLParser : BaseParser
        {
            private const int TITLE_MAX_TOKENS = 128;
            private const string NEWLINE_TEMPL = "<NEWLINE_TEXT>";
            private TokenEstimator tokenEstimator;

            public HTMLParser() : base()
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
            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(content);

            var titleNode = htmlDocument.DocumentNode.SelectSingleNode("//title");
            if (titleNode != null)
            {
                return titleNode.InnerText;
            }

            var h1Node = htmlDocument.DocumentNode.SelectSingleNode("//h1");
                if (h1Node != null)
                {
                    return h1Node.InnerText;
                }

                var h2Node = htmlDocument.DocumentNode.SelectSingleNode("//h2");
                if (h2Node != null)
                {
                    return h2Node.InnerText;
                }

                //if title is still not found, guess using the next string
                var title = GetNextStrippedString(htmlDocument);
                title = tokenEstimator.ConstructTokensWithSize(title, TITLE_MAX_TOKENS);

                if (string.IsNullOrEmpty(title))
                    title = fileName;

                return title;
            }

            public string GetNextStrippedString(HtmlDocument htmlDocument)
            {
                var allTextNodes = htmlDocument.DocumentNode.DescendantsAndSelf()
                    .Where(n => n.NodeType == HtmlNodeType.Text && n.InnerText.Trim() != "")
                    .Select(n => n.InnerText.Trim());
                // Get the first non-empty string
                var nextStrippedString = allTextNodes.FirstOrDefault() ?? "";
                return nextStrippedString;
            }
        }
    }
}
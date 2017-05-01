﻿namespace JsonToQueryable
{
    public class Source : ISource
    {
        public Source(string body, string name = "Query")
        {
            this.Name = name;
            this.Body = MonetizeLineBreaks(body);
        }

        public string Body { get; set; }
        public string Name { get; set; }

        private static string MonetizeLineBreaks(string input)
        {
            return (input ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");
        }
    }
}
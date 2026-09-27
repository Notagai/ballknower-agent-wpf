
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Xml;

namespace Ballknower.Tools;

public class ToolRequestParser
{
    public bool TryParse(
        string input,
        out ToolRequest? request,
        out string error)
    {
        request = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Tool request is empty.";
            return false;
        }

        var cleaned = input.Trim();

        // Accept JSON wrapped in Markdown code fences.
        if (cleaned.StartsWith("```"))
        {
            var firstNewLine = cleaned.IndexOf('\n');
            var lastFence = cleaned.LastIndexOf("```");

            if (firstNewLine >= 0 &&
                lastFence > firstNewLine)
            {
                cleaned = cleaned.Substring(
                    firstNewLine + 1,
                    lastFence - firstNewLine - 1).Trim();
            }
        }

        if (cleaned.StartsWith("{"))
        {
            return TryParseJson(
                cleaned,
                out request,
                out error);
        }

        if (cleaned.StartsWith(
                "<tool_call>",
                StringComparison.OrdinalIgnoreCase))
        {
            return TryParseXml(
                cleaned,
                out request,
                out error);
        }

        error = "Response is not a recognized tool request.";
        return false;
    }

    private bool TryParseJson(
        string json,
        out ToolRequest? request,
        out string error)
    {
        request = null;
        error = string.Empty;

        try
        {
            request = JsonSerializer.Deserialize<ToolRequest>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (request is null ||
                string.IsNullOrWhiteSpace(request.Tool))
            {
                error = "Tool request is missing a tool name.";
                return false;
            }

            request.Arguments ??= new();

            return true;
        }
        catch (JsonException)
        {
            error = "Tool request contains invalid JSON.";
            return false;
        }
    }

    private bool TryParseXml(
        string xml,
        out ToolRequest? request,
        out string error)
    {
        request = null;
        error = string.Empty;

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            var document = new XmlDocument
            {
                XmlResolver = null
            };

            using var stringReader = new StringReader(xml);
            using var xmlReader =
                XmlReader.Create(stringReader, settings);

            document.Load(xmlReader);

            var root = document.DocumentElement;

            if (root is null ||
                !string.Equals(root.Name, "tool_call", StringComparison.OrdinalIgnoreCase))
            {
                error = "Missing tool_call element.";
                return false;
            }

            // The tool name is the text immediately
            // inside <tool_call>, before the arguments.
            var toolName = string.Empty;

            foreach (XmlNode child in root.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Text ||
                    child.NodeType == XmlNodeType.CDATA)
                {
                    toolName = child.Value?.Trim()
                        ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(toolName))
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(toolName))
            {
                error = "Tool request is missing a tool name.";
                return false;
            }

            var arguments =
                new Dictionary<string, string>();

            // Each arg_key must be followed by
            // its corresponding arg_value.
            for (var i = 0; i < root.ChildNodes.Count; i++)
            {
                var node = root.ChildNodes[i];

                if (node.Name != "arg_key")
                    continue;

                var key = node.InnerText?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(key))
                {
                    error = "Tool argument has an empty name.";
                    return false;
                }

                XmlNode? valueNode = null;

                for (var j = i + 1;
                     j < root.ChildNodes.Count;
                     j++)
                {
                    var candidate = root.ChildNodes[j];

                    if (candidate.NodeType == XmlNodeType.Whitespace ||
                        candidate.NodeType == XmlNodeType.SignificantWhitespace)
                    {
                        continue;
                    }

                    valueNode = candidate;
                    break;
                }

                if (valueNode is null ||
                    !string.Equals(
                    valueNode.Name,
                    "arg_value",
                    StringComparison.Ordinal))
                {
                    error =
                        $"Missing value for argument '{key}'.";
                    return false;
                }

                arguments[key] = valueNode.InnerText;
            }

            // Handle the model's earlier
            // "contents" spelling as well.
            if (arguments.TryGetValue(
                    "contents",
                    out var contents) &&
                !arguments.ContainsKey("content"))
            {
                arguments["content"] = contents;
                arguments.Remove("contents");
            }

            request = new ToolRequest
            {
                Tool = toolName,
                Arguments = arguments
            };

            return true;
        }
        catch (XmlException)
        {
            error = "Tool request contains invalid XML.";
            return false;
        }
    }
}
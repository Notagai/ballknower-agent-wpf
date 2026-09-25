using System;

namespace Ballknower.Commands;

public class CommandParser
{
    public bool IsCommand(string input)
    {
        return input.TrimStart().StartsWith("/");
    }

    public (string Command, string Arguments) Parse(string input)
    {
        var trimmed = input.Trim();

        if (!trimmed.StartsWith("/"))
            return (string.Empty, string.Empty);

        var withoutSlash = trimmed[1..];

        var parts = withoutSlash.Split(
            ' ',
            2,
            StringSplitOptions.RemoveEmptyEntries);

        var command = parts.Length > 0
            ? parts[0].ToLowerInvariant()
            : string.Empty;

        var arguments = parts.Length > 1
            ? parts[1]
            : string.Empty;

        return (command, arguments);
    }
}
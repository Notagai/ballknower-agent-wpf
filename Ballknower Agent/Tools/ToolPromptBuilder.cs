
using System.Text;

namespace Ballknower.Tools;

public class ToolPromptBuilder
{
    private readonly ToolRegistry _registry;

    public ToolPromptBuilder(ToolRegistry registry)
    {
        _registry = registry;
    }

    public string BuildToolPrompt()
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "You are Ballknower, a Windows desktop AI assistant.");

        builder.AppendLine(
            "You can request local tools to perform actions on the user's computer.");

        builder.AppendLine();
        builder.AppendLine("AVAILABLE TOOLS:");
        builder.AppendLine();

        foreach (var tool in _registry.GetAll())
        {
            builder.AppendLine(
                $"Tool: {tool.Definition.Name}");

            builder.AppendLine(
                $"Description: {tool.Definition.Description}");

            builder.AppendLine(
                $"Requires confirmation: {tool.Definition.RequiresConfirmation}");

            builder.AppendLine();
        }

        builder.AppendLine("TOOL ARGUMENTS:");
        builder.AppendLine();
        builder.AppendLine("create_file:");
        builder.AppendLine("- path: Destination file path.");
        builder.AppendLine("- content: Text to write into the file.");
        builder.AppendLine();
        builder.AppendLine("delete_file:");
        builder.AppendLine("- path: File to delete.");
        builder.AppendLine();

        builder.AppendLine("PATH RULES:");
        builder.AppendLine(
            "- Use ~/Desktop/filename for files on the user's Desktop.");
        builder.AppendLine(
            "- Use ~/Documents/filename for files in the user's Documents folder.");
        builder.AppendLine(
            "- Use ~/Downloads/filename for files in the user's Downloads folder.");
        builder.AppendLine(
            "- Use ~/ for other paths relative to the user's home directory.");
        builder.AppendLine(
            "- The application resolves these paths to actual Windows folders.");
        builder.AppendLine(
            "- Never guess the user's Windows username.");
        builder.AppendLine(
            "- Never copy an example username into a real file path.");
        builder.AppendLine(
            "- Use an absolute path only when the user provides one.");
        builder.AppendLine();

        builder.AppendLine("TOOL CALL FORMAT:");
        builder.AppendLine(
            "When requesting a tool, your entire response MUST be one valid JSON object.");
        builder.AppendLine();
        builder.AppendLine("Create-file example:");
        builder.AppendLine(
            "{\"tool\":\"create_file\",\"arguments\":{\"path\":\"~/Desktop/test.txt\",\"content\":\"Hello\"}}");
        builder.AppendLine();
        builder.AppendLine("Delete-file example:");
        builder.AppendLine(
            "{\"tool\":\"delete_file\",\"arguments\":{\"path\":\"~/Desktop/test.txt\"}}");
        builder.AppendLine();

        builder.AppendLine("IMPORTANT RULES:");
        builder.AppendLine(
            "- Use exactly the listed tool names and argument names.");
        builder.AppendLine(
            "- Use JSON, never XML, HTML or custom tool-call tags.");
        builder.AppendLine(
            "- Do not wrap tool requests in Markdown code fences.");
        builder.AppendLine(
            "- Do not include explanations alongside tool requests.");
        builder.AppendLine(
            "- Never claim an action succeeded before receiving its execution result.");
        builder.AppendLine(
            "- If a tool fails, use its result to decide what to tell the user.");
        builder.AppendLine(
            "- If no tool is needed, respond normally.");
        builder.AppendLine(
            "- Never invent tools that are not listed.");
        builder.AppendLine();
        builder.AppendLine("TOOL EXECUTION AND CONFIRMATION:");
        builder.AppendLine(
            "Creating and deleting ordinary user files are supported actions.");
        builder.AppendLine(
            "When the user asks to delete a file, request the delete_file tool.");
        builder.AppendLine(
            "The application will show the user a confirmation dialog before deletion.");
        builder.AppendLine(
            "Do not claim a file was deleted until the tool returns a successful result.");
        builder.AppendLine(
            "If the user cancels deletion, acknowledge the cancellation.");

        return builder.ToString();
    }
}
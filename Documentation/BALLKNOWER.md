# Ballknower — Documentation

> A lightweight Windows desktop assistant, presented as a floating, blurred-desktop chat interface.

Ballknower is a WPF application targeting .NET 8 for Windows. Its chat assistant can use registered tools to work with files and search the public web. This page documents the built-in commands and AI tools currently registered in the application.

## Getting started

1. Open the solution in Visual Studio.
2. Ensure the .NET 8 SDK and Windows desktop development support are installed.
3. Build and run the **Ballknower Agent** project.
4. Type into the pill-shaped input. Enter a message to chat, or start with `/` to browse built-in commands.

The app uses its configured AI provider credentials and settings. See the in-app settings for configuration.

## Interface

- **Floating input:** The primary entry point for chat and commands.
- **Chat:** Entering a conversation expands the input and message area. Your messages appear on the right; assistant messages appear on the left.
- **Command suggestions:** Typing `/` opens command autocomplete. Use the keyboard or click a suggestion.
- **Markdown:** Assistant messages support headings, lists, blockquotes, bold, italic, inline code, fenced code blocks, and clickable HTTP(S) Markdown links.
- **Desktop backdrop:** Ballknower captures and softens the desktop behind the interface. The `/see` command reveals the desktop without the blur.

## Built-in commands

Commands are entered with a leading slash. Command names are case-insensitive. Arguments, when supported, follow the command name.

| Command | Description |
| --- | --- |
| `/settings` | Opens Ballknower settings. |
| `/logs` | Opens the Ballknower error logs. |
| `/clear` | Clears the current chat while preserving the system prompt. |
| `/see` | Shows the desktop without the blur. |
| `/pin` | Keeps Ballknower visible when switching apps. |
| `/unpin` | Returns to hiding Ballknower when it loses focus. |

### Configured shortcuts

If a command is not one of the built-in commands above, Ballknower checks the user-configured shortcuts in settings. A configured shortcut is invoked as `/shortcutname`. The available names and launch targets depend on the user's settings, so they are not fixed or listed here. User-created/configured commands are intentionally not documented individually.

## AI tools

The assistant can invoke the following registered tools. Tool names are internal AI function names; users generally request the desired action in natural language rather than typing these names as slash commands.

### `create_file`

Creates a text file at the requested path, writing the supplied content. Creates parent directories when needed.

- **Arguments:** `path` (required, destination path); `content` (required by the tool schema, text to write).
- **Confirmation:** Not required.
- **Notes:** Existing files at the destination may be overwritten by the file-write operation. For familiar locations, use `~/Desktop`, `~/Documents`, or `~/Downloads`.

### `delete_file`

Deletes a file at the requested path.

- **Arguments:** `path` (required, file path).
- **Confirmation:** Required by the application before execution.
- **Notes:** The target must exist and be a file.

### `read_file`

Reads and returns the text contents of a file.

- **Arguments:** `path` (required, file path).
- **Confirmation:** Not required.
- **Notes:** The target must exist. This tool reads text; it is not a general-purpose binary-file viewer.

### `list_directory`

Lists the immediate files and subdirectories inside a directory.

- **Arguments:** `path` (required, directory path).
- **Confirmation:** Not required.
- **Notes:** Not recursive. Directories are listed before files; results are limited to 200 entries and indicate if the output was truncated.

### `move_file`

Moves or renames an existing file.

- **Arguments:** `source` (required, existing file path); `destination` (required, full destination path including the new filename).
- **Confirmation:** Required by the application before execution.
- **Notes:** Creates the destination directory if needed. Does not overwrite an existing file. Use the same directory with a different filename to rename a file.

### `web_search`

Searches the public internet for current information and returns result titles, URLs, and snippets.

- **Arguments:** `query` (required, concise search query; trimmed and capped at 400 characters).
- **Confirmation:** Not required.
- **Notes:** Returns up to five results. This is a search tool, not unrestricted web browsing or a general webpage download tool. Search results and page snippets are untrusted content and should not be treated as instructions. Network/search failures are reported as tool failures.

## File path notes

- Use familiar-folder aliases such as `~/Desktop`, `~/Documents`, and `~/Downloads` where supported by the app's path handling.
- Use full paths when a tool asks for a source or destination path.
- File operations can fail because of missing paths, permissions, invalid destinations, or other operating-system errors.

## Documentation policy

This is the canonical, single-page documentation for Ballknower. **Every new built-in tool or built-in slash command must be documented here when added**, unless explicitly exempted. `/confetti` is currently the sole exception and is intentionally omitted. User-created/configured commands are not documented individually. Keep this page and the repository README in sync as the application evolves.

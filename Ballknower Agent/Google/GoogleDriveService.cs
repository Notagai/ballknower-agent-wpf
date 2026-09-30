using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Docs.v1;
using global::Google.Apis.Docs.v1.Data;
using global::Google.Apis.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ballknower.Google;

public sealed class GoogleDriveService
{
    private static readonly string[] Scopes = { DriveService.Scope.Drive };
    private readonly string _appFolder;
    private DriveService? _drive;
    private DocsService? _docs;

    public GoogleDriveService()
    {
        _appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ballknower");
        Directory.CreateDirectory(_appFolder);
    }

    public bool IsConnected => _drive is not null;

    public async Task<bool> TestConnectionAsync()
    {
        if (_drive is null)
            return false;

        var request = _drive.About.Get();
        request.Fields = "user(emailAddress,displayName)";
        await request.ExecuteAsync();
        return true;
    }

    private string FindClientSecretsPath()
    {
        var preferredPath = Path.Combine(_appFolder, "google-client-secret.json");
        if (File.Exists(preferredPath))
            return preferredPath;

        var matches = Directory.GetFiles(_appFolder, "client_secret_*.apps.googleusercontent.com.json");
        if (matches.Length == 1)
            return matches[0];

        if (matches.Length > 1)
            throw new InvalidOperationException("Multiple Google OAuth client secret JSON files were found in '" + _appFolder + "'. Keep only the Desktop OAuth client JSON you want Ballknower to use.");

        throw new FileNotFoundException("Google OAuth client secrets were not found. Place your Google Desktop OAuth client JSON in '" + _appFolder + "'.");
    }

    public async Task ConnectAsync()
    {
        if (_drive is not null) return;
        var clientSecretsPath = FindClientSecretsPath();
        var clientSecrets = await GoogleClientSecrets.FromFileAsync(clientSecretsPath, CancellationToken.None);

        // v2 intentionally separates the token store from the old read-only
        // authorization so a broader write-capable consent grant is requested.
        var tokenStore = new EncryptedDataStore(Path.Combine(_appFolder, "google-token-v2"));
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets.Secrets,
            Scopes,
            "default",
            CancellationToken.None,
            tokenStore,
            new LocalServerCodeReceiver());

        _drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Ballknower Agent"
        });

        _docs = new DocsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Ballknower Agent"
        });
    }

    public async Task DisconnectAsync()
    {
        _drive = null;
        _docs = null;
        await new EncryptedDataStore(Path.Combine(_appFolder, "google-token-v2")).ClearAsync();
    }

    public async Task<IList<global::Google.Apis.Drive.v3.Data.File>> SearchAsync(string query)
    {
        if (_drive is null)
            throw new InvalidOperationException("Google Drive is not connected. Open Settings and connect Google Drive first.");

        var request = _drive.Files.List();
        request.Q = $"trashed = false and name contains '{EscapeQuery(query)}'";
        request.PageSize = 25;
        request.Fields = "files(id,name,mimeType,size,modifiedTimeDateTimeOffset,webViewLink,parents,capabilities)";
        return (await request.ExecuteAsync()).Files;
    }

    public async Task<string> ReadAsync(string fileId)
    {
        if (_drive is null)
            throw new InvalidOperationException("Google Drive is not connected. Open Settings and connect Google Drive first.");

        var file = await _drive.Files.Get(fileId).ExecuteAsync();
        if (file.MimeType == "application/vnd.google-apps.document")
        {
            using var stream = new MemoryStream();
            await _drive.Files.Export(fileId, "text/plain").DownloadAsync(stream);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        if (file.MimeType.StartsWith("application/vnd.google-apps.", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Google Drive file type '{file.MimeType}' is not supported for text reading.");

        if (file.Size.HasValue && file.Size.Value > 2_000_000)
            throw new InvalidOperationException("The requested file is larger than the 2 MB read limit.");

        using var output = new MemoryStream();
        await _drive.Files.Get(fileId).DownloadAsync(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    public async Task<string> CreateTextFileAsync(string name, string content, string? parentId = null)
    {
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A file name is required.", nameof(name));

        // Create a real Google Docs document first, then populate it through
        // the Docs API. Uploading text/plain creates a plain text file, not a
        // native Google Docs document.
        var title = Path.GetFileNameWithoutExtension(name.Trim());
        if (string.IsNullOrWhiteSpace(title))
            title = name.Trim();

        if (_docs is null)
            throw new InvalidOperationException("Google Docs service is not connected.");

        var document = await _docs.Documents.Create(new Document { Title = title }).ExecuteAsync();

        if (!string.IsNullOrEmpty(content))
        {
            var requests = new List<Request>
            {
                new Request
                {
                    InsertText = new InsertTextRequest
                    {
                        Location = new Location { Index = 1 },
                        Text = content
                    }
                }
            };

            await _docs.Documents.BatchUpdate(
                new BatchUpdateDocumentRequest { Requests = requests },
                document.DocumentId).ExecuteAsync();
        }

        if (!string.IsNullOrWhiteSpace(parentId))
        {
            var move = _drive!.Files.Update(new global::Google.Apis.Drive.v3.Data.File(), document.DocumentId);
            move.AddParents = parentId.Trim();
            move.RemoveParents = "root";
            move.Fields = "id,name,mimeType,webViewLink,parents";
            await move.ExecuteAsync();
        }

        var result = await _drive!.Files.Get(document.DocumentId).ExecuteAsync();
        return FormatFile(result);
    }

    public async Task<string> UpdateTextFileAsync(string fileId, string content)
    {
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(fileId))
            throw new ArgumentException("A file ID is required.", nameof(fileId));

        var existing = await _drive!.Files.Get(fileId).ExecuteAsync();
        if (existing.MimeType.StartsWith("application/vnd.google-apps.", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Google Workspace file type '{existing.MimeType}' cannot be updated as plain text by this tool.");

        if (existing.MimeType != "text/plain" && existing.MimeType != "text/csv" && existing.MimeType != "application/json")
            throw new InvalidOperationException($"File type '{existing.MimeType}' is not supported for text updates.");

        var metadata = new global::Google.Apis.Drive.v3.Data.File { Name = existing.Name };
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content ?? string.Empty));
        var request = _drive.Files.Update(metadata, fileId, stream, existing.MimeType);
        request.Fields = "id,name,mimeType,webViewLink";
        await request.UploadAsync();
        if (request.ResponseBody is null)
            throw new InvalidOperationException("Google Drive did not return the updated file.");

        return FormatFile(request.ResponseBody);
    }

    public async Task<string> RenameAsync(string fileId, string newName)
    {
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(fileId))
            throw new ArgumentException("A file ID is required.", nameof(fileId));
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("A new name is required.", nameof(newName));

        var metadata = new global::Google.Apis.Drive.v3.Data.File { Name = newName.Trim() };
        var result = await _drive!.Files.Update(metadata, fileId).ExecuteAsync();
        return FormatFile(result);
    }

    public async Task<string> MoveAsync(string fileId, string destinationParentId)
    {
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(fileId))
            throw new ArgumentException("A file ID is required.", nameof(fileId));
        if (string.IsNullOrWhiteSpace(destinationParentId))
            throw new ArgumentException("A destination folder ID is required.", nameof(destinationParentId));

        var file = await _drive!.Files.Get(fileId).ExecuteAsync();
        var update = _drive.Files.Update(new global::Google.Apis.Drive.v3.Data.File(), fileId);
        update.AddParents = destinationParentId.Trim();
        update.RemoveParents = file.Parents is null ? null : string.Join(",", file.Parents);
        update.Fields = "id,name,mimeType,parents,webViewLink";
        var result = await update.ExecuteAsync();
        return FormatFile(result);
    }

    public async Task DeleteAsync(string fileId)
    {
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(fileId))
            throw new ArgumentException("A file ID is required.", nameof(fileId));

        await _drive!.Files.Delete(fileId).ExecuteAsync();
    }

    private void EnsureConnected()
    {
        if (_drive is null)
            throw new InvalidOperationException("Google Drive is not connected. Open Settings and connect Google Drive first.");
    }

    private static string FormatFile(global::Google.Apis.Drive.v3.Data.File file) =>
        $"ID: {file.Id}\nName: {file.Name}\nType: {file.MimeType}\nLink: {file.WebViewLink}";

    private static string EscapeQuery(string value) => value.Trim().Replace("\\", "\\\\").Replace("'", "\\'");
}

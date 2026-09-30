using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
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
    private static readonly string[] Scopes = { DriveService.Scope.DriveReadonly };
    private readonly string _appFolder;
    private DriveService? _drive;

    public GoogleDriveService()
    {
        _appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ballknower");
        Directory.CreateDirectory(_appFolder);
    }

    public bool IsConnected => _drive is not null;

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
        var tokenStore = new EncryptedDataStore(Path.Combine(_appFolder, "google-token"));
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(clientSecrets.Secrets, Scopes, "default", CancellationToken.None, tokenStore, new LocalServerCodeReceiver());
        _drive = new DriveService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "Ballknower Agent" });
    }

    public async Task DisconnectAsync()
    {
        _drive = null;
        await new EncryptedDataStore(Path.Combine(_appFolder, "google-token")).ClearAsync();
    }

    public async Task<IList<global::Google.Apis.Drive.v3.Data.File>> SearchAsync(string query)
    {
        if (_drive is null)
            throw new InvalidOperationException("Google Drive is not connected. Open Settings and connect Google Drive first.");
        var request = _drive.Files.List();
        request.Q = $"trashed = false and name contains '{EscapeQuery(query)}'";
        request.PageSize = 25;
        request.Fields = "files(id,name,mimeType,size,modifiedTimeDateTimeOffset,webViewLink,parents)";
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

    private static string EscapeQuery(string value) => value.Trim().Replace("\\", "\\\\").Replace("'", "\\'");
}

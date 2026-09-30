using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
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
    private readonly string _clientSecretsPath;
    private DriveService? _drive;

    public GoogleDriveService()
    {
        _appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ballknower");
        Directory.CreateDirectory(_appFolder);
        _clientSecretsPath = Path.Combine(_appFolder, "google-client-secret.json");
    }

    public bool IsConnected => _drive is not null;

    public async Task ConnectAsync()
    {
        if (_drive is not null) return;
        if (!File.Exists(_clientSecretsPath))
            throw new FileNotFoundException("Google OAuth client secrets were not found. Place your Google Desktop OAuth client JSON at '" + _clientSecretsPath + "'.");
        using var stream = new FileStream(_clientSecretsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var clientSecrets = await GoogleClientSecrets.LoadAsync(stream);
        var tokenStore = new EncryptedDataStore(Path.Combine(_appFolder, "google-token"));
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(clientSecrets.Secrets, Scopes, "default", CancellationToken.None, tokenStore, new LocalServerCodeReceiver());
        _drive = new DriveService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "Ballknower Agent" });
    }

    public async Task DisconnectAsync()
    {
        _drive = null;
        await new EncryptedDataStore(Path.Combine(_appFolder, "google-token")).ClearAsync();
    }

    public async Task<IList<Google.Apis.Drive.v3.Data.File>> SearchAsync(string query)
    {
        await ConnectAsync();
        var request = _drive!.Files.List();
        request.Q = $"trashed = false and name contains '{EscapeQuery(query)}'";
        request.PageSize = 25;
        request.Fields = "files(id,name,mimeType,size,modifiedTime,webViewLink,parents)";
        return (await request.ExecuteAsync()).Files;
    }

    public async Task<string> ReadAsync(string fileId)
    {
        await ConnectAsync();
        var file = await _drive!.Files.Get(fileId).ExecuteAsync();
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

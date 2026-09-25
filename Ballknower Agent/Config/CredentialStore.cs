
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ballknower.Config;

public class CredentialStore
{
    private readonly string _credentialsPath;

    public CredentialStore()
    {
        var appFolder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Ballknower");

        Directory.CreateDirectory(appFolder);

        _credentialsPath = Path.Combine(
            appFolder,
            "credentials.dat");
    }

    public void SaveApiKey(string provider, string apiKey)
    {
        ValidateProvider(provider);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException(
                "API key cannot be empty.",
                nameof(apiKey));
        }

        var credentials = LoadCredentials();

        credentials[provider] = apiKey;

        SaveCredentials(credentials);
    }

    public string? GetApiKey(string provider)
    {
        ValidateProvider(provider);

        var credentials = LoadCredentials();

        return credentials.TryGetValue(
            provider,
            out var apiKey)
            ? apiKey
            : null;
    }

    public void DeleteApiKey(string provider)
    {
        ValidateProvider(provider);

        var credentials = LoadCredentials();

        if (credentials.Remove(provider))
        {
            SaveCredentials(credentials);
        }
    }

    private static void ValidateProvider(string provider)
    {
        if (provider != "Groq" &&
            provider != "OpenRouter")
        {
            throw new ArgumentException(
                "Unsupported AI provider.",
                nameof(provider));
        }
    }

    private Dictionary<string, string> LoadCredentials()
    {
        if (!File.Exists(_credentialsPath))
        {
            return new Dictionary<string, string>();
        }

        var encryptedData = File.ReadAllBytes(
            _credentialsPath);

        var decryptedData = ProtectedData.Unprotect(
            encryptedData,
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);

        try
        {
            var json = Encoding.UTF8.GetString(
                decryptedData);

            return JsonSerializer.Deserialize<
                Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                decryptedData);
        }
    }

    private void SaveCredentials(
        Dictionary<string, string> credentials)
    {
        var json = JsonSerializer.Serialize(
            credentials);

        var data = Encoding.UTF8.GetBytes(json);

        try
        {
            var encryptedData = ProtectedData.Protect(
                data,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            File.WriteAllBytes(
                _credentialsPath,
                encryptedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }
}
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Ballknower.Google;

internal sealed class EncryptedDataStore : Google.Apis.Util.Store.IDataStore
{
    private readonly string _folder;
    public EncryptedDataStore(string folder) { _folder = folder; Directory.CreateDirectory(_folder); }
    public Task ClearAsync() { foreach (var file in Directory.EnumerateFiles(_folder, "*.dat")) File.Delete(file); return Task.CompletedTask; }
    public Task DeleteAsync<T>(string key) { var path = GetPath(key); if (File.Exists(path)) File.Delete(path); return Task.CompletedTask; }
    public Task<T> GetAsync<T>(string key)
    {
        var path = GetPath(key);
        if (!File.Exists(path)) return Task.FromResult(default(T)!);
        var encrypted = File.ReadAllBytes(path);
        var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try { return Task.FromResult(System.Text.Json.JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(decrypted))!); }
        finally { CryptographicOperations.ZeroMemory(decrypted); }
    }
    public Task StoreAsync<T>(string key, T value)
    {
        var data = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(value));
        try { File.WriteAllBytes(GetPath(key), ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(data); }
        return Task.CompletedTask;
    }
    private string GetPath(string key) => Path.Combine(_folder, Convert.ToBase64String(Encoding.UTF8.GetBytes(key)).Replace("/", "_").Replace("+", "-").Replace("=", "") + ".dat");
}

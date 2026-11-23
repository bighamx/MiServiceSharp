using System.Text.Json;
using MiServiceSharp.Auth;

namespace MiServiceSharp.Storage;

public sealed class FileMiOAuthTokenStore(string filePath)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public string FilePath { get; } = Path.GetFullPath(filePath);
    public string PendingAuthorizationPath => Path.ChangeExtension(FilePath, "pending.json");

    public static string GetDefaultPath()
        => Path.Combine(Path.GetDirectoryName(FileMiTokenStore.GetDefaultPath())!, ".mi.oauth.json");

    public Task<MiOAuthToken?> LoadAsync(CancellationToken cancellationToken = default)
        => LoadJsonAsync<MiOAuthToken>(FilePath, cancellationToken);

    public Task<MiOAuthAuthorization?> LoadAuthorizationAsync(CancellationToken cancellationToken = default)
        => LoadJsonAsync<MiOAuthAuthorization>(PendingAuthorizationPath, cancellationToken);

    private async Task<T?> LoadJsonAsync<T>(string filePath, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath)) return default;
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task SaveAsync(MiOAuthToken token, CancellationToken cancellationToken = default)
        => SaveJsonAsync(FilePath, token, cancellationToken);

    public Task SaveAuthorizationAsync(MiOAuthAuthorization authorization, CancellationToken cancellationToken = default)
        => SaveJsonAsync(PendingAuthorizationPath, authorization, cancellationToken);

    public async Task ClearAuthorizationAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { File.Delete(PendingAuthorizationPath); }
        finally { _gate.Release(); }
    }

    private async Task SaveJsonAsync<T>(string filePath, T token, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        var temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, token, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            _gate.Release();
        }
    }
}

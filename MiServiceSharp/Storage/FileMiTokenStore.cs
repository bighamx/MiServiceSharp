using System.Text.Json;
using MiServiceSharp.Models;

namespace MiServiceSharp.Storage;

public sealed class FileMiTokenStore : IMiTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FileMiTokenStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public async Task<MiTokenBundle?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(FilePath);
        return await JsonSerializer.DeserializeAsync<MiTokenBundle>(stream, JsonOptions, cancellationToken);
    }

    public async Task SaveAsync(MiTokenBundle tokenBundle, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(FilePath);
        await JsonSerializer.SerializeAsync(stream, tokenBundle, JsonOptions, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }

        return Task.CompletedTask;
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using MiServiceSharp.Models;

namespace MiServiceSharp.Storage;

public sealed class FileMiTokenStore : IMiTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = true
    };

    public FileMiTokenStore(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
    }

    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public string FilePath { get; }

    public static string GetDefaultPath()
    {
        var configured = Environment.GetEnvironmentVariable("MI_TOKEN_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MiServiceSharp.sln"))
                || File.Exists(Path.Combine(directory.FullName, "MiServiceSharp.slnx")))
            {
                return Path.Combine(directory.FullName, ".mi.token.json");
            }
        }
        return Path.Combine(AppContext.BaseDirectory, ".mi.token.json");
    }

    public async Task<MiTokenBundle?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }
            await using var stream = File.OpenRead(FilePath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var token = document.RootElement.Deserialize<MiTokenBundle>(JsonOptions);
            if (token is not null)
            {
                token.Services = new Dictionary<string, ServiceCredential>(token.Services ?? [], StringComparer.OrdinalIgnoreCase);
            }
            if (token is not null && !document.RootElement.TryGetProperty("services", out _))
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() == 2
                        && property.Value[0].ValueKind == JsonValueKind.String && property.Value[1].ValueKind == JsonValueKind.String)
                    {
                        token.Services[property.Name] = new ServiceCredential
                        {
                            SSecurity = property.Value[0].GetString()!,
                            ServiceToken = property.Value[1].GetString()!
                        };
                    }
                }
            }
            return token;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(MiTokenBundle tokenBundle, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        var temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, tokenBundle, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
            _fileLock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            File.Delete(FilePath);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}

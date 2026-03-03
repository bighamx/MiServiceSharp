using MiServiceSharp.Models;

namespace MiServiceSharp.Storage;

public interface IMiTokenStore
{
    Task<MiTokenBundle?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(MiTokenBundle tokenBundle, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

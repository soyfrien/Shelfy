namespace Shelfy.Features.Recognition.Storage;

/// <summary>Stores uploaded shelf photos. Default: local filesystem; Azure Blob Storage later (ADR-007).</summary>
public interface IPhotoStorage
{
    /// <summary>Stores the stream under the given key (relative, forward slashes) and returns the key. Overwrites an existing object.</summary>
    Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Opens the stored photo for sequential reading. The caller disposes the stream.</summary>
    /// <exception cref="FileNotFoundException">When the key does not exist.</exception>
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);

    /// <summary>Deletes the photo if present.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Deletes every photo under a key prefix such as <c>shelves/{shelfId}</c> (used when a shelf is deleted, to also catch a
    /// photo uploaded at the very moment of deletion). Optional: stores without folders may leave the default, which does nothing.
    /// </summary>
    Task DeleteFolderAsync(string prefix, CancellationToken ct = default) => Task.CompletedTask;
}

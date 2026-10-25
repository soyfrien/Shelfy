namespace Shelfy.Features.Recognition.Storage;

/// <summary>
/// STUB. Placeholder for Azure Blob Storage (ADR-007). It is not registered by default and every call throws.
/// Implement it with Azure.Storage.Blobs (container from configuration, managed identity) and select it with Storage__Provider=AzureBlob.
/// </summary>
public class AzureBlobPhotoStorage : IPhotoStorage
{
    public const string NotImplementedMessage =
        "Azure Blob photo storage is not implemented yet. Use Storage__Provider=Local, or implement AzureBlobPhotoStorage (docs/pipeline.md).";

    public Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default) =>
        throw new NotSupportedException(NotImplementedMessage);

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
        throw new NotSupportedException(NotImplementedMessage);

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        throw new NotSupportedException(NotImplementedMessage);
}

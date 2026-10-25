namespace Shelfy.Features.Recognition.Storage;

/// <summary>Stores photos as files under a configured root. Keys are validated so they cannot escape the root.</summary>
public class LocalFileSystemPhotoStorage : IPhotoStorage
{
    private const int BufferSize = 81920;
    private readonly string _root;

    public LocalFileSystemPhotoStorage(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A photo storage path is required (Storage__LocalPath).", nameof(rootPath));
        }

        _root = Path.GetFullPath(rootPath);
    }

    public async Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temp file first so a failed or oversized upload never leaves a partial photo behind.
        var temp = path + ".part-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                await content.CopyToAsync(file, BufferSize, ct);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = Resolve(key);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Photo '{key}' was not found in storage.", path);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        TryDelete(Resolve(key));
        return Task.CompletedTask;
    }

    public Task DeleteFolderAsync(string prefix, CancellationToken ct = default)
    {
        var folder = Resolve(prefix.TrimEnd('/'));
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains("..") || key.Contains('\\') || key.StartsWith('/') || key.Contains('\0'))
        {
            throw new ArgumentException($"Invalid photo key '{key}'.", nameof(key));
        }

        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid photo key '{key}'.", nameof(key));
        }

        return full;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

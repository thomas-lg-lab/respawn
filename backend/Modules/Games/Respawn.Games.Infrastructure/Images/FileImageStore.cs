using Microsoft.Extensions.Logging;
using Respawn.Games.Ports;

namespace Respawn.Games.Infrastructure.Images;

/// <summary>
/// The images an instance keeps, as files of its data directory:
/// <c>images/&lt;kind&gt;/&lt;source&gt;/&lt;key&gt;.&lt;extension&gt;</c>, written in <c>images/tmp</c> then moved
/// into place, so a reader finds a whole file or none. A file is never written over with other bytes:
/// its name says what it holds (<see cref="ImageName"/>), and a refreshed image that changed is a new
/// file. One folder per source, so that what a source asks to be removed goes with its folder.
/// </summary>
public sealed partial class FileImageStore(string directory, ILogger<FileImageStore> logger) : IImageStore
{
    private readonly string _images = Path.Combine(directory, "images");

    /// <summary>Whether the last write failed: a trouble is said once in the log, not at each image.</summary>
    private int _troubled;

    /// <summary>The data directory, as it was given.</summary>
    public string Directory => directory;

    public bool Keeps => true;

    /// <summary>Makes the folders, leaves nothing of a write a stop cut short, and writes and removes a
    /// file to be sure the directory can be written: called once, before the app listens.</summary>
    /// <exception cref="IOException">The directory cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The app's user may not write there.</exception>
    public void Prepare()
    {
        var tmp = System.IO.Directory.CreateDirectory(Temporary).FullName;
        foreach (var left in System.IO.Directory.EnumerateFiles(tmp))
        {
            File.Delete(left);
        }

        var probe = Path.Combine(tmp, "probe");
        File.WriteAllBytes(probe, [0]);
        File.Delete(probe);
    }

    public Task<Stream?> OpenAsync(ImageName image, CancellationToken ct)
    {
        if (PathOf(image) is not { } path)
        {
            return Task.FromResult<Stream?>(null);
        }

        try
        {
            // Shared for deletion: a refresh may remove a file another request is still sending.
            return Task.FromResult<Stream?>(new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fetched again from its source, and written again.
            LogUnreadable(logger, image.Tag, ex.Message);
            return Task.FromResult<Stream?>(null);
        }
    }

    public async Task<bool> WriteAsync(ImageName image, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        if (PathOf(image) is not { } path)
        {
            return false;
        }

        // The same name holds the same bytes, and a file is only ever moved into place whole: one that
        // is there already needs no second write.
        if (File.Exists(path))
        {
            return true;
        }

        var written = Path.Combine(Temporary, $"{Guid.CreateVersion7():N}");
        try
        {
            System.IO.Directory.CreateDirectory(Temporary);
            await File.WriteAllBytesAsync(written, bytes, ct);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Overwritten only by a write of the same image that got there first, with the same bytes.
            File.Move(written, path, overwrite: true);
            if (Interlocked.Exchange(ref _troubled, 0) == 1)
            {
                LogWritable(logger);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Remove(written);
            if (Interlocked.Exchange(ref _troubled, 1) == 0)
            {
                LogUnwritable(logger, ex.Message);
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            Remove(written);
            throw;
        }
    }

    public void Delete(ImageName image)
    {
        if (PathOf(image) is { } path)
        {
            Remove(path);
        }
    }

    private string Temporary => Path.Combine(_images, "tmp");

    /// <summary>Where an image's file is; null for a name that cannot be one, which no file has.</summary>
    private string? PathOf(ImageName image) =>
        ImageName.IsPart(image.Kind) && ImageName.IsPart(image.Source) && ImageName.IsPart(image.Key) && image.Kind != "tmp"
            && Extension(image.ContentType) is { } extension
            ? Path.Combine(_images, image.Kind, image.Source, $"{image.Key}.{extension}")
            : null;

    private static string? Extension(string contentType) => contentType switch
    {
        "image/jpeg" => "jpg",
        "image/png" => "png",
        "image/webp" => "webp",
        _ => null,
    };

    /// <summary>Removes a file, if it is there; a file that cannot be removed is left.</summary>
    private static void Remove(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "covers cannot be written to the data directory, they are passed on without being kept until they can: {Reason}")]
    private static partial void LogUnwritable(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "covers are written to the data directory again")]
    private static partial void LogWritable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "a kept image cannot be read, it is fetched again: {Image}: {Reason}")]
    private static partial void LogUnreadable(ILogger logger, string image, string reason);
}

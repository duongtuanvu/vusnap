namespace VuSnap.Core;

/// <summary>Stage data in the destination directory and publish only a complete, flushed file.</summary>
public static class AtomicFile
{
    public static void Write(string path, Action<Stream> write, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(write);
        var fullPath = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(fullPath) ?? throw new IOException("ERR_EXPORT_PATH");
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, ".vusnap-" + Guid.NewGuid().ToString("N") + ".tmp");
        var ownsTemporary = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite);
        }
        finally
        {
            if (ownsTemporary)
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public static string NewCapturePath(string directory, string extension, DateTime timestamp)
    {
        if (extension is not (".png" or ".jpg")) throw new ArgumentException("ERR_EXPORT_FORMAT", nameof(extension));
        // Uniqueness plus no-overwrite move: even a collision cannot replace an existing file.
        return Path.Combine(directory, $"VuSnap_{timestamp:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}{extension}");
    }
}

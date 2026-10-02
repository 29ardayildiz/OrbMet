using System.Text;

namespace OrbMet.Core.Storage;

/// <summary>
/// Replaces a file in one step so a reader never catches it half written: the
/// bytes land in a sibling temporary file first, then a single move swaps it in.
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllText(string path, string text) =>
        WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("The path has no directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var temporary = Path.Combine(
            directory,
            $"{Path.GetFileName(path)}.tmp-{Guid.NewGuid():N}");

        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            MoveIntoPlace(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (IOException)
                {
                    // A leftover temporary file is harmless; the next write uses a new name.
                }
            }
        }
    }

    /// <summary>
    /// A move-replace can briefly fail while another process swaps the same
    /// destination. A few quick retries keep concurrent writers from losing a
    /// capture without ever leaving a half-written file behind.
    /// </summary>
    private static void MoveIntoPlace(string temporary, string path)
    {
        const int attempts = 20;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < attempts && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(10);
            }
        }
    }
}

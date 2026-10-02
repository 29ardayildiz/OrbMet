namespace OrbMet.Core.Storage;

/// <summary>
/// A best-effort exclusive lock shared across processes, used while changing a
/// user's Claude settings so two connect attempts cannot interleave. It is not
/// a substitute for re-reading and comparing the file before writing.
/// </summary>
internal sealed class CrossProcessFileLock : IDisposable
{
    private readonly FileStream _stream;

    private CrossProcessFileLock(FileStream stream) => _stream = stream;

    public static CrossProcessFileLock Acquire(string path, TimeSpan timeout)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("The lock path has no directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new CrossProcessFileLock(stream);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(25);
            }
        }
    }

    public void Dispose() => _stream.Dispose();
}

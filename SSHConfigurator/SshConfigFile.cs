using System.Text;

namespace SSHConfigurator;

internal sealed class SshConfigFile(string homeDirectory)
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode PrivateDirectory = PrivateFile | UnixFileMode.UserExecute;
    private byte[]? _originalBytes;

    private string ConfigPath => string.IsNullOrEmpty(homeDirectory)
        ? throw new IOException("Could not locate your home directory.")
        : Path.Combine(homeDirectory, ".ssh", "config");

    public bool Exists => _originalBytes is not null;

    public string Load()
    {
        _originalBytes = ReadBytes(ConfigPath);
        if (_originalBytes is null)
        {
            return "";
        }

        var offset = _originalBytes.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        return Utf8.GetString(_originalBytes.AsSpan(offset));
    }

    public void Save(string text)
    {
        var destination = ResolveConfigPath();
        var currentBytes = ReadBytes(destination);
        EnsureUnchanged(currentBytes);
        if (currentBytes is null && string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var directory = Path.GetDirectoryName(destination)!;
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, PrivateDirectory);
        }

        var tempPath = Path.Combine(directory, $".ssh-configurator-{Guid.NewGuid():N}");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = PrivateFile;
        }

        var bytes = Utf8.GetBytes(text);
        if (_originalBytes.AsSpan().StartsWith(Utf8Bom))
        {
            bytes = [.. Utf8Bom, .. bytes];
        }

        var stream = new FileStream(tempPath, options);
        try
        {
            using (stream)
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (!OperatingSystem.IsWindows() && currentBytes is not null)
            {
                File.SetUnixFileMode(tempPath, File.GetUnixFileMode(destination));
            }

            if (destination != ResolveConfigPath())
            {
                throw new IOException("The config symlink changed on disk. Reload it before saving.");
            }
            EnsureUnchanged(ReadBytes(destination));
            File.Move(tempPath, destination, overwrite: currentBytes is not null);
            _originalBytes = bytes;
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private string ResolveConfigPath()
    {
        var info = new FileInfo(ConfigPath);
        return info.LinkTarget is null ? info.FullName :
            info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ??
            throw new IOException("Could not resolve the config symlink.");
    }

    private void EnsureUnchanged(byte[]? currentBytes)
    {
        if ((currentBytes is null) != (_originalBytes is null) ||
            !currentBytes.AsSpan().SequenceEqual(_originalBytes))
        {
            throw new IOException("The file changed on disk. Reload it before saving.");
        }
    }

    private static byte[]? ReadBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}

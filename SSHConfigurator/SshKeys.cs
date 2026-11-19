using System.Buffers.Binary;
using System.Buffers.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SSHConfigurator;

internal sealed class SshKeys(string homeDirectory)
{
    private const UnixFileMode PrivateDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private static readonly HashSet<string> NonKeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "config", "authorized_keys", "authorized_keys2", "known_hosts", "known_hosts.old"
    };

    public string DirectoryPath => string.IsNullOrEmpty(homeDirectory)
        ? throw new IOException("Could not locate your home directory.")
        : Path.Combine(homeDirectory, ".ssh");

    public IReadOnlyList<SshKeyFile> List(string? directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory ?? DirectoryPath)
                .Where(path => !NonKeyNames.Contains(Path.GetFileName(path)))
                .Select(path => ReadPrivateKey(path))
                .OfType<SshKeyFile>()
                .OrderBy(key => key.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    public string SuggestName(SshKeyType type, string directory)
    {
        var baseName = $"id_{type.Argument}";
        var name = baseName;
        for (var suffix = 2; PairExists(Path.Combine(directory, name)); suffix++)
        {
            name = $"{baseName}_{suffix}";
        }
        return name;
    }

    public async Task<string> GenerateAsync(SshKeyType type, string name, string directory,
        string options, string passphrase)
    {
        if (!Regex.IsMatch(name, @"\A[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,127}\z") ||
            name.EndsWith('.') || name.EndsWith(".pub", StringComparison.OrdinalIgnoreCase) ||
            (OperatingSystem.IsWindows() && Regex.IsMatch(name,
                @"\A(?:CON|NUL|PRN|AUX|COM[0-9]|LPT[0-9])(?:\.|\z)", RegexOptions.IgnoreCase)))
        {
            throw new ArgumentException("Enter a filename using letters, numbers, dots, hyphens, or underscores.");
        }

        if (NonKeyNames.Contains(name))
        {
            throw new ArgumentException("That filename is reserved for SSH configuration or host data.");
        }

        var arguments = ReadGenerationOptions(options);

        if (passphrase.AsSpan().IndexOfAny('\0', '\r', '\n') >= 0 ||
            Encoding.UTF8.GetByteCount(passphrase) > 1023)
        {
            throw new ArgumentException("Enter a single-line passphrase under 1024 UTF-8 bytes.");
        }

        var destination = Path.Combine(directory, name);
        if (PairExists(destination))
        {
            throw new IOException("A file with that key or public-key name already exists.");
        }

        // Generate separately so ssh-keygen never prompts to replace a user's existing key.
        var stagingDirectory = Path.Combine(directory, $".keygen-{Guid.NewGuid():N}");
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(stagingDirectory);
        }
        else
        {
            Directory.CreateDirectory(directory, PrivateDirectory);
            Directory.CreateDirectory(stagingDirectory, PrivateDirectory);
        }

        var stagedKey = Path.Combine(stagingDirectory, name);
        try
        {
            await RunKeygenAsync(type, stagedKey, arguments, passphrase);
            if (passphrase.Length > 0 && ReadPrivateKey(stagedKey, requireEncryption: true) is null)
            {
                throw new IOException("ssh-keygen did not produce an encrypted key.");
            }

            File.Move(stagedKey + ".pub", destination + ".pub");
            try
            {
                File.Move(stagedKey, destination);
            }
            catch
            {
                File.Delete(destination + ".pub");
                throw;
            }
            return destination;
        }
        finally
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static bool PairExists(string path) => Path.Exists(path) || Path.Exists(path + ".pub");

    private static IReadOnlyList<string> ReadGenerationOptions(string options)
    {
        if (string.IsNullOrWhiteSpace(options))
        {
            return [];
        }

        if (!SshConfigDocument.IsValidValue("Options", options))
        {
            throw new ArgumentException("Enter options on one line and quote values that contain spaces.");
        }

        var tokens = SshSettingValidation.ReadArguments(options);
        var arguments = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var flag = token.Length >= 2 ? token[..2] : "";
            if (flag is not ("-a" or "-b" or "-C" or "-m" or "-Z"))
            {
                throw new ArgumentException("Advanced Options accepts -a, -b, -C, -m, and -Z. Use the form for type, destination, and passphrase.");
            }

            if (token.Length == 2 && i + 1 == tokens.Count)
            {
                throw new ArgumentException($"{flag} needs a value.");
            }

            arguments.Add(flag);
            arguments.Add(token.Length > 2 ? token[2..] : tokens[++i]);
        }

        return arguments;
    }

    public string ResolveDirectory(string? input)
    {
        var path = input?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("Enter a destination folder.");
        }
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) ||
            (OperatingSystem.IsWindows() && path.StartsWith("~\\", StringComparison.Ordinal)))
        {
            path = Path.Join(Path.GetDirectoryName(DirectoryPath)!, path.Length > 2 ? path[2..] : "");
        }
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Enter a full folder path or one starting with ~/.");
        }
        return Path.GetFullPath(path);
    }

    private static async Task RunKeygenAsync(SshKeyType type, string path, IReadOnlyList<string> options,
        string passphrase)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "ssh-keygen.exe" : "ssh-keygen",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            StandardInputEncoding = new UTF8Encoding(false),
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-q", "-t", type.Argument, "-f", path })
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var option in options)
        {
            startInfo.ArgumentList.Add(option);
        }

        if (passphrase.Length == 0)
        {
            startInfo.ArgumentList.Add("-N");
            startInfo.ArgumentList.Add("");
        }
        else if (OperatingSystem.IsWindows())
        {
            startInfo.Environment["SSH_ASKPASS_REQUIRE"] = "never";
        }
        else
        {
            // OpenSSH can prefer the controlling terminal over stdin. Force an askpass
            // helper that reads the redirected pipe, even when launched from a terminal.
            var helperPath = Path.Combine(Path.GetDirectoryName(path)!, ".askpass");
            await File.WriteAllTextAsync(helperPath, """
                #!/bin/sh
                IFS= read -r phrase || exit 1
                printf '%s\n' "$phrase"
                """ + "\n");
            File.SetUnixFileMode(helperPath, PrivateDirectory);
            startInfo.Environment["SSH_ASKPASS"] = helperPath;
            startInfo.Environment["SSH_ASKPASS_REQUIRE"] = "force";
        }

        using var child = new Process();
        child.StartInfo = startInfo;
        try
        {
            child.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            throw new IOException("ssh-keygen was not found. Install the OpenSSH client or add it to PATH.", ex);
        }

        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            using (var input = child.StandardInput)
            {
                if (passphrase.Length > 0)
                {
                    input.NewLine = "\n";
                    await input.WriteLineAsync(passphrase.AsMemory(), timeout.Token);
                    await input.WriteLineAsync(passphrase.AsMemory(), timeout.Token);
                    await input.FlushAsync(timeout.Token);
                }
            }
            await child.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
            }
            await child.WaitForExitAsync();
            throw new IOException("ssh-keygen timed out.");
        }

        await stdout;
        var error = (await stderr).Trim();
        if (child.ExitCode != 0)
        {
            throw new IOException(error.Length > 0 ? error : $"ssh-keygen exited with code {child.ExitCode}.");
        }
    }

    private static SshKeyFile? ReadPrivateKey(string path, bool requireEncryption = false)
    {
        var bytes = new byte[4096];
        try
        {
            using var file = File.OpenRead(path);
            var length = file.ReadAtLeast(bytes, bytes.Length, false);
            var contents = bytes.AsSpan(0, length);
            var newline = contents.IndexOf((byte)'\n');
            var line = newline >= 0 ? contents[..newline].TrimEnd((byte)'\r') : contents;

            if (line.SequenceEqual("-----BEGIN OPENSSH PRIVATE KEY-----"u8))
            {
                var (algorithm, encrypted) = ReadOpenSshPrivateHeader(contents[(newline + 1)..]);
                return requireEncryption && !encrypted ? null :
                    new SshKeyFile(path, algorithm is null ? "OpenSSH" : DisplayType(algorithm));
            }

            if (ReadPemType(line) is not { } pem)
            {
                return null;
            }

            var pemEncrypted = line.SequenceEqual("-----BEGIN ENCRYPTED PRIVATE KEY-----"u8) ||
                contents.IndexOf("Proc-Type: 4,ENCRYPTED"u8) >= 0;
            return !requireEncryption || pemEncrypted ? new SshKeyFile(path, pem) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static (string? Algorithm, bool Encrypted) ReadOpenSshPrivateHeader(Span<byte> data)
    {
        var written = DecodePrefix(data);
        ReadOnlySpan<byte> decoded = data[..written];
        ReadOnlySpan<byte> magic = "openssh-key-v1\0"u8;
        if (!decoded.StartsWith(magic))
        {
            return default;
        }

        decoded = decoded[magic.Length..];
        var cipher = ReadField(ref decoded);
        ReadField(ref decoded); // KDF
        ReadField(ref decoded); // KDF parameters
        if (decoded.Length < 4 || BinaryPrimitives.ReadUInt32BigEndian(decoded) == 0)
        {
            return default;
        }

        decoded = decoded[4..];
        var publicBlob = ReadField(ref decoded);
        var algorithm = ReadField(ref publicBlob);
        return algorithm.IsEmpty ? default :
            (Encoding.ASCII.GetString(algorithm), !cipher.IsEmpty && !cipher.SequenceEqual("none"u8));
    }

    private static ReadOnlySpan<byte> ReadField(ref ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || BinaryPrimitives.ReadUInt32BigEndian(data) > data.Length - 4)
        {
            data = [];
            return [];
        }

        var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
        var field = data.Slice(4, length);
        data = data[(4 + length)..];
        return field;
    }

    private static int DecodePrefix(Span<byte> data)
    {
        var count = 0;
        foreach (var character in data)
        {
            if (character is (byte)'\r' or (byte)'\n')
            {
                continue;
            }
            if (character is not (>= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z'
                or >= (byte)'0' and <= (byte)'9' or (byte)'+' or (byte)'/' or (byte)'='))
            {
                break;
            }

            data[count++] = character;
        }

        var block = data[..(count - count % 4)];
        return Base64.DecodeFromUtf8InPlace(block, out var written) == System.Buffers.OperationStatus.Done
            ? written : 0;
    }

    private static string? ReadPemType(ReadOnlySpan<byte> line)
    {
        if (line.SequenceEqual("-----BEGIN RSA PRIVATE KEY-----"u8)) return "RSA";
        if (line.SequenceEqual("-----BEGIN EC PRIVATE KEY-----"u8)) return "ECDSA";
        if (line.SequenceEqual("-----BEGIN DSA PRIVATE KEY-----"u8)) return "DSA";
        if (line.SequenceEqual("-----BEGIN PRIVATE KEY-----"u8) ||
            line.SequenceEqual("-----BEGIN ENCRYPTED PRIVATE KEY-----"u8)) return "PKCS#8";
        return null;
    }

    private static string DisplayType(string type)
    {
        return type switch
        {
            "ssh-ed25519" => "Ed25519",
            "ssh-rsa" => "RSA",
            "ssh-dss" => "DSA",
            "ssh-mldsa44-ed25519" => "ML-DSA44 + Ed25519",
            _ when type.StartsWith("sk-ssh-ed25519", StringComparison.Ordinal) => "Ed25519 (security key)",
            _ when type.StartsWith("sk-ecdsa-", StringComparison.Ordinal) => "ECDSA (security key)",
            _ when type.StartsWith("ecdsa-sha2-", StringComparison.Ordinal) => "ECDSA",
            _ => type
        };
    }
}

internal sealed record SshKeyFile(string Path, string Type)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

internal sealed record SshKeyType(string Label, string Argument)
{
    public static IReadOnlyList<SshKeyType> Common { get; } =
        [new("Ed25519", "ed25519"), new("RSA", "rsa"), new("ECDSA", "ecdsa")];

    public override string ToString() => Label;
}

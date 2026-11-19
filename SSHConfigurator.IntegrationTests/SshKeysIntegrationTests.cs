using System.Diagnostics;

namespace SSHConfigurator.IntegrationTests;

public sealed class SshKeysIntegrationTests
{
    private static SshKeyType Ed25519 => SshKeyType.Common.Single(type => type.Argument == "ed25519");

    [Fact]
    public async Task GeneratesAKeyPairAndListsOnlyThePrivateKey()
    {
        using var home = new TemporaryDirectory();
        var keys = new SshKeys(home.Path);
        var destination = keys.ResolveDirectory(Path.Combine(home.Path, "test key folder"));

        var path = await keys.GenerateAsync(Ed25519, "id_test", destination, "-C \"integration test\"", "");

        Assert.Equal(Path.Combine(destination, "id_test"), path);
        Assert.StartsWith("-----BEGIN OPENSSH PRIVATE KEY-----", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.EndsWith(" integration test", (await File.ReadAllTextAsync(path + ".pub", TestContext.Current.CancellationToken)).TrimEnd());
        Assert.Equal(new SshKeyFile(path, "Ed25519"), Assert.Single(keys.List(destination)));
        Assert.Empty(keys.List(null));
        Assert.Empty(Directory.EnumerateDirectories(destination));
    }

    [Fact]
    public async Task RefusesToOverwriteAnExistingPublicKey()
    {
        using var home = new TemporaryDirectory();
        var keys = new SshKeys(home.Path);
        Directory.CreateDirectory(keys.DirectoryPath);
        var path = Path.Combine(keys.DirectoryPath, "id_test");
        await File.WriteAllTextAsync(path + ".pub", "existing", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IOException>(() => keys.GenerateAsync(Ed25519, "id_test", keys.DirectoryPath, "", ""));

        Assert.False(File.Exists(path));
        Assert.Equal("existing", await File.ReadAllTextAsync(path + ".pub", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnixAskpassCreatesAKeyThatRequiresTheSuppliedPassphrase()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "This scenario exercises the Unix askpass helper.");
        using var home = new TemporaryDirectory();
        var keys = new SshKeys(home.Path);
        const string passphrase = "  integration test passphrase  ";

        var path = await keys.GenerateAsync(Ed25519, "id_test", keys.DirectoryPath, "-a 10", passphrase);

        Assert.NotEqual(0, await TryUnlock(path, ""));
        Assert.Equal(0, await TryUnlock(path, passphrase));
        Assert.Equal(new SshKeyFile(path, "Ed25519"), Assert.Single(keys.List(null)));
        Assert.Empty(Directory.EnumerateDirectories(keys.DirectoryPath));
    }

    [Fact]
    public async Task RejectsAnOptionThatWouldOverrideTheDestination()
    {
        using var home = new TemporaryDirectory();
        var keys = new SshKeys(home.Path);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            keys.GenerateAsync(Ed25519, "id_test", keys.DirectoryPath, "-f another-file", ""));

        Assert.False(Directory.Exists(keys.DirectoryPath));
    }

    private static async Task<int> TryUnlock(string path, string passphrase)
    {
        var start = new ProcessStartInfo("ssh-keygen")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        foreach (var argument in new[] { "-y", "-f", path, "-P", passphrase })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var cancellation = TestContext.Current.CancellationToken;
        await Task.WhenAll(process.StandardOutput.ReadToEndAsync(cancellation),
            process.StandardError.ReadToEndAsync(cancellation), process.WaitForExitAsync(cancellation));
        return process.ExitCode;
    }
}

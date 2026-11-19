using System.Text;

namespace SSHConfigurator.IntegrationTests;

public sealed class SshConfigIntegrationTests
{
    [Fact]
    public void FirstSaveCreatesConfigOnlyWhenContentIsPresent()
    {
        using var home = new TemporaryDirectory();
        var file = new SshConfigFile(home.Path);
        var directory = Path.Combine(home.Path, ".ssh");
        var path = Path.Combine(directory, "config");

        Assert.Empty(file.Load());
        file.Save("  \n");
        Assert.False(Directory.Exists(directory));

        var document = new SshConfigDocument(file.Load()).AddHost("test-host", "host.example.invalid", "deploy");
        file.Save(document.Render());
        Assert.Contains("Host test-host", File.ReadAllText(path));

        file.Save("");
        Assert.Empty(File.ReadAllBytes(path));
    }

    [Fact]
    public void EditingAndSavingPreservesLineEndingsAndUnrelatedConfig()
    {
        using var home = new TemporaryDirectory();
        var path = CreateConfig(home, """
            # client config
            Include local.conf
            Host stage
                HostName stage.example.invalid  # keep this
                User deploy
            # keep this too
            Match all
                Port 22
            """.ReplaceLineEndings("\r\n"));
        var file = new SshConfigFile(home.Path);
        var document = new SshConfigDocument(file.Load());
        var stage = Assert.Single(document.Hosts);
        stage.Settings[0].Value = "next.example.invalid";
        stage.Settings[1].IsRemoved = true;
        stage.Settings.Add(new SshConfigDocument.Setting("IdentityFile", null, "\"relative keys/dev\""));

        Assert.Equal("""
            Host stage
                HostName next.example.invalid  # keep this
                IdentityFile "relative keys/dev"
            # keep this too

            """.ReplaceLineEndings("\r\n"), document.Render(stage));

        file.Save(document.Render());

        Assert.Equal("""
            # client config
            Include local.conf
            Host stage
                HostName next.example.invalid  # keep this
                IdentityFile "relative keys/dev"
            # keep this too
            Match all
                Port 22
            """.ReplaceLineEndings("\r\n"), File.ReadAllText(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavingRefusesAnExternalEdit(bool initiallyExists)
    {
        using var home = new TemporaryDirectory();
        var path = initiallyExists ? CreateConfig(home, "Host original\n") : Path.Combine(home.Path, ".ssh", "config");
        var file = new SshConfigFile(home.Path);
        file.Load();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "Host external\n");

        var error = Assert.Throws<IOException>(() => file.Save("Host draft\n"));
        Assert.Contains("changed on disk", error.Message);
        Assert.Equal("Host external\n", File.ReadAllText(path));
    }

    [Fact]
    public void SavingASymlinkPreservesItsTargetAndPermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Creating symlinks on Windows can require elevated rights.");
            return;
        }

        using var home = new TemporaryDirectory();
        var target = Path.Combine(home.Path, "shared-config");
        var configDirectory = Directory.CreateDirectory(Path.Combine(home.Path, ".ssh"));
        var config = Path.Combine(configDirectory.FullName, "config");
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.WriteAllText(target, "Host old\n", new UTF8Encoding(true));
        File.SetUnixFileMode(target, mode);
        File.CreateSymbolicLink(config, target);
        var file = new SshConfigFile(home.Path);

        Assert.Equal("Host old\n", file.Load());
        file.Save("Host new\n");

        Assert.Equal(target, new FileInfo(config).LinkTarget);
        Assert.Equal("Host new\n", File.ReadAllText(target));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(target)[..3]);
        Assert.Equal(mode, File.GetUnixFileMode(target));
    }

    private static string CreateConfig(TemporaryDirectory home, string text)
    {
        var directory = Directory.CreateDirectory(Path.Combine(home.Path, ".ssh"));
        var path = Path.Combine(directory.FullName, "config");
        File.WriteAllText(path, text);
        return path;
    }
}

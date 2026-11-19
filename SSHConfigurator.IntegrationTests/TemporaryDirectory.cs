namespace SSHConfigurator.IntegrationTests;

internal sealed class TemporaryDirectory : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ssh-configurator-tests-");

    public string Path => _directory.FullName;

    public void Dispose() => _directory.Delete(recursive: true);
}

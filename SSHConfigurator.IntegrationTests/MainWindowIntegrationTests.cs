using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

[assembly: AvaloniaTestApplication(typeof(SSHConfigurator.IntegrationTests.HeadlessApp))]

namespace SSHConfigurator.IntegrationTests;

public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class MainWindowIntegrationTests
{
    [AvaloniaFact]
    public void RemovingUndoingAndSavingASettingUpdatesTheActualConfig()
    {
        using var home = new TemporaryDirectory();
        var path = Path.Combine(home.Path, ".ssh", "config");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string original = "Host test-host\r\n    HostName host.example.invalid\r\n    User deploy\r\n";
        File.WriteAllText(path, original);

        var window = new MainWindow(new SshConfigFile(home.Path));
        window.Show();
        var remove = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
            button => AutomationProperties.GetName(button) == "Remove User");
        var preview = window.FindControl<TextBox>("ConfigPreview")!;

        Click(remove);
        Assert.Equal("Undo removal of User", AutomationProperties.GetName(remove));
        Assert.DoesNotContain("User deploy", preview.Text);
        Assert.Equal(original, File.ReadAllText(path));

        Click(remove);
        Assert.Contains("User deploy", preview.Text);
        Assert.False(window.FindControl<Button>("SaveButton")!.IsEnabled);

        Click(remove);
        Click(window.FindControl<Button>("SaveButton")!);
        Assert.Equal("Host test-host\r\n    HostName host.example.invalid\r\n", File.ReadAllText(path));
        window.Close();
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}

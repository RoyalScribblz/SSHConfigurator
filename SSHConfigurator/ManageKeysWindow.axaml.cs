using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace SSHConfigurator;

public partial class ManageKeysWindow : Window
{
    private readonly SshKeys _keys;
    private string? _folder;

    public ManageKeysWindow() : this(new SshKeys(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
    {
    }

    internal ManageKeysWindow(SshKeys keys)
    {
        _keys = keys;
        InitializeComponent();
        Opened += async (_, _) => await RefreshKeys();
    }

    private async void RefreshClicked(object? sender, RoutedEventArgs e) => await RefreshKeys();

    private async void DefaultFolderClicked(object? sender, RoutedEventArgs e)
    {
        _folder = null;
        await RefreshKeys();
    }

    private async Task RefreshKeys(string? created = null)
    {
        RefreshButton.IsEnabled = AddKeyButton.IsEnabled = DefaultFolderButton.IsEnabled = false;
        FolderPath.Text = _folder ?? "~/.ssh";
        ToolTip.SetTip(FolderPath, FolderPath.Text);
        DefaultFolderButton.IsVisible = _folder is not null;
        try
        {
            var folder = _folder;
            var files = await Task.Run(() => _keys.List(folder));
            KeysList.ItemsSource = files;
            KeyStatus.Text = created is not null ? $"Created {Path.GetFileName(created)} and its public key." :
                files.Count == 0 ? "No private SSH keys found." : "";
            if (created is not null && files.FirstOrDefault(file => file.Path == created) is { } key)
            {
                KeysList.SelectedItem = key;
                KeysList.ScrollIntoView(key);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            KeysList.ItemsSource = null;
            KeyStatus.Text = $"Could not list SSH keys. {ex.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = AddKeyButton.IsEnabled = DefaultFolderButton.IsEnabled = true;
        }
    }

    private async void AddKeyClicked(object? sender, RoutedEventArgs e)
    {
        var created = await new AddKeyWindow(_keys).ShowDialog<string?>(this);
        if (created is not null)
        {
            var directory = Path.GetDirectoryName(created);
            _folder = string.Equals(directory, _keys.DirectoryPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                ? null
                : directory;
            await RefreshKeys(created);
        }
    }

    private async void CopyPathClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SshKeyFile file } || Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(file.Path);
            KeyStatus.Text = $"Copied the path for {file.Name}.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            KeyStatus.Text = $"Could not copy the path. {ex.Message}";
        }
    }
}

using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace SSHConfigurator;

internal sealed class AddKeyWindow : Window
{
    private bool _generating;

    public AddKeyWindow(SshKeys keys)
    {
        Title = "Add SSH Key";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var type = new ComboBox
        {
            ItemsSource = SshKeyType.Common,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var filename = new TextBox { MaxLength = 128, Classes = { "config-value" } };
        var folder = new TextBox { Text = "~/.ssh", Classes = { "config-value" } };
        var options = new TextBox { PlaceholderText = "-a 100", Classes = { "config-value" } };
        var passphrase = new TextBox { PasswordChar = '●' };
        var browse = new Button
        {
            Content = "Browse…",
            Padding = new Thickness(8, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var folderField = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        Grid.SetColumn(browse, 1);
        folderField.Children.Add(folder);
        folderField.Children.Add(browse);
        AutomationProperties.SetName(type, "Key type");
        AutomationProperties.SetName(filename, "Filename");
        AutomationProperties.SetName(folder, "Destination folder");
        AutomationProperties.SetName(options, "Advanced ssh-keygen options");
        ToolTip.SetTip(options, "Key-generation options: -a (rounds), -b (bits), -C (comment), -m (format), -Z (cipher).");
        AutomationProperties.SetName(passphrase, "Passphrase");

        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Classes = { "setting-error" }
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var create = new Button { Content = "Create Key", IsDefault = true, Classes = { "accent" } };
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "Key type" }, type } },
                new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "Destination folder" }, folderField } },
                new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "Filename" }, filename } },
                new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "Advanced Options (optional)" }, options } },
                new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "Passphrase (optional)" }, passphrase } },
                error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, create }
                }
            }
        };

        string? lastSuggestion = null;
        void SuggestName()
        {
            if ((string.IsNullOrWhiteSpace(filename.Text) || filename.Text == lastSuggestion) &&
                type.SelectedItem is SshKeyType selected)
            {
                try
                {
                    lastSuggestion = keys.SuggestName(selected, keys.ResolveDirectory(folder.Text));
                    filename.Text = lastSuggestion;
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
                {
                    lastSuggestion = $"id_{selected.Argument}";
                    filename.Text = lastSuggestion;
                }
            }
        }

        type.SelectionChanged += (_, _) => SuggestName();
        folder.TextChanged += (_, _) => SuggestName();
        browse.Click += async (_, _) =>
        {
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    { Title = "Choose a destination for the SSH key" });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
                {
                    folder.Text = path;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                error.Text = $"Could not open the folder picker. {ex.Message}";
                error.IsVisible = true;
            }
        };
        SuggestName();
        cancel.Click += (_, _) => Close();
        Closing += (_, e) => e.Cancel |= _generating;
        create.Click += async (_, _) =>
        {
            if (_generating || type.SelectedItem is not SshKeyType selected)
            {
                return;
            }

            string? path = null;
            _generating = true;
            error.IsVisible = false;
            create.Content = "Creating…";
            create.IsEnabled = cancel.IsEnabled = type.IsEnabled = filename.IsEnabled = folder.IsEnabled = browse.IsEnabled = false;
            options.IsEnabled = passphrase.IsEnabled = false;
            try
            {
                var directory = keys.ResolveDirectory(folder.Text);
                path = await keys.GenerateAsync(selected, filename.Text?.Trim() ?? "", directory,
                    options.Text ?? "", passphrase.Text ?? "");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or Win32Exception or NotSupportedException)
            {
                error.Text = ex.Message;
                error.IsVisible = true;
            }
            finally
            {
                _generating = false;
                create.Content = "Create Key";
                create.IsEnabled = cancel.IsEnabled = type.IsEnabled = filename.IsEnabled = folder.IsEnabled = browse.IsEnabled = true;
                options.IsEnabled = passphrase.IsEnabled = true;
            }

            if (path is not null)
            {
                passphrase.Clear();
                Close(path);
            }
        };
    }
}

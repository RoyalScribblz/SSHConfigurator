using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace SSHConfigurator;

public partial class MainWindow : Window
{
    private readonly List<(SshConfigDocument.Setting Setting, TextBlock Error, TextBlock Warning)> _settingMessages = [];
    private readonly SshConfigFile _file;
    private SshConfigDocument? _document;
    private string _savedText = "";
    private bool _dirty;
    private bool _allowClose;

    public MainWindow() : this(new SshConfigFile(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
    {
    }

    internal MainWindow(SshConfigFile file)
    {
        _file = file;
        InitializeComponent();
        ReloadConfig();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel && _dirty && !_allowClose)
        {
            e.Cancel = true;
            if (await ConfirmDiscard())
            {
                _allowClose = true;
                Close();
            }
        }
    }

    private async void ManageKeysClicked(object? sender, RoutedEventArgs e) =>
        await new ManageKeysWindow().ShowDialog(this);

    private async void ReloadClicked(object? sender, RoutedEventArgs e)
    {
        if (!_dirty || await ConfirmDiscard())
        {
            ReloadConfig();
        }
    }

    private void ReloadConfig()
    {
        var selectedIndex = SelectedHostIndex;
        try
        {
            _savedText = _file.Load();
            _document = new SshConfigDocument(_savedText);
            _dirty = false;
            StatusText.Text = _file.Exists ? "" : "No config file yet. Add a host to get started.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _document = null;
            _dirty = false;
            StatusText.Text = $"Could not read the SSH config. {ex.Message}";
        }

        SaveButton.IsEnabled = false;
        AddHostButton.IsEnabled = _document is not null;
        RefreshHosts(selectedIndex);
    }

    private void SearchChanged(object? sender, TextChangedEventArgs e) => RefreshHosts(SelectedHostIndex);

    private int SelectedHostIndex => HostList.SelectedItem is SshConfigDocument.HostBlock host
        ? _document?.Hosts.IndexOf(host) ?? -1
        : -1;

    private void RefreshHosts(int selectedIndex)
    {
        var query = HostSearch.Text?.Trim() ?? "";
        var hosts = _document?.Hosts
            .Where(host => host.Pattern.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];

        HostList.ItemsSource = hosts;
        var preferred = selectedIndex >= 0 && selectedIndex < _document?.Hosts.Count
            ? _document.Hosts[selectedIndex]
            : null;
        HostList.SelectedItem = hosts.Contains(preferred) ? preferred : hosts.FirstOrDefault();
        ShowHost();
    }

    private void HostSelected(object? sender, SelectionChangedEventArgs e) => ShowHost();

    private void ShowHost()
    {
        SettingsPanel.Children.Clear();
        _settingMessages.Clear();
        var host = HostList.SelectedItem as SshConfigDocument.HostBlock;
        HostTitle.Text = host?.Pattern ?? (_document is null ? "Config unavailable"
            : _document.Hosts.Count == 0 ? "No host blocks"
            : "No matching hosts");
        BlockExpander.IsVisible = host is not null;
        AddSettingButton.IsVisible = host is not null;

        if (host is not null)
        {
            foreach (var setting in host.Settings)
            {
                SettingsPanel.Children.Add(CreateSettingRow(setting));
            }
        }

        OpenTerminalButton.IsVisible = host is not null && SshTerminal.IsSupported;
        var alias = host is null ? null : SshTerminal.GetAlias(host.Pattern);
        OpenTerminalButton.IsEnabled = alias is not null;
        ToolTip.SetShowOnDisabled(OpenTerminalButton, true);
        ToolTip.SetTip(OpenTerminalButton, alias is null
            ? "This host block needs a literal SSH alias."
            : $"Open ssh {alias} using the saved config.");
        UpdateMessages();
        UpdatePreview();
    }

    private Grid CreateSettingRow(SshConfigDocument.Setting setting)
    {
        var metadata = SshSettingCatalog.Find(setting.Name);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("154,*"), ColumnSpacing = 10 };
        var label = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            VerticalAlignment = VerticalAlignment.Top,
            MinHeight = 32
        };
        var help = AddSettingWindow.CreateHelp(metadata, setting.Name);
        Grid.SetColumn(help, 1);
        label.Children.Add(new TextBlock
        {
            Text = setting.Name,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        label.Children.Add(help);
        row.Children.Add(label);

        var editor = CreateEditor(setting, metadata);
        var remove = new Button { Classes = { "setting-remove" } };
        remove.Bind(WidthProperty, new Binding("Bounds.Height") { Source = editor });
        UpdateRemovalState();
        remove.Click += (_, _) =>
        {
            setting.IsRemoved = !setting.IsRemoved;
            UpdateRemovalState();
            UpdateDraft();
            UpdateMessages();
            UpdatePreview();
        };

        void UpdateRemovalState()
        {
            editor.IsEnabled = label.IsEnabled = !setting.IsRemoved;
            editor.Opacity = label.Opacity = setting.IsRemoved ? 0.45 : 1;
            remove.Content = setting.IsRemoved ? "↶" : "×";
            remove.Classes.Set("undo", setting.IsRemoved);
            var description = setting.IsRemoved ? $"Undo removal of {setting.Name}" : $"Remove {setting.Name}";
            AutomationProperties.SetName(remove, description);
            ToolTip.SetTip(remove, description);
        }

        var field = new StackPanel { Spacing = 4 };
        field.Children.Add(EditorWithButton(editor, remove));

        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { "setting-error" } };
        var warning = new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { "setting-warning" } };
        field.Children.Add(error);
        field.Children.Add(warning);
        _settingMessages.Add((setting, error, warning));
        Grid.SetColumn(field, 1);
        row.Children.Add(field);
        return row;
    }

    private Control CreateEditor(SshConfigDocument.Setting setting, SshSetting? metadata)
    {
        var arguments = SshSettingValidation.ReadArguments(setting.Value);
        var value = arguments.Count == 1 ? arguments[0] : "";
        if (metadata is { IsBoolean: true } && value is "yes" or "no")
        {
            var checkBox = new CheckBox { IsChecked = value == "yes" };
            AutomationProperties.SetName(checkBox, setting.Name);
            checkBox.IsCheckedChanged += (_, _) => SetValue(setting, checkBox.IsChecked == true ? "yes" : "no");
            return checkBox;
        }

        if (metadata is { Choices.Length: > 0 })
        {
            var dropdown = new AutoCompleteBox
            {
                Text = value.Length > 0 ? value : setting.Value,
                ItemsSource = metadata.Choices,
                MinimumPrefixLength = 0,
                FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
                IsTextCompletionEnabled = false,
                MaxDropDownHeight = 220,
                PlaceholderText = "Search choices",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(dropdown, setting.Name);
            dropdown.TextChanged += (_, _) =>
            {
                dropdown.FilterMode = AutoCompleteFilterMode.ContainsOrdinal;
                var text = dropdown.Text ?? "";
                SetValue(setting, metadata.Choices.FirstOrDefault(choice =>
                    choice.Equals(text, StringComparison.OrdinalIgnoreCase)) ?? text);
            };
            var showChoices = new Button { Content = "▾", Padding = new Thickness(8, 5) };
            AutomationProperties.SetName(showChoices, $"Show choices for {setting.Name}");
            showChoices.Click += (_, _) =>
            {
                dropdown.Focus();
                dropdown.IsDropDownOpen = false;
                dropdown.FilterMode = AutoCompleteFilterMode.None;
                dropdown.IsDropDownOpen = true;
            };
            dropdown.DropDownClosed += (_, _) => dropdown.FilterMode = AutoCompleteFilterMode.ContainsOrdinal;
            return EditorWithButton(dropdown, showChoices);
        }

        var textBox = metadata?.Integer is { } integer ? new IntegerTextBox(integer) : new TextBox();
        textBox.Text = setting.Value;
        textBox.Classes.Add("config-value");
        AutomationProperties.SetName(textBox, setting.Name);
        textBox.TextChanged += (_, _) => SetValue(setting, textBox.Text ?? "");
        if (metadata is not { IsFile: true })
        {
            return textBox;
        }

        var browse = new Button
        {
            Content = "Browse…",
            Padding = new Thickness(8, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(browse, $"Browse for {setting.Name}");
        browse.Click += async (_, _) => await BrowseForPath(textBox, metadata);
        return EditorWithButton(textBox, browse);
    }

    private static Grid EditorWithButton(Control editor, Button button)
    {
        var field = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        Grid.SetColumn(button, 1);
        field.Children.Add(editor);
        field.Children.Add(button);
        return field;
    }

    private async Task BrowseForPath(TextBox field, SshSetting setting)
    {
        try
        {
            IReadOnlyList<IStorageFile> files = setting.OutputPath
                ? (await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = $"Choose {setting.Name} path" })
                    is { } target ? [target] : [])
                : await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"Choose {setting.Name}",
                    AllowMultiple = setting.MultiplePaths
                });
            var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
            if (paths.Length > 0)
            {
                field.Text = string.Join(" ", paths.Select(SshSettingValidation.QuotePath));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            StatusText.Text = $"Could not open the file picker. {ex.Message}";
        }
    }

    private async void AddSettingClicked(object? sender, RoutedEventArgs e)
    {
        if (HostList.SelectedItem is not SshConfigDocument.HostBlock host)
        {
            return;
        }

        var definition = await new AddSettingWindow().ShowDialog<SshSetting?>(this);
        if (definition is null)
        {
            return;
        }

        var setting = new SshConfigDocument.Setting(definition.Name, null, definition.IsBoolean ? "no" : "");
        host.Settings.Add(setting);
        ShowHost();
        UpdateDraft();
        var newRow = (Grid)SettingsPanel.Children[^1];
        newRow.BringIntoView();
        newRow.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(element =>
            element is TextBox or CheckBox or AutoCompleteBox)?.Focus();
    }

    private void SetValue(SshConfigDocument.Setting setting, string value)
    {
        setting.Value = value;
        UpdateDraft();
        UpdateMessages();
        UpdatePreview();
    }

    private void UpdateMessages()
    {
        if (HostList.SelectedItem is not SshConfigDocument.HostBlock host)
        {
            return;
        }

        foreach (var (setting, error, warning) in _settingMessages)
        {
            error.Text = !setting.IsRemoved && setting.IsChanged ? SshSettingValidation.Error(setting) : null;
            error.IsVisible = error.Text is not null;
            warning.Text = setting.IsRemoved ? null : SshSettingValidation.Warning(host, setting);
            warning.IsVisible = warning.Text is not null;
        }
    }

    private void UpdateDraft()
    {
        _dirty = _document is not null && (_document.Text != _savedText ||
            _document.Hosts.SelectMany(host => host.Settings).Any(setting => setting.IsChanged));
        SaveButton.IsEnabled = _dirty;
        StatusText.Text = _dirty ? "Unsaved changes" : "";
    }

    private void UpdatePreview()
    {
        ConfigPreview.Text = HostList.SelectedItem is SshConfigDocument.HostBlock host
            ? _document?.Render(host)
            : "";
    }

    private void SaveClicked(object? sender, RoutedEventArgs e)
    {
        if (_document is null || !ValidateDraft())
        {
            return;
        }

        try
        {
            var text = _document.Render();
            _file.Save(text);
            _savedText = text;
            var selectedIndex = SelectedHostIndex;
            _document = new SshConfigDocument(text);
            UpdateDraft();
            RefreshHosts(selectedIndex);
            StatusText.Text = "Saved to ~/.ssh/config";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusText.Text = $"Could not save the SSH config. {ex.Message}";
        }
    }

    private bool ValidateDraft()
    {
        if (_document is null)
        {
            return false;
        }

        foreach (var host in _document.Hosts)
        {
            var invalid = host.Settings.FirstOrDefault(setting =>
                !setting.IsRemoved && setting.IsChanged && SshSettingValidation.Error(setting) is not null);
            if (invalid is null)
            {
                continue;
            }

            if (!ReferenceEquals(host, HostList.SelectedItem))
            {
                HostSearch.Text = "";
                RefreshHosts(_document.Hosts.IndexOf(host));
            }
            SettingsPanel.Children[host.Settings.IndexOf(invalid)].BringIntoView();
            StatusText.Text = $"Check {invalid.Name} in {host.Pattern} before saving.";
            return false;
        }

        return true;
    }

    private async void OpenTerminalClicked(object? sender, RoutedEventArgs e)
    {
        if (HostList.SelectedItem is not SshConfigDocument.HostBlock host ||
            SshTerminal.GetAlias(host.Pattern) is not { } alias)
        {
            return;
        }

        if (!new SshConfigDocument(_savedText).Hosts.Any(saved => saved.Pattern == host.Pattern))
        {
            StatusText.Text = "Save this host before opening it in Terminal.";
            return;
        }

        try
        {
            await SshTerminal.OpenAsync(alias);
            StatusText.Text = _dirty ? $"Opened {alias} using the saved config. You have unsaved changes."
                : $"Opened {alias} in Terminal.";
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            StatusText.Text = $"Could not open Terminal. {ex.Message}";
        }
    }

    private async void AddHostClicked(object? sender, RoutedEventArgs e)
    {
        if (_document is null || !ValidateDraft())
        {
            return;
        }

        var dialog = new AddHostWindow(alias => _document.Hosts.Any(host =>
            Regex.Matches(host.Pattern, @"[^ \t""]+")
                .Any(token => token.Value.Equals(alias, StringComparison.OrdinalIgnoreCase))));
        var result = await dialog.ShowDialog<AddHostWindow.HostInput?>(this);
        if (result is null)
        {
            return;
        }

        _document = _document.AddHost(result.Alias, result.Hostname, result.User);
        HostSearch.Text = "";
        RefreshHosts(0);
        UpdateDraft();
    }

    private Task<bool> ConfirmDiscard()
    {
        var keepEditing = new Button { Content = "Keep Editing", IsDefault = true, IsCancel = true };
        var discard = new Button { Content = "Discard Changes" };
        var dialog = new Window
        {
            Title = "Unsaved Changes",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "Discard your unsaved SSH config changes?" },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { keepEditing, discard }
                    }
                }
            }
        };
        keepEditing.Click += (_, _) => dialog.Close(false);
        discard.Click += (_, _) => dialog.Close(true);
        return dialog.ShowDialog<bool>(this);
    }
}

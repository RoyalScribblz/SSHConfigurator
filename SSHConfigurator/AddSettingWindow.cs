using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace SSHConfigurator;

internal sealed class AddSettingWindow : Window
{
    public AddSettingWindow()
    {
        Title = "Add Setting";
        Width = 710;
        Height = 520;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var search = new TextBox
        {
            PlaceholderText = "Search by name or description",
            Classes = { "search-field" }
        };
        AutomationProperties.SetName(search, "Search SSH settings");

        var results = new ListBox
        {
            Classes = { "config-list", "setting-picker" },
            ItemTemplate = new FuncDataTemplate(item => item is SshSetting, (item, _) =>
            {
                if (item is not SshSetting setting)
                {
                    return null;
                }

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("210,*,Auto"), ColumnSpacing = 10 };
                var description = new TextBlock
                {
                    Text = setting.Description,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var help = CreateHelp(setting);
                Grid.SetColumn(description, 1);
                Grid.SetColumn(help, 2);
                row.Children.Add(new TextBlock { Text = setting.Name, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(description);
                row.Children.Add(help);
                return row;
            })
        };
        AutomationProperties.SetName(results, "SSH settings");
        ScrollViewer.SetHorizontalScrollBarVisibility(results, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var add = new Button { Content = "Add Setting", IsDefault = true, Classes = { "accent" } };
        var empty = new TextBlock
        {
            Text = "No matching settings",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        var manual = new HyperlinkButton
        {
            Content = "OpenSSH manual",
            NavigateUri = new Uri(SshSettingCatalog.ManualUrl),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Grid.SetColumn(cancel, 1);
        Grid.SetColumn(add, 2);
        footer.Children.Add(manual);
        footer.Children.Add(cancel);
        footer.Children.Add(add);

        var layout = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
        Grid.SetRow(results, 1);
        Grid.SetRow(empty, 1);
        Grid.SetRow(footer, 2);
        layout.Children.Add(search);
        layout.Children.Add(results);
        layout.Children.Add(empty);
        layout.Children.Add(footer);
        Content = layout;

        search.TextChanged += (_, _) => Filter();
        results.SelectionChanged += (_, _) => add.IsEnabled = results.SelectedItem is not null;
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && results.SelectedItem is not null)
            {
                results.Focus();
                e.Handled = true;
            }
        };
        Opened += (_, _) => search.Focus();
        cancel.Click += (_, _) => Close();
        add.Click += (_, _) =>
        {
            if (results.SelectedItem is SshSetting setting)
            {
                Close(setting);
            }
        };
        Filter();

        void Filter()
        {
            var query = search.Text?.Trim() ?? "";
            var matches = SshSettingCatalog.All.Where(setting => setting.IsAvailableOnCurrentPlatform &&
                (setting.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 setting.Description.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(setting => setting.Name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0 :
                    setting.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 :
                    setting.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : 3)
                .ToArray();
            results.ItemsSource = matches;
            results.SelectedIndex = matches.Length == 0 ? -1 : 0;
            empty.IsVisible = matches.Length == 0;
            add.IsEnabled = results.SelectedItem is not null;
        }
    }

    internal static Button CreateHelp(SshSetting? setting, string? name = null)
    {
        var label = setting?.Name ?? name;
        var description = setting?.Description ?? "This directive is available as an editable OpenSSH value.";
        var help = new Button
        {
            Content = "ⓘ",
            Padding = new Thickness(6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { "setting-help" },
            Flyout = new Flyout
            {
                Content = new StackPanel
                {
                    MaxWidth = 320,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = label, FontWeight = FontWeight.Bold },
                        new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap },
                        new HyperlinkButton
                        {
                            Content = "Documentation",
                            NavigateUri = new Uri(setting?.DocumentationUrl ?? SshSettingCatalog.ManualUrl)
                        }
                    }
                }
            }
        };
        AutomationProperties.SetName(help, $"Help for {label}");
        ToolTip.SetTip(help, new TextBlock { Text = description, MaxWidth = 320, TextWrapping = TextWrapping.Wrap });
        return help;
    }
}

using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SSHConfigurator;

internal sealed class AddHostWindow : Window
{
    public AddHostWindow(Func<string, bool> aliasExists)
    {
        Title = "Add Host";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var alias = new TextBox();
        var hostname = new TextBox();
        var user = new TextBox();
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var add = new Button { Content = "Add Host", IsDefault = true, Classes = { "accent" } };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                Field("Alias", alias),
                Field("Hostname", hostname),
                Field("User (optional)", user),
                error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, add }
                }
            }
        };

        Opened += (_, _) => alias.Focus();
        cancel.Click += (_, _) => Close();
        add.Click += (_, _) =>
        {
            var input = new HostInput(alias.Text?.Trim() ?? "", hostname.Text?.Trim() ?? "", user.Text?.Trim() ?? "");
            error.Text = Validate(input, aliasExists);
            error.IsVisible = error.Text is not null;
            if (!error.IsVisible)
            {
                Close(input);
            }
        };
    }

    private static StackPanel Field(string label, TextBox field)
    {
        AutomationProperties.SetName(field, label);
        return new StackPanel
        {
            Spacing = 4,
            Children = { new TextBlock { Text = label }, field }
        };
    }

    private static string? Validate(HostInput input, Func<string, bool> aliasExists)
    {
        if (!Regex.IsMatch(input.Alias, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\z"))
        {
            return "Enter an alias using letters, numbers, dots, underscores, or hyphens.";
        }

        if (aliasExists(input.Alias))
        {
            return "This alias already appears in the config.";
        }

        if (!Regex.IsMatch(input.Hostname, @"\A[A-Za-z0-9_.:%-]+\z"))
        {
            return "Enter a hostname or IP address.";
        }

        if (input.User.Length > 0 && !Regex.IsMatch(input.User, @"\A[^\s\p{C}#""'\\=]+\z"))
        {
            return "Enter a user name without whitespace or config punctuation.";
        }

        return null;
    }

    internal sealed record HostInput(string Alias, string Hostname, string User);
}

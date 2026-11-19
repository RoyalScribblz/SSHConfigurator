using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.TextInput;

namespace SSHConfigurator;

internal sealed class IntegerTextBox : TextBox
{
    private readonly SshInteger _integer;

    public IntegerTextBox(SshInteger integer)
    {
        _integer = integer;
        TextInputOptions.SetContentType(this, TextInputContentType.Digits);
        PlaceholderText = integer.IsOctal ? "Octal mask" : "Whole number";
        PastingFromClipboard += async (_, e) =>
        {
            e.Handled = true;
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard || IsReadOnly)
            {
                return;
            }

            string? text;
            try
            {
                text = (await clipboard.TryGetTextAsync())?.Trim();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                return;
            }

            if (!string.IsNullOrEmpty(text) && _integer.AcceptsDigits(text))
            {
                RaiseEvent(new TextInputEventArgs { RoutedEvent = TextInputEvent, Text = text });
            }
        };
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (e.Text is { } text && !_integer.AcceptsDigits(text))
        {
            e.Handled = true;
            return;
        }

        base.OnTextInput(e);
    }
}

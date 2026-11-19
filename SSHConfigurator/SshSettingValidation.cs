using System.Text;
using System.Text.RegularExpressions;

namespace SSHConfigurator;

internal static class SshSettingValidation
{
    public static string? Error(SshConfigDocument.Setting setting)
    {
        if (string.IsNullOrWhiteSpace(setting.Value))
        {
            return "Enter a value for this setting.";
        }

        if (!SshConfigDocument.IsValidValue(setting.Name, setting.Value))
        {
            return "Enter a single-line value with balanced quotes and no inline comment.";
        }

        var metadata = SshSettingCatalog.Find(setting.Name);
        var arguments = ReadArguments(setting.Value);
        if (metadata is { IsBoolean: true } && !IsChoice(arguments, ["yes", "no"]))
        {
            return "Choose yes or no.";
        }

        if (metadata is { Choices.Length: > 0 } && !IsChoice(arguments, metadata.Choices))
        {
            return "Choose one of the values in the dropdown.";
        }

        if (metadata?.Integer is { } integer && (arguments.Count != 1 || !integer.IsValid(arguments[0])))
        {
            return integer.ErrorMessage;
        }

        if (metadata is { IsFile: true } &&
            ((!metadata.MultiplePaths && arguments.Count != 1) || arguments.Any(path => !IsPath(path, metadata))))
        {
            return metadata.MultiplePaths
                ? "Enter file paths. Put quotes around paths that contain spaces."
                : "Enter one file path. Put quotes around a path that contains spaces.";
        }

        return null;
    }

    public static string? Warning(SshConfigDocument.HostBlock host, SshConfigDocument.Setting setting)
    {
        var metadata = SshSettingCatalog.Find(setting.Name);
        if (metadata?.Name == "EnableSSHKeysign")
        {
            return "OpenSSH only enables this setting in the system-wide client config.";
        }

        if (metadata is { IsAvailableOnCurrentPlatform: false })
        {
            return "UseKeychain requires Apple’s OpenSSH.";
        }

        var first = host.Settings.First(candidate => !candidate.IsRemoved &&
            candidate.Name.Equals(setting.Name, StringComparison.OrdinalIgnoreCase));
        if (metadata is { Repeatable: false } && !ReferenceEquals(first, setting))
        {
            return "An earlier value for this setting takes precedence.";
        }

        if (metadata?.Name is "ProxyCommand" or "ProxyJump")
        {
            var firstProxy = host.Settings.First(candidate => !candidate.IsRemoved &&
                SshSettingCatalog.Find(candidate.Name)?.Name is "ProxyCommand" or "ProxyJump");
            if (!ReferenceEquals(firstProxy, setting) && !firstProxy.Name.Equals(setting.Name, StringComparison.OrdinalIgnoreCase))
            {
                return $"The earlier {firstProxy.Name} takes precedence.";
            }
        }

        if (metadata?.Name is "LocalForward" or "RemoteForward" or "DynamicForward" &&
            host.Settings.FirstOrDefault(candidate => !candidate.IsRemoved &&
                candidate.Name.Equals("ClearAllForwardings", StringComparison.OrdinalIgnoreCase)) is { } clear &&
            IsChoice(ReadArguments(clear.Value), ["yes"]))
        {
            return "ClearAllForwardings cancels this forward.";
        }

        return null;
    }

    public static string QuotePath(string path) =>
        $"\"{path.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    public static IReadOnlyList<string> ReadArguments(string value)
    {
        var arguments = new List<string>();
        var token = new StringBuilder();
        var quote = '\0';
        var started = false;
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (quote == '\0' && character is ' ' or '\t')
            {
                if (started)
                {
                    arguments.Add(token.ToString());
                    token.Clear();
                    started = false;
                }
                continue;
            }

            started = true;
            if (character == '\\' && i + 1 < value.Length &&
                (value[i + 1] is '\\' or '\'' or '"' || (quote == '\0' && value[i + 1] == ' ')))
            {
                token.Append(value[++i]);
            }
            else if (quote == character)
            {
                quote = '\0';
            }
            else if (quote == '\0' && character is '\'' or '"')
            {
                quote = character;
            }
            else
            {
                token.Append(character);
            }
        }

        if (started)
        {
            arguments.Add(token.ToString());
        }
        return arguments;
    }

    private static bool IsChoice(IReadOnlyList<string> parts, string[] choices) =>
        parts.Count == 1 && choices.Contains(parts[0], StringComparer.OrdinalIgnoreCase);

    private static bool IsPath(string path, SshSetting setting)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) ||
            path is "." or ".." or "~" || path.EndsWith('/') ||
            (OperatingSystem.IsWindows() && path.EndsWith('\\')))
        {
            return false;
        }

        // SSH paths may include tokens, globs, or a relative path resolved when ssh runs.
        // Validate syntax locally without requiring the file to exist on this machine.
        var windowsPath = OperatingSystem.IsWindows() || Regex.IsMatch(path, @"^[a-zA-Z]:[\\/]");
        return !windowsPath || setting.Name == "Include" ||
            !Regex.Replace(path, @"\$\{[^}]+\}|^\p{L}:", "").Any(c => c is '<' or '>' or ':' or '"' or '|' or '?' or '*');
    }
}

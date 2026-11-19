using System.Text.RegularExpressions;

namespace SSHConfigurator;

internal sealed class SshConfigDocument
{
    private static readonly Regex Directive = new(
        @"^[ \t]*(?<name>[A-Za-z][A-Za-z0-9]*)(?:[ \t]*=[ \t]*|[ \t]+|$)");
    private static readonly Regex Lines = new(@"[^\r\n]+|\r\n|\r|\n");

    public SshConfigDocument(string text)
    {
        Text = text;
        HostBlock? current = null;

        foreach (Match line in Lines.Matches(text))
        {
            var directive = ReadDirective(line.Value);
            if (directive is null)
            {
                continue;
            }

            var (name, start, length) = directive.Value;
            if (name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Match", StringComparison.OrdinalIgnoreCase))
            {
                FirstSectionStart ??= line.Index;
                if (current is not null)
                {
                    current.End = line.Index;
                }

                current = name.Equals("Host", StringComparison.OrdinalIgnoreCase) && length > 0
                    ? new HostBlock(line.Value.Substring(start, length), line.Index, line.Index + line.Length, text.Length)
                    : null;
                if (current is not null)
                {
                    Hosts.Add(current);
                }
            }
            else if (current is not null && length > 0)
            {
                var source = new SettingSource(line.Index + start, line.Index, line.Index + line.Length);
                current.Settings.Add(new Setting(name, source, line.Value.Substring(start, length)));
            }
        }
    }

    public string Text { get; }
    public List<HostBlock> Hosts { get; } = [];
    private int? FirstSectionStart { get; }
    private string NewLine => Regex.Match(Text, @"\r\n|\r|\n") is { Success: true } match
        ? match.Value : Environment.NewLine;

    public string Render() => RenderRange(0, Text.Length, Hosts);

    public string Render(HostBlock host) => RenderRange(host.Start, host.End, [host]);

    public SshConfigDocument AddHost(string alias, string hostname, string user)
    {
        var text = Text;
        var offset = FirstSectionStart ?? text.Length;
        var eol = NewLine;
        var prefix = offset > 0 && text[offset - 1] is not ('\r' or '\n') ? eol : "";
        var userLine = user.Length > 0 ? $"    User {user}{eol}" : "";
        var block = $"{prefix}Host {alias}{eol}    HostName {hostname}{eol}{userLine}{eol}";
        var updated = new SshConfigDocument(text.Insert(offset, block));
        for (var hostIndex = 0; hostIndex < Hosts.Count; hostIndex++)
        {
            var settings = updated.Hosts[hostIndex + 1].Settings;
            var index = 0;
            foreach (var setting in Hosts[hostIndex].Settings)
            {
                if (setting.IsNew)
                {
                    settings.Add(setting);
                    continue;
                }

                var target = settings[index++];
                target.Value = setting.Value;
                target.IsRemoved = setting.IsRemoved;
            }
        }
        return updated;
    }

    public static bool IsValidValue(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(c => char.IsControl(c) && c != '\t'))
        {
            return false;
        }

        var line = $"{name} {value}";
        return ReadDirective(line) is { Length: > 0 } directive &&
            line.AsSpan(directive.Start + directive.Length).Trim().IsEmpty &&
            !value.TrimStart(' ', '\t').StartsWith('=');
    }

    private string RenderRange(int start, int end, IEnumerable<HostBlock> hosts)
    {
        var result = new System.Text.StringBuilder(Text[start..end]);
        var eol = NewLine;
        foreach (var host in hosts.Reverse())
        {
            var additions = host.Settings.Where(setting => setting.IsNew && !setting.IsRemoved)
                .Select(setting => $"{eol}    {setting.Name} {setting.Value}");
            var insertion = host.Settings.LastOrDefault(setting => !setting.IsNew && !setting.IsRemoved)
                ?.Source?.LineEnd ?? host.HeaderEnd;
            var changes = new List<(int Offset, int Length, string Replacement)>
            {
                (insertion, 0, string.Concat(additions))
            };

            foreach (var setting in host.Settings)
            {
                if (setting.Source is not { } source || !setting.IsChanged)
                {
                    continue;
                }

                changes.Add(setting.IsRemoved
                    ? (source.LineStart, EndOfLine(source.LineEnd) - source.LineStart, "")
                    : (source.ValueStart, setting.OriginalValue.Length, setting.Value));
            }

            foreach (var change in changes.OrderByDescending(change => change.Offset))
            {
                result.Remove(change.Offset - start, change.Length);
                result.Insert(change.Offset - start, change.Replacement);
            }
        }

        return result.ToString();
    }

    private int EndOfLine(int offset) => Text.AsSpan(offset) switch
    {
        ['\r', '\n', ..] => offset + 2,
        ['\r' or '\n', ..] => offset + 1,
        _ => offset
    };

    private static (string Name, int Start, int Length)? ReadDirective(string line)
    {
        var match = Directive.Match(line);
        if (!match.Success)
        {
            return null;
        }

        var quote = '\0';
        var end = match.Length;
        var tokenStarted = false;
        for (var index = match.Length; index < line.Length; index++)
        {
            var c = line[index];
            if (quote == '\0' && c is ' ' or '\t')
            {
                tokenStarted = false;
                continue;
            }

            if (!tokenStarted && c == '#')
            {
                break;
            }

            if (c == '\\' && index + 1 < line.Length &&
                (line[index + 1] is '\\' or '"' or '\'' || (quote == '\0' && line[index + 1] == ' ')))
            {
                index++;
            }
            else if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }

            tokenStarted = true;
            end = index + 1;
        }

        if (quote != '\0')
        {
            return null;
        }

        return (match.Groups["name"].Value, match.Length, end - match.Length);
    }

    internal sealed class HostBlock(string pattern, int start, int headerEnd, int end)
    {
        public string Pattern { get; } = pattern;
        public int Start { get; } = start;
        public int End { get; set; } = end;
        public int HeaderEnd { get; } = headerEnd;
        public List<Setting> Settings { get; } = [];
        public override string ToString() => Pattern;
    }

    internal readonly record struct SettingSource(int ValueStart, int LineStart, int LineEnd);

    internal sealed class Setting(string name, SettingSource? source, string originalValue)
    {
        public string Name { get; } = name;
        public SettingSource? Source { get; } = source;
        public string OriginalValue { get; } = originalValue;
        public string Value { get; set; } = originalValue;
        public bool IsRemoved { get; set; }
        public bool IsNew => Source is null;
        public bool IsChanged => IsRemoved ? !IsNew : IsNew || Value != OriginalValue;
    }
}

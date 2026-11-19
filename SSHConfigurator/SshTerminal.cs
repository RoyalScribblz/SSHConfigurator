using System.ComponentModel;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Text.RegularExpressions;

namespace SSHConfigurator;

internal static class SshTerminal
{
    private static readonly Regex LiteralAlias = new(@"\A(?:[A-Za-z0-9_][A-Za-z0-9_.:-]*|::[A-Fa-f0-9:]*)\z");

    public static bool IsSupported => OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

    public static string? GetAlias(string hostPattern)
    {
        var patterns = SshSettingValidation.ReadArguments(hostPattern);
        return patterns.FirstOrDefault(alias => LiteralAlias.IsMatch(alias) && !patterns.Any(pattern =>
            pattern.StartsWith('!') && FileSystemName.MatchesSimpleExpression(pattern.AsSpan(1), alias)));
    }

    public static async Task OpenAsync(string alias)
    {
        if (!LiteralAlias.IsMatch(alias))
        {
            throw new ArgumentException("Select a literal SSH host alias.");
        }

        if (OperatingSystem.IsMacOS())
        {
            await OpenMacTerminalAsync(alias);
        }
        else if (OperatingSystem.IsWindows())
        {
            OpenWindowsTerminal(alias);
        }
        else
        {
            throw new PlatformNotSupportedException("Opening a terminal is available on macOS and Windows.");
        }
    }

    private static async Task OpenMacTerminalAsync(string alias)
    {
        const string script = """
            on run argv
                set sshCommand to "ssh -- " & quoted form of (item 1 of argv)
                tell application id "com.apple.Terminal"
                    activate
                    do script sshCommand
                end tell
            end run
            """;
        var start = new ProcessStartInfo("/usr/bin/osascript")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(alias);

        using var process = Process.Start(start) ?? throw new IOException("Could not start Terminal.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await output;
        if (process.ExitCode != 0)
        {
            throw new IOException((await error).Trim());
        }

        await error;
    }

    private static void OpenWindowsTerminal(string alias)
    {
        // Keep the shell open after ssh terminates so connection errors remain visible.
        string[] command = ["cmd.exe", "/d", "/k", "ssh.exe", "--", alias];
        try
        {
            StartWindowsProcess("wt.exe", ["-w", "new", "new-tab", .. command]);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            StartWindowsProcess(command[0], command[1..]);
        }
    }

    private static void StartWindowsProcess(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start);
    }
}

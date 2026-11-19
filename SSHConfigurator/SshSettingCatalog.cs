namespace SSHConfigurator;

internal static class SshSettingCatalog
{
    public const string ManualUrl = "https://man.openbsd.org/ssh_config";
    public const string AppleUrl = "https://developer.apple.com/library/archive/technotes/tn2449/_index.html";

    public static IReadOnlyList<SshSetting> All { get; } = CreateCatalog();
    private static readonly Dictionary<string, SshSetting> ByName =
        All.ToDictionary(setting => setting.Name, StringComparer.OrdinalIgnoreCase);

    public static SshSetting? Find(string name) => ByName.GetValueOrDefault(name);

    private static SshSetting[] CreateCatalog() =>
        Data.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            var parts = line.Split('|');
            var flags = parts.ElementAtOrDefault(2) ?? "";
            return new SshSetting(parts[0], parts[1], flags.Contains('b'), flags.Contains('f'),
                flags.Contains('m'), flags.Contains('r'), flags.Contains('o'),
                parts.ElementAtOrDefault(3)?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [],
                parts.ElementAtOrDefault(4));
        }).OrderBy(setting => setting.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    // Flags: b = boolean, f = file, m = multiple paths, r = repeatable, o = output path.
    // Descriptions are paraphrased from ssh_config(5), with Apple's UseKeychain extension.
    private const string Data = """
        AddKeysToAgent|Load file-backed keys into the running agent. You can also specify a lifetime or use confirm with a lifetime.
        AddressFamily|Choose IPv4, IPv6, or either for outgoing connections.||any inet inet6
        BatchMode|Disable interactive prompts for unattended SSH jobs.|b
        BindAddress|Set the local source address used to connect.
        BindInterface|Use the address of a particular local network interface.
        CanonicalDomains|List domain suffixes to try during hostname canonicalization.
        CanonicalizeFallbackLocal|Allow normal resolver lookup when the canonical domains produce no match.|b
        CanonicalizeHostname|Control SSH's hostname rewriting before the connection.||no yes always
        CanonicalizeMaxDots|Limit how many dots a hostname can contain before canonicalization is skipped.
        CanonicalizePermittedCNAMEs|List source and target patterns for CNAMEs that SSH may follow.
        CASignatureAlgorithms|Set the accepted algorithms for certificate authority signatures.
        CertificateFile|Load a public certificate for a matching authentication key. Multiple entries are allowed.|fr
        ChannelTimeout|Set inactivity limits for channels, using channel-type and interval pairs.
        CheckHostIP|Check the server IP address in the host key database as well as its hostname.|b
        Ciphers|Choose which encryption algorithms the client offers.
        ClearAllForwardings|Clear configured local, remote, and dynamic port forwards.|b
        Compression|Compress data on the SSH connection.|b
        ConnectionAttempts|Limit how many times SSH tries to connect.
        ConnectTimeout|Set the time limit for connecting and completing the initial SSH handshake.
        ControlMaster|Choose whether this session can share or create a master connection.||no yes ask auto autoask
        ControlPath|Set the socket path used to share a connection. SSH tokens such as %C are supported.|fo
        ControlPersist|Keep the master connection in the background. Use yes, no, or an idle timeout.
        DynamicForward|Listen locally as a SOCKS proxy and forward through SSH.|r
        EnableEscapeCommandline|Allow opening the forwarding command prompt with the SSH escape sequence.|b
        EnableSSHKeysign|Allow the SSH keysign helper for host-based authentication. Set this in the system-wide client config.|b
        EscapeChar|Set the interactive escape character, use caret notation, or enter none.
        ExitOnForwardFailure|Exit if SSH cannot set up a requested forward.|b
        FingerprintHash|Choose how SSH displays key fingerprints.||sha256 md5
        ForkAfterAuthentication|Move SSH to the background after authentication.|b
        ForwardAgent|Forward the authentication agent. Accepts yes, no, a socket path, or an environment variable.
        ForwardX11|Forward X11 display connections through SSH.|b
        ForwardX11Timeout|Limit how long untrusted X11 forwarding remains available.
        ForwardX11Trusted|Give remote X11 clients trusted access to the local display.|b
        GatewayPorts|Allow other machines to reach locally forwarded listening ports.|b
        GlobalKnownHostsFile|List the system-wide files to consult for trusted host keys.|fm
        GSSAPIAuthentication|Allow authentication with GSSAPI credentials where the client supports it.|b
        GSSAPIDelegateCredentials|Delegate GSSAPI credentials to the server where supported.|b
        HashKnownHosts|Hash hostnames and addresses when writing new known-host entries.|b
        HostbasedAcceptedAlgorithms|Set the signature algorithms offered for host-based authentication.
        HostbasedAuthentication|Allow authentication based on credentials for the client host.|b
        HostKeyAlgorithms|Set the preferred algorithms for server host keys.
        HostKeyAlias|Use a different name to look up and store the server's host key.
        HostName|Set the actual hostname or IP address behind this SSH alias.
        IdentitiesOnly|Use configured identities even when the agent offers other keys.|b
        IdentityAgent|Choose the authentication agent socket, use SSH_AUTH_SOCK, or enter none.
        IdentityFile|Choose a private key or a public key backed by an agent. Relative paths, home paths, and SSH tokens are allowed. Multiple entries are allowed.|fr
        IgnoreUnknown|Ignore matching unsupported keywords encountered after this directive.
        Include|Read additional config files. Relative patterns are based in ~/.ssh for user configs. Globs are allowed.|fmr
        IPQoS|Set the IP traffic class for interactive and non-interactive traffic.
        KbdInteractiveAuthentication|Allow keyboard-interactive authentication.|b
        KbdInteractiveDevices|Set the preferred keyboard-interactive authentication mechanisms.
        KexAlgorithms|Choose which key exchange algorithms the client offers.
        KnownHostsCommand|Run a command to supply extra host keys alongside known-host files.
        LocalCommand|Set a local command to run after connecting, when PermitLocalCommand allows it.
        LocalForward|Forward a local port or socket to a destination through the server.|r
        LogLevel|Set how much diagnostic output SSH emits.||QUIET FATAL ERROR INFO VERBOSE DEBUG DEBUG1 DEBUG2 DEBUG3
        LogVerbose|Force verbose logging for selected source locations.
        MACs|Choose message authentication algorithms offered by the client.
        NoHostAuthenticationForLocalhost|Skip host authentication checks for loopback connections.|b
        NumberOfPasswordPrompts|Limit the number of password prompts before SSH gives up.
        ObscureKeystrokeTiming|Mask interactive typing timing. Use yes, no, or an interval in milliseconds.
        PasswordAuthentication|Allow password authentication.|b
        PermitLocalCommand|Allow SSH to run the configured LocalCommand.|b
        PermitRemoteOpen|Restrict destinations reachable through remote SOCKS forwarding.
        PKCS11Provider|Set the library used to access keys on a PKCS#11 device.|f
        Port|Set the SSH server port, from 1 to 65535.
        PreferredAuthentications|Set the order in which authentication methods are tried.
        ProxyCommand|Connect to the server using a helper command. The first of ProxyCommand and ProxyJump takes precedence.
        ProxyJump|Connect through one or more SSH jump hosts. The first of ProxyJump and ProxyCommand takes precedence.
        ProxyUseFdpass|Expect the proxy command to hand back a connected descriptor.|b
        PubkeyAcceptedAlgorithms|Set the signatures the client may use for public-key authentication.
        PubkeyAuthentication|Enable public-key authentication or select its host-bound mode.||yes no unbound host-bound
        RefuseConnection|Stop before connecting and display a configurable reason.
        RekeyLimit|Set the data and optional time limits before keys are renegotiated.
        RemoteCommand|Set the command to run on the remote host after connecting.
        RemoteForward|Listen on the remote side and forward traffic over SSH.|r
        RequestTTY|Choose whether to allocate a terminal on the server.||no yes force auto
        RequiredRSASize|Set the minimum acceptable RSA key size.
        RevokedHostKeys|Read host keys that must be rejected from a file or key revocation list.|f
        SecurityKeyProvider|Choose a FIDO provider library, an environment variable, or the built-in provider.|f
        SendEnv|Name local environment variables to send to a server that accepts them.|r
        ServerAliveCountMax|Limit unanswered SSH keepalives before disconnecting.
        ServerAliveInterval|Set the idle time before an encrypted SSH keepalive is sent.
        SessionType|Choose a normal session, a subsystem, or a connection without a remote session.||default subsystem none
        SetEnv|Set environment variables to send to a server that permits them.|r
        StdinNull|Read standard input from the null device.|b
        StreamLocalBindMask|Set the octal permissions mask for newly created forwarding sockets.
        StreamLocalBindUnlink|Remove an existing socket before binding a forwarding socket.|b
        StrictHostKeyChecking|Choose how SSH handles new and changed host keys.||yes no off ask accept-new
        SyslogFacility|Choose the logging facility used by SSH.||DAEMON USER AUTH LOCAL0 LOCAL1 LOCAL2 LOCAL3 LOCAL4 LOCAL5 LOCAL6 LOCAL7
        TCPKeepAlive|Choose where to enable operating-system TCP keepalive messages.||transport yes all no
        Tag|Set a label that can be used by Match tagged.
        Tunnel|Choose whether to forward a tunnel device and which network layer to use.||yes no point-to-point ethernet
        TunnelDevice|Select the local and remote tunnel device numbers.
        UpdateHostKeys|Choose whether to learn additional server host keys after authentication.||yes no ask
        UseKeychain|Apple's OpenSSH extension for storing key passphrases in the macOS Keychain.|b||apple
        User|Set the account name used to sign in on the server.
        UserKnownHostsFile|List per-user host key databases. Use none to skip reading them.|fm
        VerifyHostKeyDNS|Use DNS SSHFP records when verifying server host keys.||yes no ask
        VersionAddendum|Append text to the outgoing SSH protocol identification.
        VisualHostKey|Show an ASCII fingerprint image for the server key.|b
        WarnWeakCrypto|Warn when the connection negotiates cryptography that OpenSSH considers weak.|b
        XAuthLocation|Set the path to the local xauth executable.|f
        """;
}

internal sealed record SshSetting(string Name, string Description, bool IsBoolean, bool IsFile,
    bool MultiplePaths, bool Repeatable, bool OutputPath, string[] Choices, string? Source)
{
    public bool IsAvailableOnCurrentPlatform => Source != "apple" || OperatingSystem.IsMacOS();

    public SshInteger? Integer => Name switch
    {
        "Port" => new(1, 65535),
        "RequiredRSASize" => new(1024, int.MaxValue),
        "CanonicalizeMaxDots" or "ConnectionAttempts" or "NumberOfPasswordPrompts" or "ServerAliveCountMax"
            => new(0, int.MaxValue),
        "StreamLocalBindMask" => new(0, 511, IsOctal: true),
        _ => null
    };

    public string DocumentationUrl => Source == "apple" ? SshSettingCatalog.AppleUrl :
        $"{SshSettingCatalog.ManualUrl}#{(Name == "HostName" ? "Hostname" : Name)}";
}

internal readonly record struct SshInteger(int Minimum, int Maximum, bool IsOctal = false)
{
    public bool AcceptsDigits(string text) => text.AsSpan().IndexOfAnyExceptInRange('0', IsOctal ? '7' : '9') < 0;

    public bool IsValid(string text)
    {
        if (text.Length == 0 || !AcceptsDigits(text))
        {
            return false;
        }

        var value = 0;
        var radix = IsOctal ? 8 : 10;
        foreach (var digit in text)
        {
            var next = (long)value * radix + digit - '0';
            if (next > Maximum)
            {
                return false;
            }
            value = (int)next;
        }

        return value >= Minimum;
    }

    public string ErrorMessage => IsOctal ? "Enter an octal mask from 000 to 777." :
        $"Enter a whole number from {Minimum} to {Maximum}.";
}

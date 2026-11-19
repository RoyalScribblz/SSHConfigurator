# SSH Configurator

SSH Configurator is an Avalonia desktop app for editing your OpenSSH client configuration on macOS and Windows. It works with the config OpenSSH already uses, including `~/.ssh/config` on macOS and `%USERPROFILE%\.ssh\config` on Windows.

- Search host aliases, add hosts, and edit settings with inline help and validation.
- Preview the selected host's config block before saving. Setting removals can be undone before you save.
- Browse your private keys, copy their paths, and create Ed25519, RSA, or ECDSA key pairs with `ssh-keygen`.
- Open a saved host alias in Terminal on macOS or Windows Terminal on Windows.

## Run from source

Install the .NET 10 SDK. Key generation also requires the OpenSSH client with `ssh-keygen` on your PATH.

```sh
dotnet run --project SSHConfigurator
```

You can also open `SSHConfigurator.slnx` in Rider and run the `SSHConfigurator` project.

## Publish a desktop build

For Apple silicon Macs, run the following command. Use `osx-x64` for Intel Macs.

```sh
dotnet publish SSHConfigurator -c Release -r osx-arm64 --self-contained true
```

Open `SSH Configurator.app` in the publish folder printed by the command.

For x64 Windows, run the following command. Use `win-arm64` for ARM64 Windows.

```sh
dotnet publish SSHConfigurator -c Release -r win-x64 --self-contained true
```

Run `SSHConfigurator.exe` from the publish folder. Keep its companion files alongside it.

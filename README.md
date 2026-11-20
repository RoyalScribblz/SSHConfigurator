# SSH Configurator

Manage your SSH hosts and keys from a desktop app for macOS and Windows. SSH Configurator edits the same per-user config file that OpenSSH reads, so changes you save here also work when you run `ssh` yourself.

## Download and open

Get the ZIP for your machine from [GitHub Releases](https://github.com/RoyalScribblz/SSHConfigurator/releases/latest). The download includes the .NET runtime.

| Your machine | ZIP name ends with | After extracting |
| --- | --- | --- |
| Mac with Apple silicon | `osx-arm64.zip` | Open **SSH Configurator.app** |
| Intel Mac | `osx-x64.zip` | Open **SSH Configurator.app** |
| Windows PC with Intel or AMD | `win-x64.zip` | Run **SSHConfigurator.exe** |
| Windows PC with ARM | `win-arm64.zip` | Run **SSHConfigurator.exe** |

On Windows, keep the extracted files together.

### First launch on macOS

The app is ad hoc signed and unnotarized. If macOS blocks the first launch and you trust the download, follow these steps.

1. Click **Done** in the warning dialog.
2. Open **System Settings → Privacy & Security** and scroll down to **Security**.
3. Click **Open Anyway** next to the message about SSH Configurator. Confirm and authenticate if prompted.

The button is available for about an hour after a blocked launch. If it’s missing, try opening the app again, click **Done**, and return to **Privacy & Security**. The exception applies only to SSH Configurator. [Apple’s macOS guide](https://support.apple.com/en-gb/guide/mac-help/mh40617/mac)

## Use the app

1. Select an existing host or click **Add Host** to give a new connection a memorable SSH alias.
2. Edit its settings or use **Add Setting** to search the available OpenSSH options. Expand **View config block** to see the exact changes before saving.
3. Click **Save Changes** to write your config, then **Open in Terminal** to connect using the saved alias.

Use **Manage SSH Keys** to copy a private key’s path for an `IdentityFile` setting or generate a new Ed25519, RSA, or ECDSA key pair. Key generation uses the `ssh-keygen` command from your local OpenSSH installation.

## Run from source

With the .NET 10 SDK installed, run `dotnet run --project SSHConfigurator` from the repository root.

## License

SSH Configurator is licensed under the [MIT License](LICENSE). You can use, modify, and redistribute it, including commercially, as long as you retain the copyright and license notices. The software comes as is, without warranty.

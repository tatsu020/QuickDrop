# QuickDrop

QuickDrop is a Windows-to-Windows file sender for Explorer. Keep QuickDrop running on each PC, right-click files or folders, choose `ファイルを送信`, then choose a detected destination. The receiver saves incoming items into that user's actual Windows Downloads folder, or into the custom receive folder selected in QuickDrop settings.

## Features

- Windows tray app with a lightweight TCP receiver.
- Automatic peer discovery for PCs where QuickDrop is currently running.
- LAN discovery by UDP broadcast.
- Tailscale discovery by probing online Tailscale IPv4 peers from `tailscale status --json`.
- Windows 11 modern Explorer context menu entry implemented as a native `IExplorerCommand` shell extension registered through a sparse MSIX identity package.
- Multiple selected files/folders are sent together.
- Folder selections are recursively packaged and restored as folders.
- Single files are saved as files; single folders are saved as folders; multiple selections are saved under `Downloads\QuickDrop-yyyyMMdd-HHmmss`.
- Filename/folder collisions are resolved with `(2)`, `(3)`, and so on.
- Received files and folders get the receive-time modified timestamp so new transfers stay visible at the top of Explorer's date-sorted views.

## Build

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1
```

The distributable app is written to:

```text
C:\dev\QuickDrop\dist\QuickDrop
```

By default the build is self-contained for `win-x64`, so the target PC does not need a separate .NET runtime.

## Install

Copy the `dist\QuickDrop` folder to each Windows PC, then double-click:

```text
Install-QuickDrop.cmd
```

The installer registers the Windows 11 Explorer menu, adds the startup entry, starts QuickDrop, and restarts Explorer so the menu appears. It also asks Windows for elevation through UAC when firewall rules are needed. You do not need to open an administrator PowerShell window manually.

The installer window stays open until you press a key, shows each step, and displays a completion or error dialog. A detailed log is written to `QuickDrop.install.log` in the install folder.

Windows 11's standard context menu requires package registration. The build creates `QuickDrop.Sparse.msix` and `QuickDrop.Sparse.cer`; the installer imports the public certificate for the current user, registers the sparse package with the install folder as the external location, and removes any old classic-menu registration.

If you do not want firewall rules to be added, run `Install-QuickDrop.ps1` directly without `-AddFirewallRules`.

## Use

1. Start QuickDrop on both PCs.
2. Wait a few seconds for peers to appear in the QuickDrop dashboard.
3. In Explorer, right-click one or more files/folders.
4. Choose `ファイルを送信`.
5. Choose the destination PC.

The sender starts immediately after you choose the destination. The receiver writes the result to the detected or configured receive folder.

The `ファイルを送信` submenu shows only PCs whose QuickDrop receiver has been detected as running. If no LAN, Tailscale, or manually added IP has responded yet, QuickDrop opens the app instead so you can check discovery and settings.

## Tray Settings

Right-click the QuickDrop tray icon to change common settings:

- `PC起動時に自動実行`: toggle the current user's Windows startup entry.
- `保存先フォルダー設定`: view the current receive folder, choose a custom receive folder, or return to automatic Windows Downloads detection.
- `送信先IPを追加...`: add a fixed IP address or host name to probe directly.
- `登録済み送信先IP`: enable, disable, or remove manually added destinations.

Manual destinations are probed automatically. If QuickDrop is running on that IP and the TCP receiver port is reachable, the PC appears in the Explorer `ファイルを送信` menu as a `Manual` destination. When a manual probe reaches another QuickDrop PC, that receiver also learns the sender as a `Direct` peer, so return transfers can work as long as both receiver ports are reachable.

## Ports

- TCP `48947`: file receive and Tailscale probe.
- UDP `48948`: LAN discovery beacon.

## Uninstall

From the install folder:

```text
Uninstall-QuickDrop.cmd
```

The uninstaller also self-elevates through UAC when firewall rule removal is needed.

The uninstaller writes `QuickDrop.uninstall.log` in the install folder and keeps its window open until you press a key.

## Notes

- The Explorer shell extension reads the live peer cache at `%LOCALAPPDATA%\QuickDrop\peers-menu.tsv`.
- The tray app updates that cache only with peers whose QuickDrop receiver was detected as running.
- The current default accepts incoming QuickDrop transfers while the app is running. Use the dashboard's `受信を許可` toggle to pause receiving.
- Tailscale support requires the Tailscale CLI to be installed and logged in.
- Windows 11's modern context menu is implemented with a native `IExplorerCommand` handler registered by `QuickDrop.Sparse.msix`. If Explorer has cached old shell state, restart Explorer after install.

# AM Windows Assistant

AM Windows Assistant is a Windows desktop utility that helps you start Windows 11 Setup from a local ISO. It can also check for app updates from a public GitHub Releases repository that you configure.

## Features

- Select a Windows 11 ISO already on your PC.
- Mount the ISO as a Windows virtual drive and locate its setup program.
- Start Setup in compatibility mode after an explicit confirmation.
- Open Microsoft's official Windows 11 download page if you need installation media.
- Manually check for and install app updates from a configured GitHub release.

This app mounts an ISO; it does not burn a DVD or create a bootable USB drive.

## Requirements

- Windows 10 or Windows 11, x64.
- Administrator approval to mount an ISO when Windows requests it.
- An internet connection to visit Microsoft's download page or check GitHub updates.

The published Windows x64 executable is self-contained and does not require a separate .NET runtime installation.

## Getting started

1. Download `AMWindowsAssistant.exe` from the latest release.
2. Run the app.
3. Choose a Windows 11 ISO on your PC and select **Mount ISO**.
4. Review the confirmation and select **Start setup** to open Windows Setup.
5. Follow the instructions shown by Windows Setup.

If you do not have an ISO, use the app's Microsoft download link or visit the [official Windows 11 download page](https://www.microsoft.com/software-download/windows11).

## Compatibility mode

Compatibility mode attempts to bypass some Windows 11 hardware checks, for example checks related to TPM or the processor. It does not guarantee that Setup will proceed or that the resulting installation, apps, drivers, or future updates will work as expected. Back up important files before upgrading, and use Windows installation media that you are authorized to use.

## App updates

The update checker is manual and uses the public GitHub Releases API:

1. In the app, open **About** and enter the public repository as `owner/repository`.
2. Save the repository, then select **Check for updates** in the sidebar.
3. If a newer release is found, review the release notes and confirm before downloading.
4. The app verifies the downloaded executable against the SHA-256 digest reported by GitHub, then replaces the running app and restarts it.

For the updater to find a release:

- Publish a regular (not draft or prerelease) GitHub release with a version tag, such as `v1.0.1`.
- Attach the Windows x64 executable with the exact asset name `AMWindowsAssistant.exe`.
- Set the project's `<Version>` in `AMWindowsAssistant.csproj` to the release version before building, so the app can compare versions correctly.
- GitHub must report a SHA-256 digest for the release asset.

The app does not check for updates in the background. GitHub Releases are separate from GitHub Pages; a Pages website is not required for updates.

## Build from source

Install the .NET 8 SDK, then run this command from the project directory:

```powershell
dotnet publish .\AMWindowsAssistant\AMWindowsAssistant.csproj --configuration Release --runtime win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

The published executable is created in:

```text
AMWindowsAssistant\bin\Release\net8.0-windows\win-x64\publish\AMWindowsAssistant.exe
```

## Project status

AM Windows Assistant is an independent project and is not affiliated with Microsoft. Windows and Windows Setup are Microsoft products.

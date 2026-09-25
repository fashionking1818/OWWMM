# One WuWa Mod Manager (OWWMM)

OWWMM is a portable, unofficial WWMI Mod manager for Wuthering Waves, built with .NET 10 and Avalonia. It organizes Mods by the folders present under `WWMI/Mods`; Create and manage the groups yourself.

## Features

- Create groups with a folder name, title, subtitle and cropped local icon.
- Enable or disable Mods by adding or removing the `DISABLED ` folder-name prefix, and move Mods between groups without rewriting their contents.
- Import multiple ZIP, 7Z, RAR or TAR archives by button or drag and drop. The Windows package includes 7-Zip, so a separate installation is not required. Password-protected archives prompt for a password only when needed; passwords are not saved.
- Manage preview images and `Preview/readme.txt`, rename or delete Mods, and view previews in a full-window viewer.
- Keep portable settings beside the application in `config/settings.json`. Release packages do not include personal settings.

## Install and use

Extract the entire Windows x64 application archive and run `OWWMM.exe`. The .NET 10 Runtime (x64) is required; Windows offers a download link if it is missing. On first launch, choose a language and your `WWMI/Mods` folder. For a portable relative path, place the OWWMM folder next to `WWMI`.

The left panel shows folders immediately under `WWMI/Mods` as groups. The middle panel shows each group's direct child folders as Mods. A top-level folder containing a Mod INI can also appear in the virtual ungrouped group. You decide which group each Mod belongs to.

## Build from source

Install the .NET 10 SDK. On Windows, run:

```powershell
dotnet build src/OWWMM/OWWMM.csproj -c Release
dotnet publish src/OWWMM/OWWMM.csproj -c Release -r win-x64 --self-contained false -o artifacts/publish
```

`start-win.cmd` can launch the application from source during development. Run `./package-win.ps1` to produce the Windows x64 ZIP, SHA-256 checksum and corresponding 7-Zip source archive under `artifacts/release`. Only the Windows x64 package has been built and verified; macOS and Linux packages are not provided.

## Source and third-party notices

This repository provides the source for inspection and building. OWWMM code and artwork are not licensed for reuse or redistribution unless a separate license grants those rights. Included third-party components retain their own licenses; see [7-Zip notices](src/OWWMM/ThirdParty/7zip/NOTICE.md), [other dependency notices](src/OWWMM/ThirdPartyNotices/README.md), and the [distribution notice](src/OWWMM/DistributionNotice.md). The application icon and substantial application code were made with generative AI assistance.

OWWMM is a community tool and is not affiliated with or endorsed by Kuro Games.

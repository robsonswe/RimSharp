# RimSharp – A Smarter RimWorld Mod Manager

**RimSharp** is a standalone, full-featured mod manager for **RimWorld**, built in C# with Avalonia UI (cross-platform: Windows and Linux). It goes far beyond the built-in manager — giving you better control over mod load order, versioning, dependencies, updates, and even Git integration. Designed for heavy modders, power users, and anyone tired of XML-wrangling and broken save games.

[![Build Status](https://img.shields.io/badge/Build-Passing-brightgreen)](https://github.com/robsonswe/RimSharp)  
[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL--3.0-blue.svg)](https://github.com/robsonswe/RimSharp#AGPL-3.0-1-ov-file)

---

## Features

### Mod List Management

### Optimization Toolkit

- **Auto-Sorting:** Sorts based on dependencies and rules maintained by the community.
- **Dependency Resolver:** Finds and adds missing mod dependencies.
- **Conflict Detection:** Uses built-in and user rules to flag incompatibilities.
- **Duplicate Mod Detection:** Detects and helps you clean up duplicate mods.
- **Replacement Suggestions:** Detects unsupported mods and recommends modern equivalents.
- **Version Awareness:** Combines mod info, known patches, and custom rules to highlight unsupported mods.

### Customization

- Compliment metadata for any mod:
  - Supported game versions
  - External links
  - Custom tags
- Customize rules for load order:
  - `loadBefore`, `loadAfter`, `incompatibleWith`
- Force mods to always load last.

### Workshop Downloads

- **SteamCMD Integration:** Download mods via Steam Workshop.
- **Update Checker:** Compare local mods with Workshop versions.

### Install

- **Local Zips:** Install directly from `.zip` archives.
- **GitHub Integration:** Clone mods directly from GitHub URLs.

### Git Mod Support

- **Git Tab:** Central hub for Git-installed mods.
- **Update Checker:** Detect local changes vs. remote.
- **One-Click Pull:** Sync Git mods easily.

---

## Screenshots

![Preview](./preview.png)

---

## Installation

### Windows

1. **Download:** Get the latest release from the [Releases page](https://github.com/robsonswe/RimSharp/releases).
2. **Extract:** Unzip it anywhere (e.g. `C:\Games\RimSharp`).
3. **Run:** Start the app via `RimSharp.exe`.
4. **Set Paths:** On first launch, configure your RimWorld installation, Mods folder, and Config folder.

### Linux (x64)

1. **Download:** Get `RimSharp_linux-x64_<tag>.tar.gz` from the [Releases page](https://github.com/robsonswe/RimSharp/releases).
2. **Extract:** `tar xzf RimSharp_linux-x64_<tag>.tar.gz` — creates a `RimSharp/` folder. Extract it anywhere **writable** (e.g. your home directory); the app keeps its data (config, SteamCMD, logs) next to the executable.
3. **Run:** `cd RimSharp && ./RimSharp`
4. **Set Paths:** On first launch, configure your RimWorld installation, Mods folder, and Config folder.

**Optional native dependencies:**

- **Embedded Workshop browser** (the app works without it — only the browser panel is unavailable):
  - Debian / Ubuntu: `sudo apt install libwpewebkit-2.0-1 libwpebackend-fdo-1.0-1` (or `libwebkit2gtk-4.1-0`)
  - Fedora: `sudo dnf install webkit2gtk4.1`
  - Arch: `sudo pacman -S wpewebkit wpebackend-fdo` (or `webkit2gtk-4.1`)
- **SteamCMD** (Workshop downloads) is a 32-bit binary and needs multilib support:
  - Debian / Ubuntu: `sudo apt install lib32gcc-s1`
  - Fedora: `sudo dnf install glibc.i686 libstdc++.i686`
  - Arch: enable the `[multilib]` repository, then `sudo pacman -S lib32-gcc-libs`
- **ICU** (internationalization, usually preinstalled): `libicu` on Debian/Ubuntu/Fedora/Arch.

---

## Building from Source

**Prerequisites:**

- [.NET SDK](https://dotnet.microsoft.com/download) (check the project for target version)
- Git

**Steps:**

```bash
git clone https://github.com/robsonswe/RimSharp.git
cd RimSharp
dotnet build RimSharp.sln -c Release
```

Executable will be in `bin/Release/net8.0-windows/` (Windows) or `bin/Release/net8.0/` (Linux).

You can also build via Visual Studio by opening `RimSharp.sln`.

---

## Usage Overview

- **Set paths first.** RimWorld won’t load mods without the right folders.
- **Use the tabs:**
  - **Mods:** Enable/disable, reorder, troubleshoot, save/load.
  - **Downloader:** SteamCMD-powered Workshop downloader.
  - **Git Mods:** Manage Git-installed mods and updates.
- **Save regularly:** Writes to RimWorld’s `ModsConfig.xml`.

---

## System Requirements

- **OS:** Windows 10 / 11, or Linux x64 (glibc 2.23+ — Ubuntu 22.04+, Debian 12+, Fedora, Arch, SteamOS)
- **Runtime:** None needed — releases are self-contained
- **RimWorld:** Installed and working copy (native Linux build officially supported, including Steam Deck)
- **SteamCMD:** Required for downloading Workshop mods (downloaded automatically by the app on first use)

---

## Contributing

1. Fork the repo.
2. Create a branch:  
   `git checkout -b feature/your-feature`
3. Commit your work:  
   `git commit -am "Add your feature"`
4. Push the branch:  
   `git push origin feature/your-feature`
5. Open a Pull Request.

Stick to the code style. Use comments where appropriate. File issues if you're not sure what’s breaking.

---

## Reporting Issues

Please include:

- How to reproduce it
- What you expected vs. what actually happened
- Screenshots or error messages
- Logs (in RimSharp's folder or `%AppData%`)

Report here: [GitHub Issues](https://github.com/robsonswe/RimSharp/issues)

---

## License

Licensed under [AGPL-3.0](https://github.com/robsonswe/RimSharp#AGPL-3.0-1-ov-file)

---

## Credits

- Ludeon Studios for RimWorld
- Open source libraries like LibGit2Sharp
- Modders keeping the RimWorld ecosystem alive
- [RimPy](https://github.com/rimpy-custom/RimPy) for the UI and features inspiration

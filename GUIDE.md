# How MFG Manager works

This guide is for anyone reading or changing the code. For what the app is and how to install it, see the [README](README.md).

## New to .NET?

| Word | What it is | Like in Angular or npm |
|---|---|---|
| C# | The programming language | TypeScript |
| .NET SDK | Compiler and build tool (`dotnet build`, `dotnet test`) | Node + Angular CLI |
| Solution (`.sln`) | Groups the projects | Workspace |
| Project (`.csproj`) | One buildable unit and its settings | `package.json` + `tsconfig` |
| NuGet package | A downloaded library | npm package |
| Assembly (`.dll`) | The compiled result of a project | Build output |
| `bin/`, `obj/` | Build results, safe to delete | `dist/`, cache |
| Avalonia | The window framework. It draws everything itself, so it looks the same on Windows and Linux | Angular itself |
| AXAML | The markup for screens | Component `.html` templates |

## How the projects connect

```text
MfgManager.Tests ──► MfgManager.App ──► MfgManager.Core
       └───────────────────────────────────────▲

installer/        packs the published App into a Windows .msi
packaging/linux/  scripts and package files for Linux
```

Arrows mean "uses". The most important rule is that **Core knows nothing about the window**. That's why it can be tested directly and shared between Windows and Linux.

**What happens when the app opens:**

1. [Program.cs](src/MfgManager.App/Program.cs) starts Avalonia.
2. [App.axaml.cs](src/MfgManager.App/App.axaml.cs) creates the services and the window. It's like `main.ts`, with dependency injection done by hand.
3. When the window opens, [MainViewModel](src/MfgManager.App/ViewModels/MainViewModel.cs) finds the mod, scans for games, then checks GitHub for a newer mod.
4. [GameScanner](src/MfgManager.Core/Scanning/GameScanner.cs) asks each launcher for its games, and [GameInspector](src/MfgManager.Core/Scanning/GameInspector.cs) looks through each game's files.
5. Each game gets a `GameViewModel`, `GroupedView` filters and groups them, and `CoverWall` draws the result through bindings.

**What happens when you click Install / Update:** `GameActionsViewModel` asks any questions it needs to, checks the game isn't running, and calls [ModInstaller](src/MfgManager.Core/Mod/ModInstaller.cs), which copies the DLL. Then every game's status is refreshed.

## Folder map

```text
MFG-Manager/
├─ MfgManager.sln                 the solution
├─ Directory.Build.props          settings shared by all projects (version, warnings = errors, code checks)
├─ .editorconfig                  code style rules
├─ global.json                    which .NET SDK and test runner to use
├─ src/
│  ├─ MfgManager.Core/            the logic: no window code
│  │  ├─ Collections/             GroupedView (filter, sort, group) and ListSync
│  │  ├─ Models/Game.cs           one game: name, launcher, folder, .exe, frame gen, anti-cheat, DRM
│  │  ├─ Platform/                what differs between Windows and Linux: folders, opening links, running games
│  │  ├─ Scanning/
│  │  │  ├─ Launchers/            one file per launcher: "which games are installed where?"
│  │  │  ├─ Vdf.cs                reads Steam's .vdf and .acf files
│  │  │  ├─ GameInspector.cs      looks inside a game folder
│  │  │  └─ GameScanner.cs        runs all launchers, removes duplicates, inspects each game
│  │  ├─ Mod/
│  │  │  ├─ ModPackage.cs         a MFGUnlocker.dll on disk and its version
│  │  │  ├─ PeVersionInfo.cs      reads a DLL's name and version on any system
│  │  │  ├─ ModInstaller.cs       install, update, remove, status
│  │  │  └─ UpdateChecker.cs      latest release on GitHub, verified download, safe unzip
│  │  ├─ Store/SteamStore.cs      covers and DRM info from Steam's public store, cached
│  │  ├─ Settings/AppSettings.cs  what's remembered between runs (a JSON file)
│  │  └─ PathUtil.cs              path helpers
│  └─ MfgManager.App/             the window
│     ├─ Program.cs, App.axaml    start-up
│     ├─ Components/              one folder per part of the window: Shell, Sidebar, Toolbar,
│     │                           Banners, Library, Details, Status, Dialogs
│     ├─ ViewModels/              what each part shows and does
│     ├─ Services/                pop-ups, file pickers, light and dark mode, the title bar
│     ├─ Converters/              turn values into what's displayed
│     ├─ Theme/                   Colors.axaml (every colour, both modes), Styles.axaml (how things look)
│     ├─ Assets/                  the icon and logo
│     ├─ Licenses/                notices for the libraries that ship with the app
│     └─ app.manifest             makes Windows ask for admin rights
├─ tests/MfgManager.Tests/        the tests
├─ installer/                     the Windows .msi (WiX). Package.wxs says what gets installed where
├─ packaging/linux/               menu entry, install scripts, PKGBUILD for Arch, control file for .deb
├─ tools/                         build scripts, and scripts that draw the logo and installer pictures
└─ docs/                          screenshots of made-up games
```

## How the window code works

The pattern is called **MVVM**. The Model is plain data (`Game`), the View is the `.axaml` file, and the ViewModel is the class the view binds to. The `.axaml.cs` files have almost no code on purpose.

| Angular | Here |
|---|---|
| `component.ts` class | A ViewModel, like `LibraryViewModel.cs` |
| `component.html` | `.axaml`, like `GameCard.axaml` |
| `<app-game-card>` | `<library:GameCard />` |
| `[value]="name"` | `Text="{Binding Name}"` |
| `[(ngModel)]="searchText"` | `Text="{Binding SearchText}"` (two-way for text boxes) |
| `(click)="install()"` | `Command="{Binding InstallCommand}"` |
| `*ngFor` | `ItemsSource="{Binding Games}"` with an `ItemTemplate` |
| `*ngIf` | `IsVisible="{Binding HasGame}"` |
| `[class.selected]="isSelected"` | `Classes.selected="{Binding IsSelected}"` |
| Pipes | Converters |
| CSS | `Theme/Styles.axaml`. Selectors work like CSS: `Button.accent:pointerover` |
| CSS variables | `{DynamicResource TextBrush}`, defined in `Theme/Colors.axaml` |
| Change detection | `SetProperty(...)` raises `PropertyChanged`, so the screen redraws |
| Services and DI | Plain classes, created in `App.CreateMainViewModel()` and passed in |
| `ngOnInit` | `MainViewModel.InitializeAsync()` |

Bindings are compiled, so a typo in `{Binding Nmae}` is a build error instead of an empty field on screen.

### The reusable filter and grouping class

Avalonia has no `ICollectionView` or `GroupStyle`, so `Core/Collections/GroupedView.cs` does that job for any kind of item. It watches a list and keeps a filtered, sorted, grouped version up to date:

```csharp
var view = new GroupedView<GameViewModel>(Games)
{
    Filter = game => game.Name.Contains(searchText),
    Sort = Comparer<GameViewModel>.Create((a, b) => string.Compare(a.Name, b.Name)),
    GroupBy = game => game.Game.Drm,        // a game with two DRMs appears in both groups
};

view.Refresh();                             // when what Filter or GroupBy looks at has changed

using (view.DeferRefresh())                 // change several things, and refresh once at the end
{
    view.Filter = ...;
    view.GroupBy = null;                    // no grouping: one group without a name
}
```

The window binds to `View.Groups`, and each group has `Name`, `HasName`, `Items` and `Count`. Only what's different gets updated, so cards stay in place and the scroll position holds.

### Light and dark mode

**Appearance** in the sidebar picks System, Light or Dark, and the choice is saved.

- **To change a colour,** edit `Theme/Colors.axaml`. A test fails if one mode is missing a colour the other has.
- **Readable text is enforced by tests.** Every text colour is checked against every surface it's used on, to the WCAG standard (4.5:1, or 3:1 for large text). `ReadabilityCheck` also works out the colour behind each piece of text really on screen.
- **The title bar** is handled by `Services/WindowFrame.cs`, because Avalonia only makes it dark on Windows 11. On Linux your desktop draws it.

## Finding games

Each launcher has its own file in `Scanning/Launchers/`:

| Launcher | Where its game list lives |
|---|---|
| Steam | `steamapps/libraryfolders.vdf`, then one `appmanifest_<id>.acf` per game. Redistributables, SteamVR, Proton and the Steam Linux Runtime are skipped |
| Ubisoft Connect | Registry: `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs` |
| Epic | One JSON `.item` file per game in `ProgramData\Epic\EpicGamesLauncher\Data\Manifests` |
| GOG | Registry: `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games`. DLC is skipped |
| Xbox app | An `XboxGames` folder on each drive |
| Battle.net | Windows' list of installed programs: games that uninstall with `Blizzard Uninstaller.exe` |
| EA app | The same list: games that uninstall from `Common Files\EAInstaller\` |
| Rockstar | The same list: publisher "Rockstar Games" with `uninstall.exe -uninstall=<game>` |
| Heroic (Linux) | `~/.config/heroic` or its Flatpak folder: `legendaryConfig/legendary/installed.json` for Epic, `gog_store/installed.json` for GOG |
| Anything else | **Add game...**, where you pick the `.exe` |

`GameInspector` then looks through each game folder, up to 8 folders deep:

- **Frame Generation:** the game has `nvngx_dlssg.dll` or `sl.dlss_g.dll`. `sl.interposer.dll` doesn't count, because it's Streamline's general loader, which games also use for plain DLSS and Reflex.
- **Anti-cheat:** a file or folder named like Easy Anti-Cheat, BattlEye, GameGuard, EA Javelin or XIGNCODE. Battle.net games always count, because Blizzard builds it into the game.
- **DRM from files:** for example, `steam_api64.dll` means Steamworks and `EOSSDK-Win64-Shipping.dll` means Epic Online Services.
- **DRM from the Steam store:** Denuvo and account requirements, read from the store page's DRM notice.
- **The real .exe:** Unreal games' `*-Win64-Shipping.exe` comes first, then the launcher's hint, then the biggest .exe that isn't a launcher, crash reporter or uninstaller. Hints pointing at a launcher, and anything in a `tools` folder, are skipped. **Change .exe...** overrides this, as long as the .exe is inside the game's folder.

**Cover art** comes from Steam's own cache on your computer, then MFG Manager's cache, then Steam's public store. The store needs no account or key, and it also works for games from other launchers that are sold on Steam, because it searches by name. With no art, the card shows the game's initials.

## Installing and removing the mod

The mod pretends to be a Windows DLL that games load from their own folder, like `version.dll`.

- **Install** copies `MFGUnlocker.dll` next to the game's .exe. **Auto** uses the first supported name that's still free.
- **It never overwrites a game's or another mod's DLL.** If the name you picked is taken, it refuses. `Version.dll` and `version.dll` count as the same name, even on Linux, because Wine treats them that way.
- **Update** copies the newer DLL over the old one under the same name.
- **Remove** only deletes the file if its built-in product name is "MFG Unlocker", so it can't delete a game file that reused the name.
- **The mod's settings and logs** (`RTXMFG*` and similar) stay unless you tick the checkbox. They're then moved to `Removed mod files` in the data folder, not deleted.
- **Anti-cheat:** installing into an anti-cheat game warns about bans first and only continues if you say yes.
- **No Frame Generation found:** it asks first, but doesn't block it, because the mod's experimental XeSS mode can work without DLSS Frame Generation.
- **Records:** every install is recorded in `settings.json`, and copies installed by hand are found too.

## Mod updates

- GitHub is asked once, when the app opens. Nothing is downloaded until you click **Download and verify**.
- The zip is checked against the SHA-256 fingerprint GitHub lists for it, and deleted if it doesn't match. That proves it's the file published there, not that it's safe.
- It's unzipped into `Mods`, with limits on size and file count, and any path that would land outside the folder is refused.
- **Choose DLL...** also copies your DLL into `Mods`, so the app doesn't depend on files scattered elsewhere.

## When it scans and writes

Nothing runs in the background: there are no timers, file watchers, services or scheduled tasks. `ScanRulesTests` fails if a later change adds one, or starts a scan from somewhere new.

| When | Scans or checks | Writes |
|---|---|---|
| The app opens | `Mods` and Downloads (up to two folders deep, only for `MFGUnlocker.dll`), every launcher, the Steam store for games it doesn't know yet, GitHub | The mod DLL into `Mods`, only after you say yes. New covers and store info |
| **Rescan** | Every launcher, and the Steam store for games it doesn't know yet | New covers and store info |
| **Add game...** | That one game and its store info, then every game's mod status | `settings.json`, new covers and store info |
| **Change .exe...** | That one game, then every game's mod status | `settings.json` |
| **Install, Update, Remove, Revert all** | Whether the game is running, then every game's mod status | The game folder and `settings.json` |
| **Choose DLL...** | The DLL you pick, then every game's mod status | `Mods` and `settings.json` |
| **Download and verify** | Downloads the release from GitHub, then every game's mod status | `Mods` and `settings.json` |
| **Appearance** | Nothing | `settings.json` |
| Selecting, searching, filtering, grouping, returning to the window | Nothing | Nothing |

"Mod status" only looks in the folder next to each game's .exe and needs no internet.

- **Changes made while the app is open** aren't noticed by themselves. A new game, or a file repair that removes the mod, shows after **Rescan**. A new mod release, or a new DLL in Downloads, shows after restarting the app.
- **Steam is asked again** after a failed lookup, and at every start and Rescan for a game with a Steam ID but no cover. Games it found nothing for are remembered and not asked again.

## Security design

The app talks to `api.github.com`, `github.com`, `store.steampowered.com`, `api.steampowered.com` and `shared.akamai.steamstatic.com`, over HTTPS only. To look up a game not on Steam on your computer, it sends the game's name to Steam's store search. It has no telemetry, and building it needs no accounts or keys.

Because the app runs as admin on Windows, anything from outside is treated with care:

- A newer `MFGUnlocker.dll` in Downloads is never used silently, because a DLL's "MFG Unlocker" name is easy to fake.
- `settings.json` can be changed by any program, so a DLL path in it only counts inside the `Mods` folder, an install name only counts if the mod supports it, and a saved .exe only counts inside the game's folder.
- The mod is never put in the Windows folder, whatever a launcher's files or `settings.json` say.
- A damaged `settings.json` or cache file, a strange answer from GitHub or Steam, or a DLL with a nonsense version can't stop the app or its scan.
- Web addresses from GitHub must be `https://`, and the release page must be on `github.com`.
- Downloads and zips have size limits, and the code that reads DLLs checks every offset.
- Web links open through Explorer or `xdg-open`, so your browser doesn't run as admin, and no shell is involved.
- Release builds ignore the `DOTNET_STARTUP_HOOKS` and `DOTNET_ROOT` environment variables, which could otherwise load other code into the app as admin. This means Windows builds only use the system-wide .NET.

**On Linux** the app refuses to run as root, its data folder is private to you, and the install scripts refuse root and download nothing.

**The Windows installer** installs for all users into Program Files, so only admins can change the app or its `Mods` folder. It adds no services, startup entries, scheduled tasks or PATH changes, and none of its own actions run as SYSTEM.

**The limits of this:**

- .NET has other environment variables like those two that can't be turned off, and malware already running on your account is hard to fully stop while an app runs as admin.
- Installing outside Program Files removes the admin-only protection.
- The installer isn't code-signed, and an app that runs as admin and places `version.dll` files in game folders can look suspicious to antivirus tools. If one flags it, report it to that tool's maker as a false positive.

## Packages

- **Shipped with the app:** Avalonia, its Fluent theme and the Inter font, and what those bring along: SkiaSharp and HarfBuzzSharp for drawing, ANGLE on Windows, Tmds.DBus on Linux, and MicroCom. Their notices are in `src/MfgManager.App/Licenses`.
- **Only for tests:** xunit.v3 and Avalonia.Headless.
- **Only for building:** WiX, apart from its small setup helpers inside the `.msi`. Its licence asks for a fee only from users who generate revenue with it.
- **Blocked on purpose:** `Avalonia.BuildServices`. Avalonia requires this package, and it reports build information to Avalonia's servers when you compile. `ExcludeAssets="all"` keeps its code out, and `BuildPrivacyTests` fails if that changes.

To check for known security problems:

```bash
dotnet list MfgManager.sln package --vulnerable --include-transitive
```

## Windows and Linux differences

| | Windows | Linux |
|---|---|---|
| Runs as | Admin | You, and it refuses to run as root |
| Settings and cache | `%LOCALAPPDATA%\MFG Manager` | `~/.local/share/MFG Manager` |
| Mods folder | Next to the app | `~/.local/share/MFG Manager/Mods`, because the app's folder may be read-only |
| Finds games from | Steam, Ubisoft, Epic, GOG, Xbox, Battle.net, EA, Rockstar | Steam and Heroic, normal or Flatpak |
| Loading the mod | Automatic | Steam needs a launch option like `WINEDLLOVERRIDES="version=n,b" %command%`. The details panel shows it with a **Copy** button |

## Code style

The code follows Microsoft's [C# conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions), and the build enforces most of them. `.editorconfig` holds the style rules and `AnalysisMode` in `Directory.Build.props` turns on Microsoft's recommended checks. Warnings count as errors, so breaking a rule stops the build. Most style problems fix themselves with:

```bash
dotnet format MfgManager.sln
```

- **One class per file:** each file is named after its class, and a test checks this. A small type used only inside one class stays inside that class.
- **Newer .NET features:** `[GeneratedRegex]` builds search patterns at compile time, `[LibraryImport]` generates the calls into Windows, and the `field` keyword saves writing a separate variable behind each property.

Three recommendations are skipped on purpose:

- **`ConfigureAwait(false)`:** `SteamStore` relies on staying on the window's thread, so adding this could cause bugs where two lookups change its data at once.
- **The 65-character line limit:** Microsoft sets that for its documentation pages, which are read on phones.
- **Test names:** they keep their underscores, because reading like a sentence is what Microsoft's testing guide recommends.

## Tests

```bash
dotnet test --project tests/MfgManager.Tests
```

Normal test runs use a fake web and fake game folders, so they don't need the internet or your games.

- **`Core/`:** finding games in every launcher, the game inspector, the mod installer, updates and tampered downloads, reading DLL versions, `GroupedView`, Linux paths, and the packaging files.
- **`ViewModels/`:** filters, grouping, every button's rules, the warning questions, and `ScanRulesTests`.
- **`Components/`:** every component really drawn in both modes, without opening a window. `StateTests` moves the mouse over every button, presses them, ticks boxes, opens dropdowns and selects text, then checks the colours in each state.

A few tests only run when you set an environment variable. Each one is shown here as you'd run it in Bash. In PowerShell, set the variable first: `$env:MFG_SHOTS="C:\temp\shots"`.

Save a picture of every component in both modes:

```bash
MFG_SHOTS=/some/folder dotnet test --project tests/MfgManager.Tests
```

Scan your real games and draw them. This uses the internet for covers and changes nothing real, because your settings are copied to a temporary folder and no button is pressed:

```bash
MFG_REAL_SCAN=1 MFG_SHOTS=/some/folder dotnet test --project tests/MfgManager.Tests
```

Redraw the pictures in `docs/` from made-up games. This needs Windows, and it creates and deletes `C:\Demo Games`:

```bash
MFG_DOCS=/full/path/to/docs dotnet test --project tests/MfgManager.Tests
```

## Building

### Running it while developing

```bash
dotnet run --project src/MfgManager.App -p:RequireAdmin=false
```

This skips the admin prompt, and the next normal build puts it back.

### The Windows installer

```bash
powershell -File tools/build-installer.ps1
```

This makes `artifacts/MFG-Manager-<version>.msi`. It always compiles from scratch, because a build otherwise reuses earlier results even when they were made with other settings. It stops with an error if the program wouldn't ask for admin rights, still allows startup hooks, or mentions the build computer's folders. Release builds leave out debug information because it holds those paths. Share only the `.msi`: the `.wixpdb` next to it, if there is one, is for developers and can contain them.

Installing replaces the old version and keeps the `Mods` folder and settings. Uninstalling removes `Mods` too, but your settings and the mod in your games stay, so use **Revert all** first if you want the games clean.

### Linux

Install the .NET 10 SDK, using the command for your system:

```bash
sudo pacman -S --needed dotnet-sdk                 # CachyOS, Arch
sudo apt install dotnet-sdk-10.0                   # Mint 22, Ubuntu 24.04
```

Mint 21 and Ubuntu 22.04 need Microsoft's backports first: `sudo add-apt-repository ppa:dotnet/backports`.

Then build it one of three ways:

```bash
bash tools/build-linux.sh --install    # for your user, with no root needed
bash tools/build-linux.sh --package    # .pkg.tar.zst on CachyOS and Arch, .deb on Mint and Ubuntu
bash tools/build-linux.sh              # only a portable .tar.gz in artifacts/
```

The result carries its own copy of .NET, so only the computer that builds it needs the SDK. To remove a `--install` copy, run `packaging/linux/uninstall.sh`.

It needs X11, fontconfig, OpenSSL and `xdg-open`, which desktops normally have, and the packages list them. On Wayland it runs through XWayland, which minimal setups may need to install (`xorg-xwayland` on CachyOS). The file picker uses your desktop's portal, or GTK if there's no portal.

### A new version

Raise `<Version>` in `Directory.Build.props` and `pkgver` in `packaging/linux/PKGBUILD`, and a test fails if they differ. Always raise it for a changed installer, because Windows Installer won't replace a version with the same number and may install it alongside the old one.

### The logo

Edit the colours at the top of `tools/make-icon.ps1`, then redraw the icon and the installer's pictures:

```bash
powershell -File tools/make-icon.ps1 -OutFolder src/MfgManager.App/Assets
powershell -File tools/make-installer-art.ps1 -OutFolder installer/Assets
```

## Known limits

- **Linux hasn't run on a real desktop yet.** It's been built and its scripts run from Windows, with stand-ins for `dpkg-deb` and links into `/opt`, and its code paths are tested. The window, Proton and the real package tools are still untested.
- **Epic and Heroic** have only been tested with fake folders.
- **Xbox app games** are found, but their .exe often sits in a protected folder the scan can't read. Add-ons can also show up as games.
- **Denuvo** is only detected for games sold on Steam, because it comes from the store page.
- **Anti-cheat** is found by file and folder names, so it can miss some kinds.

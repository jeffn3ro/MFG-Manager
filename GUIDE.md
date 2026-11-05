# MFG Manager 2.0: how it was built

MFG Manager finds your games, shows which ones have DLSS Frame Generation, and installs or removes the
[MFG Unlocker](https://github.com/dashdogy/MFG-Unlocker) mod in them. Version 2.0 is rebuilt on Avalonia, so the
same code runs on Windows and Linux. It does everything 1.0.6 did and reads the same settings file, so mods installed with an older version stay known.

For what the app is and how to get it, start with the [README](README.md). This guide explains how it works inside.

---

## 1. Running it on Windows

Download the `.msi` from the Releases page, or build it yourself (section 12), and follow the setup wizard. It installs into `C:\Program Files\MFG Manager`
(**Change...** opens Windows' normal folder picker; updates remember the folder) and adds **MFG Manager** to the Start menu,
plus the desktop if "Create a desktop shortcut" stays ticked.

* **Upgrading from an older version:** just run it. It replaces the old version and keeps your `Mods` folder and settings.
* **Uninstalling:** from *Apps & features*. Setup asks first and ends on "MFG Manager has been uninstalled". This also removes the `Mods` folder.
  Your settings stay, and so does the mod in your games: use **Revert all** first if you want those gone.
* **Admin rights:** Windows asks on every start. Say yes; game folders under *Program Files* can't be changed without them.
* **Needs:** Windows 10 or 11 and the [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0), installed the normal way. The installer checks for it. The app only uses the system-wide copy, for safety, so a copy found only through `DOTNET_ROOT` doesn't count.

What it does on start:

1. finds the newest `MFGUnlocker.dll` in its `Mods` folder. On first start (or when Downloads has a newer one), it
   **asks** before copying it from your Downloads folder into `Mods\MFGUnlocker-v<version>\`
2. scans your launchers for games
3. loads cover art and DRM info
4. asks GitHub whether a newer mod version is out

---

## 2. Building and running it on Linux

One script does it all, on CachyOS, Mint or any other 64-bit Linux. First get the source code onto the new system, then install the .NET 10 SDK.

On CachyOS or Arch:

```bash
sudo pacman -S --needed dotnet-sdk
```

On Mint 22 or Ubuntu 24.04:

```bash
sudo apt install dotnet-sdk-10.0
```

On Mint 21 or Ubuntu 22.04, add Microsoft's backports first:

```bash
sudo add-apt-repository ppa:dotnet/backports
```

```bash
sudo apt install dotnet-sdk-10.0
```

Then pick one. The quickest installs it for your user only, with no root needed:

```bash
bash tools/build-linux.sh --install
```

Or make a system package you can install and remove with your package manager (`.pkg.tar.zst` on CachyOS and Arch, `.deb` on Mint and Ubuntu):

```bash
bash tools/build-linux.sh --package
```

Or only make a portable `artifacts/MFG-Manager-<version>-linux-x64.tar.gz`, which you unpack and install with its `install.sh`:

```bash
bash tools/build-linux.sh
```

The built app carries its own copy of .NET (about 104 MB, 44 MB as the archive), so only the computer that builds it needs the SDK.
To remove a `--install` copy, run `packaging/linux/uninstall.sh`.

**What's different on Linux:**

| | Windows | Linux |
|---|---|---|
| Runs as | Admin | You. It refuses to run as root; your games are in your own folders |
| Settings and cache | `%LOCALAPPDATA%\MFG Manager` | `~/.local/share/MFG Manager` |
| Mods folder | Next to the app | `~/.local/share/MFG Manager/Mods`, because the app's own folder may be read-only |
| Finds games from | Steam, Ubisoft, Epic, GOG, Xbox, Battle.net, EA, Rockstar | Steam (normal and Flatpak), Heroic (its Epic and GOG games, normal and Flatpak) |
| Other launchers, like Lutris | **Add game...** | **Add game...** |
| Loading the mod | Automatic | Steam needs a launch option. The details panel shows it with a **Copy** button: `WINEDLLOVERRIDES="version=n,b" %command%` |

**It needs** the libraries every desktop already has: X11, fontconfig, OpenSSL and `xdg-open`. The packages list them. On a Wayland desktop it runs
through XWayland, which comes with Plasma and GNOME but may be missing on minimal setups (`xorg-xwayland` on CachyOS). Hyprland and similar need it installed.
The file picker uses your desktop's portal, or GTK if there is no portal.

**How much of this has been tested:** see [Known limits](#11-known-limits). In short: it was built and its scripts were run from Windows, but it has not yet been run on a real Linux desktop.

---

## 3. What was used

| Thing | What it is | Angular comparison |
|---|---|---|
| **C#** (on .NET 10) | The programming language | TypeScript |
| **.NET 10 SDK** | Compiler and build tool (`dotnet build`, `dotnet test`) | Node + Angular CLI |
| **Avalonia 12.1.5** | The window framework. Draws everything itself, so it looks the same on Windows and Linux | Angular itself |
| **AXAML** | The markup for screens (`GameCard.axaml`) | Component `.html` templates |
| **Fluent theme + `Theme/`** | Avalonia's built-in controls with this app's own colours | A theme like Angular Material |
| **xUnit v3 + Avalonia.Headless** | Tests that really draw the window without showing it | Jest + a headless browser |
| **WiX 6** | Builds the Windows installer | |

**Every package was checked** before use, and again at the end (see [Safety](#9-safety-and-antivirus-honest-notes)): who publishes it, its licence, and known security problems.

* **Shipped in the app:** Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, and what those bring along
  (SkiaSharp and HarfBuzzSharp for drawing, ANGLE on Windows, Tmds.DBus on Linux, MicroCom). All MIT, ANGLE is BSD, the Inter font is OFL.
  `src/MfgManager.App/Licenses` holds their notices and is shipped with the app, because those licences ask for it.
* **Only for tests:** xunit.v3 and Avalonia.Headless. These aren't shipped.
* **Only for the build:** WiX. Also not shipped, apart from its small setup helpers inside the `.msi`.
* **Blocked on purpose:** `Avalonia.BuildServices`. Avalonia requires this package, and it reports build information to Avalonia's servers when you compile.
  `ExcludeAssets="all"` in the project file keeps its code out, and `BuildPrivacyTests` fails if that ever changes.

---

## 4. Project layout

Each part of the window is its own component in its own folder, so a file does one thing.

```
MfgManager/
├─ README.md, LICENSE             what the app is, and the MIT licence
├─ MfgManager.sln                 the "workspace" file
├─ Directory.Build.props          settings shared by all projects (version, warnings = errors)
├─ global.json                    which .NET SDK and test runner to use
├─ src/
│  ├─ MfgManager.Core/            THE BRAINS: no window code, so it's easy to test and runs on both systems
│  │  ├─ Collections/
│  │  │  ├─ GroupedView.cs        reusable filter + sort + group. See section 6
│  │  │  └─ ListSync.cs           updates a list with as few changes as possible
│  │  ├─ Models/Game.cs           one game: name, launcher, folder, exe, frame gen, anti-cheat, DRM
│  │  ├─ Platform/                everything that differs between Windows and Linux
│  │  │  ├─ AppPaths.cs           where settings and mods are kept
│  │  │  ├─ KnownFolders.cs       where your Downloads folder is
│  │  │  ├─ DesktopShell.cs       opens web pages and folders, never through a shell
│  │  │  └─ GameProcesses.cs      is this game running?
│  │  ├─ Scanning/
│  │  │  ├─ Launchers/            one file per launcher: "which games are installed where?"
│  │  │  ├─ Vdf.cs                reads Steam's .vdf and .acf files
│  │  │  ├─ GameInspector.cs      looks inside a game folder (frame gen? anti-cheat? DRM? real .exe?)
│  │  │  └─ GameScanner.cs        runs all launchers, removes duplicates, inspects each game
│  │  ├─ Mod/
│  │  │  ├─ ModPackage.cs         a MFGUnlocker.dll on disk + its version
│  │  │  ├─ PeVersionInfo.cs      reads a DLL's name and version on any system
│  │  │  ├─ ModInstaller.cs       install / update / remove (revert) / status
│  │  │  └─ UpdateChecker.cs      GitHub: latest release, verified download, safe unzip
│  │  ├─ Store/SteamStore.cs      cover art + DRM info from Steam's public store, cached
│  │  ├─ Settings/AppSettings.cs  what's remembered between runs (JSON file)
│  │  └─ PathUtil.cs              path helpers
│  └─ MfgManager.App/             THE WINDOW
│     ├─ App.axaml(.cs)           start-up: creates the services, opens the window (like main.ts)
│     ├─ Program.cs               the very first code that runs
│     ├─ Components/              the window, one component per folder
│     │  ├─ Shell/                MainWindow: lays out the parts below
│     │  ├─ Sidebar/              Sidebar, FilterList, ModCard, ThemePicker
│     │  ├─ Toolbar/              LibraryToolbar: search, filters, grouping
│     │  ├─ Banners/              BannerStack: update, missing mod
│     │  ├─ Library/              CoverWall, GameCard
│     │  ├─ Details/              DetailsPanel, GameHeader, InstallSection, GameInfoSection, ExeSection
│     │  ├─ Status/               StatusBar
│     │  └─ Dialogs/              MessageDialog: pop-up questions in the app's own look
│     ├─ ViewModels/              what each part shows and does (like component classes)
│     │  ├─ MainViewModel.cs      connects the parts below
│     │  ├─ LibraryViewModel.cs   the games, filters and selection
│     │  ├─ GameViewModel.cs      one game
│     │  ├─ GameActionsViewModel.cs  install, remove, revert, change .exe
│     │  ├─ ModViewModel.cs       which mod is used, updates
│     │  ├─ StatusViewModel.cs    the line along the bottom
│     │  └─ Mvvm.cs               ObservableObject + RelayCommand (tiny helpers)
│     ├─ Services/                pop-ups, file pickers, light and dark mode, the title bar
│     ├─ Converters/              value -> display helpers (like Angular pipes)
│     ├─ Theme/
│     │  ├─ Colors.axaml          every colour, for both modes
│     │  └─ Styles.axaml          how things look (like a global stylesheet)
│     ├─ Assets/                  app.ico, logo.png, logo.svg
│     ├─ Licenses/                notices for what's built in
│     └─ app.manifest             makes Windows require admin rights
├─ installer/                     the Windows .msi (WiX): Package.wxs says what gets installed where
│  └─ CustomActions/              the modern "Select folder" picker for the install folder page
├─ packaging/linux/               menu entry, install scripts, PKGBUILD for CachyOS, control file for .deb
├─ tests/MfgManager.Tests/        the tests, see section 10
├─ tools/
│  ├─ build-linux.sh              builds the Linux version
│  ├─ build-installer.ps1         builds artifacts\MFG-Manager-<version>.msi
│  ├─ make-icon.ps1               draws the logo in all sizes
│  └─ make-installer-art.ps1      draws the setup wizard's pictures
└─ docs/                          screenshots of made-up games
```

---

## 5. Avalonia for an Angular developer

| Angular | Avalonia (this project) |
|---|---|
| `component.ts` class | A ViewModel, like `LibraryViewModel.cs` |
| `component.html` | `.axaml`, like `GameCard.axaml` |
| A component inside another: `<app-game-card>` | `<library:GameCard />` |
| `{{ name }}` / `[value]="name"` | `Text="{Binding Name}"` |
| `[(ngModel)]="searchText"` | `Text="{Binding SearchText}"` (two-way for text boxes) |
| `(click)="install()"` | `Command="{Binding InstallCommand}"` |
| `*ngFor` | `ItemsSource="{Binding Games}"` + an `ItemTemplate` |
| `*ngIf` | `IsVisible="{Binding HasGame}"` |
| `[class.selected]="isSelected"` | `Classes.selected="{Binding IsSelected}"` |
| Pipes | Converters (`Converters/`) |
| CSS | `Theme/Styles.axaml`. Selectors work like CSS: `Button.accent:pointerover` |
| CSS variables | `{DynamicResource TextBrush}`, defined in `Theme/Colors.axaml` |
| Change detection | `SetProperty(...)` raises `PropertyChanged` so the screen redraws |
| Services + DI | Plain classes, created in `App.CreateMainViewModel()` and passed in |
| `ngOnInit` | `MainViewModel.InitializeAsync()` |

Bindings are **compiled**: `AvaloniaUseCompiledBindingsByDefault` makes a typo in `{Binding Nmae}` a build error instead of an empty field on screen.

The pattern is called **MVVM** (Model-View-ViewModel). The Model is `Game`, the View is the `.axaml` file, and the ViewModel is the class the view binds to.
The `.axaml.cs` files have almost no code on purpose.

---

## 6. The reusable filter and grouping class

WPF had `ICollectionView` and `GroupStyle`. Avalonia has neither, so `Core/Collections/GroupedView.cs` does that job, and it's reusable for any kind of item.
It watches a list and keeps a filtered, sorted, grouped version of it up to date:

```csharp
var view = new GroupedView<GameViewModel>(Games)
{
    Filter = game => game.Name.Contains(searchText),
    Sort = Comparer<GameViewModel>.Create((a, b) => string.Compare(a.Name, b.Name)),
    GroupBy = game => game.Game.Drm,        // a game with two DRMs appears in both groups
};

view.Refresh();                             // call this when what Filter or GroupBy looks at has changed

using (view.DeferRefresh())                 // change several things, and only refresh once at the end
{
    view.Filter = ...;
    view.GroupBy = null;                    // null = no grouping: one group without a name
}
```

The window then just binds to it:

```xml
<ItemsControl ItemsSource="{Binding View.Groups}">    <!-- each group has Name, HasName, Items and Count -->
```

It has no window code in it, so `GroupedViewTests` can test it directly and it works in any project. When something changes it updates only what's different,
so cards stay in place instead of being rebuilt, and the scroll position stays where it was.

---

## 7. Light and dark mode

**Appearance** in the sidebar picks System, Light or Dark, and the choice is saved.

* **To change a colour,** edit `Theme/Colors.axaml`. It has one list for each mode, and the whole app follows. `Dark_and_light_mode_define_exactly_the_same_colours` fails if one list misses a colour.
* **Readable text is enforced by tests.** Every text colour is checked against every surface it's used on, to the WCAG standard (4.5:1, or 3:1 for large text).
  Then `ReadabilityCheck` goes further: it walks through what's really on screen and works out the colour behind each piece of text. That catches what a colour list can't.
* **The title bar** is handled by `Services/WindowFrame.cs`, because Avalonia only makes it dark on Windows 11. On Linux your desktop draws it and follows your desktop's own theme.

Building this found and fixed real problems that looked fine until each state was tried: the "Search games" text was too faint, the tick in a ticked box was nearly invisible in dark mode,
selected text in the search box was unreadable in both modes, the chosen row in an open dropdown was hard to read, and the title bar stayed white above a dark window on Windows 10.

---

## 8. How each feature works

### Finding games (`Scanning/`)
Each launcher keeps a list of installed games somewhere. Each one has its own file in `Scanning/Launchers/`:

| Launcher | Where its game list lives |
|---|---|
| Steam | `steamapps/libraryfolders.vdf` (lists every library drive), then one `appmanifest_<id>.acf` per game. Redistributables, SteamVR, Proton and the Steam Linux Runtime are skipped; they aren't games |
| Ubisoft Connect | Registry: `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs` |
| Epic | One JSON `.item` file per game in `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests` |
| GOG | Registry: `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games`. DLC is skipped, so Cyberpunk shows as "Cyberpunk 2077", not "Phantom Liberty" |
| Xbox app | An `XboxGames` folder on each drive |
| Battle.net | Windows' "Apps & features" list: games that uninstall with `Blizzard Uninstaller.exe` |
| EA app | "Apps & features": games that uninstall from `Common Files\EAInstaller\` |
| Rockstar Games Launcher | "Apps & features": publisher "Rockstar Games" with an `uninstall.exe -uninstall=<game>` |
| Heroic (Linux) | `~/.config/heroic` or its Flatpak folder: `legendaryConfig/legendary/installed.json` for Epic games, `gog_store/installed.json` for GOG games |
| Anything else | The **Add game...** button (you pick the `.exe`) |

`GameInspector.Inspect()` then looks through each game folder, up to 8 folders deep:

* **Frame Generation:** the game has `nvngx_dlssg.dll` or `sl.dlss_g.dll`. `sl.interposer.dll` doesn't count: it's NVIDIA Streamline's general loader, which games also use for plain DLSS and Reflex. Dead Space has it and no Frame Generation.
* **Anti-cheat:** a file or folder named like Easy Anti-Cheat, BattlEye, GameGuard, EA Javelin or XIGNCODE. Battle.net games always count, because Blizzard builds it into the game.
* **DRM from files:** `steam_api64.dll` means Steamworks, `uplay_r2_loader64.dll` and similar mean Ubisoft Connect, `EOSSDK-*.dll` means Epic Online Services.
* **DRM from the Steam store:** Denuvo, plus the game's account requirement (Ubisoft or EA). This is read from the store page's DRM notice.
* **The real .exe:** Unreal games' `*-Win64-Shipping.exe` comes first. Otherwise it uses the launcher's hint, otherwise
  the biggest .exe that isn't a launcher, crash reporter or uninstaller. A hint that points at a launcher (GOG's
  `REDprelauncher.exe` for Cyberpunk 2077) is skipped, and so is anything in a `tools` folder. You can override this with **Change .exe...**, as long as the .exe you pick is inside the game's folder, because the mod goes next to it.

### Cover art (`Store/SteamStore.cs`)
1. Steam's own art cache on your PC.
2. MFG Manager's cache (`cache/art` in its data folder).
3. Steam's public store API. It needs no account or key and also works for Ubisoft and Epic games that are sold on Steam, because it searches the store by name.

If there's no art, the card shows the game's initials.

### Installing and reverting (`Mod/ModInstaller.cs`)
The mod pretends to be a Windows DLL that games load from their own folder (`version.dll`, `dxgi.dll`, ...). The rules follow the mod's README:

* **Install** copies `MFGUnlocker.dll` next to the game's .exe. **Auto** uses the first supported name that is still free.
* **It never overwrites a game's or another mod's DLL.** If the name you picked is taken, it refuses. On Linux, `Version.dll` and `version.dll` count as the same name, as they do for the game.
* **Update** copies the newer DLL over the old one, using the same name.
* **Remove (revert)** deletes the mod DLL. It first checks that the file really is the mod: the DLL's built-in product name must be "MFG Unlocker". So it can never delete a game file, even if a game update reused the name.
* The mod's settings and logs (`RTXMFG*`) are kept by default. Tick the checkbox to move them to `Removed mod files` in the data folder; they're moved there, not deleted.
* **Anti-cheat:** installing into an anti-cheat game first warns that it can get your account banned, and only continues if you say yes. The game's card has an **Anti-cheat** label, and its details panel has a warning. **Remove** takes the mod out of one game, and **Revert all** takes it out of every game.
* **No Frame Generation found:** you can still install, but it asks first, because the mod probably has nothing to unlock. It isn't blocked, since the mod's experimental XeSS mode can work without DLSS Frame Generation.
* Every install is recorded in `settings.json`. Copies you installed by hand are found too.

### Updates (`Mod/UpdateChecker.cs`)
* **GitHub is checked exactly once, when you open the app.** It asks for the latest release of `dashdogy/MFG-Unlocker`. There's no timer or background checking.
* If it's newer than your copy, a blue banner offers **Download and verify**. Nothing is downloaded until you click.
* The zip is checked against the **SHA-256 fingerprint GitHub publishes for it**. If they don't match, the file is deleted.
* It's then unzipped into `Mods`, refusing any "escape the folder" paths inside the zip, with limits on size and file count.
* **Choose DLL...** also copies the DLL you pick into `Mods`, so the app never depends on files scattered elsewhere.

### When it scans and writes
Nothing runs in the background: there are no timers, file watchers, services or scheduled tasks, and the registry is only read.
A full game scan only happens when the app opens and when you press **Rescan**. `ScanRulesTests` fails if a later change starts one from anywhere else.

| When | Scans or checks | Writes |
|---|---|---|
| You open the app | `Mods` and Downloads (up to two folders deep, only for `MFGUnlocker.dll`), every launcher, the Steam store for games it doesn't know yet, and GitHub for the latest mod version | The mod DLL into `Mods`, only after you said yes. New cover art, once per game. `cache/store-cache.json` once, only if a game was new |
| **Rescan** | Every launcher and the Steam store for games it doesn't know yet. **Not** GitHub, `Mods` or Downloads | New cover art and `cache/store-cache.json`, only if a game was new |
| **Add game...** | That one game and its Steam store info, then the mod status of every game | `settings.json`, and new cover art and `cache/store-cache.json` if the game is new |
| **Change .exe...** | That one game, then the mod status of every game | `settings.json` |
| **Install / Update / Remove / Revert** | Whether the game is running, then the mod status of every game | The game folder and `settings.json` |
| **Choose DLL...** | The DLL you pick, then the mod status of every game | `Mods` and `settings.json` |
| **Download and verify** | Downloads the release's checksum and zip from GitHub, then the mod status of every game | `Mods` and `settings.json` |
| **Appearance** | Nothing | `settings.json` |
| Selecting a game, searching, filtering, grouping, switching back to the window | Nothing | Nothing |

"Mod status" only looks in the folder next to each game's .exe, not its subfolders, and needs no internet.

* **Changes made while the app is open** aren't noticed on their own. A newly installed game, or a Steam file repair that removes the mod, shows after **Rescan**. A new mod release on GitHub, or a new DLL in Downloads, shows after you restart the app.
* **The Steam store is asked again** for a new game, after a lookup that failed (for example with no internet), and at every start and Rescan for a game that has a Steam ID but no cover on Steam. Games it found nothing for are remembered as "not found" and aren't asked again.

---

## 9. Safety and antivirus: honest notes

**Nobody can promise that no antivirus will ever flag a program.** Antivirus tools judge new programs by what they do,
and "a program that runs as admin and puts `version.dll` files into game folders" looks a bit like DLL hijacking. The app itself only does plain, visible things:

| What it does | Why it's fine |
|---|---|
| Reads launcher lists (registry, files) | Read-only |
| Copies one DLL into a game folder, deletes it again | Only the mod; checked by product name before deleting |
| Talks to `api.github.com`, `github.com`, `store.steampowered.com`, `api.steampowered.com`, `shared.akamai.steamstatic.com` | HTTPS only; no personal data is sent |
| Opens web links through Explorer or `xdg-open` | So your browser does **not** run as admin, and no shell is involved |

It also has no packing or obfuscation, no hidden windows, no startup entries, scheduled tasks or services, no injecting into other programs, and it never asks you to add antivirus exclusions.

**Security check (3 Nov 2026, v2.0.4):**

* **Secrets:** none, and nothing needs any. No `.env`, keys, tokens, passwords or certificates in the code, project files or build scripts. Building needs no accounts.
* **Packages:** none of the 44 packages (app and tests, including everything they bring along) has a known security problem, checked with both
  `dotnet list package --vulnerable --include-transitive` and the OSV database.
* **Telemetry:** none in the app. The Avalonia build reporting is blocked, and both build scripts turn off .NET's and Avalonia's reporting. No developer tools are shipped.
* **Anything from outside is treated with care,** because the app runs as admin on Windows:
  * a newer `MFGUnlocker.dll` in Downloads is never used silently; the app asks first (a DLL's "MFG Unlocker" name is easy to fake)
  * a DLL path in `settings.json` is only trusted if it points inside the app's `Mods` folder
  * a saved install name only counts if it's a name the mod supports, and a saved .exe only counts if it's inside the game's folder, so a changed `settings.json` can't send the mod somewhere else
  * the mod is never put in the Windows folder, whatever a launcher's files or `settings.json` say
  * a damaged `settings.json`, a strange answer from GitHub or a DLL with a nonsense version number can't stop the app or its scan
  * release builds ignore the `DOTNET_STARTUP_HOOKS` and `DOTNET_ROOT` environment variables, which any program you run could set to load its own code into the app as admin. .NET has other variables like these that can't be turned off, so this reduces the risk rather than removing it. Malware already running on your account is hard to fully stop while an app runs as admin
  * web addresses from GitHub's answer must be `https://`, and the release page must be on `https://github.com/`
  * downloads and zips have size limits, and the code that reads DLLs checks every offset, so a broken file can't crash it or make it read outside the file
* **On Linux** it refuses to run as root, its folders are private to you, and the install scripts refuse root too and download nothing.
* **The Windows installer:** all users, `C:\Program Files\MFG Manager`, so only admins can change the app or its `Mods` folder. No services, startup entries, scheduled tasks, PATH changes or scripts.
  It isn't code-signed (that needs a paid certificate), so Windows may warn about an "unknown publisher" when you copy it to another PC.
  * Every action inside the .msi runs as you, only while the setup window is open. None runs as SYSTEM, so a "repair" started by a non-admin can't be used to get admin rights.
  * The uninstall clean-up only deletes the `Mods` path stored in `HKLM\Software\MFG Manager`, which only admins can change.
  * Inside are 38 files: no debug files, no bundled mod, nothing for Linux.
* **Virus scan:** Microsoft Defender found no threats in the .msi, the Windows files, the Linux files or the Linux archive.

**If a scanner ever flags MFG Manager**, report it to that scanner's maker as a false positive (for Microsoft: microsoft.com/wdsi/filesubmission). **Code-signing** the installer is what really reduces warnings.

---

## 10. Tests

Run them from the `MfgManager` folder:

```bash
dotnet test --project tests/MfgManager.Tests
```

The **310 tests** never use the internet or your real games. A fake "web" and fake game folders are used instead. 4 of them only run when you ask, as described below: 2 scan your real games and 2 redraw the pictures in `docs/`.

* **`Core/`:** finding games in every launcher on fake folders, the game inspector, the mod installer, updates and tampered downloads, reading DLL versions, the reusable `GroupedView`, Linux paths, and the Linux and Windows packaging files
* **`ViewModels/`:** filters, grouping, every button's rules, the anti-cheat question, and `ScanRulesTests`: it counts full scans and GitHub checks while pressing every button and really clicking through the window, and it fails if the source gains a timer, a file watcher or a new place that starts a scan
* **`Components/`:** every component really drawn in both modes, without opening a window:
  * `ComponentTests`: each one shows the right things, and all its text is readable
  * `StateTests`: really moves the mouse over every button, presses them, ticks boxes, opens dropdowns, selects text, and checks the colours in each of those states, plus the smallest allowed window
  * `ThemeContrastTests`: every colour pair, in both modes

To get a picture of every component in both modes:

```bash
MFG_SHOTS=/some/folder dotnet test --project tests/MfgManager.Tests
```

To also scan this computer's real games and draw them. It uses the internet to look up covers, and changes nothing real: your settings are copied to a temporary folder first, and no button is pressed:

```bash
MFG_REAL_SCAN=1 MFG_SHOTS=/some/folder dotnet test --project tests/MfgManager.Tests
```

To redraw the pictures in `docs/` from made-up games, so they show no real username, library or cover art (it needs Windows, and creates and deletes `C:\Demo Games`):

```bash
MFG_DOCS=/full/path/to/docs dotnet test --project tests/MfgManager.Tests
```

(In PowerShell, set them first: `$env:MFG_SHOTS="C:\temp\shots"`.)

---

## 11. Known limits

* **Linux hasn't run on a real desktop yet.** From Windows, the Linux version was built and confirmed to be a real Linux program with all its parts. Its install, uninstall and package scripts were run with a throw-away home folder, with stand-ins for the two steps Windows can't do (real `dpkg-deb` and links into `/opt`). The Linux code paths are covered by tests. But the window itself, Proton, and the real package tools have only been tested once you run them there. If anything's off when you move, that's where to look first.
* **Launchers only tested with fake folders:** Epic and Heroic.
* **Xbox app games** are found, but their .exe often sits in a protected folder the scan can't read; use **Change .exe...** if the details panel says no .exe was found. Add-ons can also show up as games.
* **Uninstalling MFG Manager doesn't take the mod out of your games.** Click **Revert all** first if you want them clean.
* **Denuvo** is only detected for games sold on Steam, because it comes from the Steam store page.
* **Mint 21 and other older systems** need Microsoft's backports to build it, as shown in section 2.

---

## 12. Building it yourself

```bash
dotnet build
```

To build the Windows installer (`artifacts\MFG-Manager-<version>.msi`). It publishes the app without debug files, which would contain the build computer's folder paths, and it stops with an error if the program doesn't ask for admin rights. WiX comes from nuget.org the first time and isn't installed on the computer:

```bash
powershell -File tools\build-installer.ps1
```

For a new version, raise `<Version>` in `Directory.Build.props` and `pkgver` in `packaging/linux/PKGBUILD` (a test fails if they differ), and build again. Installing the new .msi replaces the old version.

To change the logo, edit the colours at the top of `tools/make-icon.ps1`, then run this, and `make-installer-art.ps1` for the wizard's pictures:

```bash
powershell -File tools\make-icon.ps1 -OutFolder src\MfgManager.App\Assets
```

```bash
powershell -File tools\make-installer-art.ps1 -OutFolder installer\Assets
```

While developing, you can skip the admin prompt. The next normal build puts it back by itself:

```bash
dotnet run --project src/MfgManager.App -p:RequireAdmin=false
```

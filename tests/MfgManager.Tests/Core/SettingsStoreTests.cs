using MfgManager.Core.Settings;

namespace MfgManager.Tests.Core;

public class SettingsStoreTests
{
    [Fact]
    public void Saves_and_loads_everything()
    {
        using var temp = new TempFolder();
        var store = new SettingsStore(Path.Combine(temp.Path, "settings.json"));
        var settings = new AppSettings { ModDllPath = """C:\mod.dll""" };
        settings.ManualGames.Add("""D:\Game\game.exe""");
        settings.ExeOverrides["""D:\Game"""] = """D:\Game\bin\game.exe""";
        settings.Installs.Add(new InstallRecord("""D:\Game\bin""", "version.dll", "1.7.0", new DateTime(2026, 11, 1)));

        store.Save(settings);
        AppSettings loaded = store.Load();

        Assert.Equal("""C:\mod.dll""", loaded.ModDllPath);
        Assert.Equal(settings.ManualGames, loaded.ManualGames);
        // Folder lookups ignore case.
        Assert.Equal("""D:\Game\bin\game.exe""", loaded.ExeOverrides["""d:\game"""]);
        Assert.Equal(settings.Installs, loaded.Installs);
    }

    [Fact]
    public void A_file_saved_by_2_0_5_loads_and_is_written_back_the_same_way()
    {
        // Exactly what 2.0.5 wrote, before the JSON code was generated while building.
        const string Saved205 = """
            {
              "Theme": 2,
              "ModDllPath": "C:\\Mods\\MFGUnlocker.dll",
              "ManualGames": [
                "D:\\Game\\game.exe"
              ],
              "ExeOverrides": {
                "D:\\Game": "D:\\Game\\bin\\game.exe"
              },
              "Installs": [
                {
                  "Folder": "D:\\Game\\bin",
                  "DllName": "version.dll",
                  "ModVersion": "1.7.0",
                  "InstalledAt": "2026-11-01T00:00:00"
                }
              ]
            }
            """;
        using var temp = new TempFolder();
        string file = temp.File("settings.json", Saved205);
        var store = new SettingsStore(file);

        AppSettings loaded = store.Load();
        Assert.Equal(ThemeChoice.Dark, loaded.Theme);
        Assert.Equal("""C:\Mods\MFGUnlocker.dll""", loaded.ModDllPath);
        Assert.Equal(["""D:\Game\game.exe"""], loaded.ManualGames);
        Assert.Equal("""D:\Game\bin\game.exe""", loaded.ExeOverrides["""D:\Game"""]);
        Assert.Equal(new InstallRecord("""D:\Game\bin""", "version.dll", "1.7.0", new DateTime(2026, 11, 1)), Assert.Single(loaded.Installs));

        store.Save(loaded);
        Assert.Equal(Saved205.ReplaceLineEndings(), File.ReadAllText(file).ReplaceLineEndings());
    }

    [Fact]
    public void Broken_or_missing_file_gives_empty_settings()
    {
        using var temp = new TempFolder();
        string path = temp.File("settings.json", "{ broken");

        Assert.Empty(new SettingsStore(path).Load().Installs);
        Assert.Empty(new SettingsStore(Path.Combine(temp.Path, "missing.json")).Load().Installs);
    }

    [Theory]
    [InlineData("""{"ExeOverrides":null,"Installs":null,"ManualGames":null}""")]
    [InlineData("""{"Installs":[null,{"Folder":null,"DllName":"version.dll"},{"Folder":"","DllName":""},{"Folder":"D:\\a\u0000b","DllName":"version.dll"}]}""")]
    [InlineData("""{"ManualGames":[null,"","D:\\a\u0000b.exe"],"ExeOverrides":{"D:\\game":null}}""")]
    [InlineData("""{"Theme":99,"ModDllPath":"D:\\a\u0000b.dll"}""")]
    public void Valid_json_with_nonsense_in_it_gives_clean_settings(string json)
    {
        using var temp = new TempFolder();

        AppSettings loaded = new SettingsStore(temp.File("settings.json", json)).Load();

        Assert.Empty(loaded.Installs);
        Assert.Empty(loaded.ManualGames);
        Assert.Empty(loaded.ExeOverrides);
        Assert.Null(loaded.ModDllPath);
        Assert.Equal(ThemeChoice.System, loaded.Theme);
    }

    [Fact]
    public void Cleaning_keeps_the_good_entries()
    {
        using var temp = new TempFolder();
        string json = """{"Installs":[null,{"Folder":"D:\\game","DllName":"version.dll","ModVersion":"1.7.0"}],"ManualGames":["","D:\\game\\a.exe"],"ExeOverrides":{"D:\\game":"D:\\game\\a.exe","D:\\other":null},"Theme":2}""";

        AppSettings loaded = new SettingsStore(temp.File("settings.json", json)).Load();

        Assert.Equal("""D:\game""", Assert.Single(loaded.Installs).Folder);
        Assert.Equal("""D:\game\a.exe""", Assert.Single(loaded.ManualGames));
        Assert.Equal("""D:\game\a.exe""", Assert.Single(loaded.ExeOverrides).Value);
        Assert.Equal(ThemeChoice.Dark, loaded.Theme);
    }

    [Fact]
    public void Two_folder_names_that_differ_only_in_case_dont_stop_the_app()
    {
        using var temp = new TempFolder();
        string json = """{"ExeOverrides":{"D:\\Game":"D:\\Game\\a.exe","d:\\game":"d:\\game\\b.exe"}}""";

        AppSettings loaded = new SettingsStore(temp.File("settings.json", json)).Load();

        Assert.NotEmpty(loaded.ExeOverrides);
    }
}

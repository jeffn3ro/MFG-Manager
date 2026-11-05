using MfgManager.Core.Settings;

namespace MfgManager.Tests.Core;

public class SettingsThemeTests
{
    [Fact]
    public void Theme_is_saved_and_old_settings_files_still_load()
    {
        using var temp = new TempFolder();
        string path = Path.Combine(temp.Path, "settings.json");
        var store = new SettingsStore(path);

        store.Save(new AppSettings { Theme = ThemeChoice.Light });
        Assert.Equal(ThemeChoice.Light, store.Load().Theme);

        // Written by 1.0.x.
        File.WriteAllText(path, """{ "ModDllPath": null, "ManualGames": [], "ExeOverrides": {}, "Installs": [] }""");
        Assert.Equal(ThemeChoice.System, store.Load().Theme);
    }
}

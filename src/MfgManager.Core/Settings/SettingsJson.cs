using System.Text.Json.Serialization;

namespace MfgManager.Core.Settings;

/// <summary>
/// The code that reads and writes settings.json, generated while building. Without it, .NET works that code out
/// while the app starts, before the window can show.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;

using System.Text.Json.Serialization;

namespace MfgManager.Core.Store;

/// <summary>The code that reads and writes the store cache file, generated while building instead of at start-up.</summary>
[JsonSerializable(typeof(SteamStore.StoreCache))]
internal sealed partial class StoreJson : JsonSerializerContext;

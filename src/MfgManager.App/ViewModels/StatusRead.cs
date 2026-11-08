using MfgManager.Core.Mod;

namespace MfgManager.App.ViewModels;

/// <summary>What <see cref="GameViewModel.ReadStatus"/> found on disk for one game, ready to show with <see cref="GameViewModel.Apply"/>.</summary>
/// <param name="FreeNames">The mod's file names the game folder doesn't use yet. Only read when the mod isn't in the game.</param>
public sealed record StatusRead(ModStatus Status, IReadOnlyList<string> FreeNames);

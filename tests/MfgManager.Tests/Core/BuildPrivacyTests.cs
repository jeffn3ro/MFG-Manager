namespace MfgManager.Tests.Core;

/// <summary>
/// Avalonia.BuildServices sends build information to Avalonia's servers when you compile. The projects exclude it;
/// this checks that its code really isn't part of the build, so a future package update can't turn it back on.
/// </summary>
public class BuildPrivacyTests
{
    public static readonly TheoryData<string> Projects = new(@"src/MfgManager.App/obj/MfgManager.App.csproj.nuget.g.targets", @"tests/MfgManager.Tests/obj/MfgManager.Tests.csproj.nuget.g.targets");

    [Theory]
    [MemberData(nameof(Projects))]
    public void Build_telemetry_is_not_part_of_the_build(string targetsFile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MfgManager.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var targets = File.ReadAllText(Path.Combine(dir.FullName, targetsFile));

        Assert.DoesNotContain("avalonia.buildservices", targets, StringComparison.OrdinalIgnoreCase);
    }
}

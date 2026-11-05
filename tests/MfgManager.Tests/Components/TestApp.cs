using Avalonia;
using Avalonia.Headless;
using MfgManager.App;
using MfgManager.Tests.Components;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace MfgManager.Tests.Components;

/// <summary>Runs the real app with real drawing, but without opening anything on screen.</summary>
public static class TestApp
{
    /// <summary>Runs a test on the app's UI thread. Write tests as: <c>public Task Name() => TestApp.Run(() => { ... });</c></summary>
    public static Task Run(Action test) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(TestApp).Assembly).Dispatch(test, TestContext.Current.CancellationToken);

    /// <summary>
    /// The same, for tests that wait for something. The result value is what makes Dispatch wait for the whole test:
    /// given a plain Task it stops at the first wait, and anything that fails after that goes unnoticed.
    /// </summary>
    public static Task Run(Func<Task> test) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(TestApp).Assembly).Dispatch(async () =>
        {
            await test();
            return true;
        }, TestContext.Current.CancellationToken);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App.App>()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

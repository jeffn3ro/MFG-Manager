using MfgManager.Core;

namespace MfgManager.Tests.Core;

public class SideBySideTests
{
    [Fact]
    public void Every_item_runs_exactly_once()
    {
        int[] runs = new int[200];

        SideBySide.For(runs.Length, i => Interlocked.Increment(ref runs[i]));

        Assert.All(runs, count => Assert.Equal(1, count));
    }

    [Fact]
    public void An_error_comes_through_as_it_was_not_wrapped()
    {
        // Code that calls this catches IOException and friends, so a wrapper would slip past those catches.
        IOException error = Assert.Throws<IOException>(() => SideBySide.For(20, i =>
        {
            if (i == 7) throw new IOException("disk said no");
        }));

        Assert.Equal("disk said no", error.Message);
    }

    [Fact]
    public void Nothing_to_do_runs_nothing()
    {
        bool ran = false;

        SideBySide.For(0, _ => ran = true);

        Assert.False(ran);
    }
}

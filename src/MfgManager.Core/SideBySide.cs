using System.Runtime.ExceptionServices;

namespace MfgManager.Core;

/// <summary>
/// Runs the same work for many items at the same time. Used for disk work, like looking through each game's folder:
/// that's mostly waiting on the disk, and an SSD can answer many requests at once.
/// </summary>
public static class SideBySide
{
    /// <summary>Calls <paramref name="action"/> for 0 to <paramref name="count"/> - 1 at the same time, and returns when all are done.</summary>
    /// <remarks>
    /// .NET wraps an error from any of them in an AggregateException. This passes on the first one as it was,
    /// like a normal loop would, so the code that catches errors doesn't have to know the work ran side by side.
    /// </remarks>
    public static void For(int count, Action<int> action)
    {
        try
        {
            Parallel.For(0, count, action);
        }
        catch (AggregateException ex)
        {
            ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
        }
    }
}

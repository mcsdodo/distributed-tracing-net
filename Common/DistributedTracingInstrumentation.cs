using System.Diagnostics;

namespace Common;

public static class DistributedTracingInstrumentation
{
    static DistributedTracingInstrumentation()
    {
        Source = new(AppDomain.CurrentDomain.FriendlyName);
    }
    public static ActivitySource Source { get; private set; }
    public static string ActivitySourceName => Source.Name;
}
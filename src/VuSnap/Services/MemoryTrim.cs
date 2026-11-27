namespace VuSnap.Services;

/// <summary>Compatibility shim: leave collection and working-set management to the runtime/OS.
/// Forced full GC on the UI dispatcher caused avoidable pause risks.</summary>
public static class MemoryTrim
{
    public static void Schedule(int delayMs = 1500) { }
    public static void Now() { }
}

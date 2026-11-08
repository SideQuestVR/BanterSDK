using System;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--peer") return Peer.Run();
        int n = int.TryParse(Environment.GetEnvironmentVariable("BRIDGE_FUZZ_N"), out var nn) ? nn : 100_000;
        uint seed = uint.TryParse(Environment.GetEnvironmentVariable("BRIDGE_FUZZ_SEED"), out var ss) ? ss : (uint)Environment.TickCount;
        T.Run("the core exports no public types (kept off the SDK's public surface and Visual Scripting)", () =>
        {
            var exported = typeof(BS.Bridge.BridgeView).Assembly.GetExportedTypes();
            T.Eq(0, exported.Length, "public: " + string.Join(", ", System.Linq.Enumerable.Select(exported, t => t.FullName)));
        });
        VectorTests.Run();
        SimTests.Run();
        OutboxTests.Run();
        ReviewTests.Run();
        RereviewTests.Run();
        DrainQueueTests.Run();
        CallTests.Run();
        PushTests.Run();
        FuzzTests.Run(n, seed);
        CallTests.Fuzz(n, seed);
        PushTests.Fuzz(n, seed);
        Console.WriteLine($"\n{T.Passed} passed, {T.Failed} failed");
        foreach (var f in T.Failures) Console.WriteLine("  FAILED: " + f);
        return T.Failed == 0 ? 0 : 1;
    }
}

using NUnitLite;

namespace VideoStreamTests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "extract") return Tools.Extract(args[1], args[2]);
            if (args.Length == 2 && args[0] == "requests") return Probe.Requests(args[1]);
            if (args.Length == 4 && args[0] == "audio") return Tools.AudioOnly(args[1], long.Parse(args[2]), long.Parse(args[3]));
            return new AutoRun(typeof(Program).Assembly).Execute(args);
        }
    }
}

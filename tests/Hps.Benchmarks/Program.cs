using System;
using BenchmarkDotNet.Running;

namespace Hps.Benchmarks
{
    internal static class Program
    {
        private const int SuccessExitCode = 0;

        public static int Main(string[] args)
        {
            if (args.Length == 1 && string.Equals(args[0], "--target", StringComparison.OrdinalIgnoreCase))
            {
                BenchmarkTargets.Print(Console.Out);
                return SuccessExitCode;
            }

            if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase))
            {
                PrintUsage();
                return SuccessExitCode;
            }

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
            return SuccessExitCode;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("사용법:");
            Console.WriteLine("  Hps.Benchmarks --target");
            Console.WriteLine("  Hps.Benchmarks [BenchmarkDotNet arguments]");
        }
    }
}

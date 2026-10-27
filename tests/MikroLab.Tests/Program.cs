using System;

namespace MikroLab.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("  MikroLab · KR580VM80A / Intel 8080 test suite");
            Console.WriteLine("=================================================\n");

            int failures = 0;
            failures += Run("CoreTests", CoreTests.RunAll);
            failures += Run("AssemblerTests", AssemblerTests.RunAll);
            failures += Run("ExtendedCpuTests", ExtendedCpuTests.RunAll);
            failures += Run("StudioIntegrationTests", StudioIntegrationTests.RunAll);

            Console.WriteLine("=================================================");
            if (failures == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  ALL TEST SUITES PASSED (0 failures)");
                Console.ResetColor();
                Console.WriteLine("=================================================");
                return 0;
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  TEST SUITE COMPLETED WITH {failures} FAILURES");
            Console.ResetColor();
            Console.WriteLine("=================================================");
            return 1;
        }

        private static int Run(string name, Action suite)
        {
            try
            {
                suite();
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] {name}: {ex.Message}\n{ex.StackTrace}\n");
                Console.ResetColor();
                return 1;
            }
        }
    }
}

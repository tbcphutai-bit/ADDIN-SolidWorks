using System;

namespace ADDIN.AutoFrameJointResearch
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length!=0 && (args.Length!=1 || args[0]!="--self-test"))
                {
                    Console.Error.WriteLine("Usage: AutoFrameJointResearch.exe [--self-test]");
                    return 2;
                }
                int passed=FrameJointGeometryTests.Run();
                passed+=FrameJointNumericTests.Run();
                Console.WriteLine("SUCCESS: "+passed+" pure-geometry assertions.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }
    }
}

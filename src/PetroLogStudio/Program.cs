using System;
using System.IO;
using PetroLogStudio.Parsing;

namespace PetroLogStudio
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string testFilePath = @"C:\Users\Legion5\Zteelers\My Project eiei\test-swe-intern\TestData\39399.LAS";

            if (!File.Exists(testFilePath))
            {
                Console.WriteLine($"[ERROR] File not found at: {testFilePath}");
                return;
            }

            Console.WriteLine($"Testing LasParser with: {Path.GetFileName(testFilePath)}");
            Console.WriteLine(new string('-', 50));

            var parser = new LasParser();
            var well = parser.ParseFile(testFilePath);

            Console.WriteLine("\n[1] Well Header Information:");
            Console.WriteLine($"Well Name   : {well.WellName}");
            Console.WriteLine($"Null Value  : {well.NullValue}");
            Console.WriteLine($"Start Depth : {well.StartDepth}");
            Console.WriteLine($"Stop Depth  : {well.StopDepth}");
            Console.WriteLine($"Step        : {well.Step}");

            Console.WriteLine($"\n[2] Curves Discovered ({well.Curves.Count} total):");
            foreach (var kvp in well.Curves)
            {
                var def = kvp.Value.Definition;
                Console.WriteLine($"  - Col {def.ColumnIndex}: {def.Mnemonic,-10} | Unit: {def.Unit,-8} | Desc: {def.Description}");
            }

            Console.WriteLine("\n[3] Data Section Verification:");
            if (well.Curves.ContainsKey("DEPT"))
            {
                var deptCurve = well.Curves["DEPT"];
                Console.WriteLine($"Total Depth Rows: {deptCurve.Values.Count}");
                if (deptCurve.Values.Count > 0)
                {
                    Console.WriteLine($"First Depth Value: {deptCurve.Values[0]}");
                    Console.WriteLine($"Last Depth Value : {deptCurve.Values[^1]}");
                }
            }
            else
            {
                Console.WriteLine("Warning: 'DEPT' mnemonic not found directly. Check available curve keys.");
            }

            Console.WriteLine("\n[4] Parse Errors & Warnings:");
            if (well.ParseErrors.Count == 0)
            {
                Console.WriteLine("No parsing errors encountered! Clean parse.");
            }
            else
            {
                Console.WriteLine($"Total errors: {well.ParseErrors.Count}");
                foreach (var err in well.ParseErrors)
                {
                    Console.WriteLine($"  ! {err}");
                }
            }
        }
    }
}
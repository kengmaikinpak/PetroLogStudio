using System;
using System.IO;
using PetroLogStudio.Parsing;
using PetroLogStudio.Conditioning;
using PetroLogStudio.Plotting;
using PetroLogStudio.Batch;

namespace PetroLogStudio
{
	internal class Program
	{
		static string FindTestDataDirectory()
		{
			var currentDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
			while (currentDir != null)
			{
				string candidate = Path.Combine(currentDir.FullName, "TestData");
				if (Directory.Exists(candidate))
				{
					return candidate;
				}
				currentDir = currentDir.Parent;
			}
			return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");
		}

		static string GetTestDataPath(string fileName)
		{
			return Path.Combine(FindTestDataDirectory(), fileName);
		}

		static void Main(string[] args)
		{
			string fileName = "s_b080a52a6437d8989146ff25fdd6e124.las";
			string testFilePath = GetTestDataPath(fileName);

			// 1. Verify file existence before processing
			if (!File.Exists(testFilePath))
			{
				Console.WriteLine($"[ERROR] File not found at: {testFilePath}");
				return;
			}

			Console.WriteLine($"Testing LasParser with: {Path.GetFileName(testFilePath)}");
			Console.WriteLine(new string('-', 50));

			// 2. Parse & Condition well data
			var parser = new LasParser();
			var well = parser.ParseFile(testFilePath);

			var conditioner = new DataConditioner();
			conditioner.ConditionWell(well);

			// 3. Print diagnostic info to Console
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

			Console.WriteLine("\n[4] Parser Diagnostics:");
			if (well.ParseErrors.Count == 0 && well.ParseNotices.Count == 0)
			{
				Console.WriteLine("No parsing errors or notices encountered! Clean parse.");
			}
			else
			{
				if (well.ParseErrors.Count > 0)
				{
					Console.WriteLine($"Errors ({well.ParseErrors.Count}):");
					foreach (var err in well.ParseErrors)
					{
						Console.WriteLine($"  ! [Error] {err}");
					}
				}

				if (well.ParseNotices.Count > 0)
				{
					Console.WriteLine($"Notices ({well.ParseNotices.Count}):");
					foreach (var note in well.ParseNotices)
					{
						Console.WriteLine($"  * [Notice] {note}");
					}
				}
			}

			Console.WriteLine("\n[5] Data Conditioning Report:");
			foreach (var log in well.ConditioningLogs)
			{
				Console.WriteLine($"  * {log}");
			}

			if (well.Curves.TryGetValue("DEPT", out var conditionedDept) && conditionedDept.Values.Count > 0)
			{
				Console.WriteLine($"\n[6] Post-Conditioning Depth Verification:");
				Console.WriteLine($"  First Depth: {conditionedDept.Values[0]}");
				Console.WriteLine($"  Last Depth : {conditionedDept.Values[^1]}");
			}

			// 4. Generate composite log plot
			var plotter = new LogPlotter();
			plotter.GenerateWellPlot(well, "output_plots");

			// Optional: Entry point for batch processing all LAS files in TestData
			var batch = new BatchProcessor();
			batch.ProcessDirectory(FindTestDataDirectory(), @"./outputFolder");
		}
	}
}
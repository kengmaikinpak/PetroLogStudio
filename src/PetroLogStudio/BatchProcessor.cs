using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PetroLogStudio.Conditioning;
using PetroLogStudio.Models;
using PetroLogStudio.Parsing;
using PetroLogStudio.Plotting;

namespace PetroLogStudio.Batch
{
	public record BatchItemResult(
		string FileName,
		string WellName,
		bool IsSuccess,
		long ElapsedMs,
		int TotalRows,
		List<string> MappedCurves,
		List<string> Issues,
		string ErrorDetails = ""
	);

	public class BatchProcessor
	{
		private readonly LasParser _parser = new();
		private readonly DataConditioner _conditioner = new();
		private readonly LogPlotter _plotter = new();

		public void ProcessDirectory(string inputDirectory, string outputDirectory)
		{
			var lasFiles = Directory.GetFiles(inputDirectory, "*.las");
			Console.WriteLine($"Found {lasFiles.Length} LAS files for processing.\n");

			var results = new List<BatchItemResult>();
			string plotDir = Path.Combine(outputDirectory, "plots");
			Directory.CreateDirectory(plotDir);

			var stopwatch = new Stopwatch();

			for (int i = 0; i < lasFiles.Length; i++)
			{
				string filePath = lasFiles[i];
				string fileName = Path.GetFileName(filePath);
				Console.Write($"[{i + 1}/{lasFiles.Length}] Processing: {fileName,-45} ");

				stopwatch.Restart();
				var issues = new List<string>();

				try
				{
					// 1. Parsing
					var well = _parser.ParseFile(filePath);
					issues.AddRange(well.ParseErrors.Select(e => $"[Error] {e}"));
					issues.AddRange(well.ParseNotices.Select(n => $"[Notice] {n}"));

					// 2. Conditioning
					_conditioner.ConditionWell(well);
					issues.AddRange(well.ConditioningLogs.Where(l => l.Contains("[Warning]") || l.Contains("[Error]")));

					// 3. Plotting
					_plotter.GenerateWellPlot(well, plotDir);

					stopwatch.Stop();

					var mapped = new List<string>();
					foreach (var key in new[] { "DEPT", "GR", "RES", "RHOB", "NPHI" })
					{
						if (well.Curves.ContainsKey(key)) mapped.Add(key);
					}

					results.Add(new BatchItemResult(
						FileName: fileName,
						WellName: string.IsNullOrWhiteSpace(well.WellName) ? "-" : well.WellName,
						IsSuccess: true,
						ElapsedMs: stopwatch.ElapsedMilliseconds,
						TotalRows: well.Curves.TryGetValue("DEPT", out var d) ? d.Values.Count : 0,
						MappedCurves: mapped,
						Issues: issues
					));

					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine($"[SUCCESS] ({stopwatch.ElapsedMilliseconds} ms)");
					Console.ResetColor();
				}
				catch (Exception ex)
				{
					stopwatch.Stop();
					issues.Add($"Fatal: {ex.Message}");

					results.Add(new BatchItemResult(
						FileName: fileName,
						WellName: "-",
						IsSuccess: false,
						ElapsedMs: stopwatch.ElapsedMilliseconds,
						TotalRows: 0,
						MappedCurves: new List<string>(),
						Issues: issues,
						ErrorDetails: ex.Message
					));

					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine($"[FAILED] ({ex.Message})");
					Console.ResetColor();
				}
			}

			GenerateSummaryReport(results, outputDirectory);
		}

		private void GenerateSummaryReport(List<BatchItemResult> results, string outputDirectory)
		{
			string reportPath = Path.Combine(outputDirectory, "batch_report.md");
			using var writer = new StreamWriter(reportPath);

			writer.WriteLine("# PetroLogStudio Batch Processing Report");
			writer.WriteLine($"**Generated at:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

			int total = results.Count;
			int success = results.Count(r => r.IsSuccess);
			int failed = total - success;

			writer.WriteLine($"* **Total Files:** {total}");
			writer.WriteLine($"* **Successfully Processed:** {success}");
			writer.WriteLine($"* **Failed:** {failed}\n");

			writer.WriteLine("## Detailed Results\n");
			writer.WriteLine("| File Name | Well Name | Status | Rows | Standard Curves | Issues / Notices | Error Details | Time (ms) |");
			writer.WriteLine("|:---|:---|:---:|:---:|:---|:---|:---|:---:|");

			foreach (var r in results)
			{
				string status = r.IsSuccess ? "Success" : "Failed";
				string curves = string.Join(", ", r.MappedCurves);
				string issueSummary = r.Issues.Count > 0
					? string.Join("<br>", r.Issues.Select(i => i.Replace("|", "\\|")))
					: "None";
				string errorDetails = string.IsNullOrEmpty(r.ErrorDetails) ? "-" : r.ErrorDetails.Replace("|", "\\|");

				writer.WriteLine($"| {r.FileName} | {r.WellName.Replace("|", "\\|")} | {status} | {r.TotalRows} | {curves} | {issueSummary} | {errorDetails} | {r.ElapsedMs} |");
			}

			Console.WriteLine($"\nBatch run completed. Summary report written to: {reportPath}");
		}
	}
}
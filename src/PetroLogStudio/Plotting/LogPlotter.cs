using System;
using System.IO;
using System.Linq;
using PetroLogStudio.Models;
using ScottPlot;

namespace PetroLogStudio.Plotting
{
	/// <summary>
	/// Generates petrophysical well log composite plots using ScottPlot.
	/// </summary>
	public class LogPlotter
	{
		public void GenerateWellPlot(WellLog well, string outputDirectory)
		{
			if (!well.Curves.TryGetValue("DEPT", out var deptCurve) || deptCurve.Values.Count == 0)
			{
				Console.WriteLine($"[Plot Error] Well '{well.WellName}' does not contain depth data. Plotting skipped.");
				return;
			}

			double[] depths = deptCurve.Values.Select(v => v ?? double.NaN).ToArray();
			var validDepths = depths.Where(d => !double.IsNaN(d)).ToList();

			if (validDepths.Count == 0) return;

			double minDepth = validDepths.Min();
			double maxDepth = validDepths.Max();

			var multiplot = new ScottPlot.Multiplot();
			multiplot.Layout = new ScottPlot.MultiplotLayouts.Grid(rows: 1, columns: 3);
			multiplot.AddPlots(3);

			// Track 1: Gamma Ray (Linear: 0 - 150 GAPI)
			Plot track1 = multiplot.GetPlot(0);
			SetupTrack1(track1, well, depths, minDepth, maxDepth);

			// Track 2: Deep Resistivity (Logarithmic: 0.2 - 2000 ohm.m)
			Plot track2 = multiplot.GetPlot(1);
			SetupTrack2(track2, well, depths, minDepth, maxDepth);

			// Track 3: Density & Neutron Porosity (Dual Axis: RHOB 1.95-2.95 / NPHI 0.45 - -0.15)
			Plot track3 = multiplot.GetPlot(2);
			SetupTrack3(track3, well, depths, minDepth, maxDepth);

			string sanitizedWellName = string.Join("_", well.WellName.Split(Path.GetInvalidFileNameChars())).Trim();
			if (string.IsNullOrEmpty(sanitizedWellName))
			{
				sanitizedWellName = "Unknown_Well";
			}

			string wellFolder = Path.Combine(outputDirectory, sanitizedWellName);
			Directory.CreateDirectory(wellFolder);

			string filePrefix = !string.IsNullOrEmpty(well.FilePath) ? Path.GetFileNameWithoutExtension(well.FilePath) : sanitizedWellName;
			string outputPath = Path.Combine(wellFolder, $"{filePrefix}_3Track.png");

			multiplot.SavePng(outputPath, 1200, 1600);
			Console.WriteLine($"[Plot Success] Generated composite log: {outputPath}");
		}

		private void SetupTrack1(Plot plot, WellLog well, double[] depths, double minDepth, double maxDepth)
		{
			plot.Title("Track 1: Gamma Ray");
			plot.Axes.Left.Label.Text = "Depth (ft)";
			plot.Axes.Bottom.Label.Text = "GR (GAPI)";
			plot.Axes.Top.MinimumSize = 30;
			plot.Axes.SetLimitsY(bottom: maxDepth, top: minDepth);
			plot.Axes.SetLimitsX(0, 150, plot.Axes.Bottom);

			if (well.Curves.TryGetValue("GR", out var grCurve) && grCurve.Values.Any(v => v.HasValue))
			{
				double[] grValues = grCurve.Values.Select(v => v ?? double.NaN).ToArray();
				var scatter = plot.Add.ScatterLine(xs: grValues, ys: depths);
				scatter.Color = Colors.C1;
				scatter.LineWidth = 1.5f;
			}
			else
			{
				plot.Add.Text("GR Not Available", 50, (minDepth + maxDepth) / 2);
			}
		}

		private void SetupTrack2(Plot plot, WellLog well, double[] depths, double minDepth, double maxDepth)
		{
			plot.Title("Track 2: Resistivity");
			plot.Axes.Left.Label.Text = ""; // Hide duplicate labels
			plot.Axes.Bottom.Label.Text = "Resistivity (ohm.m)";
			plot.Axes.Top.MinimumSize = 30;
			plot.Axes.SetLimitsY(bottom: maxDepth, top: minDepth);
			plot.Axes.SetLimitsX(Math.Log10(0.2), Math.Log10(2000), plot.Axes.Bottom);

			double[] tickPositions = new[]
			{
				Math.Log10(0.2),
				Math.Log10(1),
				Math.Log10(10),
				Math.Log10(100),
				Math.Log10(1000),
				Math.Log10(2000)
			};
			string[] tickLabels = new[] { "0.2", "1", "10", "100", "1k", "2k" };
			plot.Axes.Bottom.SetTicks(tickPositions, tickLabels);

			if (well.Curves.TryGetValue("RES", out var resCurve) && resCurve.Values.Any(v => v.HasValue && v.Value > 0))
			{
				double[] logResValues = resCurve.Values
					.Select(v => (v.HasValue && v.Value > 0) ? Math.Log10(v.Value) : double.NaN)
					.ToArray();

				var scatter = plot.Add.ScatterLine(xs: logResValues, ys: depths);
				scatter.Color = Colors.ForestGreen;
				scatter.LineWidth = 1.2f;
				scatter.LegendText = $"{resCurve.Definition.Mnemonic} (Deep)";
			}
			else
			{
				plot.Add.Text("Resistivity Not Available", Math.Log10(3), (minDepth + maxDepth) / 2);
			}

			plot.ShowLegend(Alignment.UpperRight);
		}

		private void SetupTrack3(Plot plot, WellLog well, double[] depths, double minDepth, double maxDepth)
		{
			plot.Title("Track 3: Density / Porosity");
			plot.Axes.Left.Label.Text = "";
			plot.Axes.Bottom.Label.Text = "RHOB (Red: 1.95 - 2.95) | NPHI (Blue: 0.45 - -0.15)";
			plot.Axes.Right.MinimumSize = 65;
			plot.Axes.Top.IsVisible = true;
			plot.Axes.SetLimitsY(bottom: maxDepth, top: minDepth);
			plot.Axes.SetLimitsX(1.95, 2.95, plot.Axes.Top);
			plot.Axes.SetLimitsX(1.95, 2.95, plot.Axes.Bottom);

			double[] nphiTickPositions = new[] { 1.95, 2.15, 2.35, 2.55, 2.75, 2.95 };
			string[] nphiLabels = new[] { "0.45", "0.33", "0.21", "0.09", "-0.03", "-0.15" };
			plot.Axes.Top.SetTicks(nphiTickPositions, nphiLabels);

			double[] rhobTicks = new[] { 1.95, 2.15, 2.35, 2.55, 2.75, 2.95 };
			string[] rhobLabels = new[] { "1.95", "2.15", "2.35", "2.55", "2.75", "2.95" };
			plot.Axes.Bottom.SetTicks(rhobTicks, rhobLabels);

			// 1. RHOB (Solid Red)
			bool hasRhob = well.Curves.TryGetValue("RHOB", out var rhobCurve) && rhobCurve != null && rhobCurve.Values.Any(v => v.HasValue);

			if (hasRhob && rhobCurve != null)
			{
				double[] rhobValues = rhobCurve.Values.Select(v => v ?? double.NaN).ToArray();
				var rhobScatter = plot.Add.ScatterLine(xs: rhobValues, ys: depths);
				rhobScatter.Color = Colors.Red;
				rhobScatter.LineWidth = 1.5f;
				rhobScatter.LegendText = $"RHOB ({rhobCurve.Definition.Mnemonic})";
			}

			// 2. NPHI (Dashed or Solid Blue)
			bool hasNphi = well.Curves.TryGetValue("NPHI", out var nphiCurve) && nphiCurve != null && nphiCurve.Values.Any(v => v.HasValue);

			if (hasNphi && nphiCurve != null)
			{
				var rhobValues = rhobCurve?.Values;

				// Curve-level unit detection: check unit string or max value across entire valid curve
				string unit = nphiCurve.Definition.Unit.ToUpperInvariant().Trim();
				bool isPercentUnit = unit.Contains("%") || unit.Contains("PERC") || unit.Contains("PU");
				double maxNphi = nphiCurve.Values.Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(0).Max();
				bool needsPercentConversion = isPercentUnit || maxNphi > 1.5;

				double[] normalizedNphi = nphiCurve.Values.Select((v, idx) =>
				{
					if (!v.HasValue || double.IsNaN(v.Value)) return double.NaN;

					// If RHOB is available in this well, synchronize casing cutoffs (mask out casing intervals)
					if (hasRhob && rhobValues != null && idx < rhobValues.Count)
					{
						if (!rhobValues[idx].HasValue || double.IsNaN(rhobValues[idx]!.Value))
						{
							return double.NaN;
						}
					}

					double n = v.Value;
					if (needsPercentConversion)
					{
						n /= 100.0;
					}

					// Filter out unreasonable values (-0.15 to 0.60)
					if (n < -0.15 || n > 0.60) return double.NaN;

					// Linear mapping: 0.45 -> 1.95 and -0.15 -> 2.95
					return 1.95 + ((0.45 - n) / (0.45 - (-0.15))) * (2.95 - 1.95);
				}).ToArray();

				var nphiScatter = plot.Add.ScatterLine(xs: normalizedNphi, ys: depths);
				nphiScatter.Color = Colors.RoyalBlue;
				nphiScatter.LineWidth = 1.2f;
				nphiScatter.LegendText = $"NPHI ({nphiCurve.Definition.Mnemonic})";
			}

			if (!hasRhob && !hasNphi)
			{
				plot.Add.Text("RHOB / NPHI Not Available", 2.15, (minDepth + maxDepth) / 2);
			}

			plot.ShowLegend(Alignment.UpperRight);
		}
	}
}
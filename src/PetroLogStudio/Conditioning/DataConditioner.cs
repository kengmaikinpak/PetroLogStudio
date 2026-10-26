using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic;
using PetroLogStudio.Models;

namespace PetroLogStudio.Conditioning
{
	/// <summary>
	/// Standardizes, validates, and sanitizes parsed WellLog data for downstream plotting and analysis.
	/// </summary>
	public class DataConditioner
	{
		// Alias Dictionary
		private static readonly Dictionary<string, string[]> StandardAliases = new(StringComparer.OrdinalIgnoreCase)
		{
			{ "DEPT", new[] { "DEPT", "DEPTH", "DEP", "MD", "TVD" } },
			{ "GR",   new[] { "GR", "GRGC", "GRSG", "HGR", "ECGR", "GAM", "GAPI", "GR_EDTC", "CGR", "SGR" } },
			{ "RES",  new[] { "RT_HRLT", "RT", "DDLL", "RES", "ILD", "LLD", "RLA5", "RLA4", "RLA3", "RLA2", "RLA1", "RLA0", "DSLL", "HMIN", "HMNO", "RILD" } },
			{ "RHOB", new[] { "RHOZ", "RHOB", "DEN", "ZDEN", "BDEN", "RHO" } },
			{ "NPHI", new[] { "NPHI", "TNPH", "NPRL", "NPRS", "NPRD", "HNPO", "NPOR", "CNL", "PHIN", "NPHI_LS" } }
		};

		public void ConditionWell(WellLog well)
		{
			if (well == null || well.Curves.Count == 0)
			{
				well?.ConditioningLogs.Add("Skipped conditioning: Well contains no curves.");
				return;
			}

			// step 1: Resolving curve names (for example: GRGC to GR)
			ResolveCurveAliases(well);

			// step 2: Normalize depth order (Ascending)
			NormalizeDepthOrder(well);

			// step 3: Sanitize curve values (Replace null with 9999.25)
			SanitizeCurveValues(well);

		}

		private void ResolveCurveAliases(WellLog well)
		{
			foreach (var target in StandardAliases)
			{
				string standardKey = target.Key;
				string[] candidates = target.Value;

				if (well.Curves.ContainsKey(standardKey))
				{
					well.ConditioningLogs.Add($"[Alias] Standard curve '{standardKey}' found directly.");
					continue;
				}

				string? matchedMnemonic = candidates.FirstOrDefault(alias => well.Curves.ContainsKey(alias));

				if (matchedMnemonic != null)
				{
					well.Curves[standardKey] = well.Curves[matchedMnemonic];
					well.ConditioningLogs.Add($"[Alias] Mapped standard key '{standardKey}' existing curve '{matchedMnemonic}'.");
					continue;
				}

				var fallbackCurve = TryHeuristicMatch(well, standardKey);
				if (fallbackCurve != null)
				{
					well.Curves[standardKey] = fallbackCurve;
					well.ConditioningLogs.Add($"[Alias] Mapped standard key '{standardKey}' using heuristic fallback.");
					continue;
				}

				well.ConditioningLogs.Add($"[Warning] Required standard curve '{standardKey}' could not be resolved from available curves.");

			}
		}

		private CurveData? TryHeuristicMatch(WellLog well, string standardKey)
		{
			foreach (var curve in well.Curves.Values.Distinct())
			{
				string mnem = curve.Definition.Mnemonic.ToUpperInvariant();
				string unit = curve.Definition.Unit.ToUpperInvariant();
				string desc = curve.Definition.Description.ToUpperInvariant();

				switch (standardKey)
				{
					case "GR":
						if (unit == "GAPI" || desc.Contains("GAMMA RAY") || desc.Contains("GAMMA-RAY") || mnem.StartsWith("GR"))
							return curve;
						break;

					case "RES":
						if ((unit.Contains("OHM") && desc.Contains("DEEP") || desc.Contains("TRUE") || desc.Contains("RESISTIVITY")) && !desc.Contains("MUD") && !desc.Contains("FLUSHED") && !desc.Contains("INVADED"))
							return curve;
						break;

					case "RHOB":
						bool isDensityCorrectionCurve = desc.Contains("DENSITY CORRECTION") || mnem.Contains("COR") || mnem.Contains("DRHO");
						if ((unit == "G/C3" || unit == "G/CC" || unit == "GM/CC") && desc.Contains("DENSITY") && !isDensityCorrectionCurve)
							return curve;
						break;

					case "NPHI":
						if (desc.Contains("NEUTRON") && (desc.Contains("POR") || unit == "V/V" || unit == "%" || unit == "PERC" || unit == "CFCF" || unit == "PU"))
							return curve;
						break;
				}
			}
			return null;
		}

		private void NormalizeDepthOrder(WellLog well)
		{
			if (!well.Curves.TryGetValue("DEPT", out var deptCurve) || deptCurve.Values.Count <= 1)
			{
				well.ConditioningLogs.Add("[Error] Cannot normalize depth: 'DEPT' curve is missing or empty.");
				return;
			}

			var depths = deptCurve.Values;
			int count = depths.Count;

			bool isStrictlyAscending = true;
			bool isStrictlyDescending = true;

			for (int i = 0; i < count - 1; i++)
			{
				double? curr = depths[i];
				double? next = depths[i + 1];

				if (!curr.HasValue || !next.HasValue) continue;

				if (curr.Value > next.Value) isStrictlyAscending = false;
				if (curr.Value < next.Value) isStrictlyDescending = false;

				if (isStrictlyAscending)
				{
					well.ConditioningLogs.Add("[Depth] Verified strictly ascending depth order (Top-to-Bottom).");
					return;
				}

				// case 1: Arrange in reverse order, from the bottom of the hole up to the top (e.g., Step = -0.5).
				if (isStrictlyDescending)
				{
					foreach (var curve in well.Curves.Values.Distinct())
					{
						curve.Values.Reverse();
					}

					well.ConditioningLogs.Add($"[Depth] Detected descending depth order ({depths[^1]} to {depths[0]}). Reversed all curves in-place to ascending order.");
					return;
				}

				// case 2: Scrambled/Corrupted order
				SortAllCurvesByDepth(well, deptCurve);
			}
		}

		private void SortAllCurvesByDepth(WellLog well, CurveData deptCurve)
		{
			int count = deptCurve.Values.Count;

			int[] sortedIndices = Enumerable.Range(0, count)
				.OrderBy(i => deptCurve.Values[i].HasValue ? 0 : 1)
				.ThenBy(i => deptCurve.Values[i] ?? double.MaxValue)
				.ToArray();

			foreach (var curve in well.Curves.Values.Distinct())
			{
				var oldValues = curve.Values;
				var newValues = new List<double?>(count);

				for (int i = 0; i < count; i++)
				{
					newValues.Add(oldValues[sortedIndices[i]]);
				}

				curve.Values = newValues;
			}

			well.ConditioningLogs.Add("[Depth] Detected non-monotonic/scrambled depth values. Re-indexed and sorted all curves in ascending order.");
		}

		private void SanitizeCurveValues(WellLog well)
		{
			// 1. RES must be positive for log scale
			if (well.Curves.TryGetValue("RES", out var resCurve))
			{
				int invalidResCount = 0;
				var values = resCurve.Values;

				for (int i = 0; i < values.Count; i++)
				{
					if (values[i].HasValue && values[i]!.Value <= 0)
					{
						values[i] = null;
						invalidResCount++;
					}
				}

				if (invalidResCount > 0)
				{
					well.ConditioningLogs.Add($"[Sanitize] Replaced {invalidResCount} non-positive values (<= 0) in 'RES' ({resCurve.Definition.Mnemonic}) with null for logarithmic scale safety.");
				}
			}

			// 2. GR must be positive for log scale
			if (well.Curves.TryGetValue("GR", out var grCurve))
			{
				int negativeGrCount = 0;
				var values = grCurve.Values;

				for (int i = 0; i < values.Count; i++)
				{
					if (values[i].HasValue && values[i]!.Value <= 0)
					{
						values[i] = null;
						negativeGrCount++;
					}
				}

				if (negativeGrCount > 0)
				{
					well.ConditioningLogs.Add($"[Sanitize] Replaced {negativeGrCount} unphysical negative values (< 0) in 'GR' ({grCurve.Definition.Mnemonic}) with null.");
				}
			}

		}
	}
}
using System;
using System.Collections.Generic;
using System.Linq;
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
			{ "GR",   new[] { "GR", "GRGC", "GRSG", "GAMMA", "HGR", "ECGR", "GAM", "GAPI", "GR_EDTC", "CGR", "SGR" } },
			{ "RES",  new[] { "M2RX", "M2R9", "M2R6", "RT_HRLT", "RT", "AT90", "AHT90", "AF90", "AO90", "ILD", "LLD", "RILD", "RLA5", "RLA4", "DDLL", "RES", "M2R3", "M2R2", "M2R1", "RLA3", "RLA2", "RLA1", "RLA0", "DSLL" } },
			{ "RHOB", new[] { "ZDNC", "RHOZ", "RHOB", "DEN", "ZDEN", "BDEN", "RHO8", "RHO" } },
			{ "NPHI", new[] { "CNC", "NPHI", "TNPH", "NPRL", "NPRS", "NPRD", "HNPO", "NPOR", "CNL", "PHIN", "NPHI_LS" } }
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
						bool isGammaDesc = desc.Contains("GAMMA") || desc.Contains("GAMMA RAY") || desc.Contains("GAMMA-RAY");
						bool hasGapiUnit = unit == "GAPI" || unit == "API";
						bool isLithology = desc.Contains("GRANITE") || mnem.StartsWith("GRNT") || mnem.StartsWith("GRAN") || desc.Contains("GRAIN");
						if (!isLithology && (hasGapiUnit || isGammaDesc || (mnem.StartsWith("GR") && (string.IsNullOrEmpty(unit) || hasGapiUnit))))
							return curve;
						break;

					case "RES":
						bool isResistivityDesc = desc.Contains("RESIST") || desc.Contains("INDUCTION") || desc.Contains("LATEROLOG") || desc.Contains("DEEP RES") || desc.Contains("TRUE RES");
						bool hasOhmUnit = unit.Contains("OHM");
						bool isExcluded = desc.Contains("MUD") || desc.Contains("FLUSHED") || desc.Contains("INVADED") || desc.Contains("MICR") || desc.Contains("SHALLOW");
						if ((hasOhmUnit || isResistivityDesc) && !isExcluded && (desc.Contains("DEEP") || isResistivityDesc))
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
			double? lastValidDepth = null;
			int validDepthCount = 0;

			for (int i = 0; i < count; i++)
			{
				double? curr = depths[i];
				if (!curr.HasValue || double.IsNaN(curr.Value)) continue;

				if (lastValidDepth.HasValue)
				{
					if (curr.Value <= lastValidDepth.Value) isStrictlyAscending = false;
					if (curr.Value >= lastValidDepth.Value) isStrictlyDescending = false;
				}

				lastValidDepth = curr;
				validDepthCount++;
			}

			if (validDepthCount <= 1)
			{
				well.ConditioningLogs.Add("[Depth] Insufficient depth points to re-order.");
				return;
			}

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
					int targetIndex = sortedIndices[i];

					if (targetIndex >= 0 && targetIndex < oldValues.Count)
					{
						newValues.Add(oldValues[targetIndex]);
					}
					else
					{
						newValues.Add(null);
					}
				}

				curve.Values = newValues;
			}

			well.ConditioningLogs.Add("[Depth] Detected non-monotonic/scrambled depth values. Re-indexed and sorted all curves in ascending order.");
		}

		private void SanitizeCurveValues(WellLog well)
		{
			// 1. RES must be positive and within physical tool limits for log scale (Track 2: 0.2 - 2000 ohm.m)
			if (well.Curves.TryGetValue("RES", out var resCurve))
			{
				int invalidResCount = 0;
				var values = resCurve.Values;

				for (int i = 0; i < values.Count; i++)
				{
					// Saturation (99999.9999 or >= 40k) and off-scale sub-limits (< 0.2 ohm.m e.g., noise in casing)
					if (values[i].HasValue && (values[i]!.Value < 0.2 || values[i]!.Value >= 40_000))
					{
						values[i] = null;
						invalidResCount++;
					}
				}

				if (invalidResCount > 0)
				{
					well.ConditioningLogs.Add($"[Sanitize] Replaced {invalidResCount} invalid/saturated values (< 0.2 or >= 40k) in 'RES' ({resCurve.Definition.Mnemonic}) with null for logarithmic scale safety.");
				}
			}

			// 2. RHOB physical bounds (Formation bulk density is typically 1.0 - 2.95 g/cc; values > 2.95 are casing/steel or noise spikes)
			if (well.Curves.TryGetValue("RHOB", out var rhobCurveSanitize))
			{
				int invalidRhobCount = 0;
				var values = rhobCurveSanitize.Values;

				for (int i = 0; i < values.Count; i++)
				{
					if (values[i].HasValue && (values[i]!.Value < 1.0 || values[i]!.Value > 2.95))
					{
						values[i] = null;
						invalidRhobCount++;
					}
				}

				if (invalidRhobCount > 0)
				{
					well.ConditioningLogs.Add($"[Sanitize] Replaced {invalidRhobCount} unphysical/casing spike values (< 1.0 or > 2.95 g/cc) in 'RHOB' ({rhobCurveSanitize.Definition.Mnemonic}) with null.");
				}
			}

			// 3. GR must be positive for log scale
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

			// 4. Remove Flatline / Frozen sensor dummy values FIRST (before spike filter)
			if (well.Curves.TryGetValue("RHOB", out var rhobCurve))
			{
				FilterFlatlineValues(rhobCurve, "RHOB", well, minRunLength: 50);
			}

			if (well.Curves.TryGetValue("RES", out var resC))
			{
				FilterFlatlineValues(resC, "RES", well, minRunLength: 50);
			}

			// 5. Remove steep edge artifacts adjacent to large null gaps (e.g. casing shoe cement boundary spikes)
			if (well.Curves.TryGetValue("RHOB", out var rhobEdgeCurve))
			{
				FilterCasingEdgeArtifacts(rhobEdgeCurve, "RHOB", well, maxJump: 0.15, thresholdValue: 2.50, minGapSize: 50);
			}

			// 6. Remove isolated non-null spike clusters (surrounded by nulls on both sides)
			if (well.Curves.TryGetValue("RES", out var resSpikeCurve))
			{
				FilterIsolatedSpikes(resSpikeCurve, "RES", well);
			}

			if (well.Curves.TryGetValue("RHOB", out var rhobSpikeCurve))
			{
				FilterIsolatedSpikes(rhobSpikeCurve, "RHOB", well);
			}
		}

		private void FilterCasingEdgeArtifacts(CurveData curve, string standardKey, WellLog well, double maxJump = 0.15, double thresholdValue = 2.50, int minGapSize = 50)
		{
			var values = curve.Values;
			if (values == null || values.Count == 0) return;

			int count = values.Count;
			int trimmed = 0;

			// Scan forward to find end of valid segment before large null gap
			for (int i = 0; i < count - 1; i++)
			{
				if (values[i].HasValue)
				{
					// Check if next is a large null gap
					int nullGap = 0;
					int j = i + 1;
					while (j < count && !values[j].HasValue)
					{
						nullGap++;
						j++;
					}

					if (nullGap >= minGapSize && i >= 1 && values[i - 1].HasValue)
					{
						// Check if value[i] spiked sharply compared to value[i-1] into heavy density
						double diff = values[i]!.Value - values[i - 1]!.Value;
						if (diff > maxJump && values[i]!.Value > thresholdValue)
						{
							values[i] = null;
							trimmed++;

							// Check if i-1 also spiked relative to i-2
							if (i >= 2 && values[i - 2].HasValue)
							{
								double diff2 = values[i - 1]!.Value - values[i - 2]!.Value;
								if (diff2 > maxJump && values[i - 1]!.Value > thresholdValue)
								{
									values[i - 1] = null;
									trimmed++;
								}
							}
						}
					}
				}
			}

			// Scan backward from end of large null gap to start of valid segment
			for (int i = 1; i < count; i++)
			{
				if (values[i].HasValue)
				{
					int nullGap = 0;
					int j = i - 1;
					while (j >= 0 && !values[j].HasValue)
					{
						nullGap++;
						j--;
					}

					if (nullGap >= minGapSize && i < count - 1 && values[i + 1].HasValue)
					{
						double diff = values[i]!.Value - values[i + 1]!.Value;
						if (diff > maxJump && values[i]!.Value > thresholdValue)
						{
							values[i] = null;
							trimmed++;

							if (i < count - 2 && values[i + 2].HasValue)
							{
								double diff2 = values[i + 1]!.Value - values[i + 2]!.Value;
								if (diff2 > maxJump && values[i + 1]!.Value > thresholdValue)
								{
									values[i + 1] = null;
									trimmed++;
								}
							}
						}
					}
				}
			}

			if (trimmed > 0)
			{
				well.ConditioningLogs.Add($"[Sanitize] Removed {trimmed} casing boundary edge artifact(s) in '{standardKey}' ({curve.Definition.Mnemonic}).");
			}
		}

		private void FilterIsolatedSpikes(CurveData curve, string standardKey, WellLog well, int minClusterSize = 3)
		{
			var values = curve.Values;
			if (values == null || values.Count == 0) return;

			int count = values.Count;
			int removedSpikes = 0;
			int i = 0;

			while (i < count)
			{
				if (!values[i].HasValue)
				{
					i++;
					continue;
				}

				int start = i;
				while (i < count && values[i].HasValue)
				{
					i++;
				}
				int clusterLength = i - start;

				// If an island/cluster of valid values is isolated between null gaps and is shorter than minClusterSize, treat as noise spike
				bool leftNull = (start == 0) || !values[start - 1].HasValue;
				bool rightNull = (i == count) || !values[i].HasValue;

				if (clusterLength < minClusterSize && leftNull && rightNull)
				{
					for (int k = start; k < i; k++)
					{
						values[k] = null;
					}
					removedSpikes += clusterLength;
				}
			}

			if (removedSpikes > 0)
			{
				well.ConditioningLogs.Add($"[Sanitize] Removed {removedSpikes} isolated noise spike point(s) (< {minClusterSize} continuous samples) in '{standardKey}' ({curve.Definition.Mnemonic}).");
			}
		}

		private void FilterFlatlineValues(CurveData curve, string standardKey, WellLog well, int minRunLength = 50)
		{
			var values = curve.Values;
			if (values == null || values.Count == 0) return;

			int count = values.Count;
			int totalFlatlineCount = 0;
			int i = 0;

			while (i < count)
			{
				if (!values[i].HasValue)
				{
					i++;
					continue;
				}

				double currentVal = values[i]!.Value;
				int runLength = 1;
				int j = i + 1;

				while (j < count && values[j].HasValue && Math.Abs(values[j]!.Value - currentVal) < 1e-4)
				{
					runLength++;
					j++;
				}

				if (runLength >= minRunLength)
				{
					for (int k = i; k < j; k++)
					{
						values[k] = null;
					}
					totalFlatlineCount += runLength;
				}

				i = j;
			}

			if (totalFlatlineCount > 0)
			{
				well.ConditioningLogs.Add($"[Sanitize] Detected and removed {totalFlatlineCount} frozen/flatline sensor values in '{standardKey}' ({curve.Definition.Mnemonic}) repeated consecutively >= {minRunLength} times.");
			}
		}
	}
}
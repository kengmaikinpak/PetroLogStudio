using System;
using System.Globalization;
using System.IO;
using PetroLogStudio.Models;

namespace PetroLogStudio.Parsing
{
	public enum LasSection
	{
		None,
		Version,
		Well,
		Curve,
		Parameter,
		AsciiData
	}

	///<summary>
	/// Custom parser for CWLS LAS 2.0 files with error tolerance and memory efficiency. 
	///</summary>
	public class LasParser
	{
		private static readonly char[] WhitespaceChars = { ' ', '\t' };

		public WellLog ParseFile(string filePath)
		{
			var well = new WellLog
			{
				FilePath = filePath,
				WellName = Path.GetFileNameWithoutExtension(filePath)
			};

			if (!File.Exists(filePath))
			{
				well.ParseErrors.Add($"File not found: {filePath}");
				return well;
			}

			LasSection currentSection = LasSection.None;

			var orderedCurves = new List<CurveData>();
			int currentColumn = 0;

			try
			{
				using var reader = new StreamReader(filePath);
				string? line;

				while ((line = reader.ReadLine()) != null)
				{
					// Remove whitespace and the SUB character (ASCII 0x1A)
					ReadOnlySpan<char> lineSpan = line.AsSpan().Trim().Trim('\u001A'); ;

					if (lineSpan.IsEmpty || lineSpan[0] == '#')
					{
						continue;
					}

					if (lineSpan[0] == '~')
					{
						currentSection = DetermineSection(lineSpan);
						continue;
					}

					switch (currentSection)
					{
						case LasSection.Well:
							ParseWellInfo(lineSpan, well);
							break;
						case LasSection.Curve:
							ParseCurveDefinition(lineSpan, well, orderedCurves);
							break;
						case LasSection.AsciiData:
							ParseDataLine(lineSpan, well, orderedCurves, ref currentColumn);
							break;
					}
				}
				if (currentColumn != 0)
				{
					well.ParseErrors.Add($"Incomplete data row at end of file: {currentColumn} of {orderedCurves.Count} expected values were read and discarded.");
				}
			}
			catch (Exception ex)
			{
				well.ParseErrors.Add($"Critical read error: {ex.Message}");
			}

			return well;
		}

		private LasSection DetermineSection(ReadOnlySpan<char> line)
		{
			char sectionKey = char.ToUpperInvariant(line.Length > 1 ? line[1] : ' ');
			return sectionKey switch
			{
				'V' => LasSection.Version,
				'W' => LasSection.Well,
				'C' => LasSection.Curve,
				'P' => LasSection.Parameter,
				'A' => LasSection.AsciiData,
				_ => LasSection.None
			};
		}

		private void ParseWellInfo(ReadOnlySpan<char> line, WellLog well)
		{
			var (mnemonic, _, value, description) = SplitHeaderLine(line);

			switch (mnemonic.ToUpperInvariant())
			{
				case "WELL":
					string wellNameCandidate = value;
					if (string.IsNullOrWhiteSpace(wellNameCandidate) || wellNameCandidate.Equals("WELL", StringComparison.OrdinalIgnoreCase))
					{
						wellNameCandidate = description;
					}

					if (!string.IsNullOrWhiteSpace(wellNameCandidate))
					{
						well.WellName = wellNameCandidate;
					}
					break;

				case "NULL":
					if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double nullValue))
					{
						well.NullValue = nullValue;
					}
					break;

				case "STRT":
					if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double startVal))
					{
						well.StartDepth = startVal;
					}
					break;

				case "STOP":
					if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double stopVal))
					{
						well.StopDepth = stopVal;
					}
					break;

				case "STEP":
					if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double stepVal))
					{
						well.Step = stepVal;
					}
					break;
			}
		}

		private void ParseCurveDefinition(ReadOnlySpan<char> line, WellLog well, List<CurveData> orderedCurves)
		{
			var (mnemonic, unit, _, description) = SplitHeaderLine(line);

			if (string.IsNullOrWhiteSpace(mnemonic)) return;

			var definition = new CurveDefinition
			{
				Mnemonic = mnemonic.ToUpperInvariant(),
				Unit = unit,
				Description = description,
				ColumnIndex = orderedCurves.Count
			};

			var curveData = new CurveData(definition);
			orderedCurves.Add(curveData);

			string key = well.Curves.ContainsKey(definition.Mnemonic)
				? $"{definition.Mnemonic}_{definition.ColumnIndex}"
				: definition.Mnemonic;

			well.Curves[key] = curveData;
		}

		private void ParseDataLine(ReadOnlySpan<char> line, WellLog well, List<CurveData> orderedCurves, ref int currentColumn)
		{
			int expectedCurves = orderedCurves.Count;
			if (expectedCurves == 0) return;

			double? nullVal = well.NullValue;

			while (!line.IsEmpty)
			{
				int spaceIndex = line.IndexOfAny(WhitespaceChars);

				ReadOnlySpan<char> token;

				if (spaceIndex >= 0)
				{
					token = line.Slice(0, spaceIndex);
					line = line.Slice(spaceIndex + 1).TrimStart();
				}
				else
				{
					token = line;
					line = ReadOnlySpan<char>.Empty;
				}

				if (token.IsEmpty) continue;

				double? valToAdd = null;
				if (double.TryParse(token, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value))
				{
					if (!nullVal.HasValue || Math.Abs(value - nullVal.Value) >= 0.001)
					{
						valToAdd = value;
					}
				}

				orderedCurves[currentColumn].Values.Add(valToAdd);

				currentColumn++;
				if (currentColumn == expectedCurves)
				{
					currentColumn = 0; // Reset for next row
				}
			}
		}

		private (string mnemonic, string unit, string value, string description) SplitHeaderLine(ReadOnlySpan<char> line)
		{
			// CWLS format: MNEM.UNIT  VALUE : DESCRIPTION
			int colonIndex = line.IndexOf(':');
			ReadOnlySpan<char> mainPart = colonIndex >= 0 ? line.Slice(0, colonIndex).Trim() : line;
			string description = colonIndex >= 0 ? line.Slice(colonIndex + 1).Trim().ToString() : string.Empty;

			// Split mainPart into mnemonic and unit
			int dotIndex = mainPart.IndexOf('.');
			if (dotIndex < 0)
				return (mainPart.Trim().ToString(), string.Empty, string.Empty, description); // No unit found

			string mnemonic = mainPart.Slice(0, dotIndex).Trim().ToString();
			ReadOnlySpan<char> afterdot = mainPart.Slice(dotIndex + 1);

			string unit = string.Empty;
			string value = string.Empty;

			// Split rest into unit and value
			int spaceIndex = afterdot.IndexOfAny(WhitespaceChars);
			if (spaceIndex >= 0)
			{
				unit = afterdot.Slice(0, spaceIndex).Trim().ToString();
				value = afterdot.Slice(spaceIndex).Trim().ToString();
			}
			else
			{
				unit = afterdot.Trim().ToString();
				value = string.Empty;
			}
			return (mnemonic, unit, value, description);

		}
	}
}
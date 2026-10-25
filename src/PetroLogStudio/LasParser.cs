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
		private bool _isWrapped = false;
		private readonly List<double?> _rowBuffer = new();
		
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

            try
            {
                using var reader = new StreamReader(filePath);
                string? line;
                int lineNumber = 0;
				int curveIndex = 0;

                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    line = line.Trim();

                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    {
                        continue; // Skip empty lines and comments
                    }

                    if (line.StartsWith("~"))
                    {
                        currentSection = DetermineSection(line);
                        continue;
                    
                    }

                    switch (currentSection)
					{
						case LasSection.Version:
							ParseVersionInfo(line);
							break;
                        case LasSection.Well:
                            ParseWellInfo(line, well);
                            break;
                        case LasSection.Curve:
                            ParseCurveDefinition(line, well, ref curveIndex);
                            break;
                        case LasSection.AsciiData:
                            ParseDataLine(line, well, lineNumber);
                            break;
                    }
                }
				if (_rowBuffer.Count > 0)
				{
					well.ParseErrors.Add($"Incomplete data at end of file: {_rowBuffer.Count} orphaned values discarded.");
				}
            }
            catch (Exception ex)
            {
                well.ParseErrors.Add($"Critical read error: {ex.Message}");
            }

            return well;
        }

        private LasSection DetermineSection(string line)
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

		private void ParseVersionInfo(string line)
		{
			var (mnemonic, _, value) = SplitHeaderLine(line);
			if (mnemonic.Equals("WRAP", StringComparison.OrdinalIgnoreCase))
			{
				_isWrapped = value.Equals("YES", StringComparison.OrdinalIgnoreCase);
			}
		}

        private void ParseWellInfo(string line, WellLog well)
		{
			var (mnemonic, unit, value) = SplitHeaderLine(line);

            switch (mnemonic.ToUpperInvariant())
            {
                case "WELL":
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        well.WellName = value;
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

        private void ParseCurveDefinition(string line, WellLog well, ref int curveIndex)
		{
			var (mnemonic, unit, description) = SplitHeaderLine(line);

            if (string.IsNullOrWhiteSpace(mnemonic)) return;

            var definition = new CurveDefinition
            {
                Mnemonic = mnemonic.ToUpperInvariant(),
                Unit = unit,
                Description = description,
                ColumnIndex = curveIndex++
            };

            if (!well.Curves.ContainsKey(definition.Mnemonic))
            {
                well.Curves[definition.Mnemonic] = new CurveData(definition);
            }
            else
			{
				string uniqueKey = $"{definition.Mnemonic}_{definition.ColumnIndex}";
                well.Curves[uniqueKey] = new CurveData(definition);
			}
		}

		private void ParseDataLine(string line, WellLog well, int lineNumber)
		{
			var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			int expectedCurves = well.Curves.Count;

			if (expectedCurves == 0) return;

			foreach (var token in tokens)
			{
				if (double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
				{
					if (well.NullValue.HasValue && Math.Abs(value - well.NullValue.Value) < 0.001)
					{
						_rowBuffer.Add(null); // Treat as null value
					}
					else
					{
						_rowBuffer.Add(value);
					}
				}
				else
				{
					_rowBuffer.Add(null); // Invalid number, treat as null
				}
			}

			if (_rowBuffer.Count == expectedCurves)
			{
				int colIndex = 0;
				foreach (var kvp in well.Curves)
				{
					kvp.Value.Values.Add(_rowBuffer[colIndex]);
					colIndex++;
				}
				_rowBuffer.Clear();
			}
		}

        private (string mnemonic, string unit, string value) SplitHeaderLine(string line)
		{
			// CWLS format: MNEM.UNIT  VALUE : DESCRIPTION
            int colonIndex = line.IndexOf(':');
            string mainPart = colonIndex >= 0 ? line.Substring(0, colonIndex).Trim() : line;
            string description = colonIndex >= 0 ? line.Substring(colonIndex + 1).Trim() : string.Empty;

            // Split mainPart into mnemonic and unit
            int dotIndex = mainPart.IndexOf('.');
            if (dotIndex < 0) return (mainPart.Trim(), string.Empty, description); // No unit found

            string mnemonic = mainPart.Substring(0, dotIndex).Trim();
            string afterdot = mainPart.Substring(dotIndex + 1);

			string unit = string.Empty;
			string value = string.Empty;

            // Split rest into unit and value
            int spaceIndex = afterdot.IndexOfAny(new[] { ' ', '\t' });
            if (spaceIndex >= 0)
			{
				unit = afterdot.Substring(0, spaceIndex).Trim();
            	value = afterdot.Substring(spaceIndex).Trim();
			}
			else
			{
				unit = afterdot.Trim();
				value = string.Empty;
			}
            return (mnemonic, unit, value);
		}
    }
}
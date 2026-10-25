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
            int curveIndex = 0;

            try
            {
                using var reader = new StreamReader(filePath);
                string? line;
                int lineNumber = 0;

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

        private void ParseWellInfo(string line, WellLog well)
		{
			var (mnemonic, unit, value) = SplitHeaderLine(line, well);

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
			var (mnemonic, unit, description) = SplitHeaderLine(line, well);

            if (string.IsNullOrWhiteSpace(mnemonic)) return;

            var definition = new CurveDefinition
            {
                Mnemonic = mnemonic.ToUpperInvariant(),
                Unit = unit,
                Description = description,
                ColumnIndex = curveIndex
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

            if (tokens.Length != well.Curves.Count)
			{
				well.ParseErrors.Add($"Line {lineNumber}: Incomplete data columns (expected {well.Curves.Count}, got {tokens.Length})");
			}

            int index = 0;
            foreach (var kvp in well.Curves)
			{
				if (index < tokens.Length)
				{
					if (double.TryParse(tokens[index], NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
                    {
                        if(well.NullValue.HasValue && Math.Abs(value - well.NullValue.Value) < 0.001)
						{
							kvp.Value.Values.Add(null); // Treat as null value
						}
                        else
						{
							kvp.Value.Values.Add(value);
						}
					}
                    else
					{
						kvp.Value.Values.Add(null); // Invalid number, treat as null
					}
                   
				}
                else
				{
					kvp.Value.Values.Add(null); // Missing value, treat as null
				}
                index++;
			}
		}

        private (string mnemonic, string unit, string value) SplitHeaderLine(string line, WellLog well)
		{
			// CWLS format: MNEM.UNIT  VALUE : DESCRIPTION
            int colonIndex = line.IndexOf(':');
            string mainPart = colonIndex >= 0 ? line.Substring(0, colonIndex).Trim() : line;
            string description = colonIndex >= 0 ? line.Substring(colonIndex + 1).Trim() : string.Empty;

            // Split mainPart into mnemonic and unit
            int dotIndex = mainPart.IndexOf('.');
            if (dotIndex < 0) return (mainPart.Trim(), string.Empty, description); // No unit found

            string mnemonic = mainPart.Substring(0, dotIndex).Trim();
            string rest = mainPart.Substring(dotIndex + 1).Trim();

            // Split rest into unit and value
            int spaceIndex = rest.IndexOfAny(new[] { ' ', '\t' });
            if (spaceIndex < 0) return (mnemonic, rest.Trim(), description); // No value found

            string unit = rest.Substring(0, spaceIndex).Trim();
            string value = rest.Substring(spaceIndex).Trim();

            return (mnemonic, unit, value);
		}
    }
}
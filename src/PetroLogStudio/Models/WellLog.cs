using System.Collections.Generic;

namespace PetroLogStudio.Models
{
	public class WellLog
	{
		public string FilePath { get; set; } = string.Empty;
		public string WellName { get; set; } = string.Empty;

		// reference -999.25, -9999.25, etc.
		public double? NullValue { get; set; }
		public double? StartDepth { get; set; }
		public double? StopDepth { get; set; }
		public double? Step { get; set; }

		public Dictionary<string, CurveData> Curves { get; set; } = new Dictionary<string, CurveData>();

		public List<string> ParseErrors { get; set; } = new List<string>();
		public List<string> ConditionalWarnings { get; set; } = new List<string>();
	}
}

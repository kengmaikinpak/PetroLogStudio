using System.Collections.Generic;

namespace PetroLogStudio.Models
{
	public class WellLog
	{
		public string FilePath { get; set; } = string.Empty;
		public string WellName { get; set; } = string.Empty;
		public double? NullValue { get; set; }
		public double? StartDepth { get; set; }
		public double? StopDepth { get; set; }
		public double? Step { get; set; }

		public Dictionary<string, CurveData> Curves { get; set; } = new Dictionary<string, CurveData>();
		public List<string> ParseErrors { get; set; } = new List<string>();
		public List<string> ParseNotices { get; set; } = new List<string>();
		public List<string> ConditioningLogs { get; set; } = new List<string>();
	}
}

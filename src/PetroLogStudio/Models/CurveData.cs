///<summary>
/// CurveData class represents the data of a curve, including its definition and the corresponding values.
///</summary>

using System.Collections.Generic;

namespace PetroLogStudio.Models
{
	public class CurveData
	{
		public CurveDefinition Definition { get; set; }

		// double? to allow for null values in the curve data
		public List<double?> Values { get; set; } = new List<double?>();

		// Constructor to initialize CurveData with a given CurveDefinition
		public CurveData(CurveDefinition definition)
		{
			Definition = definition;
		}
	}
}
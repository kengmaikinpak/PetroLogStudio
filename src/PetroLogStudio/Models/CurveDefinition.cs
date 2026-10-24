///<summary>
/// CurveDefinition class represents the definition of a curve, including its mnemonic, unit, description, and column index.
///</summary>

namespace PetroLogStudio.Models
{
	public class CurveDefinition
	{
		// DEPT, GR, RHOB, NPHI, etc.
		public string Mnemonic { get; set; } = string.Empty;

		// ft, API, g/cm³, etc.
		public string Unit { get; set; } = string.Empty;

		// Description of the curve
		public string Description { get; set; } = string.Empty;
		
		// The index of the column in the data set
		public int ColumnIndex { get; set; }
	}
}
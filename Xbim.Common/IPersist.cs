// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool Xbim.CodeGeneration 
//  
//     Changes to this file may cause incorrect behaviour and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------
namespace Xbim.Common
{
	public interface IPersist
	{
		//This function is used by parsers to read in serialized data
		void Parse(int propIndex, IPropertyValue value, int[] nested);

		/// <summary>
        ///   Validates the object against the Ifc schema where rule, returns empty string if the object complies or an error string indicating the reason for compliance failure
        /// </summary>
        /// <returns></returns>
        string WhereRule();
	}
}
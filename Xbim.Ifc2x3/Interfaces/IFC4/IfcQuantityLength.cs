// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool Xbim.CodeGeneration 
//  
//     Changes to this file may cause incorrect behaviour and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

using Xbim.Ifc4.Interfaces;
using System.Collections.Generic;
using System.Linq;

// ReSharper disable once CheckNamespace
namespace Xbim.Ifc2x3.QuantityResource
{
	public partial class @IfcQuantityLength : IIfcQuantityLength
	{
		Xbim.Ifc4.MeasureResource.IfcLengthMeasure IIfcQuantityLength.LengthValue 
		{ 
			get
			{
				return new Xbim.Ifc4.MeasureResource.IfcLengthMeasure((double)LengthValue);
			} 
		}
		Xbim.Ifc4.MeasureResource.IfcLabel? IIfcQuantityLength.Formula 
		{ 
			get
			{
				throw new System.NotImplementedException();
			} 
		}
	}
}
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
namespace Xbim.Ifc2x3.ProductExtension
{
	public partial class @IfcZone : IIfcZone
	{
		Xbim.Ifc4.MeasureResource.IfcLabel? IIfcZone.LongName 
		{ 
			get
			{
				throw new System.NotImplementedException();
			} 
		}
		IEnumerable<IIfcRelServicesBuildings> IIfcSystem.ServicesBuildings 
		{ 
			get
			{
				return Model.Instances.Where<IIfcRelServicesBuildings>(e => (e.RelatingSystem as IfcZone) == this);
			} 
		}
	}
}
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
namespace Xbim.Ifc2x3.ProfileResource
{
	public partial class @IfcUShapeProfileDef : IIfcUShapeProfileDef
	{
		Xbim.Ifc4.MeasureResource.IfcPositiveLengthMeasure IIfcUShapeProfileDef.Depth 
		{ 
			get
			{
				return new Xbim.Ifc4.MeasureResource.IfcPositiveLengthMeasure((double)Depth);
			} 
		}
		Xbim.Ifc4.MeasureResource.IfcPositiveLengthMeasure IIfcUShapeProfileDef.FlangeWidth 
		{ 
			get
			{
				return new Xbim.Ifc4.MeasureResource.IfcPositiveLengthMeasure((double)FlangeWidth);
			} 
		}
		Xbim.Ifc4.MeasureResource.IfcPositiveLengthMeasure IIfcUShapeProfileDef.WebThickness 
		{ 
			get
			{
				return new Xbim.Ifc4.MeasureResource.IfcPositiveLengthMeasure((double)WebThickness);
			} 
		}
		Xbim.Ifc4.MeasureResource.IfcPositiveLengthMeasure IIfcUShapeProfileDef.FlangeThickness 
		{ 
			get
			{
				return new Xbim.Ifc4.MeasureResource.IfcPositiveLengthMeasure((double)FlangeThickness);
			} 
		}
		Xbim.Ifc4.MeasureResource.IfcNonNegativeLengthMeasure? IIfcUShapeProfileDef.FilletRadius 
		{ 
			get
			{
				if (FilletRadius == null) return null;
				return new Xbim.Ifc4.MeasureResource.IfcNonNegativeLengthMeasure((double)FilletRadius);
			} 
		}
		Xbim.Ifc4.MeasureResource.IfcNonNegativeLengthMeasure? IIfcUShapeProfileDef.EdgeRadius 
		{ 
			get
			{
				if (EdgeRadius == null) return null;
				return new Xbim.Ifc4.MeasureResource.IfcNonNegativeLengthMeasure((double)EdgeRadius);
			} 
		}
		Xbim.Ifc4.MeasureResource.IfcPlaneAngleMeasure? IIfcUShapeProfileDef.FlangeSlope 
		{ 
			get
			{
				if (FlangeSlope == null) return null;
				return new Xbim.Ifc4.MeasureResource.IfcPlaneAngleMeasure((double)FlangeSlope);
			} 
		}
	}
}
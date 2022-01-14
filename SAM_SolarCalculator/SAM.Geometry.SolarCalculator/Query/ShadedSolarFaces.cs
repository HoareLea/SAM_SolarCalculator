using SAM.Geometry.Spatial;
using System;
using SAM.Core;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        public static List<SolarFace> ShadedSolarFaces(this IEnumerable<SolarFace> solarFaces, Vector3D sunDirection, double tolerance_Area = Tolerance.MacroDistance, double tolerance_Snap = Tolerance.MacroDistance, double tolerance_Distance = Tolerance.Distance)
        {
            SolarFaces(solarFaces, sunDirection, out List<SolarFace> solarFaces_Shaded, out List<SolarFace> solarFaces_ExposedToSun, true, false, tolerance_Area, tolerance_Snap, tolerance_Distance);

            return solarFaces_Shaded;
        }
    }
}

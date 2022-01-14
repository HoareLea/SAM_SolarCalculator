using SAM.Geometry.Spatial;
using SAM.Core;
using System.Collections.Generic;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        public static List<SolarFace> ExposedToSunSolarFaces(this IEnumerable<SolarFace> solarFaces, Vector3D sunDirection, double tolerance_Area = Tolerance.MacroDistance, double tolerance_Snap = Tolerance.MacroDistance, double tolerance_Distance = Tolerance.Distance)
        {
            SolarFaces(solarFaces, sunDirection, out List<SolarFace> solarFaces_Shaded, out List<SolarFace> solarFaces_ExposedToSun, false, true, tolerance_Area, tolerance_Snap, tolerance_Distance);

            return solarFaces_ExposedToSun;
        }
    }
}

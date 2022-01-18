using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Create
    {
        public static SolarFaceSimulationResult SolarFaceSimulationResult(this SolarFace solarFace, IEnumerable<Tuple<DateTime, List<Face3D>>> sunExposure, string name = null)
        {
            if(solarFace == null)
            {
                return null;
            }

            SolarFaceSimulationResult result = new SolarFaceSimulationResult(name, Query.Source(), solarFace.Guid.ToString(), sunExposure);
            return result;
        }
    }
}

using SAM.Geometry.Object.Spatial;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Create
    {
        public static SolarFaceSimulationResult SolarFaceSimulationResult(this LinkedFace3D linkedFace3D, IEnumerable<Tuple<DateTime, List<Face3D>>> sunExposure, string name = null)
        {
            if(linkedFace3D == null)
            {
                return null;
            }

            return SolarFaceSimulationResult(linkedFace3D.Guid, sunExposure, name);
        }

        public static SolarFaceSimulationResult SolarFaceSimulationResult(this Guid guid, IEnumerable<Tuple<DateTime, List<Face3D>>> sunExposure, string name = null)
        {
            SolarFaceSimulationResult result = new SolarFaceSimulationResult(name, Query.Source(), guid.ToString(), sunExposure);
            return result;
        }
    }
}

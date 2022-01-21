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

            SolarFaceSimulationResult result = new SolarFaceSimulationResult(name, Query.Source(), linkedFace3D.Guid.ToString(), sunExposure);
            return result;
        }
    }
}

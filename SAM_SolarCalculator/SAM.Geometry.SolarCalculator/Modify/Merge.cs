using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Modify
    {
        public static SolarFaceSimulationResult Merge(this SolarFaceSimulationResult solarFaceSimulationResult_1, SolarFaceSimulationResult solarFaceSimulationResult_2)
        {
            if (solarFaceSimulationResult_1 == null || solarFaceSimulationResult_2 == null)
            {
                return null;
            }

            Dictionary<DateTime, Tuple<Radiation, List<Face3D>>> sunExposure = new Dictionary<DateTime, Tuple<Radiation, List<Face3D>>>();
            
            List<DateTime> dateTimes = null;

            dateTimes = solarFaceSimulationResult_1.DateTimes;
            if(dateTimes != null)
            {
                foreach(DateTime dateTime in dateTimes)
                {
                    sunExposure[dateTime] = new Tuple<Radiation, List<Face3D>>(solarFaceSimulationResult_1.GetRadiation(dateTime), solarFaceSimulationResult_1.GetSunExposureFace3Ds(dateTime));
                }
            }

            dateTimes = solarFaceSimulationResult_2.DateTimes;
            if (dateTimes != null)
            {
                foreach (DateTime dateTime in dateTimes)
                {
                    sunExposure[dateTime] = new Tuple<Radiation, List<Face3D>>(solarFaceSimulationResult_2.GetRadiation(dateTime), solarFaceSimulationResult_2.GetSunExposureFace3Ds(dateTime));
                }
            }

            return new SolarFaceSimulationResult(solarFaceSimulationResult_1, sunExposure);
        }
    }
}

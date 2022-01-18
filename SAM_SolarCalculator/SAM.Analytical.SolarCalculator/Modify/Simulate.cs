using SAM.Geometry.SolarCalculator;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Modify
    {
        public static List<SolarFaceSimulationResult> Simulate(this AnalyticalModel analyticalModel, IEnumerable<DateTime> dateTimes, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Distance = Core.Tolerance.Distance)
        {
            if(analyticalModel == null || dateTimes == null)
            {
                return null;
            }

            SolarModel solarModel = Convert.ToSAM_SolarModel(analyticalModel);
            if(solarModel == null)
            {
                return null;
            }

            List<SolarFaceSimulationResult> result = solarModel.Simulate(dateTimes, tolerance_Area, tolerance_Snap, tolerance_Distance);
            if(result != null && result.Count != 0)
            {
                List<Panel> panels = analyticalModel.GetPanels();
                foreach (SolarFaceSimulationResult solarFaceSimulationResult in result)
                {
                    Guid guid = Guid.Empty;

                    Panel panel = panels.Find(x => x.Guid.ToString().Equals(solarFaceSimulationResult.Reference));
                    if(panel != null)
                    {
                        guid = panel.Guid;
                    }

                    analyticalModel.AddResult<Panel>(solarFaceSimulationResult, guid);
                }
            }

            return result;
        }
    }
}

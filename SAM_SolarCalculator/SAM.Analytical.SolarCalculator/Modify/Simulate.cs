using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Modify
    {
        public static List<SolarFaceSimulationResult> Simulate(this AnalyticalModel analyticalModel, IEnumerable<DateTime> dateTimes, bool merge = false, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance)
        {
            if(analyticalModel == null || dateTimes == null)
            {
                return null;
            }

            Core.Location location = analyticalModel.Location;
            if (location == null)
            {
                return null;
            }

            Dictionary<DateTime, Vector3D> directionDictionary = new Dictionary<DateTime, Vector3D>();
            foreach (DateTime dateTime in dateTimes)
            {
                directionDictionary[dateTime] = Geometry.SolarCalculator.Query.SunDirection(location, dateTime, false);
            }

            return Simulate(analyticalModel, directionDictionary, merge, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance);
        }

        public static List<SolarFaceSimulationResult> Simulate(this AnalyticalModel analyticalModel, Dictionary<DateTime, Vector3D> directionDictionary, bool merge = false,double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance)
        {
            if (analyticalModel == null || directionDictionary == null)
            {
                return null;
            }

            SolarModel solarModel = Convert.ToSAM_SolarModel(analyticalModel);
            if (solarModel == null)
            {
                return null;
            }

            List<SolarFaceSimulationResult> result = null;

            List<SolarFaceSimulationResult> solarFaceSimulationResults = solarModel.Simulate(directionDictionary, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance);
            if (solarFaceSimulationResults != null && solarFaceSimulationResults.Count != 0)
            {
                result = new List<SolarFaceSimulationResult>();

                List<Panel> panels = analyticalModel.GetPanels();
                foreach (SolarFaceSimulationResult solarFaceSimulationResult in solarFaceSimulationResults)
                {
                    Guid guid = Guid.Empty;

                    Panel panel = panels.Find(x => x.Guid.ToString().Equals(solarFaceSimulationResult.Reference));
                    if (panel != null)
                    {
                        guid = panel.Guid;
                    }

                    if(!merge)
                    {
                        analyticalModel.AddResult<Panel>(solarFaceSimulationResult, guid);
                        result.Add(solarFaceSimulationResult);
                        continue;
                    }

                    List<SolarFaceSimulationResult> solarFaceSimulationResuls_Panel = analyticalModel.GetRelatedObjects<SolarFaceSimulationResult>(panel);
                    if(solarFaceSimulationResuls_Panel == null || solarFaceSimulationResuls_Panel.Count == 0)
                    {
                        analyticalModel.AddResult<Panel>(solarFaceSimulationResult, guid);
                        result.Add(solarFaceSimulationResult);
                        continue;
                    }

                    foreach(SolarFaceSimulationResult solarFaceSimulationResult_Panel in solarFaceSimulationResuls_Panel)
                    {
                        SolarFaceSimulationResult solarFaceSimulationResult_New = solarFaceSimulationResult_Panel.Merge(solarFaceSimulationResult);
                        if(solarFaceSimulationResult_New == null)
                        {
                            continue;
                        }

                        analyticalModel.AddResult<Panel>(solarFaceSimulationResult_New, guid);
                        result.Add(solarFaceSimulationResult_New);
                    }
                }
            }

            return solarFaceSimulationResults;
        }

        public static List<SolarFaceSimulationResult> Simulate(this BuildingModel buildingModel, IEnumerable<DateTime> dateTimes, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance)
        {
            if (buildingModel == null || dateTimes == null)
            {
                return null;
            }

            SolarModel solarModel = Convert.ToSAM_SolarModel(buildingModel);
            if (solarModel == null)
            {
                return null;
            }

            List<SolarFaceSimulationResult> result = solarModel.Simulate(dateTimes, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance);
            if (result != null && result.Count != 0)
            {
                List<IPartition> partitions = buildingModel.GetPartitions();
                foreach (SolarFaceSimulationResult solarFaceSimulationResult in result)
                {
                    Guid guid = Guid.Empty;

                    IPartition partition = partitions.Find(x => x.Guid.ToString().Equals(solarFaceSimulationResult.Reference));
                    if (partition != null)
                    {
                        guid = partition.Guid;
                    }

                    buildingModel.Add<IPartition>(solarFaceSimulationResult, guid);
                }
            }

            return result;
        }
    }
}

using SAM.Geometry.Object.Spatial;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Modify
    {
        public static List<SolarFaceSimulationResult> Simulate(this SolarModel solarModel, Dictionary<DateTime, Vector3D> directionDictionary, double minHorizonAngle = Core.Tolerance.Angle, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance)
        {
            if (solarModel == null || directionDictionary == null)
            {
                return null;
            }

            Core.Location location = solarModel.Location;

            List<LinkedFace3D> LinkedFace3Ds = solarModel.GetLinkedFace3Ds();
            if (LinkedFace3Ds == null)
            {
                return null;
            }

            Dictionary<LinkedFace3D, List<LinkedFace3D>> dictionary_Merge = Query.Merge(LinkedFace3Ds, tolerance_Snap, tolerance_Area, tolerance_Distance);
            if (dictionary_Merge == null)
            {
                return null;
            }

            List<LinkedFace3D> LinkedFace3Ds_Merge = new List<LinkedFace3D>(dictionary_Merge.Keys);

            Dictionary<LinkedFace3D, List<Tuple<DateTime, Face3D>>> dictionary = new Dictionary<LinkedFace3D, List<Tuple<DateTime, Face3D>>>();

            List<Tuple<DateTime, List<LinkedFace3D>>> tuples = Enumerable.Repeat<Tuple<DateTime, List<LinkedFace3D>>>(null, directionDictionary.Count()).ToList();
            Parallel.For(0, directionDictionary.Count(), (int i) =>
            //for (int i = 0; i < directionDictionary.Count(); i++)
            {
                DateTime dateTime = directionDictionary.Keys.ElementAt(i);

                Vector3D sunDirection = directionDictionary[dateTime];
                if (sunDirection == null || !sunDirection.IsValid())
                {
                    return;
                    //continue;
                }

                if (sunDirection.Z > 0)
                {
                    return;
                    //continue;
                }

                //The 9th Hour is the position of the sun at 8:30 am.The sun rises at 8:11am.That time the sun will be on the horizon.We have a hedge so the sun needs to be above the hedge(just like a hedgerow) for it to be seen.So the sun should be above the horizon by 0.1 degrees.
                double angle = Plane.WorldXY.Project(sunDirection).SmallestAngle(sunDirection);
                if (angle < minHorizonAngle)// 0.1 radians
                {
                    return;
                    //continue;
                }

                List <LinkedFace3D> linkedFace3Ds_ExposedToSun = Object.Spatial.Query.VisibleLinkedFace3Ds(LinkedFace3Ds_Merge, sunDirection, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance);
                if (linkedFace3Ds_ExposedToSun == null || linkedFace3Ds_ExposedToSun.Count == 0)
                {
                    return;
                    //continue;
                }

                List<LinkedFace3D> LinkedFace3Ds_DateTime = new List<LinkedFace3D>();
                foreach (LinkedFace3D linkedFace3D_ExposedToSun in linkedFace3Ds_ExposedToSun)
                {
                    LinkedFace3D linkedFace3D_Merge = LinkedFace3Ds_Merge.Find(x => x.Guid == linkedFace3D_ExposedToSun.Guid);
                    if (LinkedFace3Ds_Merge == null)
                    {
                        continue;
                    }

                    if (!dictionary_Merge.TryGetValue(linkedFace3D_Merge, out List<LinkedFace3D> solarFaces_SolarModel) || solarFaces_SolarModel == null)
                    {
                        continue;
                    }

                    Face3D face3D_ExposedToSun = linkedFace3D_ExposedToSun.Face3D;
                    Plane plane = face3D_ExposedToSun.GetPlane();
                    Planar.Face2D face2D_ExposedToSun = plane.Convert(face3D_ExposedToSun);

                    foreach (LinkedFace3D linkedFace3D_SolarModel in solarFaces_SolarModel)
                    {
                        Face3D face3D_SolarModel = linkedFace3D_SolarModel?.Face3D;
                        if (face3D_SolarModel == null)
                        {
                            continue;
                        }

                        Planar.Face2D face2D = plane.Convert(plane.Project(face3D_SolarModel));

                        List<Planar.Face2D> face2Ds_Intersection = Planar.Query.Intersection(face2D, face2D_ExposedToSun, tolerance_Distance);
                        if (face2Ds_Intersection == null || face2Ds_Intersection.Count == 0)
                        {
                            continue;
                        }

                        Plane plane_SolarModel = face3D_SolarModel.GetPlane();

                        foreach (Planar.Face2D face2D_Intersection in face2Ds_Intersection)
                        {
                            Face3D face3D = plane.Convert(face2D_Intersection);
                            if (face3D == null)
                            {
                                continue;
                            }

                            LinkedFace3Ds_DateTime.Add(new LinkedFace3D(linkedFace3D_SolarModel.Guid, plane_SolarModel.Project(face3D)));
                        }
                    }

                }

                tuples[i] = new Tuple<DateTime, List<LinkedFace3D>>(dateTime, LinkedFace3Ds_DateTime);
            });

            foreach (LinkedFace3D linkedFace3D in LinkedFace3Ds)
            {
                List<Tuple<DateTime, List<Face3D>>> sunExposure = new List<Tuple<DateTime, List<Face3D>>>();
                foreach (Tuple<DateTime, List<LinkedFace3D>> tuple in tuples)
                {
                    List<LinkedFace3D> linkedFace3Ds_Tuple = tuple?.Item2?.FindAll(x => x.Guid == linkedFace3D.Guid);
                    if (linkedFace3Ds_Tuple == null || linkedFace3Ds_Tuple.Count == 0)
                    {
                        continue;
                    }

                    sunExposure.Add(new Tuple<DateTime, List<Face3D>>(tuple.Item1, linkedFace3Ds_Tuple.ConvertAll(x => x.Face3D)));
                }

                if (sunExposure == null || sunExposure.Count == 0)
                {
                    continue;
                }

                SolarFaceSimulationResult solarFaceSimulationResult = Create.SolarFaceSimulationResult(linkedFace3D, sunExposure);
                if (solarFaceSimulationResult == null)
                {
                    continue;
                }

                solarModel.Add(solarFaceSimulationResult, linkedFace3D.Guid);
            }

            return solarModel.GetSolarFaceSimulationResults();
        }


        public static List<SolarFaceSimulationResult> Simulate(this SolarModel solarModel, IEnumerable<DateTime> dateTimes, double minHorizonAngle = Core.Tolerance.Angle, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance)
        {
            if(solarModel == null || dateTimes == null)
            {
                return null;
            }

            Core.Location location = solarModel.Location;

            if(location == null)
            {
                return null;
            }

            Dictionary<DateTime, Vector3D> directionDictionary = new Dictionary<DateTime, Vector3D>();
            foreach(DateTime dateTime in dateTimes)
            {
                directionDictionary[dateTime] = Query.SunDirection(location, dateTime, false);
            }

            return Simulate(solarModel, directionDictionary, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance);
        }

        /// <summary>
        /// Simulates SolarModel
        /// </summary>
        /// <param name="solarModel"></param>
        /// <param name="year"></param>
        /// <param name="hoursOfYear">hours of the year. Values starting from 0 to 8760</param>
        /// <param name="minHorizonAngle">Minimal Angle to Horizon</param>
        /// <param name="tolerance_Area"></param>
        /// <param name="tolerance_Snap"></param>
        /// <param name="tolerance_Angle"></param>
        /// <param name="tolerance_Distance"></param>
        /// <returns>SolarFaceSimulationResults</returns>
        public static List<SolarFaceSimulationResult> Simulate(this SolarModel solarModel, int year, List<int> hoursOfYear, double minHorizonAngle = Core.Tolerance.Angle, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance)
        {
            if(solarModel == null || hoursOfYear == null)
            {
                return null;
            }

            List<DateTime> dateTimes = new List<DateTime>();
            foreach(int hourOfYear in hoursOfYear)
            {
                DateTime dateTime = new DateTime(year, 1, 1);
                dateTime = dateTime.AddHours(hourOfYear);

                dateTimes.Add(dateTime);
            }

            return Simulate(solarModel, dateTimes, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance);
        }
    }
}

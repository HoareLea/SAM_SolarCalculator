using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Modify
    {
        public static List<SolarFaceSimulationResult> Simulate(this SolarModel solarModel, IEnumerable<DateTime> dateTimes, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Distance = Core.Tolerance.Distance)
        {
            if(solarModel == null || dateTimes == null)
            {
                return null;
            }

            Core.Location location = solarModel.Location;

            List<SolarFace> solarFaces = solarModel.GetSolarFaces();
            if(solarFaces == null)
            {
                return null;
            }

            Dictionary<SolarFace, List<SolarFace>> dictionary_Merge = Query.Merge(solarFaces, tolerance_Snap, tolerance_Area, tolerance_Distance);
            if(dictionary_Merge == null)
            {
                return null;
            }

            List<SolarFace> solarFaces_Merge = new List<SolarFace>(dictionary_Merge.Keys);

            Dictionary<SolarFace, List<Tuple<DateTime, Face3D>>> dictionary = new Dictionary<SolarFace, List<Tuple<DateTime, Face3D>>>();

            List<Tuple<DateTime, List<SolarFace>>> tuples = Enumerable.Repeat<Tuple<DateTime, List<SolarFace>>>(null, dateTimes.Count()).ToList();
            Parallel.For(0, dateTimes.Count(), (int i) =>
            //for (int i = 0; i < dateTimes.Count(); i++)
            {
                DateTime dateTime = dateTimes.ElementAt(i);

                Vector3D sunDirection = Query.SunDirection(location, dateTime, false);
                if (sunDirection == null)
                {
                    return;
                    //continue;
                }

                List<SolarFace> solarFaces_ExposedToSun = Query.ExposedToSunSolarFaces(solarFaces_Merge, sunDirection, tolerance_Area, tolerance_Snap, tolerance_Distance);
                if (solarFaces_ExposedToSun == null || solarFaces_ExposedToSun.Count == 0)
                {
                    return;
                    //continue;
                }

                List<SolarFace> solarFaces_DateTime = new List<SolarFace>();
                foreach (SolarFace solarFace_ExposedToSun in solarFaces_ExposedToSun)
                {
                    SolarFace solarFace_Merge = solarFaces_Merge.Find(x => x.Guid == solarFace_ExposedToSun.Guid);
                    if (solarFaces_Merge == null)
                    {
                        continue;
                    }

                    if (!dictionary_Merge.TryGetValue(solarFace_Merge, out List<SolarFace> solarFaces_SolarModel) || solarFaces_SolarModel == null)
                    {
                        continue;
                    }

                    Face3D face3D_ExposedToSun = solarFace_ExposedToSun.Face3D;
                    Plane plane = face3D_ExposedToSun.GetPlane();
                    Planar.Face2D face2D_ExposedToSun = plane.Convert(face3D_ExposedToSun);

                    foreach (SolarFace solarFace_SolarModel in solarFaces_SolarModel)
                    {
                        Face3D face3D_SolarModel = solarFace_SolarModel?.Face3D;
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

                            solarFaces_DateTime.Add(new SolarFace(solarFace_SolarModel.Guid, plane_SolarModel.Project(face3D)));
                        }
                    }

                }

                tuples[i] = new Tuple<DateTime, List<SolarFace>>(dateTime, solarFaces_DateTime);
            });

            foreach(SolarFace solarFace in solarFaces)
            {
                List<Tuple<DateTime, List<Face3D>>> sunExposure = new List<Tuple<DateTime, List<Face3D>>>();
                foreach(Tuple<DateTime, List<SolarFace>> tuple in tuples)
                {
                    List<SolarFace> solarFaces_Tuple = tuple?.Item2?.FindAll(x => x.Guid == solarFace.Guid);
                    if(solarFaces_Tuple == null || solarFaces_Tuple.Count == 0)
                    {
                        continue;
                    }

                    sunExposure.Add(new Tuple<DateTime, List<Face3D>>(tuple.Item1, solarFaces_Tuple.ConvertAll(x => x.Face3D)));
                }

                if(sunExposure == null && sunExposure.Count == 0)
                {
                    continue;
                }

                SolarFaceSimulationResult solarFaceSimulationResult =Create.SolarFaceSimulationResult(solarFace, sunExposure);
                if(solarFaceSimulationResult == null)
                {
                    continue;
                }

                solarModel.Add(solarFaceSimulationResult, solarFace.Guid);
            }

            return solarModel.GetSolarFaceSimulationResults();
        }

        /// <summary>
        /// Simulates SolarModel
        /// </summary>
        /// <param name="solarModel"></param>
        /// <param name="year"></param>
        /// <param name="hoursOfYear">hours of the year. Values starting from 0 to 8760</param>
        /// <param name="tolerance_Area"></param>
        /// <param name="tolerance_Snap"></param>
        /// <param name="tolerance_Distance"></param>
        /// <returns>SolarFaceSimulationResults</returns>
        public static List<SolarFaceSimulationResult> Simulate(this SolarModel solarModel, int year, List<int> hoursOfYear, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Distance = Core.Tolerance.Distance)
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

            return Simulate(solarModel, dateTimes, tolerance_Area, tolerance_Snap, tolerance_Distance);
        }
    }
}

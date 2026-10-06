// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Object.Spatial;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SAM.Weather.SolarCalculator
{
    public static partial class Modify
    {
        public static List<SolarFaceSimulationResult> Simulate(this SolarModel solarModel, Dictionary<DateTime, Vector3D> directionDictionary, bool calctulateRadiation, double minHorizonAngle = Core.Tolerance.Angle, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance, double sampleSize = double.NaN)
        {
            // Single shared sun-visibility pipeline: ComputeSunExposure runs the (exact or sampled)
            // occlusion pass for every timestep; this front-end only materialises the heavy
            // SolarFaceSimulationResults from the returned exposure map.
            Dictionary<Guid, List<Tuple<DateTime, Radiation, List<Face3D>>>> dictionary_SunExposure = ComputeSunExposure(solarModel, directionDictionary, calctulateRadiation, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, sampleSize, out List<LinkedFace3D> linkedFace3Ds);
            if (dictionary_SunExposure == null || linkedFace3Ds == null)
            {
                return null;
            }

            AttachResults(solarModel, linkedFace3Ds, dictionary_SunExposure);
            return solarModel.GetSolarFaceSimulationResults();
        }

        /// <summary>
        /// Coverage-only variant of <see cref="Simulate(SolarModel, Dictionary{DateTime, Vector3D}, bool, double, double, double, double, double, double)"/>.
        /// Runs the same sun-visibility pipeline but emits <see cref="SolarCoverageSimulationResult"/>
        /// (per-timestep lit-area / total-area ratio) and stores them on the SolarModel instead of
        /// the heavier <see cref="SolarFaceSimulationResult"/>. Use this when you want to benchmark
        /// against TAS-imported shade-coverage data and don't need the per-timestep lit polygons.
        /// </summary>
        public static List<SolarCoverageSimulationResult> Simulate_Coverage(this SolarModel solarModel, Dictionary<DateTime, Vector3D> directionDictionary, double minHorizonAngle = Core.Tolerance.Angle, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance, double sampleSize = double.NaN)
        {
            if (solarModel == null || directionDictionary == null)
            {
                return null;
            }

            // Pre-collect every timestep that passes the sun-above-horizon check so we
            // can emit an all-zero coverage series for faces that are fully shaded across
            // the entire run — keeping the per-face DateTime grid consistent with TAS.
            List<DateTime> validDateTimes = new List<DateTime>();
            foreach (KeyValuePair<DateTime, Vector3D> kvp in directionDictionary)
            {
                if (ValidSunDirection(kvp.Value, minHorizonAngle))
                {
                    validDateTimes.Add(kvp.Key);
                }
            }

            Dictionary<Guid, List<Tuple<DateTime, Radiation, List<Face3D>>>> dictionary_SunExposure = ComputeSunExposure(solarModel, directionDictionary, false, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, sampleSize, out List<LinkedFace3D> LinkedFace3Ds);
            if (dictionary_SunExposure == null)
            {
                return null;
            }

            AttachCoverageResults(solarModel, LinkedFace3Ds, dictionary_SunExposure, validDateTimes);
            return solarModel.SolarCoverageSimulationResults;
        }

        public static List<SolarCoverageSimulationResult> Simulate_Coverage(this SolarModel solarModel, IEnumerable<DateTime> dateTimes, double minHorizonAngle = Core.Tolerance.Angle, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance, double sampleSize = double.NaN)
        {
            if (solarModel == null || dateTimes == null)
            {
                return null;
            }

            Core.Location location = solarModel.Location;
            if (location == null)
            {
                return null;
            }

            Dictionary<DateTime, Vector3D> directionDictionary = new Dictionary<DateTime, Vector3D>();
            foreach (DateTime dateTime in dateTimes)
            {
                directionDictionary[dateTime] = SAM.Geometry.SolarCalculator.Query.SunDirection(location, dateTime, false);
            }

            return Simulate_Coverage(solarModel, directionDictionary, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, sampleSize);
        }

        private static void AttachResults(SolarModel solarModel, List<LinkedFace3D> linkedFace3Ds, Dictionary<Guid, List<Tuple<DateTime, Radiation, List<Face3D>>>> dictionary_SunExposure)
        {
            foreach (LinkedFace3D linkedFace3D in linkedFace3Ds)
            {
                if (!dictionary_SunExposure.TryGetValue(linkedFace3D.Guid, out List<Tuple<DateTime, Radiation, List<Face3D>>> sunExposure) || sunExposure == null || sunExposure.Count == 0)
                {
                    continue;
                }

                SolarFaceSimulationResult solarFaceSimulationResult = Geometry.SolarCalculator.Create.SolarFaceSimulationResult(linkedFace3D, sunExposure);
                if (solarFaceSimulationResult == null)
                {
                    continue;
                }

                solarModel.Add(solarFaceSimulationResult, linkedFace3D.Guid);
            }
        }

        private static void AttachCoverageResults(SolarModel solarModel, List<LinkedFace3D> linkedFace3Ds, Dictionary<Guid, List<Tuple<DateTime, Radiation, List<Face3D>>>> dictionary_SunExposure, List<DateTime> allValidDateTimes = null)
        {
            foreach (LinkedFace3D linkedFace3D in linkedFace3Ds)
            {
                dictionary_SunExposure.TryGetValue(linkedFace3D.Guid, out List<Tuple<DateTime, Radiation, List<Face3D>>> sunExposure);

                // Build the coverage series over EVERY valid timestep: the lit entry where the face is
                // exposed, and an explicit zero-coverage entry where it is fully shaded. This keeps the
                // per-face DateTime grid complete and consistent with TAS — without it, a face that
                // alternates lit/shaded would record only its lit timesteps, leaving the shaded ones as
                // gaps (NaN) that CompareSolarCoverage then skips, biasing the comparison. Handles all
                // cases uniformly: fully shaded (all zero), partially shaded (mix), fully lit (all lit).
                List<Tuple<DateTime, Radiation, List<Face3D>>> exposure;
                if (allValidDateTimes != null && allValidDateTimes.Count > 0)
                {
                    Dictionary<DateTime, Tuple<DateTime, Radiation, List<Face3D>>> litByDateTime = new Dictionary<DateTime, Tuple<DateTime, Radiation, List<Face3D>>>();
                    if (sunExposure != null)
                    {
                        foreach (Tuple<DateTime, Radiation, List<Face3D>> entry in sunExposure)
                        {
                            if (entry != null && !litByDateTime.ContainsKey(entry.Item1))
                            {
                                litByDateTime[entry.Item1] = entry;
                            }
                        }
                    }

                    exposure = new List<Tuple<DateTime, Radiation, List<Face3D>>>(allValidDateTimes.Count);
                    foreach (DateTime dateTime in allValidDateTimes)
                    {
                        exposure.Add(litByDateTime.TryGetValue(dateTime, out Tuple<DateTime, Radiation, List<Face3D>> lit)
                            ? lit
                            : Tuple.Create(dateTime, (Radiation)null, (List<Face3D>)null));
                    }
                }
                else
                {
                    // No valid-timestep grid supplied — fall back to the lit entries only (legacy behaviour).
                    if (sunExposure == null || sunExposure.Count == 0)
                    {
                        continue;
                    }
                    exposure = sunExposure;
                }

                SolarCoverageSimulationResult solarCoverageSimulationResult = Geometry.SolarCalculator.Create.SolarCoverageSimulationResult(linkedFace3D, exposure);
                if (solarCoverageSimulationResult == null)
                {
                    continue;
                }

                solarModel.Add(solarCoverageSimulationResult, linkedFace3D.Guid);
            }
        }

        /// <summary>
        /// Shared sun-visibility pipeline used by both <see cref="Simulate"/> and
        /// <see cref="Simulate_Coverage"/>. Returns the raw per-face exposure map; callers
        /// decide whether to materialise SolarFaceSimulationResult or SolarCoverageSimulationResult.
        /// </summary>
        private static Dictionary<Guid, List<Tuple<DateTime, Radiation, List<Face3D>>>> ComputeSunExposure(SolarModel solarModel, Dictionary<DateTime, Vector3D> directionDictionary, bool calctulateRadiation, double minHorizonAngle, double tolerance_Area, double tolerance_Snap, double tolerance_Angle, double tolerance_Distance, double sampleSize, out List<LinkedFace3D> linkedFace3Ds_Out)
        {
            linkedFace3Ds_Out = null;

            if (solarModel == null || directionDictionary == null)
            {
                return null;
            }

            List<LinkedFace3D> LinkedFace3Ds = solarModel.GetLinkedFace3Ds();
            if (LinkedFace3Ds == null)
            {
                return null;
            }

            linkedFace3Ds_Out = LinkedFace3Ds;

            Dictionary<LinkedFace3D, List<LinkedFace3D>> dictionary_Merge = Geometry.SolarCalculator.Query.Merge(LinkedFace3Ds, tolerance_Snap, tolerance_Area, tolerance_Distance);
            if (dictionary_Merge == null)
            {
                return null;
            }

            WeatherData weatherData = !calctulateRadiation ? null : solarModel.GetValue<WeatherData>(SolarModelParameter.WeatherData);

            List<LinkedFace3D> LinkedFace3Ds_Merge = new List<LinkedFace3D>(dictionary_Merge.Keys);

            Dictionary<Guid, LinkedFace3D> dictionary_LinkedFace3D_Merge = new Dictionary<Guid, LinkedFace3D>();
            foreach (LinkedFace3D linkedFace3D_Merge in LinkedFace3Ds_Merge)
            {
                dictionary_LinkedFace3D_Merge[linkedFace3D_Merge.Guid] = linkedFace3D_Merge;
            }

            KeyValuePair<DateTime, Vector3D>[] directionKeyValuePairs = directionDictionary.ToArray();

            List<Tuple<DateTime, List<LinkedFace3D>>> tuples;
            if (!double.IsNaN(sampleSize) && sampleSize > tolerance_Distance)
            {
                List<SampleCell> sampleCells = SampleCells(dictionary_Merge, sampleSize, tolerance_Area, tolerance_Distance);
                if (sampleCells == null || sampleCells.Count == 0)
                {
                    return new Dictionary<Guid, List<Tuple<DateTime, Radiation, List<Face3D>>>>();
                }

                tuples = Enumerable.Repeat<Tuple<DateTime, List<LinkedFace3D>>>(null, directionKeyValuePairs.Length).ToList();
                Parallel.For(0, directionKeyValuePairs.Length, i =>
                {
                    DateTime dateTime = directionKeyValuePairs[i].Key;
                    Vector3D sunDirection = directionKeyValuePairs[i].Value;
                    if (!ValidSunDirection(sunDirection, minHorizonAngle))
                    {
                        return;
                    }

                    List<LinkedFace3D> linkedFace3Ds_ExposedToSun = ExposedSampleLinkedFace3Ds(sampleCells, LinkedFace3Ds_Merge, sunDirection, tolerance_Area, tolerance_Angle, tolerance_Distance);
                    if (linkedFace3Ds_ExposedToSun == null || linkedFace3Ds_ExposedToSun.Count == 0)
                    {
                        return;
                    }

                    tuples[i] = new Tuple<DateTime, List<LinkedFace3D>>(dateTime, linkedFace3Ds_ExposedToSun);
                });
            }
            else
            {
                tuples = Enumerable.Repeat<Tuple<DateTime, List<LinkedFace3D>>>(null, directionKeyValuePairs.Length).ToList();
                Parallel.For(0, directionKeyValuePairs.Length, i =>
                {
                    DateTime dateTime = directionKeyValuePairs[i].Key;

                    Vector3D sunDirection = directionKeyValuePairs[i].Value;
                    if (!ValidSunDirection(sunDirection, minHorizonAngle))
                    {
                        return;
                    }

                    List<LinkedFace3D> linkedFace3Ds_ExposedToSun = ExposedExactLinkedFace3Ds(LinkedFace3Ds_Merge, dictionary_LinkedFace3D_Merge, dictionary_Merge, sunDirection, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance);
                    if (linkedFace3Ds_ExposedToSun == null || linkedFace3Ds_ExposedToSun.Count == 0)
                    {
                        return;
                    }

                    tuples[i] = new Tuple<DateTime, List<LinkedFace3D>>(dateTime, linkedFace3Ds_ExposedToSun);
                });
            }

            Dictionary<Guid, List<Tuple<DateTime, Radiation, List<Face3D>>>> dictionary_SunExposure = new Dictionary<Guid, List<Tuple<DateTime, Radiation, List<Face3D>>>>();
            foreach (Tuple<DateTime, List<LinkedFace3D>> tuple in tuples)
            {
                if (tuple?.Item2 == null || tuple.Item2.Count == 0)
                {
                    continue;
                }

                foreach (IGrouping<Guid, LinkedFace3D> grouping in tuple.Item2.GroupBy(x => x.Guid))
                {
                    List<LinkedFace3D> linkedFace3Ds_Tuple = grouping.ToList();
                    Radiation radiation = null;
                    if (weatherData != null)
                    {
                        Plane plane = linkedFace3Ds_Tuple[0]?.Face3D?.GetPlane();
                        if (plane != null)
                        {
                            radiation = Create.Radiation(weatherData, tuple.Item1, plane);
                        }
                    }

                    if (!dictionary_SunExposure.TryGetValue(grouping.Key, out List<Tuple<DateTime, Radiation, List<Face3D>>> sunExposure))
                    {
                        sunExposure = new List<Tuple<DateTime, Radiation, List<Face3D>>>();
                        dictionary_SunExposure[grouping.Key] = sunExposure;
                    }

                    sunExposure.Add(new Tuple<DateTime, Radiation, List<Face3D>>(tuple.Item1, radiation, linkedFace3Ds_Tuple.ConvertAll(x => x.Face3D)));
                }
            }

            return dictionary_SunExposure;
        }


        public static List<SolarFaceSimulationResult> Simulate(this SolarModel solarModel, IEnumerable<DateTime> dateTimes, bool calctulateRadiation, double minHorizonAngle = Core.Tolerance.Angle, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance, double sampleSize = double.NaN)
        {
            if (solarModel == null || dateTimes == null)
            {
                return null;
            }

            Core.Location location = solarModel.Location;

            if (location == null)
            {
                return null;
            }

            Dictionary<DateTime, Vector3D> directionDictionary = new Dictionary<DateTime, Vector3D>();
            foreach (DateTime dateTime in dateTimes)
            {
                directionDictionary[dateTime] = SAM.Geometry.SolarCalculator.Query.SunDirection(location, dateTime, false);
            }

            return Simulate(solarModel, directionDictionary, calctulateRadiation, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, sampleSize);
        }

        /// <summary>
        /// Simulates SolarModel
        /// </summary>
        /// <param name="solarModel"></param>
        /// <param name="year"></param>
        /// <param name="hoursOfYear">hours of the year. Values starting from 0 to 8760</param>
        /// <param name="calctulateRadiation"></param>
        /// <param name="minHorizonAngle">Minimal Angle to Horizon</param>
        /// <param name="tolerance_Area"></param>
        /// <param name="tolerance_Snap"></param>
        /// <param name="tolerance_Angle"></param>
        /// <param name="tolerance_Distance"></param>
        /// <param name="sampleSize">Optional sample grid size. NaN or values less than tolerance_Distance use exact geometry.</param>
        /// <returns>SolarFaceSimulationResults</returns>
        public static List<SolarFaceSimulationResult> Simulate(this SolarModel solarModel, int year, List<int> hoursOfYear, bool calctulateRadiation, double minHorizonAngle = Core.Tolerance.Angle, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance, double sampleSize = double.NaN)
        {
            if (solarModel == null || hoursOfYear == null)
            {
                return null;
            }

            List<DateTime> dateTimes = new List<DateTime>();
            foreach (int hourOfYear in hoursOfYear)
            {
                DateTime dateTime = new DateTime(year, 1, 1);
                dateTime = dateTime.AddHours(hourOfYear);

                dateTimes.Add(dateTime);
            }

            return Simulate(solarModel, dateTimes, calctulateRadiation, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, sampleSize);
        }

        /// <summary>
        /// The exact (projective) sun-visibility primitive shared by every exact-mode simulation path:
        /// for one sun direction, projects the visible merged faces and clips them back onto the
        /// source faces, returning the lit fragments as LinkedFace3Ds keyed by their source Guids.
        /// Extracted from the three previously duplicated occlusion loops (Simulate, Simulate_Sampled
        /// and ComputeSunExposure) — behaviour unchanged.
        /// </summary>
        private static List<LinkedFace3D> ExposedExactLinkedFace3Ds(List<LinkedFace3D> linkedFace3Ds_Merge, Dictionary<Guid, LinkedFace3D> dictionary_LinkedFace3D_Merge, Dictionary<LinkedFace3D, List<LinkedFace3D>> dictionary_Merge, Vector3D sunDirection, double tolerance_Area, double tolerance_Snap, double tolerance_Angle, double tolerance_Distance)
        {
            if (linkedFace3Ds_Merge == null || linkedFace3Ds_Merge.Count == 0 || dictionary_LinkedFace3D_Merge == null || dictionary_Merge == null || sunDirection == null)
            {
                return null;
            }

            List<LinkedFace3D> linkedFace3Ds_ExposedToSun = Geometry.Object.Spatial.Query.VisibleLinkedFace3Ds(linkedFace3Ds_Merge, sunDirection, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance);
            if (linkedFace3Ds_ExposedToSun == null || linkedFace3Ds_ExposedToSun.Count == 0)
            {
                return null;
            }

            List<LinkedFace3D> result = new List<LinkedFace3D>();
            foreach (LinkedFace3D linkedFace3D_ExposedToSun in linkedFace3Ds_ExposedToSun)
            {
                if (!dictionary_LinkedFace3D_Merge.TryGetValue(linkedFace3D_ExposedToSun.Guid, out LinkedFace3D linkedFace3D_Merge))
                {
                    continue;
                }

                if (!dictionary_Merge.TryGetValue(linkedFace3D_Merge, out List<LinkedFace3D> solarFaces_SolarModel) || solarFaces_SolarModel == null)
                {
                    continue;
                }

                Face3D face3D_ExposedToSun = linkedFace3D_ExposedToSun.Face3D;
                Plane plane = face3D_ExposedToSun.GetPlane();
                if (plane == null)
                {
                    continue;
                }

                Geometry.Planar.Face2D face2D_ExposedToSun = plane.Convert(face3D_ExposedToSun);
                if (face2D_ExposedToSun == null)
                {
                    continue;
                }

                foreach (LinkedFace3D linkedFace3D_SolarModel in solarFaces_SolarModel)
                {
                    Face3D face3D_SolarModel = linkedFace3D_SolarModel?.Face3D;
                    if (face3D_SolarModel == null)
                    {
                        continue;
                    }

                    Geometry.Planar.Face2D face2D = plane.Convert(plane.Project(face3D_SolarModel));

                    List<Geometry.Planar.Face2D> face2Ds_Intersection = Geometry.Planar.Query.Intersection(face2D, face2D_ExposedToSun, tolerance_Distance);
                    if (face2Ds_Intersection == null || face2Ds_Intersection.Count == 0)
                    {
                        continue;
                    }

                    Plane plane_SolarModel = face3D_SolarModel.GetPlane();
                    if (plane_SolarModel == null)
                    {
                        continue;
                    }

                    foreach (Geometry.Planar.Face2D face2D_Intersection in face2Ds_Intersection)
                    {
                        Face3D face3D = plane.Convert(face2D_Intersection);
                        if (face3D == null)
                        {
                            continue;
                        }

                        result.Add(new LinkedFace3D(linkedFace3D_SolarModel.Guid, plane_SolarModel.Project(face3D)));
                    }
                }
            }

            return result;
        }

        private static bool ValidSunDirection(Vector3D sunDirection, double minHorizonAngle)
        {
            if (sunDirection == null || !sunDirection.IsValid())
            {
                return false;
            }

            if (sunDirection.Z > 0)
            {
                return false;
            }

            double angle = Plane.WorldXY.Project(sunDirection).SmallestAngle(sunDirection);
            return angle >= minHorizonAngle;
        }

        private static List<LinkedFace3D> ExposedSampleLinkedFace3Ds(List<SampleCell> sampleCells, List<LinkedFace3D> linkedFace3Ds_Merge, Vector3D sunDirection, double tolerance_Area, double tolerance_Angle, double tolerance_Distance)
        {
            if (sampleCells == null || sampleCells.Count == 0 || linkedFace3Ds_Merge == null || linkedFace3Ds_Merge.Count == 0)
            {
                return null;
            }

            Plane plane = SunPlane(linkedFace3Ds_Merge, sunDirection, out Vector3D vector3D, out Vector3D vector3D_Ray, tolerance_Distance);
            if (plane == null)
            {
                return null;
            }

            List<ProjectedFace> projectedFaces = ProjectedFaces(linkedFace3Ds_Merge, plane, vector3D, sunDirection, tolerance_Area, tolerance_Angle, tolerance_Distance);
            if (projectedFaces == null || projectedFaces.Count == 0)
            {
                return null;
            }

            // Which merged faces are front-facing (can receive sun) for this sun direction. A sample cell
            // is lit only when its ray first hits its OWN merged face AND that face is front-facing; a hit
            // on a back-facing face (kept as an occluder) means the cell is shaded, not lit.
            HashSet<Guid> solarCandidateGuids = new HashSet<Guid>();
            foreach (ProjectedFace projectedFace in projectedFaces)
            {
                if (projectedFace?.IsSolarCandidate == true && projectedFace.LinkedFace3D != null)
                {
                    solarCandidateGuids.Add(projectedFace.LinkedFace3D.Guid);
                }
            }

            STRtree<int> index = new STRtree<int>();
            for (int i = 0; i < projectedFaces.Count; i++)
            {
                Envelope envelope = ToEnvelope(projectedFaces[i].BoundingBox2D, tolerance_Distance);
                if (envelope != null)
                {
                    index.Insert(envelope, i);
                }
            }
            index.Build();

            List<LinkedFace3D> result = new List<LinkedFace3D>();
            foreach (SampleCell sampleCell in sampleCells)
            {
                Point3D point3D_Start = plane.Project(sampleCell.Point3D, vector3D, tolerance_Distance);
                if (point3D_Start == null)
                {
                    continue;
                }

                Geometry.Planar.Point2D point2D = plane.Convert(point3D_Start);
                if (point2D == null)
                {
                    continue;
                }

                Envelope envelope = ToEnvelope(point2D, tolerance_Distance);
                if (envelope == null)
                {
                    continue;
                }

                IList<int> indexes = index.Query(envelope);
                if (indexes == null || indexes.Count == 0)
                {
                    continue;
                }

                List<LinkedFace3D> candidates = new List<LinkedFace3D>();
                foreach (int index_Temp in indexes)
                {
                    ProjectedFace projectedFace = projectedFaces[index_Temp];
                    if (projectedFace?.BoundingBox2D == null || projectedFace.Face2D == null || projectedFace.LinkedFace3D == null)
                    {
                        continue;
                    }

                    if (!projectedFace.BoundingBox2D.InRange(point2D, tolerance_Distance))
                    {
                        continue;
                    }

                    if (!projectedFace.Face2D.Inside(point2D, tolerance_Distance) && !projectedFace.Face2D.On(point2D, tolerance_Distance))
                    {
                        continue;
                    }

                    candidates.Add(projectedFace.LinkedFace3D);
                }

                if (candidates.Count == 0)
                {
                    continue;
                }

                Point3D point3D_End = point3D_Start.GetMoved(vector3D_Ray) as Point3D;
                if (point3D_End == null)
                {
                    continue;
                }

                Segment3D segment3D = new Segment3D(point3D_Start, point3D_End);
                List<Tuple<LinkedFace3D, Point3D>> tuples_Intersection = Geometry.Object.Spatial.Query.IntersectionTuples(segment3D, candidates, true, tolerance_Distance);
                if (tuples_Intersection == null || tuples_Intersection.Count == 0)
                {
                    continue;
                }

                if (tuples_Intersection[0].Item1.Guid == sampleCell.MergedGuid && solarCandidateGuids.Contains(sampleCell.MergedGuid))
                {
                    result.Add(new LinkedFace3D(sampleCell.Guid, sampleCell.Face3D));
                }
            }

            return result;
        }

        internal static Plane SunPlane(List<LinkedFace3D> linkedFace3Ds, Vector3D sunDirection, out Vector3D vector3D, out Vector3D vector3D_Ray, double tolerance_Distance)
        {
            vector3D = null;
            vector3D_Ray = null;

            BoundingBox3D boundingBox3D = Geometry.Object.Spatial.Create.BoundingBox3D(linkedFace3Ds);
            if (boundingBox3D == null || !boundingBox3D.IsValid())
            {
                return null;
            }

            double distance = boundingBox3D.Min.Distance(boundingBox3D.Max);
            if (distance <= tolerance_Distance)
            {
                return null;
            }

            vector3D = new Vector3D(sunDirection).Unit * distance;
            Point3D point3D = boundingBox3D.GetCentroid().GetMoved(vector3D.GetNegated()) as Point3D;
            if (point3D == null)
            {
                return null;
            }

            vector3D_Ray = 2 * vector3D;
            return new Plane(point3D, vector3D.Unit);
        }

        internal static List<ProjectedFace> ProjectedFaces(List<LinkedFace3D> linkedFace3Ds, Plane plane, Vector3D vector3D, Vector3D sunDirection, double tolerance_Area, double tolerance_Angle, double tolerance_Distance)
        {
            List<ProjectedFace> result = new List<ProjectedFace>();
            foreach (LinkedFace3D linkedFace3D in linkedFace3Ds)
            {
                Face3D face3D = linkedFace3D?.Face3D;
                if (face3D == null)
                {
                    continue;
                }

                // Include EVERY face as a ray-cast candidate — occlusion is geometric and independent of
                // orientation, so a back-facing wall/shade must still be able to block the ray. The
                // orientation test only decides whether the HIT face can itself RECEIVE sun (front-facing),
                // which is recorded on the ProjectedFace and checked when a sample cell hits its own face.
                bool isSolarCandidate = IsSolarCandidate(face3D, sunDirection, tolerance_Angle, tolerance_Distance);

                Face3D face3D_Project = plane.Project(face3D, vector3D, tolerance_Distance);
                if (face3D_Project == null || !face3D_Project.IsValid())
                {
                    continue;
                }

                Geometry.Planar.Face2D face2D = plane.Convert(face3D_Project);
                if (face2D == null || face2D.GetArea() < tolerance_Area)
                {
                    continue;
                }

                Geometry.Planar.BoundingBox2D boundingBox2D = face2D.GetBoundingBox();
                if (boundingBox2D == null)
                {
                    continue;
                }

                result.Add(new ProjectedFace(linkedFace3D, face2D, boundingBox2D, isSolarCandidate));
            }

            return result;
        }

        private static bool IsSolarCandidate(Face3D face3D, Vector3D sunDirection, double tolerance_Angle, double tolerance_Distance)
        {
            Plane plane = face3D?.GetPlane();
            if (plane == null)
            {
                return false;
            }

            // sunDirection points from sun toward surface; exclude back-facing surfaces (normal points same way as sun ray)
            Vector3D normal = plane.Normal;
            if (normal != null && normal.IsValid() && normal.DotProduct(sunDirection) > 0)
            {
                return false;
            }

            Vector3D vector3D_Project = plane.Project(sunDirection);
            if (vector3D_Project == null || !vector3D_Project.IsValid() || vector3D_Project.Length <= tolerance_Distance)
            {
                return true;
            }

            return sunDirection.SmallestAngle(vector3D_Project) >= tolerance_Angle;
        }

        private static List<SampleCell> SampleCells(Dictionary<LinkedFace3D, List<LinkedFace3D>> dictionary_Merge, double sampleSize, double tolerance_Area, double tolerance_Distance)
        {
            List<SampleCell> result = new List<SampleCell>();
            foreach (KeyValuePair<LinkedFace3D, List<LinkedFace3D>> keyValuePair in dictionary_Merge)
            {
                LinkedFace3D linkedFace3D_Merge = keyValuePair.Key;
                if (linkedFace3D_Merge == null)
                {
                    continue;
                }

                List<LinkedFace3D> linkedFace3Ds = keyValuePair.Value;
                if (linkedFace3Ds == null || linkedFace3Ds.Count == 0)
                {
                    linkedFace3Ds = new List<LinkedFace3D>() { linkedFace3D_Merge };
                }

                foreach (LinkedFace3D linkedFace3D in linkedFace3Ds)
                {
                    AddSampleCells(result, linkedFace3D, linkedFace3D_Merge.Guid, sampleSize, tolerance_Area, tolerance_Distance);
                }
            }

            return result;
        }

        private static void AddSampleCells(List<SampleCell> sampleCells, LinkedFace3D linkedFace3D, Guid mergedGuid, double sampleSize, double tolerance_Area, double tolerance_Distance)
        {
            Face3D face3D = linkedFace3D?.Face3D;
            if (sampleCells == null || face3D == null || face3D.GetPlane() == null)
            {
                return;
            }

            // Cell subdivision is shared with the aperture analysis pipeline
            // (Geometry.SolarCalculator.Query.AnalysisCells) — one implementation, one grid.
            List<AnalysisCell> analysisCells = Geometry.SolarCalculator.Query.AnalysisCells(face3D, sampleSize, tolerance_Area, tolerance_Distance);
            if (analysisCells == null || analysisCells.Count == 0)
            {
                return;
            }

            foreach (AnalysisCell analysisCell in analysisCells)
            {
                Point3D point3D = analysisCell?.InternalPoint3D;
                Face3D face3D_Cell = analysisCell?.Face3D;
                if (point3D == null || face3D_Cell == null || !face3D_Cell.IsValid())
                {
                    continue;
                }

                sampleCells.Add(new SampleCell(linkedFace3D.Guid, mergedGuid, point3D, face3D_Cell));
            }
        }

        internal static Envelope ToEnvelope(Geometry.Planar.BoundingBox2D boundingBox2D, double tolerance_Distance)
        {
            if (boundingBox2D == null)
            {
                return null;
            }

            Geometry.Planar.Point2D min = boundingBox2D.Min;
            Geometry.Planar.Point2D max = boundingBox2D.Max;
            if (min == null || max == null)
            {
                return null;
            }

            return new Envelope(min.X - tolerance_Distance, max.X + tolerance_Distance, min.Y - tolerance_Distance, max.Y + tolerance_Distance);
        }

        internal static Envelope ToEnvelope(Geometry.Planar.Point2D point2D, double tolerance_Distance)
        {
            if (point2D == null)
            {
                return null;
            }

            return new Envelope(point2D.X - tolerance_Distance, point2D.X + tolerance_Distance, point2D.Y - tolerance_Distance, point2D.Y + tolerance_Distance);
        }

        internal class ProjectedFace
        {
            public ProjectedFace(LinkedFace3D linkedFace3D, Geometry.Planar.Face2D face2D, Geometry.Planar.BoundingBox2D boundingBox2D, bool isSolarCandidate)
            {
                LinkedFace3D = linkedFace3D;
                Face2D = face2D;
                BoundingBox2D = boundingBox2D;
                IsSolarCandidate = isSolarCandidate;
            }

            public LinkedFace3D LinkedFace3D { get; }

            public Geometry.Planar.Face2D Face2D { get; }

            public Geometry.Planar.BoundingBox2D BoundingBox2D { get; }

            // True when this face is oriented to receive direct sun (front-facing). Occluders are kept
            // regardless of orientation; only a HIT on a front-facing face counts the sample cell as lit.
            public bool IsSolarCandidate { get; }
        }

        private class SampleCell
        {
            public SampleCell(Guid guid, Guid mergedGuid, Point3D point3D, Face3D face3D)
            {
                Guid = guid;
                MergedGuid = mergedGuid;
                Point3D = point3D;
                Face3D = face3D;
            }

            public Guid Guid { get; }

            public Guid MergedGuid { get; }

            public Point3D Point3D { get; }

            public Face3D Face3D { get; }
        }
    }
}

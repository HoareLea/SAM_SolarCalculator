using SAM.Geometry.Spatial;
using System;
using SAM.Core;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        public static void SolarFaces(this IEnumerable<SolarFace> solarFaces, Vector3D sunDirection, out List<SolarFace> solarFaces_Shaded, out List<SolarFace> solarFaces_ExposedToSun, bool shaded = true, bool exposedToSun = true, double tolerance_Area = Tolerance.MacroDistance, double tolerance_Snap = Tolerance.MacroDistance, double tolerance_Distance = Tolerance.Distance)
        {
            solarFaces_Shaded = null;
            solarFaces_ExposedToSun = null;

            if (solarFaces == null || sunDirection == null || !sunDirection.IsValid())
            {
                return;
            }

            if(exposedToSun)
            {
                solarFaces_ExposedToSun = new List<SolarFace>();
            }

            if(shaded)
            {
                solarFaces_Shaded = new List<SolarFace>();
            }

            if(solarFaces.Count() < 2)
            {
                return;
            }

            BoundingBox3D boundingBox3D = Spatial.Create.BoundingBox3D(solarFaces);
            if(boundingBox3D == null || !boundingBox3D.IsValid())
            {
                return;
            }

            Vector3D vector3D = new Vector3D(sunDirection).Unit * boundingBox3D.Min.Distance(boundingBox3D.Max);
            Point3D point3D = boundingBox3D.GetCentroid().GetMoved(vector3D.GetNegated()) as Point3D;

            Plane plane = new Plane(point3D, vector3D.Unit);

            List<SolarFace> solarFaces_Filtered = new List<SolarFace>();
            List<Planar.Segment2D> segment2Ds = new List<Planar.Segment2D>();
            foreach(SolarFace solarFace in solarFaces)
            {
                Face3D face3D = solarFace?.Face3D;
                if(face3D == null)
                {
                    continue;
                }

                Face3D face3D_Project = plane.Project(face3D, vector3D, tolerance_Distance);
                if(face3D_Project == null || !face3D_Project.IsValid())
                {
                    continue;
                }

                Planar.Face2D face2D = plane.Convert(face3D_Project);
                if(face2D == null || face2D.GetArea() < tolerance_Area)
                {
                    continue;
                }

                Planar.ISegmentable2D segmentable2D = face2D.ExternalEdge2D as Planar.ISegmentable2D;
                if(segmentable2D != null)
                {
                    List<Planar.Segment2D> segment2Ds_Temp = segmentable2D.GetSegments();
                    if(segment2Ds_Temp != null)
                    {
                        segment2Ds.AddRange(segment2Ds_Temp);
                    }
                }

                List<Planar.IClosed2D> closed2Ds = face2D.InternalEdge2Ds;
                if(closed2Ds != null)
                {
                    foreach(Planar.IClosed2D closed2D in closed2Ds)
                    {
                        List<Planar.Segment2D> segment2Ds_Temp = (closed2D as Planar.ISegmentable2D)?.GetSegments();
                        if (segment2Ds_Temp != null)
                        {
                            segment2Ds.AddRange(segment2Ds_Temp);
                        }
                    }
                }

                solarFaces_Filtered.Add(solarFace);
            }

            if(segment2Ds == null || solarFaces_Filtered == null)
            {
                return;
            }

            segment2Ds = Planar.Query.Split(segment2Ds, tolerance_Distance);

            segment2Ds = Planar.Query.Snap(segment2Ds, true, tolerance_Snap);

            List<Planar.Polygon2D> polygon2Ds = Planar.Create.Polygon2Ds(segment2Ds, tolerance_Distance);
            if (polygon2Ds == null)
            {
                return;
            }

            Vector3D vector3D_Ray =  2 * vector3D;

            List<Tuple<Planar.Polygon2D, List<SolarFace>>> tuples_Shaded = Enumerable.Repeat<Tuple<Planar.Polygon2D, List<SolarFace>>>(null, polygon2Ds.Count).ToList();
            List<Tuple<Planar.Polygon2D, SolarFace>> tuples_ExposedToSun = Enumerable.Repeat<Tuple<Planar.Polygon2D, SolarFace>>(null, polygon2Ds.Count).ToList();

            Parallel.For(0, polygon2Ds.Count, (int i) =>
            //for(int i =0; i < polygon2Ds.Count; i++)
            {
                Planar.Polygon2D polygon2D = polygon2Ds[i];

                Planar.Point2D point2D = polygon2D?.GetInternalPoint2D(tolerance_Distance);
                if (point2D == null)
                {
                    return;
                    //continue;
                }

                Point3D point3D_Start = plane.Convert(point2D);
                Point3D point3D_End = point3D_Start.GetMoved(vector3D_Ray) as Point3D;

                Segment3D segment3D = new Segment3D(point3D_Start, point3D_End);
                BoundingBox3D boundingBox3D_Segment3D = segment3D.GetBoundingBox();

                List<SolarFace> solarFaces_Polygon2D = solarFaces_Filtered.FindAll(x => x.GetBoundingBox().InRange(boundingBox3D_Segment3D, tolerance_Distance) && x.GetBoundingBox().InRange(segment3D, tolerance_Distance));

                Dictionary<SolarFace, Point3D> dictionary_Intersection = Spatial.Query.IntersectionDictionary(segment3D, solarFaces_Polygon2D, true, tolerance_Distance);
                if (dictionary_Intersection == null || dictionary_Intersection.Count == 0)
                {
                    return;
                    //continue;
                }

                List<SolarFace> solarFaces_Temp = new List<SolarFace>(dictionary_Intersection.Keys);

                if (exposedToSun)
                {
                    tuples_ExposedToSun[i] = new Tuple<Planar.Polygon2D, SolarFace>(polygon2D, solarFaces_Temp[0]);
                }

                if (!shaded || dictionary_Intersection.Count < 2)
                {
                    return;
                    //continue;
                }

                solarFaces_Temp.RemoveAt(0);

                tuples_Shaded[i] = new Tuple<Planar.Polygon2D, List<SolarFace>>(polygon2D, solarFaces_Temp);
            });

            tuples_ExposedToSun.RemoveAll(x => x == null);
            tuples_Shaded.RemoveAll(x => x == null);


            if (tuples_Shaded != null || tuples_Shaded.Count != 0)
            {
                List<List<SolarFace>> solarFacesList = Enumerable.Repeat<List<SolarFace>>(null, solarFaces_Filtered.Count).ToList();

                //Parallel.For(0, solarFacesList.Count, (int i) =>
                for (int i = 0; i < solarFaces_Filtered.Count; i++)
                {
                    SolarFace solarFace = solarFaces_Filtered[i];

                    List<Tuple<Planar.Polygon2D, List<SolarFace>>> tuples_SolarFace = tuples_Shaded.FindAll(x => x.Item2.Contains(solarFace));
                    if (tuples_SolarFace == null || tuples_SolarFace.Count == 0)
                    {
                        return;
                        //continue;
                    }

                    List<SolarFace> solarFaces_Temp = Create.SolarFaces(solarFace, vector3D, tuples_SolarFace.ConvertAll(x => x.Item1), plane, tolerance_Distance);
                    if (solarFaces_Temp == null || solarFaces_Temp.Count == 0)
                    {
                        return;
                        //continue;
                    }

                    solarFacesList.Add(solarFaces_Temp);
                };//);

                foreach (List<SolarFace> solarFaces_Temp in solarFacesList)
                {
                    if (solarFaces_Temp == null || solarFaces_Temp.Count == 0)
                    {
                        continue;
                    }

                    solarFaces_Shaded.AddRange(solarFaces_Temp);
                }
            }

            if (tuples_ExposedToSun != null || tuples_ExposedToSun.Count != 0)
            {
                List<List<SolarFace>> solarFacesList = Enumerable.Repeat<List<SolarFace>>(null, solarFaces_Filtered.Count).ToList();

                Parallel.For(0, solarFacesList.Count, (int i) => 
                //for (int i = 0; i < solarFaces_Filtered.Count; i++)
                {
                    SolarFace solarFace = solarFaces_Filtered[i];

                    List<Tuple<Planar.Polygon2D, SolarFace>> tuples_SolarFace = tuples_ExposedToSun.FindAll(x => x.Item2 == solarFace);
                    if (tuples_SolarFace == null || tuples_SolarFace.Count == 0)
                    {
                        return;
                        //continue;
                    }

                    List<SolarFace> solarFaces_Temp = Create.SolarFaces(solarFace, vector3D, tuples_SolarFace.ConvertAll(x => x.Item1), plane, tolerance_Distance);
                    if (solarFaces_Temp == null || solarFaces_Temp.Count == 0)
                    {
                        return;
                        //continue;
                    }

                    solarFacesList.Add(solarFaces_Temp);
                });

                foreach(List<SolarFace> solarFaces_Temp in solarFacesList)
                {
                    if(solarFaces_Temp == null || solarFaces_Temp.Count == 0)
                    {
                        continue;
                    }

                    solarFaces_ExposedToSun.AddRange(solarFaces_Temp);
                }
            }
        }
    }
}

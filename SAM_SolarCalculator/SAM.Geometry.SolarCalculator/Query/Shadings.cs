using SAM.Geometry.Spatial;
using System;
using SAM.Core;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        public static List<SolarFace> Shadings(this IEnumerable<SolarFace> solarFaces, Vector3D sunDirection, double tolerance_Area = Tolerance.MacroDistance, double tolerance_Snap = Tolerance.MacroDistance, double tolerance_Distance = Tolerance.Distance)
        {
            if(solarFaces == null || sunDirection == null || !sunDirection.IsValid())
            {
                return null;
            }

            List<SolarFace> result = new List<SolarFace>();

            if(solarFaces.Count() < 2)
            {
                return result;
            }

            BoundingBox3D boundingBox3D = Spatial.Create.BoundingBox3D(solarFaces);
            if(boundingBox3D == null || !boundingBox3D.IsValid())
            {
                return result;
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
                    result.Add(solarFace);
                    continue;
                }

                Planar.Face2D face2D = plane.Convert(face3D_Project);
                if(face2D == null || face2D.GetArea() < tolerance_Area)
                {
                    result.Add(solarFace);
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
                return result;
            }

            segment2Ds = Planar.Query.Split(segment2Ds, tolerance_Distance);

            segment2Ds = Planar.Query.Snap(segment2Ds, true, tolerance_Snap);

            List<Planar.Polygon2D> polygon2Ds = Planar.Create.Polygon2Ds(segment2Ds, tolerance_Distance);
            if (polygon2Ds == null)
            {
                return result;
            }

            Vector3D vector3D_Ray =  2 * vector3D;

            List<Tuple<Planar.Polygon2D, List<SolarFace>>> tuples = new List<Tuple<Planar.Polygon2D, List<SolarFace>>>();
            foreach(Planar.Polygon2D polygon2D in polygon2Ds)
            {
                Planar.Point2D point2D = polygon2D?.GetInternalPoint2D(tolerance_Distance);
                if(point2D == null)
                {
                    continue;
                }

                Point3D point3D_Start = plane.Convert(point2D);
                Point3D point3D_End = point3D_Start.GetMoved(vector3D_Ray) as Point3D;

                Segment3D segment3D = new Segment3D(point3D_Start, point3D_End);

                List<SolarFace> solarFaces_Polygon2D = solarFaces_Filtered.FindAll(x => x.GetBoundingBox().InRange(segment3D, tolerance_Distance));

                Dictionary<SolarFace, Point3D> dictionary_Intersection = Spatial.Query.IntersectionDictionary(segment3D, solarFaces_Polygon2D, true, tolerance_Distance);
                if(dictionary_Intersection == null || dictionary_Intersection.Count < 2)
                {
                    continue;
                }

                List<SolarFace> solarFaces_Temp = new List<SolarFace>(dictionary_Intersection.Keys);
                solarFaces_Temp.RemoveAt(0);

                tuples.Add(new Tuple<Planar.Polygon2D, List<SolarFace>>(polygon2D, solarFaces_Temp));
            }

            if(tuples == null || tuples.Count == 0)
            {
                return result;
            }

            foreach(SolarFace solarFace in solarFaces_Filtered)
            {
                List<Tuple<Planar.Polygon2D, List<SolarFace>>> tuples_SolarFace = tuples.FindAll(x => x.Item2.Contains(solarFace));
                if(tuples_SolarFace == null || tuples_SolarFace.Count == 0)
                {
                    continue;
                }

                List<Planar.Polygon2D> polygon2Ds_SolarFace = Planar.Query.Union(tuples_SolarFace.ConvertAll(x => x.Item1), tolerance_Distance);
                if(polygon2Ds_SolarFace == null || polygon2Ds_SolarFace.Count == 0)
                {
                    continue;
                }

                Face3D face3D_SolarFace = solarFace.Face3D;
                Plane plane_SolarFace = face3D_SolarFace.GetPlane();
                Planar.Face2D face2D = plane_SolarFace.Convert(face3D_SolarFace);
                
                foreach(Planar.Polygon2D polygon2D_SolarFace in polygon2Ds_SolarFace)
                {
                    Polygon3D polygon3D_Shade = plane.Convert(polygon2D_SolarFace);

                    polygon3D_Shade = plane_SolarFace.Project(polygon3D_Shade, vector3D, tolerance_Distance);
                    if(polygon3D_Shade == null)
                    {
                        continue;
                    }

                    Planar.Polygon2D polygon2D_Shade = plane_SolarFace.Convert(polygon3D_Shade);
                    if(polygon2D_Shade == null)
                    {
                        continue;
                    }

                    List<Planar.Face2D> face2Ds_Shade = Planar.Query.Intersection(face2D, new Planar.Face2D(polygon2D_Shade));
                    if(face2Ds_Shade == null || face2Ds_Shade.Count == 0)
                    {
                        continue;
                    }

                    foreach(Planar.Face2D face2D_Shade in face2Ds_Shade)
                    {
                        result.Add(new SolarFace(solarFace.Guid, plane_SolarFace.Convert(face2D_Shade)));
                    }
                }
            }

            return result;
        }
    }
}

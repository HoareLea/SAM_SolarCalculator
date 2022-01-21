using NetTopologySuite.Geometries;
using SAM.Geometry.Planar;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        public static Dictionary<LinkedFace3D, List<LinkedFace3D>> Merge(this IEnumerable<LinkedFace3D> linkedFace3Ds, double offset, double minArea = Core.Tolerance.MacroDistance, double tolerance = Core.Tolerance.Distance)
        {
            if(double.IsNaN(offset))
            {
                return null;
            }

            List<LinkedFace3D> linkedFace3Ds_Temp = linkedFace3Ds?.ToList();
            if(linkedFace3Ds_Temp == null)
            {
                return null;
            }

            linkedFace3Ds_Temp.RemoveAll(x => x?.Face3D == null || !x.Face3D.IsValid());

            linkedFace3Ds_Temp.Sort((x, y) => y.Face3D.GetArea().CompareTo(x.Face3D.GetArea()));

            Dictionary<LinkedFace3D, List<LinkedFace3D>> result = new Dictionary<LinkedFace3D, List<LinkedFace3D>>();

            HashSet<Guid> guids = new HashSet<Guid>();
            while (linkedFace3Ds_Temp.Count > 0)
            {
                LinkedFace3D linkedFace3D = linkedFace3Ds_Temp[0];
                linkedFace3Ds_Temp.RemoveAt(0);

                Plane plane = linkedFace3D.Face3D.GetPlane();
                if (plane == null)
                {
                    continue;
                }

                List<LinkedFace3D> linkedFace3Ds_Offset = new List<LinkedFace3D>();
                foreach(LinkedFace3D linkedFace3D_Temp in linkedFace3Ds_Temp)
                {
                    Plane plane_Temp = linkedFace3D_Temp.Face3D.GetPlane();
                    if (plane == null)
                    {
                        continue;
                    }

                    if (!plane.Coplanar(plane_Temp, tolerance))
                    {
                        continue;
                    }

                    double distance = plane.Distance(plane_Temp, tolerance);
                    if (distance > offset)
                    {
                        continue;
                    }

                    linkedFace3Ds_Offset.Add(linkedFace3D_Temp);
                }

                if(linkedFace3Ds_Offset == null || linkedFace3Ds_Offset.Count == 0)
                {
                    result[new LinkedFace3D(linkedFace3D)] = new List<LinkedFace3D>() { linkedFace3D};
                    continue;
                }

                linkedFace3Ds_Offset.Add(linkedFace3D);

                List<Tuple<Polygon, LinkedFace3D>> tuples_Polygon = new List<Tuple<Polygon, LinkedFace3D>>();
                List<Point2D> point2Ds = new List<Point2D>(); //Snap Points
                foreach (LinkedFace3D linkedFace3D_Temp in linkedFace3Ds_Offset)
                {
                    Face3D face3D = linkedFace3D_Temp.Face3D;
                    foreach (IClosedPlanar3D closedPlanar3D in face3D.GetEdge3Ds())
                    {
                        ISegmentable3D segmentable3D = closedPlanar3D as ISegmentable3D;
                        if (segmentable3D == null)
                            continue;

                        segmentable3D.GetPoints()?.ForEach(x => Planar.Modify.Add(point2Ds, plane.Convert(x), tolerance));
                    }

                    Face2D face2D = plane.Convert(plane.Project(face3D));
                    tuples_Polygon.Add(new Tuple<Polygon, LinkedFace3D>(face2D.ToNTS(tolerance), linkedFace3D_Temp));
                }

                List<Polygon> polygons_Temp = tuples_Polygon.ConvertAll(x => x.Item1);
                Planar.Modify.RemoveAlmostSimilar_NTS(polygons_Temp, tolerance);

                polygons_Temp = Planar.Query.Union(polygons_Temp);
                foreach (Polygon polygon in polygons_Temp)
                {
                    if (polygon.Area < minArea)
                    {
                        continue;
                    }

                    List<Tuple<Polygon, LinkedFace3D>> tuples_LinkedFace3D = tuples_Polygon.FindAll(x => polygon.Contains(x.Item1.InteriorPoint));
                    if (tuples_LinkedFace3D == null || tuples_LinkedFace3D.Count == 0)
                    {
                        continue;
                    }

                    tuples_LinkedFace3D.Sort((x, y) => y.Item1.Area.CompareTo(x.Item1.Area));

                    foreach (Tuple<Polygon, LinkedFace3D> tuple in tuples_LinkedFace3D)
                    {
                        linkedFace3Ds_Temp.Remove(tuple.Item2);
                    }

                    LinkedFace3D LinkedFace3D_Old = tuples_LinkedFace3D[0].Item2;

                    Polygon polygon_Temp = Planar.Query.SimplifyBySnapper(polygon, tolerance);
                    polygon_Temp = Planar.Query.SimplifyByTopologyPreservingSimplifier(polygon_Temp, tolerance);

                    Face2D face2D = polygon_Temp.ToSAM(minArea, Core.Tolerance.MicroDistance)?.Snap(point2Ds, tolerance);
                    if (face2D == null)
                    {
                        continue;
                    }

                    Face3D face3D = new Face3D(plane, face2D);
                    Guid guid = LinkedFace3D_Old.Guid;
                    if (guids.Contains(guid))
                        guid = Guid.NewGuid();

                    LinkedFace3D linkedFace3D_New = new LinkedFace3D(guid, face3D);

                    result[linkedFace3D_New] = tuples_LinkedFace3D.ConvertAll(x => x.Item2);
                }
            }

            return result;
        }
    }
}

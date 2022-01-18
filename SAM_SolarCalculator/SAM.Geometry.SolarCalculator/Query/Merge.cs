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
        public static Dictionary<SolarFace, List<SolarFace>> Merge(this IEnumerable<SolarFace> solarFaces, double offset, double minArea = Core.Tolerance.MacroDistance, double tolerance = Core.Tolerance.Distance)
        {
            if(double.IsNaN(offset))
            {
                return null;
            }

            List<SolarFace> solarFaces_Temp = solarFaces?.ToList();
            if(solarFaces_Temp == null)
            {
                return null;
            }

            solarFaces_Temp.RemoveAll(x => x?.Face3D == null || !x.Face3D.IsValid());

            solarFaces_Temp.Sort((x, y) => y.Face3D.GetArea().CompareTo(x.Face3D.GetArea()));

            Dictionary<SolarFace, List<SolarFace>> result = new Dictionary<SolarFace, List<SolarFace>>();

            HashSet<Guid> guids = new HashSet<Guid>();
            while (solarFaces_Temp.Count > 0)
            {
                SolarFace solarFace = solarFaces_Temp[0];
                solarFaces_Temp.RemoveAt(0);

                Plane plane = solarFace.Face3D.GetPlane();
                if (plane == null)
                {
                    continue;
                }

                List<SolarFace> solarFaces_Offset = new List<SolarFace>();
                foreach(SolarFace solarFace_Temp in solarFaces_Temp)
                {
                    Plane plane_Temp = solarFace_Temp.Face3D.GetPlane();
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

                    solarFaces_Offset.Add(solarFace_Temp);
                }

                if(solarFaces_Offset == null || solarFaces_Offset.Count == 0)
                {
                    result[new SolarFace(solarFace)] = new List<SolarFace>() { solarFace};
                    continue;
                }

                solarFaces_Offset.Add(solarFace);

                List<Tuple<Polygon, SolarFace>> tuples_Polygon = new List<Tuple<Polygon, SolarFace>>();
                List<Point2D> point2Ds = new List<Point2D>(); //Snap Points
                foreach (SolarFace solarFace_Temp in solarFaces_Offset)
                {
                    Face3D face3D = solarFace_Temp.Face3D;
                    foreach (IClosedPlanar3D closedPlanar3D in face3D.GetEdge3Ds())
                    {
                        ISegmentable3D segmentable3D = closedPlanar3D as ISegmentable3D;
                        if (segmentable3D == null)
                            continue;

                        segmentable3D.GetPoints()?.ForEach(x => Planar.Modify.Add(point2Ds, plane.Convert(x), tolerance));
                    }

                    Face2D face2D = plane.Convert(plane.Project(face3D));
                    tuples_Polygon.Add(new Tuple<Polygon, SolarFace>(face2D.ToNTS(tolerance), solarFace_Temp));
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

                    List<Tuple<Polygon, SolarFace>> tuples_SolarFace = tuples_Polygon.FindAll(x => polygon.Contains(x.Item1.InteriorPoint));
                    if (tuples_SolarFace == null || tuples_SolarFace.Count == 0)
                    {
                        continue;
                    }

                    tuples_SolarFace.Sort((x, y) => y.Item1.Area.CompareTo(x.Item1.Area));

                    foreach (Tuple<Polygon, SolarFace> tuple in tuples_SolarFace)
                    {
                        solarFaces_Temp.Remove(tuple.Item2);
                    }

                    SolarFace solarFace_Old = tuples_SolarFace[0].Item2;

                    Polygon polygon_Temp = Planar.Query.SimplifyBySnapper(polygon, tolerance);
                    polygon_Temp = Planar.Query.SimplifyByTopologyPreservingSimplifier(polygon_Temp, tolerance);

                    Face2D face2D = polygon_Temp.ToSAM(minArea, Core.Tolerance.MicroDistance)?.Snap(point2Ds, tolerance);
                    if (face2D == null)
                    {
                        continue;
                    }

                    Face3D face3D = new Face3D(plane, face2D);
                    Guid guid = solarFace_Old.Guid;
                    if (guids.Contains(guid))
                        guid = Guid.NewGuid();

                    SolarFace solarFace_New = new SolarFace(guid, face3D);

                    result[solarFace_New] = tuples_SolarFace.ConvertAll(x => x.Item2);
                }
            }

            return result;
        }
    }
}

using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        public static Point3D DistantPoint3D(this SolarFace solarFace, Line3D line3D, out double distance)
        {
            distance = double.NaN;

            if (solarFace == null || line3D == null)
            {
                return null;
            }

            IClosedPlanar3D closedPlanar3D = solarFace.Face3D.GetExternalEdge3D();
            if(closedPlanar3D == null)
            {
                return null;
            }

            ISegmentable3D segmentable3D = closedPlanar3D as ISegmentable3D;
            if(segmentable3D == null)
            {
                throw new NotImplementedException();
            }

            List<Point3D> point3Ds = segmentable3D.GetPoints();
            if(point3Ds == null || point3Ds.Count == 0)
            {
                return null;
            }

            Point3D point3D_Origin = line3D.Origin;

            Point3D result = null;

            distance = double.MinValue;
            foreach (Point3D point3D_Temp in point3Ds)
            {
                if(point3D_Temp == null)
                {
                    continue;
                }

                Point3D point3D_Project = line3D.Project(point3D_Temp);

                double distance_Temp = point3D_Project.Distance(point3D_Origin);
                if(distance_Temp > distance)
                {
                    result = point3D_Temp;
                    distance = distance_Temp;
                }
            }

            return result;
        }
    }
}

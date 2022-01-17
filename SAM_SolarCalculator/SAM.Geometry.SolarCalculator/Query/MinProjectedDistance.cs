using Innovative.Geometry;
using SAM.Geometry.Spatial;
using Innovative.SolarCalculator;
using System;
using System.Collections.Generic;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        public static double MinProjectedDistance(this SolarFace solarFace, Line3D line3D)
        {
            if (solarFace == null || line3D == null)
            {
                return double.NaN;
            }

            IClosedPlanar3D closedPlanar3D = solarFace.Face3D.GetExternalEdge3D();
            if(closedPlanar3D == null)
            {
                return double.NaN;
            }

            ISegmentable3D segmentable3D = closedPlanar3D as ISegmentable3D;
            if(segmentable3D == null)
            {
                throw new NotImplementedException();
            }

            List<Point3D> point3Ds = segmentable3D.GetPoints();
            if(point3Ds == null || point3Ds.Count == 0)
            {
                return double.NaN;
            }

            Point3D point3D_Origin = line3D.Origin;

            double result = double.MaxValue;
            foreach (Point3D point3D_Temp in point3Ds)
            {
                if(point3D_Temp == null)
                {
                    continue;
                }

                Point3D point3D_Project = line3D.Project(point3D_Temp);

                double distance = point3D_Project.Distance(point3D_Origin);
                if(distance < result)
                {
                    result = distance;
                }
            }

            return result;
        }
    }
}

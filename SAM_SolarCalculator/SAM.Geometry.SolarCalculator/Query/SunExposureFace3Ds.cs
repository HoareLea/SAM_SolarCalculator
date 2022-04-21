using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        public static List<Face3D> SunExposureFace3Ds(this SolarFaceSimulationResult solarFaceSimulationResult, Face3D face3D, System.DateTime dateTime, double tolerance = Core.Tolerance.Distance)
        {
            if(solarFaceSimulationResult == null || face3D == null)
            {
                return null;
            }

            List<Face3D> face3Ds = solarFaceSimulationResult.GetSunExposureFace3Ds(dateTime);
            if(face3Ds == null || face3Ds.Count == 0)
            {
                return null;
            }

            Plane plane = null;
            for (int i = 0; i < face3Ds.Count; i++)
            {
                plane = face3Ds[i].GetPlane();
                if(plane != null)
                {
                    break;
                }
            }

            if(!plane.Coplanar(face3D, tolerance))
            {
                return null;
            }

            List<Planar.Face2D> face2Ds = face3Ds.ConvertAll(x => plane.Convert(x));
            Planar.Face2D face2D = plane.Convert(face3D);

            List<Planar.Face2D> face2Ds_Intersection = new List<Planar.Face2D>();
            foreach (Planar.Face2D face2D_Temp in face2Ds)
            {
                List<Planar.Face2D> face2Ds_Intersection_Temp = Planar.Query.Intersection(face2D, face2D_Temp, tolerance);
                if(face2Ds_Intersection_Temp != null && face2Ds_Intersection_Temp.Count != 0)
                {
                    face2Ds_Intersection.AddRange(face2Ds_Intersection_Temp);
                }
            }

            if(face2Ds_Intersection != null && face2Ds_Intersection.Count > 1)
            {
                face2Ds_Intersection = Planar.Query.Union(face2Ds_Intersection, tolerance);
            }

            return face2Ds_Intersection.ConvertAll(x => plane.Convert(x));
        }
    }
}
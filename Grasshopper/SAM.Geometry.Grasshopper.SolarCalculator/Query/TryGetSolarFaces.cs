using Grasshopper.Kernel.Types;
using SAM.Geometry.SolarCalculator;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.Grasshopper.SolarCalculator
{
    public static partial class Query
    {
        public static bool TryGetSolarFaces(this GH_ObjectWrapper objectWrapper, out List<SolarFace> solarFaces)
        {
            solarFaces = null;

            if (objectWrapper == null || objectWrapper.Value == null)
            {
                return false;
            }

            if (Grasshopper.Query.TryGetSAMGeometries(objectWrapper, out List<ISAMGeometry> sAMGeometries) && sAMGeometries != null)
            {
                solarFaces = new List<SolarFace>();
                foreach (ISAMGeometry sAMGeometry in sAMGeometries)
                {
                    if (sAMGeometry is Spatial.Face3D)
                    {
                        solarFaces.Add(new SolarFace(Guid.NewGuid(), sAMGeometry as Spatial.Face3D));
                    }
                    else if (sAMGeometry is Spatial.IFace3DObject)
                    {
                        Core.SAMObject sAMObject = sAMGeometry as Core.SAMObject;

                        solarFaces.Add(new SolarFace(sAMObject == null ? Guid.NewGuid() : sAMObject.Guid, ((Spatial.IFace3DObject)sAMGeometry).Face3D));
                    }
                    else if(sAMGeometry is Spatial.Shell)
                    {
                        solarFaces.AddRange(((Spatial.Shell)sAMGeometry).Face3Ds?.ConvertAll(x => new SolarFace(Guid.NewGuid(), x)));
                    }
                    else if(sAMGeometry is Spatial.Mesh3D)
                    {
                        Spatial.Mesh3D mesh3D = (Spatial.Mesh3D)sAMGeometry;
                        solarFaces.AddRange(mesh3D.GetTriangles().ConvertAll(x => new SolarFace(Guid.NewGuid(), new Spatial.Face3D(x))));
                    }
                    else if(sAMGeometry is Spatial.IClosedPlanar3D && sAMGeometry is Spatial.ISegmentable3D)
                    {
                        Spatial.IClosedPlanar3D closedPlanar3D = (Spatial.IClosedPlanar3D)sAMGeometry;
                        Spatial.Plane plane = closedPlanar3D.GetPlane();
                        List<Spatial.Face3D> face3Ds = Spatial.Create.Face3Ds(new Planar.IClosed2D[] { Spatial.Query.Convert(plane, closedPlanar3D) }, plane);
                        if(face3Ds != null)
                        {
                            solarFaces.AddRange(face3Ds.FindAll(x => x != null).ConvertAll(x => new SolarFace(Guid.NewGuid(), x)));
                        }
                    }
                }
            }

            return true;
        }

        public static bool TryGetSolarFaces(this IEnumerable<GH_ObjectWrapper> objectWrappers, out List<SolarFace> solarFaces)
        {
            solarFaces = null;

            if(objectWrappers == null || objectWrappers.Count() == 0)
            {
                return false;
            }

            solarFaces = new List<SolarFace>();
            foreach(GH_ObjectWrapper objectWrapper in objectWrappers)
            {
                if(TryGetSolarFaces(objectWrapper, out List<SolarFace> solarFaces_Temp) && solarFaces_Temp != null)
                {
                    solarFaces.AddRange(solarFaces_Temp);
                }
            }

            return solarFaces != null && solarFaces.Count != 0;
        }
    }
}

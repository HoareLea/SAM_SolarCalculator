using Grasshopper.Kernel.Types;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.Grasshopper.SolarCalculator
{
    public static partial class Query
    {
        public static bool TryGetLinkedFace3Ds(this GH_ObjectWrapper objectWrapper, out List<LinkedFace3D> linkedFace3Ds)
        {
            linkedFace3Ds = null;

            if (objectWrapper == null || objectWrapper.Value == null)
            {
                return false;
            }

            if (Grasshopper.Query.TryGetSAMGeometries(objectWrapper, out List<ISAMGeometry> sAMGeometries) && sAMGeometries != null)
            {
                linkedFace3Ds = new List<LinkedFace3D>();
                foreach (ISAMGeometry sAMGeometry in sAMGeometries)
                {
                    if (sAMGeometry is Face3D)
                    {
                        linkedFace3Ds.Add(new LinkedFace3D(Guid.NewGuid(), sAMGeometry as Face3D));
                    }
                    else if (sAMGeometry is IFace3DObject)
                    {
                        Core.SAMObject sAMObject = sAMGeometry as Core.SAMObject;

                        linkedFace3Ds.Add(new LinkedFace3D(sAMObject == null ? Guid.NewGuid() : sAMObject.Guid, ((IFace3DObject)sAMGeometry).Face3D));
                    }
                    else if(sAMGeometry is Shell)
                    {
                        linkedFace3Ds.AddRange(((Shell)sAMGeometry).Face3Ds?.ConvertAll(x => new LinkedFace3D(Guid.NewGuid(), x)));
                    }
                    else if(sAMGeometry is Mesh3D)
                    {
                        Mesh3D mesh3D = (Mesh3D)sAMGeometry;
                        linkedFace3Ds.AddRange(mesh3D.GetTriangles().ConvertAll(x => new LinkedFace3D(Guid.NewGuid(), new Face3D(x))));
                    }
                    else if(sAMGeometry is IClosedPlanar3D && sAMGeometry is ISegmentable3D)
                    {
                        IClosedPlanar3D closedPlanar3D = (IClosedPlanar3D)sAMGeometry;
                        Plane plane = closedPlanar3D.GetPlane();
                        List<Face3D> face3Ds = Spatial.Create.Face3Ds(new Planar.IClosed2D[] { Spatial.Query.Convert(plane, closedPlanar3D) }, plane);
                        if(face3Ds != null)
                        {
                            linkedFace3Ds.AddRange(face3Ds.FindAll(x => x != null).ConvertAll(x => new LinkedFace3D(Guid.NewGuid(), x)));
                        }
                    }
                }
            }

            return true;
        }

        public static bool TryGetLinkedFace3Ds(this IEnumerable<GH_ObjectWrapper> objectWrappers, out List<LinkedFace3D> linkedFace3Ds)
        {
            linkedFace3Ds = null;

            if(objectWrappers == null || objectWrappers.Count() == 0)
            {
                return false;
            }

            linkedFace3Ds = new List<LinkedFace3D>();
            foreach(GH_ObjectWrapper objectWrapper in objectWrappers)
            {
                if(TryGetLinkedFace3Ds(objectWrapper, out List<LinkedFace3D> linkedFace3Ds_Temp) && linkedFace3Ds_Temp != null)
                {
                    linkedFace3Ds.AddRange(linkedFace3Ds_Temp);
                }
            }

            return linkedFace3Ds != null && linkedFace3Ds.Count != 0;
        }
    }
}

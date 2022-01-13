using Grasshopper.Kernel;
using SAM.Geometry.Grasshopper.SolarCalculator.Properties;
using SAM.Core.Grasshopper;
using SAM.Geometry.SolarCalculator;
using System;
using System.Collections.Generic;
using Grasshopper.Kernel.Types;

namespace SAM.Geometry.Grasshopper.SolarCalculator
{
    public class SAMGeometryShadingBySunDirection : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("46f6432c-78a3-49d2-ad5f-8162ee5374ff");

        /// <summary>
        /// The latest version of this component
        /// </summary>
        public override string LatestComponentVersion => "1.0.0";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        public override GH_Exposure Exposure => GH_Exposure.primary;

        /// <summary>
        /// Initializes a new instance of the SAM_point3D class.
        /// </summary>
        public SAMGeometryShadingBySunDirection()
          : base("SAMGeometry.ShadingBySunDirection", "SAMGeometry.ShadingBySunDirection",
              "Gets Shading Geometry for given Geometry and sun direction",
              "SAM WIP", "Solar")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "_geometries", NickName = "_geometries", Description = "SAM Geometry", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Vector() { Name = "_sunDirection", NickName = "_sunDirection", Description = "Sun Direction", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                return result.ToArray();
            }
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override GH_SAMParam[] Outputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "shadings", NickName = "shadings", Description = "Shading Geometries", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                return result.ToArray();
            }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="dataAccess">
        /// The DA object is used to retrieve from inputs and store in outputs.
        /// </param>
        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            int index = -1;

            index = Params.IndexOfInputParam("_sunDirection");
            global::Rhino.Geometry.Vector3d vector3d = global::Rhino.Geometry.Vector3d.Unset;
            if (index == -1 || !dataAccess.GetData(index, ref vector3d) || vector3d == global::Rhino.Geometry.Vector3d.Unset)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            index = Params.IndexOfInputParam("_geometries");
            List<GH_ObjectWrapper> objectWrappers = new List<GH_ObjectWrapper>();
            if (index == -1 || !dataAccess.GetDataList(index, objectWrappers) || objectWrappers== null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            List<SolarFace> solarFaces = new List<SolarFace>();
            foreach (GH_ObjectWrapper objectWrapper in objectWrappers)
            {
                if (!Query.TryGetSAMGeometries(objectWrapper, out List<ISAMGeometry> sAMGeometries) || sAMGeometries == null)
                {
                    continue;
                }

                foreach(ISAMGeometry sAMGeometry in sAMGeometries)
                {
                    if(sAMGeometry is Spatial.Face3D)
                    {
                        solarFaces.Add(new SolarFace(Guid.NewGuid(), sAMGeometry as Spatial.Face3D));
                    }
                    else if(sAMGeometry is Spatial.IFace3DObject)
                    {
                        Core.SAMObject sAMObject = sAMGeometry as Core.SAMObject;

                        solarFaces.Add(new SolarFace(sAMObject == null ? Guid.NewGuid() : sAMObject.Guid, ((Spatial.IFace3DObject)sAMGeometry).Face3D));
                    }
                }
            }

            List<SolarFace> solarFaces_Result = Geometry.SolarCalculator.Query.Shadings(solarFaces, Rhino.Convert.ToSAM(vector3d));

            index = Params.IndexOfOutputParam("shadings");
            if (index != -1)
                dataAccess.SetDataList(index, solarFaces_Result?.ConvertAll(x => x?.Face3D));
        }
    }
}
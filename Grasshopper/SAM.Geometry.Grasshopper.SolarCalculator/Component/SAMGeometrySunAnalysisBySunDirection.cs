using Grasshopper.Kernel;
using SAM.Geometry.Grasshopper.SolarCalculator.Properties;
using SAM.Core.Grasshopper;
using System;
using System.Collections.Generic;
using Grasshopper.Kernel.Types;
using SAM.Geometry.Spatial;
using SAM.Geometry.Object.Spatial;

namespace SAM.Geometry.Grasshopper.SolarCalculator
{
    public class SAMGeometrySunAnalysisBySunDirection : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("cb478aba-aa27-48bf-b7a0-ff41114c8e10");

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
        public SAMGeometrySunAnalysisBySunDirection()
          : base("SAMGeometry.SunAnalysisBySunDirection", "SAMGeometry.SunAnalysisBySunDirection",
              "Sun Analysis by Sun Direction",
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

                global::Grasshopper.Kernel.Parameters.Param_Boolean param_Boolean = null;

                param_Boolean = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_shaded", NickName = "_shaded", Description = "Calculate Shaded Faces", Access = GH_ParamAccess.item };
                param_Boolean.SetPersistentData(true);
                result.Add(new GH_SAMParam(param_Boolean, ParamVisibility.Binding));

                param_Boolean = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_exposedToSun", NickName = "_exposedToSun", Description = "Calculate Exposed To Sun Faces", Access = GH_ParamAccess.item };
                param_Boolean.SetPersistentData(true);
                result.Add(new GH_SAMParam(param_Boolean, ParamVisibility.Binding));

                param_Boolean = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_run", NickName = "_run", Description = "Run", Access = GH_ParamAccess.item };
                param_Boolean.SetPersistentData(false);
                result.Add(new GH_SAMParam(param_Boolean, ParamVisibility.Binding));

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
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "shaded", NickName = "shaded", Description = "Shaded Faces", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "exposedToSun", NickName = "exposedToSun", Description = "Exposed To Sun Faces", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            bool run = false;
            index = Params.IndexOfInputParam("_run");
            if (!dataAccess.GetData(index, ref run))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            if (!run)
                return;

            index = Params.IndexOfInputParam("_shaded");
            bool shaded = false;
            if (index == -1 || !dataAccess.GetData(index, ref shaded))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            index = Params.IndexOfInputParam("_exposedToSun");
            bool exposedToSun = false;
            if (index == -1 || !dataAccess.GetData(index, ref exposedToSun))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

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

            if(!Query.TryGetLinkedFace3Ds(objectWrappers, out List<LinkedFace3D> linkedFace3Ds) || linkedFace3Ds == null || linkedFace3Ds.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            Object.Spatial.Query.ViewField(linkedFace3Ds, Rhino.Convert.ToSAM(vector3d), out List<LinkedFace3D> linkedFace3Ds_Shaded, out List<LinkedFace3D> linkedFace3Ds_ExposedToSun, shaded, exposedToSun);

            index = Params.IndexOfOutputParam("shaded");
            if (index != -1)
                dataAccess.SetDataList(index, linkedFace3Ds_Shaded?.ConvertAll(x => x?.Face3D));

            index = Params.IndexOfOutputParam("exposedToSun");
            if (index != -1)
                dataAccess.SetDataList(index, linkedFace3Ds_ExposedToSun?.ConvertAll(x => x?.Face3D));
        }
    }
}
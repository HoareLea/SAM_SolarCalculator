using Grasshopper.Kernel;
using SAM.Analytical.Grasshopper.SolarCalculator.Properties;
using SAM.Core.Grasshopper;
using SAM.Geometry.SolarCalculator;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.Grasshopper.SolarCalculator
{
    public class SAMGeometrySunExposure : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("c3326128-e4f4-4a3d-b8c1-0935f5647ea7");

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
        public SAMGeometrySunExposure()
          : base("SAMGeometry.SunExposure", "SAMGeometry.SunExposure",
              "Gets Sun Exposure Data",
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
                result.Add(new GH_SAMParam(new GooAnalyticalModelParam() { Name = "_analyticalModel", NickName = "_analyticalModel", Description = "SAM Analytical Model", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "_panel", NickName = "_panel", Description = "SAM Analytical Panel", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Time() { Name = "_time", NickName = "_time", Description = "Time", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Brep() { Name = "face3D", NickName = "face3D", Description = "SAM Analytical Panel Face3D", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "exposedToSunPercent", NickName = "exposedToSunPercent", Description = "Percent of face exposed to sun", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Brep() { Name = "exposedToSunFace3Ds", NickName = "exposedToSunFace3D", Description = "Face3D exposed to sun", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            index = Params.IndexOfInputParam("_analyticalModel");
            AnalyticalModel analyticalModel = null;
            if (index == -1 || !dataAccess.GetData(index, ref analyticalModel) || analyticalModel == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            index = Params.IndexOfInputParam("_panel");
            Panel panel = null;
            if (index == -1 || !dataAccess.GetData(index, ref panel) || panel == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            index = Params.IndexOfInputParam("_time");
            DateTime dateTime = DateTime.MinValue;
            if (index == -1 || !dataAccess.GetData(index, ref dateTime) || dateTime == DateTime.MinValue)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            panel = analyticalModel?.GetPanels()?.Find(x => x.Guid == panel.Guid);
            if(panel == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            Geometry.Spatial.Face3D face3D = panel.Face3D;
            double percent = 0;
            List<Geometry.Spatial.Face3D> face3Ds = null;

            SolarFaceSimulationResult solarFaceSimulationResult = analyticalModel.GetResults<SolarFaceSimulationResult>(panel)?.FirstOrDefault();
            if(solarFaceSimulationResult != null)
            {
                face3Ds = solarFaceSimulationResult.GetSunExposureFace3Ds(dateTime);
                if(face3Ds != null && face3Ds.Count != 0)
                {
                    double area = face3Ds.ConvertAll(x => x.GetArea()).Sum();
                    percent = area / face3D.GetArea();
                }

            }

            index = Params.IndexOfOutputParam("face3D");
            if (index != -1)
                dataAccess.SetData(index, Geometry.Rhino.Convert.ToRhino_Brep(face3D));

            index = Params.IndexOfOutputParam("exposedToSunPercent");
            if (index != -1)
                dataAccess.SetData(index, percent);

            index = Params.IndexOfOutputParam("exposedToSunFace3Ds");
            if (index != -1)
                dataAccess.SetDataList(index, face3Ds?.ConvertAll(x => Geometry.Rhino.Convert.ToRhino_Brep(x)));
        }
    }
}
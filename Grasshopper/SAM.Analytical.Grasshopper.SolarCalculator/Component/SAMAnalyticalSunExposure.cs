using Grasshopper.Kernel;
using SAM.Analytical.Grasshopper.SolarCalculator.Properties;
using SAM.Core.Grasshopper;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
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
        public override string LatestComponentVersion => "1.0.3";

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
              "Gets Sun Exposure Data for Panel \n*optionally Aperture can be connected to limit results display to specific items  ",
              "SAM", "Solar")
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
                result.Add(new GH_SAMParam(new GooAnalyticalObjectParam() { Name = "_panel", NickName = "_panel", Description = "SAM Analytical Panel \n*Aperture is possible as well", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
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

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Brep() { Name = "face3Ds_Panel", NickName = "face3Ds_Panel", Description = "SAM Analytical Panel Face3Ds (Excluding Apertures)", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "exposedToSunPercent_Panel", NickName = "exposedToSunPercent_Panel", Description = "Percent of Panel face exposed to sun (Excluding Apertures)", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Brep() { Name = "exposedToSunFace3Ds_Panel", NickName = "exposedToSunFace3D_Panel", Description = "Panel Face3Ds exposed to sun (Excluding Apertures)", Access = GH_ParamAccess.list }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Brep() { Name = "face3Ds_Apertures", NickName = "face3Ds_Apertures", Description = "SAM Analytical Apertures Face3Ds", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "exposedToSunPercent_Apertures", NickName = "exposedToSunPercent_Apertures", Description = "Percent of Apertures faces exposed to sun", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Brep() { Name = "exposedToSunFace3Ds_Apertures", NickName = "exposedToSunFace3D_Apertures", Description = "Apertures Face3Ds exposed to sun", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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
            IAnalyticalObject analyticalObject = null;
            if (index == -1 || !dataAccess.GetData(index, ref analyticalObject) || analyticalObject == null)
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

            Panel panel = null;
            List<Aperture> apertures = null;

            if(analyticalObject is Panel)
            {
                panel = (Panel)analyticalObject;
            }
            else if(analyticalObject is Aperture)
            {
                apertures = new List<Aperture>() { (Aperture)analyticalObject };
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            panel = apertures != null ? analyticalModel?.AdjacencyCluster?.GetPanel(apertures[0]) : analyticalModel?.GetPanels()?.Find(x => x.Guid == panel.Guid);

            if(panel == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            SolarFaceSimulationResult solarFaceSimulationResult = analyticalModel.GetResults<SolarFaceSimulationResult>(panel)?.FirstOrDefault();
            if(solarFaceSimulationResult == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "SolarFaceSimulationResult not found. It seems model has not been simulated.");
            }

            List<Face3D> face3Ds = null;
            Face3D face3D = null;
            double percent = double.NaN;


            face3D = panel.Face3D;
            percent = 0;
            face3Ds = solarFaceSimulationResult?.GetSunExposureFace3Ds(dateTime);
            if (face3Ds != null && face3Ds.Count != 0)
            {
                double area = face3Ds.ConvertAll(x => x.GetArea()).Sum();
                percent = area / face3D.GetArea();
            }

            index = Params.IndexOfOutputParam("face3D");
            if (index != -1)
            {
                dataAccess.SetData(index, Geometry.Rhino.Convert.ToRhino_Brep(face3D));
            }

            index = Params.IndexOfOutputParam("exposedToSunPercent");
            if (index != -1)
            {
                dataAccess.SetData(index, percent);
            }

            index = Params.IndexOfOutputParam("exposedToSunFace3Ds");
            if (index != -1)
            {
                dataAccess.SetDataList(index, face3Ds?.ConvertAll(x => Geometry.Rhino.Convert.ToRhino_Brep(x)));
            }

            percent = 0;
            face3Ds = null;

            List<Face3D> face3Ds_Panel = panel.GetFace3Ds(true);
            if(face3Ds_Panel != null)
            {
                face3Ds = new List<Face3D>();

                double area_SunExposure = 0;
                double area = 0;
                foreach (Face3D face3D_Panel in face3Ds_Panel)
                {
                    area += face3D_Panel.GetArea();
                    List<Face3D> face3Ds_SunExposure = Geometry.SolarCalculator.Query.SunExposureFace3Ds(solarFaceSimulationResult, face3D_Panel, dateTime);
                    if (face3Ds_SunExposure != null && face3Ds_SunExposure.Count != 0)
                    {
                        area_SunExposure += face3Ds_SunExposure.ConvertAll(x => x.GetArea()).Sum();
                        face3Ds.AddRange(face3Ds_SunExposure);
                    }
                }

                percent = area_SunExposure / area;
            }

            index = Params.IndexOfOutputParam("face3Ds_Panel");
            if (index != -1)
            {
                dataAccess.SetDataList(index, face3Ds_Panel?.ConvertAll(x => Geometry.Rhino.Convert.ToRhino_Brep(x)));
            }

            index = Params.IndexOfOutputParam("exposedToSunPercent_Panel");
            if (index != -1)
            {
                dataAccess.SetData(index, percent);
            }

            index = Params.IndexOfOutputParam("exposedToSunFace3Ds_Panel");
            if (index != -1)
            {
                dataAccess.SetDataList(index, face3Ds?.ConvertAll(x => Geometry.Rhino.Convert.ToRhino_Brep(x)));
            }


            if (apertures == null)
            {
                apertures = panel.Apertures;
            }

            List<Geometry.Spatial.Face3D> face3Ds_Apertures = new List<Geometry.Spatial.Face3D>();
            List<double> percents = new List<double>();
            List<Geometry.Spatial.Face3D> face3Ds_Apertures_SunExposure = new List<Geometry.Spatial.Face3D>();
            if(apertures != null)
            {
                foreach(Aperture aperture in apertures)
                {
                    face3D = aperture.GetFace3D();
                    percent = 0;
                    face3Ds = Geometry.SolarCalculator.Query.SunExposureFace3Ds(solarFaceSimulationResult, face3D, dateTime);
                    if (face3Ds != null && face3Ds.Count != 0)
                    {
                        double area = face3Ds.ConvertAll(x => x.GetArea()).Sum();
                        percent = area / face3D.GetArea();
                    }

                    face3Ds_Apertures.Add(face3D);
                    percents.Add(percent);
                    if(face3Ds != null)
                    {
                        face3Ds_Apertures_SunExposure.AddRange(face3Ds);
                    }
                }
            }

            index = Params.IndexOfOutputParam("face3Ds_Apertures");
            if (index != -1)
            {
                dataAccess.SetDataList(index, face3Ds_Apertures?.ConvertAll(x => Geometry.Rhino.Convert.ToRhino_Brep(x)));
            }

            index = Params.IndexOfOutputParam("exposedToSunPercent_Apertures");
            if (index != -1)
            {
                dataAccess.SetDataList(index, percents);
            }

            index = Params.IndexOfOutputParam("exposedToSunFace3Ds_Apertures");
            if (index != -1)
            {
                dataAccess.SetDataList(index, face3Ds_Apertures_SunExposure?.ConvertAll(x => Geometry.Rhino.Convert.ToRhino_Brep(x)));
            }

        }
    }
}
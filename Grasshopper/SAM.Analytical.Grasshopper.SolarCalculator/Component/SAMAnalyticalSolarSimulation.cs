using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SAM.Analytical.Grasshopper.SolarCalculator.Properties;
using SAM.Core;
using SAM.Core.Grasshopper;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.Grasshopper.SolarCalculator
{
    public class SAMAnalyticalSolarSimulation : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("10cb35d6-1dcb-4b7a-8069-a54dcadd17f8");

        /// <summary>
        /// The latest version of this component
        /// </summary>
        public override string LatestComponentVersion => "1.0.5";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        public override GH_Exposure Exposure => GH_Exposure.primary;

        /// <summary>
        /// Initializes a new instance of the SAM_point3D class.
        /// </summary>
        public SAMAnalyticalSolarSimulation()
          : base("SAMAnalytical.SolarSimulation", "SAMAnalytical.SolarSimulation",
              "This node tries to replicate shading calculation as per T3D. \nCalculate for each given hour % that is exposed to sun\n*This node take quite long time to complete\n_timeShift_ Is set to  -30min to follow Tas EDSL apporach at 9:00 is calculated at 8:30",
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

                global::Grasshopper.Kernel.Parameters.Param_Integer integer = null;

                integer = new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "_year", NickName = "_year", Description = "year", Access = GH_ParamAccess.item };
                integer.SetPersistentData(2018);
                result.Add(new GH_SAMParam(integer, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_GenericObject genericObject = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_hoursOfYear", NickName = "_hoursOfYear", Description = "Hours Of Year", Access = GH_ParamAccess.list };
                genericObject.SetPersistentData(Analytical.Query.DefaultHoursOfYear().ToArray());
                result.Add(new GH_SAMParam(genericObject, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number number = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_angleTolerance_", NickName = "_angleTolerance_", Description = "Angle Tolerance", Access = GH_ParamAccess.item };
                number.SetPersistentData(Core.Tolerance.Angle);
                result.Add(new GH_SAMParam(number, ParamVisibility.Voluntary));

                number = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_timeShift_", NickName = "_timeShift_", Description = "Time Shift in minutes to be added/deducted from hour of the year\n Default value -30min to align with Tas EDSL", Access = GH_ParamAccess.item };
                number.SetPersistentData(-30);
                result.Add(new GH_SAMParam(number, ParamVisibility.Binding));

                number = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_minHorizonAngle_", NickName = "_minHorizonAngle_", Description = "Minimal Angle to Horizon", Access = GH_ParamAccess.item };
                number.SetPersistentData(SAM.Core.Tolerance.Angle);
                result.Add(new GH_SAMParam(number, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Boolean boolean = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_run", NickName = "_run", Description = "Run", Access = GH_ParamAccess.item };
                boolean.SetPersistentData(false);
                result.Add(new GH_SAMParam(boolean, ParamVisibility.Binding));

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
                result.Add(new GH_SAMParam(new GooAnalyticalModelParam() { Name = "analyticalModel", NickName = "analyticalModel", Description = "SAM Analytical Model", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooResultParam() { Name = "solarFaceSimulationResults", NickName = "solarFaceSimulationResults", Description = "SAM Analytical Model", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Integer integer = new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "hoursOfYear", NickName = "hoursOfYear", Description = "Hours Of Year", Access = GH_ParamAccess.list };
                result.Add(new GH_SAMParam(integer, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "successful", NickName = "successful", Description = "Successful?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
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

            int index_Successful = Params.IndexOfOutputParam("successful");
            if (index_Successful != -1)
                dataAccess.SetData(index_Successful, false);

            bool run = false;
            index = Params.IndexOfInputParam("_run");
            if (!dataAccess.GetData(index, ref run))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            if (!run)
                return;

            index = Params.IndexOfInputParam("_angleTolerance_");
            double tolerance_Angle = Core.Tolerance.Angle;
            if (index != -1)
            {
                double tolerance_Angle_Temp = tolerance_Angle;
                if(dataAccess.GetData(index, ref tolerance_Angle_Temp) && !double.IsNaN(tolerance_Angle_Temp))
                {
                    tolerance_Angle = tolerance_Angle_Temp;
                }
            }

            index = Params.IndexOfInputParam("_analyticalModel");
            AnalyticalModel analyticalModel = null;
            if (index == -1 || !dataAccess.GetData(index, ref analyticalModel) || analyticalModel == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }


            index = Params.IndexOfInputParam("_hoursOfYear");
            List<GH_ObjectWrapper> objectWrappers = new List<GH_ObjectWrapper>();
            if (index == -1 || !dataAccess.GetDataList(index, objectWrappers) || objectWrappers == null || objectWrappers.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            index = Params.IndexOfInputParam("_year");
            int year = -1;
            if (index == -1 || !dataAccess.GetData(index, ref year) || year == -1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            index = Params.IndexOfInputParam("_timeShift_");
            double timeShift = 0;
            if (index != -1)
            {
                double timeShift_Temp = timeShift;
                if (dataAccess.GetData(index, ref timeShift_Temp) && !double.IsNaN(timeShift_Temp))
                {
                    timeShift = timeShift_Temp;
                }
            }

            List<DateTime> dateTimes = new List<DateTime>();
            foreach(GH_ObjectWrapper objectWrapper in objectWrappers)
            {
                object @object = (objectWrapper?.Value as dynamic).Value;
                if(@object == null)
                {
                    continue;
                }

                DateTime dateTime = DateTime.MinValue;

                if(Core.Query.IsNumeric(@object))
                {
                    dateTime = new DateTime(year, 1, 1).AddHours(System.Convert.ToInt32(@object));
                }
                else if (@object is DateTime)
                {
                    dateTime = (DateTime)@object;
                }

                if(dateTime == DateTime.MinValue)
                {
                    continue;
                }

                if(timeShift != 0)
                {
                    dateTime = dateTime.AddMinutes(timeShift);
                }

                dateTimes.Add(dateTime);

                //hoursOfYear.ConvertAll(x => new DateTime(year, 1, 1).AddHours(x)
            }


            //List<int> hoursOfYear = new List<int>();
            //if (index == -1 || !dataAccess.GetDataList(index, hoursOfYear) || hoursOfYear == null || hoursOfYear.Count == 0)
            //{
            //    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
            //    return;
            //}



            if(analyticalModel.Location == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "AnalyticalModel missing location");
                return;
            }

            index = Params.IndexOfInputParam("_minHorizonAngle_");
            double minHorizonAngle = Core.Tolerance.Angle;
            if (index != -1)
            {
                double minHorizonAngle_Temp = minHorizonAngle;
                if (dataAccess.GetData(index, ref minHorizonAngle_Temp) && !double.IsNaN(minHorizonAngle_Temp))
                {
                    minHorizonAngle = minHorizonAngle_Temp;
                }
            }

            analyticalModel = new AnalyticalModel(analyticalModel);
            List<Geometry.SolarCalculator.SolarFaceSimulationResult> solarFaceSimulationResults = Analytical.SolarCalculator.Modify.Simulate(analyticalModel, dateTimes, minHorizonAngle: minHorizonAngle, tolerance_Angle: tolerance_Angle);

            
            index = Params.IndexOfOutputParam("hoursOfYear");
            if (index != -1)
            {
                dataAccess.SetDataList(index, dateTimes?.ConvertAll(x => x.HourOfYear()));
            }


            index = Params.IndexOfOutputParam("analyticalModel");
            if (index != -1)
            {
                dataAccess.SetData(index, analyticalModel);
            }

            index = Params.IndexOfOutputParam("solarFaceSimulationResults");
            if (index != -1)
            {
                dataAccess.SetDataList(index, solarFaceSimulationResults?.ConvertAll(x => new GooResult(x)));
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, solarFaceSimulationResults != null);
            }
        }
    }
}
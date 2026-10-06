// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SAM.Analytical.Grasshopper.SolarCalculator.Properties;
using SAM.Analytical.SolarCalculator;
using SAM.Core.Grasshopper;
using SAM.Core.SolarCalculator;
using SAM.Geometry.SolarCalculator;
using SAM.Weather;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.Grasshopper.SolarCalculator
{
    public class SAMAnalyticalApertureIrradiance : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7f3c9a10-5b28-4e63-9a41-6c0d2e7b1103");

        /// <summary>
        /// The latest version of this component
        /// </summary>
        public override string LatestComponentVersion => "1.0.0";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        public override GH_Exposure Exposure => GH_Exposure.primary;

        public SAMAnalyticalApertureIrradiance()
          : base("SAMAnalytical.ApertureIrradiance", "SAMAnalytical.ApertureIrradiance",
              "SUMMARY\nHow much solar energy each window actually receives over the chosen hours, with the rest of the building and the site geometry blocking the sun where they really do. Reports the direct beam, the diffuse sky and the ground-reflected components separately.\n*The first run on a model does the geometric work and can take a while; later runs on the same geometry reuse it.*\n\nINPUTS\n  _analyticalModel — the SAM Analytical Model.\n  _weatherData_ — hourly weather (EPW imported through the SAM Weather nodes). Supplied weather WINS; otherwise the weather attached to the model is used; with neither, the node stops and says so.\n  _apertures_ — apertures to analyse, as SAM Apertures or Guids. Empty = every external sun-exposed aperture.\n  _analysisPeriod_ — hours to calculate. Empty = the full year.\n  _HOYs_ — explicit hours of the year. These OVERRIDE _analysisPeriod_.\n  _gridSize_ — spacing of the sample points across each opening [m]. Default 0.5 m.\n  _sunAngleStep_ — how finely similar sun positions are grouped [°]. Default 2°. Smaller is more precise and slower.\n  _skyModel_ — Perez anisotropic (default) or isotropic diffuse sky.\n  _albedo_ — ground reflectance, 0-1. Default 0.2.\n  _sunTimeConvention_ — what a weather timestamp means: IntervalStart (default, SAM/EPW), OnTheHour, or IntervalEnd (the Tas EDSL comparison convention).\n  _recalculate_ — true forces the solar calculation to be redone even when it could be reused.\n  _run — nothing happens until this is true.\n\nOUTPUTS\n  analyticalModel — the model with the results and the solar calculation attached. Wire this on: the next node then reuses the work rather than repeating it.\n  apertureIrradianceResults — full per-aperture results.\n  apertureGuids — aperture identity per result.\n  totalEnergy / directEnergy / diffuseEnergy / groundReflectedEnergy — energy on each opening over the period [kWh].\n  averageIrradiance — energy per unit opening area [kWh/m²].\n  reusedPreviousCalculation — true when no geometric work was needed.\n  hourCount — hours actually evaluated.\n  successful — true when results were produced.\n\nNOTES\nUnits: energies in kWh, densities in kWh/m², areas in m², angles in degrees.\nChanging the period, the weather or the sky model costs almost nothing — none of them changes the geometry. Changing the model, _gridSize_ or _sunAngleStep_ does, and the calculation is redone automatically; the node says so when that happens.\nHours where the weather file is missing radiation values, or the sun is below the horizon cutoff, are counted out rather than assumed to be zero.\nThis node reports solar arriving at the opening. It does not model what happens to it inside the room.\n\nEXAMPLE\nAnalyticalModel → ApertureSolarTargets → ApertureIrradiance — wire ApertureSolarTargets' apertureGuids output into _apertures_ — with _analysisPeriod_ from AnalysisPeriod (Summer) and _run = true.",
              "SAM", "Solar")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooAnalyticalModelParam() { Name = "_analyticalModel", NickName = "_analyticalModel", Description = "SAM Analytical Model", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_weatherData_", NickName = "_weatherData_", Description = "SAM WeatherData (hourly).\nSupplied weather wins; otherwise the weather attached to the model is used", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_apertures_", NickName = "_apertures_", Description = "Apertures to analyse, as SAM Apertures or Guids.\nEmpty = every external sun-exposed aperture", Access = GH_ParamAccess.list, Optional = true }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_analysisPeriod_", NickName = "_analysisPeriod_", Description = "Hours to calculate, from SAMAnalytical.AnalysisPeriod.\nEmpty = full year", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_HOYs_", NickName = "_HOYs_", Description = "Explicit hours of the year. These override _analysisPeriod_", Access = GH_ParamAccess.list, Optional = true }, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number gridSize = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_gridSize_", NickName = "_gridSize_", Description = "SPACING between the analysis sample points across each opening [m].\n\nALLOWED: greater than zero, and not finer than about 0.032 m - below that a sample cell is smaller than the geometry area tolerance and NO samples can be produced at all.\nHalving it roughly quadruples the sample count and the runtime.\nDefault 0.5 m", Access = GH_ParamAccess.item };
                gridSize.SetPersistentData(0.5);
                result.Add(new GH_SAMParam(gridSize, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number sunAngleStep = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_sunAngleStep_", NickName = "_sunAngleStep_", Description = "How finely similar sun positions are grouped [°].\nDefault 2°", Access = GH_ParamAccess.item };
                sunAngleStep.SetPersistentData(2.0);
                result.Add(new GH_SAMParam(sunAngleStep, ParamVisibility.Voluntary));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_skyModel_", NickName = "_skyModel_", Description = "Diffuse sky model: PerezAnisotropic (default) or Isotropic", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number albedo = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_albedo_", NickName = "_albedo_", Description = "Ground reflectance, 0-1.\nDefault 0.2", Access = GH_ParamAccess.item };
                albedo.SetPersistentData(0.2);
                result.Add(new GH_SAMParam(albedo, ParamVisibility.Voluntary));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_sunTimeConvention_", NickName = "_sunTimeConvention_", Description = "What a weather timestamp means: IntervalStart (default, SAM/EPW), OnTheHour, IntervalEnd (Tas EDSL comparison)", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean recalculate = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_recalculate_", NickName = "_recalculate_", Description = "Force the solar calculation to be redone even when it could be reused.\nDefault false", Access = GH_ParamAccess.item };
                recalculate.SetPersistentData(false);
                result.Add(new GH_SAMParam(recalculate, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean run = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_run", NickName = "_run", Description = "Nothing is calculated until this is true", Access = GH_ParamAccess.item };
                run.SetPersistentData(false);
                result.Add(new GH_SAMParam(run, ParamVisibility.Binding));

                return result.ToArray();
            }
        }

        protected override GH_SAMParam[] Outputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooAnalyticalModelParam() { Name = "analyticalModel", NickName = "analyticalModel", Description = "The model with results and the solar calculation attached", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooResultParam() { Name = "apertureIrradianceResults", NickName = "apertureIrradianceResults", Description = "Per-aperture irradiance results", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "apertureGuids", NickName = "apertureGuids", Description = "Aperture Guid per result", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "totalEnergy", NickName = "totalEnergy", Description = "Total solar energy on each opening over the period [kWh]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "directEnergy", NickName = "directEnergy", Description = "Direct-beam energy [kWh]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "diffuseEnergy", NickName = "diffuseEnergy", Description = "Diffuse-sky energy [kWh]", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "groundReflectedEnergy", NickName = "groundReflectedEnergy", Description = "Ground-reflected energy [kWh]", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "averageIrradiance", NickName = "averageIrradiance", Description = "Energy per unit opening area [kWh/m²]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "reusedPreviousCalculation", NickName = "reusedPreviousCalculation", Description = "True when no geometric work was needed", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "hourCount", NickName = "hourCount", Description = "Hours actually evaluated", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "successful", NickName = "successful", Description = "Successful?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                return result.ToArray();
            }
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            int index_Successful = Params.IndexOfOutputParam("successful");
            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, false);
            }

            int index = Params.IndexOfInputParam("_run");
            bool run = false;
            if (index == -1 || !dataAccess.GetData(index, ref run))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply a value for _run.");
                return;
            }

            if (!run)
            {
                return;
            }

            index = Params.IndexOfInputParam("_analyticalModel");
            AnalyticalModel analyticalModel = null;
            if (index == -1 || !dataAccess.GetData(index, ref analyticalModel) || analyticalModel == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply a valid SAM AnalyticalModel.");
                return;
            }

            analyticalModel = new AnalyticalModel(analyticalModel);

            WeatherData weatherData = null;
            index = Params.IndexOfInputParam("_weatherData_");
            if (index != -1)
            {
                GH_ObjectWrapper objectWrapper = null;
                if (dataAccess.GetData(index, ref objectWrapper) && objectWrapper?.Value != null)
                {
                    weatherData = Query.Value<WeatherData>(objectWrapper);
                    if (weatherData == null)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_weatherData_ is not SAM WeatherData. Supply hourly weather from the SAM Weather nodes, or leave it empty to use the weather attached to the model.");
                        return;
                    }
                }
            }

            if (weatherData == null && analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData) == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "WeatherData is required. Supply WeatherData or attach it to the AnalyticalModel.");
                return;
            }

            List<Guid> apertureGuids = new List<Guid>();
            index = Params.IndexOfInputParam("_apertures_");
            if (index != -1)
            {
                List<GH_ObjectWrapper> objectWrappers = new List<GH_ObjectWrapper>();
                if (dataAccess.GetDataList(index, objectWrappers))
                {
                    apertureGuids = SAMAnalyticalApertureSolarTargets.ApertureGuids(objectWrappers, out int unrecognised);
                    if (unrecognised > 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Format("{0} item(s) on _apertures_ were neither a SAM Aperture nor an aperture Guid and were ignored.", unrecognised));
                    }
                }
            }

            AnalysisPeriod analysisPeriod = null;
            index = Params.IndexOfInputParam("_analysisPeriod_");
            if (index != -1)
            {
                GH_ObjectWrapper objectWrapper = null;
                if (dataAccess.GetData(index, ref objectWrapper) && objectWrapper?.Value != null)
                {
                    analysisPeriod = Query.Value<AnalysisPeriod>(objectWrapper);
                    if (analysisPeriod == null)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_analysisPeriod_ is not an AnalysisPeriod. Use SAMAnalytical.AnalysisPeriod to build one.");
                        return;
                    }
                }
            }

            List<int> hoursOfYear = new List<int>();
            index = Params.IndexOfInputParam("_HOYs_");
            if (index != -1)
            {
                List<GH_ObjectWrapper> objectWrappers = new List<GH_ObjectWrapper>();
                if (dataAccess.GetDataList(index, objectWrappers))
                {
                    hoursOfYear = Query.HoursOfYear(objectWrappers);
                }
            }

            int year = analysisPeriod?.Year ?? DefaultYear(analyticalModel, weatherData);
            AnalysisPeriod resolvedAnalysisPeriod = Core.SolarCalculator.Create.ResolvedAnalysisPeriod(year, analysisPeriod, hoursOfYear, out bool hoursOverrodeAnalysisPeriod);
            if (hoursOverrodeAnalysisPeriod)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Explicit HOYs override the connected AnalysisPeriod.");
            }

            double gridSize = Number(dataAccess, "_gridSize_", 0.5);
            // Zero, negative, NaN — and the case that used to fail silently: a grid so fine that
            // every sample cell falls below the geometry area tolerance and NOTHING can be built.
            if (SAM.Analytical.SolarCalculator.Query.GridSizeValidity(gridSize, out string gridSizeMessage) != GridSizeValidity.Valid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, gridSizeMessage);
                return;
            }

            double sunAngleStep = Number(dataAccess, "_sunAngleStep_", 2.0);
            if (double.IsNaN(sunAngleStep) || sunAngleStep <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_sunAngleStep_ must be greater than zero, in degrees.");
                return;
            }

            double albedo = Number(dataAccess, "_albedo_", 0.2);
            if (double.IsNaN(albedo) || albedo < 0 || albedo > 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_albedo_ must be between 0 and 1.");
                return;
            }

            SkyModel skyModel = SkyModel.PerezAnisotropic;
            index = Params.IndexOfInputParam("_skyModel_");
            if (index != -1)
            {
                GH_ObjectWrapper objectWrapper = null;
                if (dataAccess.GetData(index, ref objectWrapper) && objectWrapper?.Value != null)
                {
                    if (!Query.TryGetEnum(objectWrapper, out skyModel) || skyModel == SkyModel.Undefined)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_skyModel_ was not recognised. Use PerezAnisotropic or Isotropic.");
                        return;
                    }
                }
            }

            SunTimeConvention sunTimeConvention = SunTimeConvention.IntervalStart;
            index = Params.IndexOfInputParam("_sunTimeConvention_");
            if (index != -1)
            {
                GH_ObjectWrapper objectWrapper = null;
                if (dataAccess.GetData(index, ref objectWrapper) && objectWrapper?.Value != null)
                {
                    if (!Query.TryGetEnum(objectWrapper, out sunTimeConvention) || sunTimeConvention == SunTimeConvention.Undefined)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_sunTimeConvention_ was not recognised. Use IntervalStart, OnTheHour or IntervalEnd.");
                        return;
                    }
                }
            }

            bool recalculate = false;
            index = Params.IndexOfInputParam("_recalculate_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref recalculate);
            }

            List<ApertureIrradianceResult> results = analyticalModel.SimulateApertures(
                resolvedAnalysisPeriod, out bool reusedPreviousCalculation, weatherData,
                apertureGuids.Count == 0 ? null : apertureGuids,
                gridSize, skyModel, sunAngleStep, recalculate, albedo, sunTimeConvention);

            if (results == null || results.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No results were produced. Check that the model has external sun-exposed apertures, that WeatherData covers the analysis year, and that the site location resolves to a time zone.");
                return;
            }

            if (!reusedPreviousCalculation && !recalculate)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Geometry changed; solar calculation was updated.");
            }

            int missingWeatherHours = 0;
            foreach (ApertureIrradianceResult result in results)
            {
                missingWeatherHours = Math.Max(missingWeatherHours, result.MissingWeatherHours);
            }

            if (missingWeatherHours > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, string.Format("{0} hour(s) in the period had no usable radiation values in the weather file and were left out of the totals.", missingWeatherHours));
            }

            index = Params.IndexOfOutputParam("analyticalModel");
            if (index != -1)
            {
                dataAccess.SetData(index, analyticalModel);
            }

            index = Params.IndexOfOutputParam("apertureIrradianceResults");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => new GooResult(x)));
            }

            index = Params.IndexOfOutputParam("apertureGuids");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.Reference));
            }

            index = Params.IndexOfOutputParam("totalEnergy");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.TotalEnergy));
            }

            index = Params.IndexOfOutputParam("directEnergy");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.DirectEnergy));
            }

            index = Params.IndexOfOutputParam("diffuseEnergy");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.DiffuseEnergy));
            }

            index = Params.IndexOfOutputParam("groundReflectedEnergy");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.GroundReflectedEnergy));
            }

            index = Params.IndexOfOutputParam("averageIrradiance");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.AverageIrradiance));
            }

            index = Params.IndexOfOutputParam("reusedPreviousCalculation");
            if (index != -1)
            {
                dataAccess.SetData(index, reusedPreviousCalculation);
            }

            index = Params.IndexOfOutputParam("hourCount");
            if (index != -1)
            {
                dataAccess.SetData(index, results[0].DateTimes?.Count ?? 0);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, true);
            }
        }

        /// <summary>The weather's own year when no period says otherwise, so the default full year lands on real weather.</summary>
        internal static int DefaultYear(AnalyticalModel analyticalModel, WeatherData weatherData)
        {
            WeatherData resolved = weatherData ?? analyticalModel?.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            WeatherYear weatherYear = resolved?.WeatherYears?.Find(x => x != null);
            return weatherYear?.Year ?? DateTime.Now.Year;
        }

        private double Number(IGH_DataAccess dataAccess, string name, double defaultValue)
        {
            int index = Params.IndexOfInputParam(name);
            if (index == -1)
            {
                return defaultValue;
            }

            double value = defaultValue;
            if (dataAccess.GetData(index, ref value) && !double.IsNaN(value))
            {
                return value;
            }

            return defaultValue;
        }
    }
}

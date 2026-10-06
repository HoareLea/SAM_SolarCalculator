// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using SAM.Analytical.Grasshopper.SolarCalculator.Properties;
using SAM.Analytical.SolarCalculator;
using SolarCreate = SAM.Analytical.SolarCalculator.Create;
using SolarQuery = SAM.Analytical.SolarCalculator.Query;
using SAM.Core.Grasshopper;
using SAM.Core.SolarCalculator;
using SAM.Weather;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.Grasshopper.SolarCalculator
{
    public class SAMAnalyticalShadingPotentialField : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7f3c9a10-5b28-4e63-9a41-6c0d2e7b1104");

        /// <summary>
        /// The latest version of this component.
        ///
        /// 1.0.1 — added _previewMode_ (Points / Voxels / None) and a legend output, and made the
        /// red/blue convention explicit everywhere it is described. Manual testing found the map was
        /// read as "where the sun is" rather than as "where shading material is worth putting", and
        /// the two are not the same picture.
        ///
        /// 1.0.2 — TERMINOLOGY. positiveTotal / negativeTotal are renamed
        /// positiveShadingPotential / negativeShadingPotential and no longer carry a kWh label:
        /// manual testing read "benefit 5504 kWh" as a saving, when the totals sum a value that
        /// every voxel along a ray's path receives and are not an amount of energy anything could
        /// save. "Jeopardy" is gone. The negative total is reported as a positive magnitude, and the
        /// legend now states what the totals are NOT.
        /// </summary>
        public override string LatestComponentVersion => "1.0.2";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        public SAMAnalyticalShadingPotentialField()
          : base("SAMAnalytical.ShadingPotentialField", "SAMAnalytical.ShadingPotentialField",
              "SUMMARY\nMaps the space in front of ONE window and answers, everywhere in it: if shading material were placed here, how much solar would it intercept, and would that solar be unwanted or wanted?\n\nThe answer is in kWh, from the real sun path, the real weather and the real surroundings. Solar that a neighbouring building already blocks is never counted, so a shading device can never take credit for it.\n*This node casts rays and takes time on the first run for a given model and grid.*\n\nHOW TO READ THE COLOURS\nThe colour is NOT 'how much sun is here'. It is the VALUE OF PUTTING SHADING MATERIAL AT THAT LOCATION.\n  RED  = SHADE HERE. Material here blocks solar you asked to block. Stronger red, bigger gain.\n  BLUE = KEEP OPEN. Material here would destroy solar you asked to KEEP. Stronger blue, worse the loss.\n  grey = neither; almost no beam passes through, so material here does nothing. Near-zero locations are left out entirely.\nSo the map is a set of instructions, not a heat map: fill the red, stay out of the blue. The legend output says the same thing next to the geometry.\n\nINPUTS\n  _analyticalModel — the SAM Analytical Model.\n  _apertureSolarTarget — the window to study, from ApertureSolarTargets.\n  _weatherData_ — hourly weather. Supplied weather wins; otherwise the weather attached to the model.\n  _unwantedPeriod_ — hours whose solar you want blocked (typically summer).\n  _wantedPeriod_ — hours whose solar you want kept (typically winter).\n  _desirability_ — a full weighting strategy. When supplied it WINS over the two periods.\n  _maxDepth_ — how far out from the glass to look [m]. Default 1.0 m.\n  _voxelSize_ — resolution of the map [m]. Default 0.1 m. Smaller is finer and slower.\n  _marginAbove_ / _marginBelow_ / _marginSides_ — how far past the opening the studied space extends [m]. Defaults 0.5, 0.0, 0.3 m.\n  _wantedSolarPenalty_ — how important preserving wanted solar is, relative to blocking unwanted solar. DIMENSIONLESS. 1.0 = equal importance; 2.0 strongly protects wanted/winter solar; 0.5 prioritises blocking unwanted solar. It changes where the map turns from red to blue, not any measured energy. Default 1.0.\n  _previewMode_ — Points (default), Voxels or None. Display only; the numbers never change. Large maps fall back to points automatically.\n  _gridSize_ — the analysis grid size [m]. Keep it the same as the one used for the targets. Default 0.5 m.\n  _sunAngleStep_ — how finely similar sun positions are grouped [°]. Default 2°.\n  _recalculate_ — true forces the solar calculation to be redone even when it could be reused.\n  _run — nothing happens until this is true.\n\nOUTPUTS\n  shadingPotentialField — the map itself. Feed it to IdealShadingShape and RationaliseShading.\n  legend — what the colours mean and the two totals, as text to panel beside the map.\n  previewPoints / previewColours — the coloured map, if you want to drive your own display. RED where shading blocks unwanted solar, BLUE where it would destroy wanted solar, and near-zero locations are left out.\n  positiveShadingPotential — sum of the positive location values. NOT an energy saving; no unit is given, because one ray is counted at every location along its path.\n  negativeShadingPotential — sum of the negative location values, as a positive magnitude. Same caution as positiveShadingPotential.\n  maxScore / minScore — best and worst single location [kWh].\n  voxelCount — number of points in the map.\n  reusedPreviousCalculation — true when no ray casting was needed.\n  successful — true when a map was produced.\n\nNOTES\nWith no periods and no strategy, the default brief is summer unwanted, winter wanted, flipped automatically in the southern hemisphere. State your own periods for any real study.\nThe map answers for each location INDEPENDENTLY — 'how useful is material HERE'. Filling every red point is therefore not the best possible device, which is why RationaliseShading optimises against energy rather than copying the shape.\nDirect beam only. Diffuse sky is not part of shading desirability.\n\nEXAMPLE\nApertureSolarTargets → ShadingPotentialField (summer unwanted, winter wanted) → IdealShadingShape → RationaliseShading.",
              "SAM", "Solar")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooAnalyticalModelParam() { Name = "_analyticalModel", NickName = "_analyticalModel", Description = "SAM Analytical Model", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooApertureSolarTargetParam() { Name = "_apertureSolarTarget", NickName = "_apertureSolarTarget", Description = "The window to study, from SAMAnalytical.ApertureSolarTargets", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_weatherData_", NickName = "_weatherData_", Description = "SAM WeatherData (hourly).\nSupplied weather wins; otherwise the weather attached to the model", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_unwantedPeriod_", NickName = "_unwantedPeriod_", Description = "Hours whose solar should be blocked, from SAMAnalytical.AnalysisPeriod.\nDefault: summer", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_wantedPeriod_", NickName = "_wantedPeriod_", Description = "Hours whose solar should be kept, from SAMAnalytical.AnalysisPeriod.\nDefault: winter", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_desirability_", NickName = "_desirability_", Description = "A full desirability strategy. When supplied it overrides _unwantedPeriod_ and _wantedPeriod_", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));

                result.Add(new GH_SAMParam(Number("_maxDepth_", "How far out from the glass to look [m].\nDefault 1.0 m", 1.0), ParamVisibility.Binding));
                result.Add(new GH_SAMParam(Number("_voxelSize_", "Resolution of the map [m].\nDefault 0.1 m", 0.1), ParamVisibility.Binding));
                result.Add(new GH_SAMParam(Number("_marginAbove_", "How far above the opening the studied space extends [m].\nDefault 0.5 m", 0.5), ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(Number("_marginBelow_", "How far below the opening the studied space extends [m].\nDefault 0.0 m", 0.0), ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(Number("_marginSides_", "How far either side of the opening the studied space extends [m].\nDefault 0.3 m", 0.3), ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(Number("_wantedSolarPenalty_",
                    "How many kWh of unwanted solar blocked are worth one kWh of wanted solar lost. DIMENSIONLESS WEIGHT.\nHere it decides only how the map is SCORED and coloured: potential = unwanted − penalty × wanted.\n\n"
                    + "ALLOWED: any finite number from 0 upwards. There is no upper limit.\n"
                    + "NORMAL: 0.5 to 2.0.\n"
                    + "DEFAULT: 1.0.\n\n"
                    + "RAISE IT and the BLUE keep-open region grows: more of the space in front of the window is judged too valuable to fill.\n"
                    + "NEGATIVE IS REFUSED: it would colour the keep-open region as though shading it were a gain.\n\n"
                    + "Use the SAME value on RationaliseShading, or the map and the device are answering different questions.", 1.0), ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_String previewMode = new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "_previewMode_", NickName = "_previewMode_", Description = "How the map is drawn: Points (default, cheap), Voxels (shaded cells, for smaller maps), None.\nThe numbers are the same whichever you choose", Access = GH_ParamAccess.item, Optional = true };
                previewMode.SetPersistentData("Points");
                result.Add(new GH_SAMParam(previewMode, ParamVisibility.Voluntary));

                result.Add(new GH_SAMParam(Number("_gridSize_", "The analysis grid size [m]. Keep it the same as the targets.\nDefault 0.5 m", 0.5), ParamVisibility.Binding));
                result.Add(new GH_SAMParam(Number("_sunAngleStep_", "How finely similar sun positions are grouped [°].\nDefault 2°", 2.0), ParamVisibility.Voluntary));

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
                result.Add(new GH_SAMParam(new GooShadingPotentialFieldParam() { Name = "shadingPotentialField", NickName = "shadingPotentialField", Description = "Where shading material would help, and where it would harm", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "legend", NickName = "legend", Description = "What the colours mean, the two totals, and what those totals are NOT. Panel it next to the map", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Point() { Name = "previewPoints", NickName = "previewPoints", Description = "Points of the coloured map (near-zero locations omitted)", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Colour() { Name = "previewColours", NickName = "previewColours", Description = "Colour per preview point.\nRED = SHADE HERE, it blocks solar you asked to block. BLUE = KEEP OPEN, material here would block solar you asked to keep", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "positiveShadingPotential", NickName = "positiveShadingPotential", Description = "Sum of the positive location values — the size of the SHADE-HERE opportunity.\n\nNOT AN ENERGY SAVING, and no unit is given because it is not kWh of anything. One solar ray passes through many locations and is counted at every one, so this total counts the same solar repeatedly. Use it to rank locations and compare thresholds. For what a device actually saves, read VerifyShading", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "negativeShadingPotential", NickName = "negativeShadingPotential", Description = "Sum of the negative location values, as a positive magnitude — the size of the KEEP-OPEN risk.\n\nSame caution as positiveShadingPotential: a cumulative spatial total, not wanted solar any device would destroy", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "maxScore", NickName = "maxScore", Description = "Value of the single best location [kWh].\nA per-location value IS an energy: the beam a piece of material there would intercept over the year", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "minScore", NickName = "minScore", Description = "Value of the single worst location [kWh]. Negative: wanted solar that material there would destroy", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "voxelCount", NickName = "voxelCount", Description = "Number of candidate locations in the map", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "reusedPreviousCalculation", NickName = "reusedPreviousCalculation", Description = "True when no ray casting was needed", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "successful", NickName = "successful", Description = "Successful?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                return result.ToArray();
            }
        }

        private static global::Grasshopper.Kernel.Parameters.Param_Number Number(string name, string description, double defaultValue)
        {
            global::Grasshopper.Kernel.Parameters.Param_Number result = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = name, NickName = name, Description = description, Access = GH_ParamAccess.item };
            result.SetPersistentData(defaultValue);
            return result;
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

        private T Object<T>(IGH_DataAccess dataAccess, string name, out bool supplied, out bool wrongType) where T : class
        {
            supplied = false;
            wrongType = false;

            int index = Params.IndexOfInputParam(name);
            if (index == -1)
            {
                return null;
            }

            GH_ObjectWrapper objectWrapper = null;
            if (!dataAccess.GetData(index, ref objectWrapper) || objectWrapper?.Value == null)
            {
                return null;
            }

            supplied = true;
            T result = Query.Value<T>(objectWrapper);
            wrongType = result == null;
            return result;
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

            index = Params.IndexOfInputParam("_apertureSolarTarget");
            ApertureSolarTarget inputTarget = null;
            if (index == -1 || !dataAccess.GetData(index, ref inputTarget) || inputTarget == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply an aperture solar target from SAMAnalytical.ApertureSolarTargets.");
                return;
            }

            WeatherData weatherData = Object<WeatherData>(dataAccess, "_weatherData_", out bool weatherSupplied, out bool weatherWrongType);
            if (weatherWrongType)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_weatherData_ is not SAM WeatherData. Supply hourly weather from the SAM Weather nodes, or leave it empty to use the weather attached to the model.");
                return;
            }

            if (!weatherSupplied && analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData) == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "WeatherData is required. Supply WeatherData or attach it to the AnalyticalModel.");
                return;
            }

            AnalysisPeriod unwantedPeriod = Object<AnalysisPeriod>(dataAccess, "_unwantedPeriod_", out bool unwantedSupplied, out bool unwantedWrongType);
            AnalysisPeriod wantedPeriod = Object<AnalysisPeriod>(dataAccess, "_wantedPeriod_", out bool wantedSupplied, out bool wantedWrongType);
            if (unwantedWrongType || wantedWrongType)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The unwanted/wanted period must be an AnalysisPeriod. Use SAMAnalytical.AnalysisPeriod to build one.");
                return;
            }

            IDesirabilityStrategy desirabilityStrategy = Object<IDesirabilityStrategy>(dataAccess, "_desirability_", out bool strategySupplied, out bool strategyWrongType);
            if (strategyWrongType)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_desirability_ is not a desirability strategy.");
                return;
            }

            if (strategySupplied && (unwantedSupplied || wantedSupplied))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "_desirability_ overrides the connected unwanted/wanted periods.");
            }
            else if (!strategySupplied && !unwantedSupplied && !wantedSupplied)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No brief supplied: using summer solar unwanted and winter solar wanted.");
            }

            double gridSize = Number(dataAccess, "_gridSize_", 0.5);
            double sunAngleStep = Number(dataAccess, "_sunAngleStep_", 2.0);
            double maxDepth = Number(dataAccess, "_maxDepth_", 1.0);
            double voxelSize = Number(dataAccess, "_voxelSize_", 0.1);
            double marginAbove = Number(dataAccess, "_marginAbove_", 0.5);
            double marginBelow = Number(dataAccess, "_marginBelow_", 0.0);
            double marginSides = Number(dataAccess, "_marginSides_", 0.3);
            double wantedSolarPenalty = Number(dataAccess, "_wantedSolarPenalty_", 1.0);

            // Zero, negative, NaN — and the case that used to fail silently: a grid so fine that
            // every sample cell falls below the geometry area tolerance and NOTHING can be built.
            if (SolarQuery.GridSizeValidity(gridSize, out string gridSizeMessage) != GridSizeValidity.Valid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, gridSizeMessage);
                return;
            }

            if (sunAngleStep <= 0 || double.IsNaN(sunAngleStep))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_sunAngleStep_ [°] must be greater than zero. It is how finely similar sun positions are grouped; the default is 2°.");
                return;
            }

            if (maxDepth <= 0 || voxelSize <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_maxDepth_ and _voxelSize_ must both be greater than zero, in metres.");
                return;
            }

            if (marginAbove < 0 || marginBelow < 0 || marginSides < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The margins must be zero or greater, in metres.");
                return;
            }

            // Here the penalty only decides how the map is SCORED and coloured — Score(v) =
            // unwanted(v) - penalty x wanted(v) — but the sign argument is the same one the
            // optimiser makes: negative would turn the wanted-solar term into a reward, so the blue
            // "keep open" region would colour red and the map would recommend the opposite.
            if (ShadingObjective.Validity(wantedSolarPenalty) == PenaltyValidity.Invalid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Format(
                    "_wantedSolarPenalty_ must be a finite number of zero or more (it is {0}). It is a dimensionless weight: 1.0 trades one kWh of wanted solar for one kWh of unwanted solar blocked. A negative value would colour the keep-open region as though shading it were a gain.",
                    wantedSolarPenalty));
                return;
            }

            bool recalculate = false;
            index = Params.IndexOfInputParam("_recalculate_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref recalculate);
            }

            int year = unwantedPeriod?.Year ?? wantedPeriod?.Year ?? SAMAnalyticalApertureIrradiance.DefaultYear(analyticalModel, weatherData);

            ApertureShadingSetup setup = SolarCreate.ApertureShadingSetup(
                analyticalModel, inputTarget.ApertureGuid, year, out string setupMessage, weatherData, desirabilityStrategy,
                unwantedPeriod, wantedPeriod, null, gridSize, sunAngleStep, recalculate);

            if (setup == null)
            {
                // The builder says WHICH cause it was - an unusable grid size, an opening below the
                // area tolerance, an aperture that is not this model's, a site with no time zone -
                // because they are fixed in completely different places.
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, setupMessage ?? "This aperture could not be prepared for shading analysis.");
                return;
            }

            if (!setup.ReusedPreviousCalculation && !recalculate)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Geometry changed; solar calculation was updated.");
            }

            ShadingVolume volume = SolarCreate.ShadingVolume(setup.Target, maxDepth, marginAbove, marginBelow, marginSides, marginSides, voxelSize);
            if (volume == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The studied space could not be built. Check _maxDepth_, _voxelSize_ and the margins.");
                return;
            }

            ShadingPotentialField field = SolarCreate.ShadingPotentialField(setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability, volume, setup.CellIndexOffset);
            if (field == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No shading potential map could be produced for this aperture.");
                return;
            }

            double positiveTotal = field.PositiveTotal(wantedSolarPenalty);
            if (positiveTotal <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No unwanted solar reaches this aperture over the period requested: nothing here is worth shading.");
            }

            ShadingFieldPreviewMode previewMode = ShadingFieldPreviewMode.Points;
            index = Params.IndexOfInputParam("_previewMode_");
            if (index != -1)
            {
                string previewModeText = null;
                if (dataAccess.GetData(index, ref previewModeText) && !string.IsNullOrWhiteSpace(previewModeText)
                    && !Enum.TryParse(previewModeText.Trim(), true, out previewMode))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Format("'{0}' is not a preview mode. Use Points, Voxels or None. Showing points.", previewModeText));
                    previewMode = ShadingFieldPreviewMode.Points;
                }
            }

            index = Params.IndexOfOutputParam("shadingPotentialField");
            if (index != -1)
            {
                dataAccess.SetData(index, new GooShadingPotentialField(field, previewMode, wantedSolarPenalty));
            }

            index = Params.IndexOfOutputParam("legend");
            if (index != -1)
            {
                dataAccess.SetData(index, GooShadingPotentialField.Legend(field, wantedSolarPenalty));
            }

            // The numbers are already final; a preview problem must never take them down with it.
            try
            {
                List<Tuple<Point3d, System.Drawing.Color>> previewPoints = GooShadingPotentialField.PreviewPoints(field, wantedSolarPenalty);

                index = Params.IndexOfOutputParam("previewPoints");
                if (index != -1)
                {
                    dataAccess.SetDataList(index, previewPoints.ConvertAll(x => x.Item1));
                }

                index = Params.IndexOfOutputParam("previewColours");
                if (index != -1)
                {
                    dataAccess.SetDataList(index, previewPoints.ConvertAll(x => x.Item2));
                }
            }
            catch (Exception exception)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The preview could not be drawn: " + exception.Message + ". The numerical results are unaffected.");
            }

            index = Params.IndexOfOutputParam("positiveShadingPotential");
            if (index != -1)
            {
                dataAccess.SetData(index, positiveTotal);
            }

            index = Params.IndexOfOutputParam("negativeShadingPotential");
            if (index != -1)
            {
                // As a POSITIVE magnitude: "negative shading potential 22.2" reads as a size, where
                // "-22.2" invited being added to the positive total and cancelling part of it.
                dataAccess.SetData(index, Math.Abs(field.NegativeTotal(wantedSolarPenalty)));
            }

            index = Params.IndexOfOutputParam("maxScore");
            if (index != -1)
            {
                dataAccess.SetData(index, field.MaxScore(wantedSolarPenalty));
            }

            index = Params.IndexOfOutputParam("minScore");
            if (index != -1)
            {
                dataAccess.SetData(index, field.MinScore(wantedSolarPenalty));
            }

            index = Params.IndexOfOutputParam("voxelCount");
            if (index != -1)
            {
                dataAccess.SetData(index, volume.VoxelCount);
            }

            index = Params.IndexOfOutputParam("reusedPreviousCalculation");
            if (index != -1)
            {
                dataAccess.SetData(index, setup.ReusedPreviousCalculation);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, true);
            }
        }
    }
}


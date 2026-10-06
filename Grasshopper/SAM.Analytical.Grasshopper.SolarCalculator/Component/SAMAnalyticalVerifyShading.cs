// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
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
    public class SAMAnalyticalVerifyShading : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7f3c9a10-5b28-4e63-9a41-6c0d2e7b1107");

        /// <summary>
        /// The latest version of this component.
        ///
        /// 1.0.1 — the device is now checked against the aperture it was designed for and refused
        /// when they disagree, instead of being measured on whatever target happened to be wired in.
        /// The null device ("build nothing") verifies as a real answer. Added verificationSummary /
        /// status / apertureGuid / azimuth for multi-window runs.
        ///
        /// 1.0.2 — the NEUTRAL residual is exposed. Under the default seasonal brief part of the
        /// admitted beam is in neither the unwanted nor the wanted period, so a window admitting
        /// 61.3 kWh with 46.0 unwanted and 0.0 wanted looked as though 15.3 kWh had gone missing.
        /// admittedNeutralSolar, neutralSolarIntercepted and accountingSummary make both balances
        /// close, and unwantedSolarIntercepted / wantedSolarBlocked are now on their own wires.
        ///
        /// 1.1.0 — the SHADING SCHEME path. Connect a complete ShadingScheme (from
        /// SAMAnalytical.AssembleShadingSchemes) to _shadingDevice together with its targets on the
        /// new _apertureSolarTargets_ input, and the scheme is verified as a WHOLE: every aperture
        /// is measured against the complete scheme, so an overhang over one window that also shades
        /// its neighbour is credited on the neighbour. The existing single-aperture behaviour is
        /// unchanged.
        /// </summary>
        public override string LatestComponentVersion => "1.1.0";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        public SAMAnalyticalVerifyShading()
          : base("SAMAnalytical.VerifyShading", "SAMAnalytical.VerifyShading",
              "SUMMARY\nMeasures what a shading device actually does to one window: the direct solar the window admits without it, how much of that the device stops, and how the stopped energy splits between the solar you wanted blocked and the solar you wanted kept.\n\nThis is the ground-truth node. Every candidate is built as real geometry and traced against the real sun path and the real surroundings — nothing here is a rule of thumb or a profile-angle estimate.\n\nINPUTS\n  _analyticalModel — the SAM Analytical Model.\n  _apertureSolarTarget — the window, from ApertureSolarTargets.\n  _shadingDevice — the device to test: a device from RationaliseShading, a result from it, or a whole shading scheme from AssembleShadingSchemes or SelectShadingScheme. Devices from RationaliseShading remember which window they were designed for, and verifying one against a DIFFERENT window is refused rather than measured — a correct measurement of the wrong design is the worst answer this node could give.\n  _apertureSolarTargets_ — the scheme's windows, from ApertureSolarTargets. Required only when _shadingDevice carries a scheme: a scheme covers several windows and is verified against all of them at once.\n  _weatherData_ / _unwantedPeriod_ / _wantedPeriod_ / _desirability_ — the brief. Use the SAME brief that produced the device, or the percentages will not compare.\n  _gridSize_ / _sunAngleStep_ / _recalculate_ — as on ApertureIrradiance.\n  _run — nothing happens until this is true.\n\nOUTPUTS\n  shadingPerformance — the full result object.\n  verificationSummary — the measured answer in one line, e.g. '180° | Overhang | 742 kWh baseline | 618 kWh intercepted | 83.2% unwanted blocked | 95.6% wanted retained'.\n  status — OK / NO SHADE / WARNING.\n  apertureGuid / azimuth — which window this result is about.\n  baselineDirectSolar — direct solar the window admits with the surroundings in place and NO device [kWh]. This is the denominator of the efficiency.\n  directSolarIntercepted — of that, how much the device stops [kWh].\n  directShadingEfficiency — intercepted / baseline [%].\n  unwantedSolarBlocked — [%] of the unwanted solar the window would have admitted.\n  wantedSolarRetained — [%] of the wanted solar that still gets through.\n  admittedUnwantedSolar / admittedWantedSolar — the unshaded totals behind those percentages [kWh].\n  elementNames / elementGuids / elementEnergy — which part of the device stops what [kWh], credited to the element the sun reaches FIRST, so overlapping parts never double-count.\n  unattributedEnergy — energy stopped by something that is not the device [kWh]. It should be zero; anything else means the model and the device disagree and is shown rather than folded into a total.\n  materialFraction — device area / window area.\n  shadingGeometry — the device as surfaces.\n  reusedPreviousCalculation / successful.\n  schemeResult / perApertureResults — the whole-scheme verification and its per-aperture results, populated when _shadingDevice carries a scheme.\n\nNOTES\nCONTEXT IS NEVER CREDITED TO THE DEVICE. Solar already blocked by another building or by the roof is outside the baseline, so a device placed in permanent shade scores near zero — which is the correct answer.\nUNAVAILABLE IS NOT ZERO. When a percentage has no denominator — no unwanted solar in the brief, no wanted solar, nothing admitted at all — it is reported as NaN, never as 0 % or 100 %.\nDirect beam only. Diffuse sky and ground-reflected solar are not part of these figures, and no reflection off the device is modelled: intercepted solar is stopped, not redirected.\nElement spacing finer than the analysis grid cannot be resolved; the node warns when a device is close to that limit.\n\nEXAMPLE\nOne window: RationaliseShading.shadingDevice → VerifyShading._shadingDevice, with the window's own ApertureSolarTargets target on _apertureSolarTarget — then compare directShadingEfficiency and wantedSolarRetained between two devices under the same brief.\nA whole scheme: AssembleShadingSchemes.shadingSchemes or SelectShadingScheme.selectedScheme → VerifyShading._shadingDevice, with ApertureSolarTargets.apertureSolarTargets → VerifyShading._apertureSolarTargets_; read schemeResult and perApertureResults.",
              "SAM", "Solar")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooAnalyticalModelParam() { Name = "_analyticalModel", NickName = "_analyticalModel", Description = "SAM Analytical Model", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooApertureSolarTargetParam() { Name = "_apertureSolarTarget", NickName = "_apertureSolarTarget", Description = "The window, from SAMAnalytical.ApertureSolarTargets", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_shadingDevice", NickName = "_shadingDevice", Description = "The device to test: a shading device or an optimised shading result from SAMAnalytical.RationaliseShading", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_weatherData_", NickName = "_weatherData_", Description = "SAM WeatherData (hourly).\nSupplied weather wins; otherwise the weather attached to the model", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_unwantedPeriod_", NickName = "_unwantedPeriod_", Description = "Hours whose solar should be blocked.\nDefault: summer", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_wantedPeriod_", NickName = "_wantedPeriod_", Description = "Hours whose solar should be kept.\nDefault: winter", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_desirability_", NickName = "_desirability_", Description = "A full desirability strategy. When supplied it overrides the two periods", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number gridSize = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_gridSize_", NickName = "_gridSize_", Description = "The analysis grid size [m]: the SPACING between analysis sample points. Keep it the same as the targets.\n\nALLOWED: greater than zero, and not finer than about 0.032 m - below that a sample cell is smaller than the geometry area tolerance and NO samples can be produced at all.\nDefault 0.5 m", Access = GH_ParamAccess.item };
                gridSize.SetPersistentData(0.5);
                result.Add(new GH_SAMParam(gridSize, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number sunAngleStep = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_sunAngleStep_", NickName = "_sunAngleStep_", Description = "How finely similar sun positions are grouped [°].\nDefault 2°", Access = GH_ParamAccess.item };
                sunAngleStep.SetPersistentData(2.0);
                result.Add(new GH_SAMParam(sunAngleStep, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean recalculate = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_recalculate_", NickName = "_recalculate_", Description = "Force the solar calculation to be redone even when it could be reused.\nDefault false", Access = GH_ParamAccess.item };
                recalculate.SetPersistentData(false);
                result.Add(new GH_SAMParam(recalculate, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean run = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_run", NickName = "_run", Description = "Nothing is calculated until this is true", Access = GH_ParamAccess.item };
                run.SetPersistentData(false);
                result.Add(new GH_SAMParam(run, ParamVisibility.Binding));

                // Appended AFTER every pre-existing input so saved definitions keep their positional
                // parameter indices (the legacy parameter reader restores parameters by position).
                result.Add(new GH_SAMParam(new GooApertureSolarTargetParam() { Name = "_apertureSolarTargets_", NickName = "_apertureSolarTargets_", Description = "The scheme's windows. Required only when _shadingDevice carries a ShadingScheme - a scheme covers several windows and is verified against all of them at once", Access = GH_ParamAccess.list, Optional = true }, ParamVisibility.Voluntary));

                return result.ToArray();
            }
        }

        protected override GH_SAMParam[] Outputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "shadingPerformance", NickName = "shadingPerformance", Description = "The measured performance of the device on this window", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "verificationSummary", NickName = "verificationSummary", Description = "The measured answer in one line: baseline, intercepted, and the two percentages the design is judged on", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "status", NickName = "status", Description = "OK / NO SHADE / WARNING / NOT EVALUATED. Readable across many apertures at once", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "apertureGuid", NickName = "apertureGuid", Description = "The aperture this result belongs to", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "azimuth", NickName = "azimuth", Description = "Compass direction the window faces [°]", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "baselineDirectSolar", NickName = "baselineDirectSolar", Description = "Direct solar admitted with the surroundings in place and no device [kWh]", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "directSolarIntercepted", NickName = "directSolarIntercepted", Description = "Of that, how much the device stops [kWh]", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "directShadingEfficiency", NickName = "directShadingEfficiency", Description = "Intercepted / baseline [%]. NaN when nothing is admitted", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "unwantedSolarBlocked", NickName = "unwantedSolarBlocked", Description = "Unwanted solar blocked [%]. NaN when there is no unwanted solar", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "wantedSolarRetained", NickName = "wantedSolarRetained", Description = "Wanted solar retained [%]. NaN when there is no wanted solar", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "admittedUnwantedSolar", NickName = "admittedUnwantedSolar", Description = "Unwanted direct solar admitted without the device [kWh]", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "admittedWantedSolar", NickName = "admittedWantedSolar", Description = "Wanted direct solar admitted without the device [kWh]", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "admittedNeutralSolar", NickName = "admittedNeutralSolar", Description = "Direct solar admitted that the brief claimed NEITHER way [kWh] — under the default brief, the spring and autumn beam that is in neither the unwanted nor the wanted period.\n\nThis is what makes the numbers add up:\nbaselineDirectSolar = admittedUnwantedSolar + admittedWantedSolar + admittedNeutralSolar", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "unwantedSolarIntercepted", NickName = "unwantedSolarIntercepted", Description = "The part of what the device stops that you asked to block [kWh]", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "wantedSolarBlocked", NickName = "wantedSolarBlocked", Description = "The part of what the device stops that you asked to keep [kWh] — the price of the design", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "neutralSolarIntercepted", NickName = "neutralSolarIntercepted", Description = "The part of what the device stops that the brief claimed neither way [kWh].\n\ndirectSolarIntercepted = unwantedSolarIntercepted + wantedSolarBlocked + neutralSolarIntercepted", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "accountingSummary", NickName = "accountingSummary", Description = "The two energy balances written out, so the headline numbers can be reconciled by eye:\n'Admitted without the device: 61.3 kWh = 46 unwanted + 0 wanted + 15.3 neither'", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "elementNames", NickName = "elementNames", Description = "Name of each part of the device", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "elementGuids", NickName = "elementGuids", Description = "Identity of each part of the device", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "elementEnergy", NickName = "elementEnergy", Description = "Direct solar stopped by each part [kWh], credited to the part the sun reaches first", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "unattributedEnergy", NickName = "unattributedEnergy", Description = "Energy stopped by something that is not the device [kWh]. Should be zero", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "materialFraction", NickName = "materialFraction", Description = "Device area / window area", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Brep() { Name = "shadingGeometry", NickName = "shadingGeometry", Description = "The device as surfaces", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "reusedPreviousCalculation", NickName = "reusedPreviousCalculation", Description = "True when no ray casting was needed to set up", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "successful", NickName = "successful", Description = "Successful?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                // Appended AFTER every pre-existing output so saved definitions keep their positional
                // parameter indices (the legacy parameter reader restores parameters by position).
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "schemeResult", NickName = "schemeResult", Description = "The complete scheme verification, when _shadingDevice carries a ShadingScheme", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "perApertureResults", NickName = "perApertureResults", Description = "The scheme verification per aperture, when _shadingDevice carries a ShadingScheme", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                return result.ToArray();
            }
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

            // ---- the device, and WHICH APERTURE IT WAS DESIGNED FOR.
            //
            // A bare IShadingTypology is a rule, not a design: it will happily be built against any
            // target and measured correctly, which is precisely how a ten-window script produces a
            // number that is arithmetically right and about the wrong facade. Whenever the wire
            // carries an identity, it is checked here and a disagreement is an error, not a shrug.
            IShadingTypology typology = null;
            Guid deviceApertureGuid = Guid.Empty;
            bool identityCarried = false;
            ShadingScheme scheme = null;

            index = Params.IndexOfInputParam("_shadingDevice");
            if (index != -1)
            {
                GH_ObjectWrapper objectWrapper = null;
                if (dataAccess.GetData(index, ref objectWrapper) && objectWrapper?.Value != null)
                {
                    object @object = Query.Unwrap(objectWrapper);

                    if (@object is ShadingScheme shadingScheme)
                    {
                        scheme = shadingScheme;
                    }
                    else if (@object is ShadingDevice shadingDevice)
                    {
                        typology = shadingDevice.Typology;
                        deviceApertureGuid = shadingDevice.ApertureGuid;
                        identityCarried = true;
                    }
                    else if (@object is OptimisedShadingResult optimisedShadingResult)
                    {
                        typology = optimisedShadingResult.Typology();
                        deviceApertureGuid = optimisedShadingResult.ApertureGuid;
                        identityCarried = true;
                    }
                    else
                    {
                        typology = @object as IShadingTypology;
                    }
                }
            }

            if (scheme != null)
            {
                SolveScheme(dataAccess, analyticalModel, scheme);
                return;
            }

            index = Params.IndexOfInputParam("_apertureSolarTarget");
            ApertureSolarTarget inputTarget = null;
            if (index == -1 || !dataAccess.GetData(index, ref inputTarget) || inputTarget == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply an aperture solar target from SAMAnalytical.ApertureSolarTargets.");
                return;
            }

            if (typology == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply a shading device, an optimised shading result, or a shading scheme from SAMAnalytical.RationaliseShading or SAMAnalytical.AssembleShadingSchemes.");
                return;
            }

            if (identityCarried && deviceApertureGuid != Guid.Empty && deviceApertureGuid != inputTarget.ApertureGuid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Format(
                    "This device was designed for aperture {0}, but it is being verified against aperture {1}. The result would be a correct measurement of the wrong design. Match the device to its own target — with several windows, wire _apertureSolarTarget_ and _shadingDevice_ straight through from the same RationaliseShading branch rather than reordering or flattening either one.",
                    deviceApertureGuid, inputTarget.ApertureGuid));
                return;
            }

            if (!identityCarried)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "This device carries no aperture identity, so it is being taken on trust as belonging to the connected window. Devices from SAMAnalytical.RationaliseShading carry theirs and are checked.");
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

            ShadingResolutionState resolutionState = SolarQuery.ShadingResolution(typology, setup.Target, gridSize, out string resolutionMessage, out double _);
            if (resolutionState == ShadingResolutionState.BelowResolutionLimit)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, resolutionMessage);
            }
            else if (resolutionState == ShadingResolutionState.NearResolutionLimit)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, resolutionMessage);
            }

            ShadingPerformance performance = SolarCreate.ShadingPerformance(setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability, setup.Context.ContextOccluders, typology, setup.CellIndexOffset);
            if (performance == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The device could not be measured on this window: the aperture, the solar calculation and the device geometry do not describe the same analysis points. Check that _gridSize_ is the same value used for the targets, that the target came from THIS model, and that the device produces geometry in front of the opening.");
                return;
            }

            bool noShading = typology is NoShading;
            if (noShading)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "This is the null device: the recommendation was to leave this window unshaded. The figures below are that answer measured, not a failed run — nothing is intercepted, so none of the unwanted solar is blocked and all of the wanted solar is kept.");
            }

            if (Math.Abs(performance.UnattributedInterceptedEnergy) > 1e-9)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Format("{0:0.###} kWh was stopped by something that is not the device. The unshaded baseline and the traced geometry disagree; treat the totals with care.", performance.UnattributedInterceptedEnergy));
            }

            if (double.IsNaN(performance.UnwantedSolarBlocked))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No unwanted solar reaches this window over the hours given, so 'unwanted solar blocked' has no denominator and is reported as unavailable rather than as a percentage.");
            }

            List<Guid> elementGuids = new List<Guid>();
            List<string> elementNames = new List<string>();
            List<double> elementEnergy = new List<double>();

            List<ShadingElement> elements = typology.ShadingElements(setup.Target);
            Dictionary<Guid, double> energyPerElement = performance.EnergyPerElement;
            foreach (ShadingElement element in elements ?? new List<ShadingElement>())
            {
                if (element == null)
                {
                    continue;
                }

                elementGuids.Add(element.Guid);
                elementNames.Add(element.Name);
                elementEnergy.Add(energyPerElement.TryGetValue(element.Guid, out double energy) ? energy : double.NaN);
            }

            index = Params.IndexOfOutputParam("shadingPerformance");
            if (index != -1)
            {
                dataAccess.SetData(index, new GooSAMObject(performance));
            }

            index = Params.IndexOfOutputParam("verificationSummary");
            if (index != -1)
            {
                dataAccess.SetData(index, SolarQuery.VerificationSummary(performance, setup.Target.Azimuth));
            }

            index = Params.IndexOfOutputParam("status");
            if (index != -1)
            {
                ShadingDesignStatus status;
                if (noShading)
                {
                    status = ShadingDesignStatus.NoShading;
                }
                else if (resolutionState != ShadingResolutionState.Resolved || Math.Abs(performance.UnattributedInterceptedEnergy) > 1e-9)
                {
                    status = ShadingDesignStatus.Warning;
                }
                else
                {
                    status = ShadingDesignStatus.Ok;
                }

                dataAccess.SetData(index, SolarQuery.StatusText(status));
            }

            index = Params.IndexOfOutputParam("apertureGuid");
            if (index != -1)
            {
                dataAccess.SetData(index, setup.Target.ApertureGuid.ToString());
            }

            index = Params.IndexOfOutputParam("azimuth");
            if (index != -1)
            {
                dataAccess.SetData(index, setup.Target.Azimuth);
            }

            SetNumber(dataAccess, "baselineDirectSolar", performance.AdmittedDirectEnergy);
            SetNumber(dataAccess, "directSolarIntercepted", performance.DirectSolarIntercepted);
            SetNumber(dataAccess, "directShadingEfficiency", Query.Percentage(performance.DirectShadingEfficiency));
            SetNumber(dataAccess, "unwantedSolarBlocked", Query.Percentage(performance.UnwantedSolarBlocked));
            SetNumber(dataAccess, "wantedSolarRetained", Query.Percentage(performance.WantedSolarRetained));
            SetNumber(dataAccess, "admittedUnwantedSolar", performance.AdmittedUnwantedEnergy);
            SetNumber(dataAccess, "admittedWantedSolar", performance.AdmittedWantedEnergy);
            SetNumber(dataAccess, "admittedNeutralSolar", performance.AdmittedNeutralEnergy);
            SetNumber(dataAccess, "unwantedSolarIntercepted", performance.UnwantedSolarIntercepted);
            SetNumber(dataAccess, "wantedSolarBlocked", performance.WantedSolarBlocked);
            SetNumber(dataAccess, "neutralSolarIntercepted", performance.NeutralSolarIntercepted);
            SetNumber(dataAccess, "unattributedEnergy", performance.UnattributedInterceptedEnergy);

            index = Params.IndexOfOutputParam("accountingSummary");
            if (index != -1)
            {
                dataAccess.SetData(index, SolarQuery.AccountingSummary(performance));
            }
            SetNumber(dataAccess, "materialFraction", performance.MaterialFraction);

            index = Params.IndexOfOutputParam("elementNames");
            if (index != -1)
            {
                dataAccess.SetDataList(index, elementNames);
            }

            index = Params.IndexOfOutputParam("elementGuids");
            if (index != -1)
            {
                dataAccess.SetDataList(index, elementGuids.ConvertAll(x => x.ToString()));
            }

            index = Params.IndexOfOutputParam("elementEnergy");
            if (index != -1)
            {
                dataAccess.SetDataList(index, elementEnergy);
            }

            index = Params.IndexOfOutputParam("shadingGeometry");
            if (index != -1)
            {
                dataAccess.SetDataList(index, SAMAnalyticalRationaliseShading.Geometry(typology, setup.Target));
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

        /// <summary>
        /// The scheme path: verifies a complete ShadingScheme as a WHOLE — every aperture measured
        /// against every scheme element, so cross-shading between one window's device and its
        /// neighbour is credited. The legacy single-aperture path is untouched.
        /// </summary>
        private void SolveScheme(IGH_DataAccess dataAccess, AnalyticalModel analyticalModel, ShadingScheme scheme)
        {
            int index = Params.IndexOfInputParam("_apertureSolarTargets_");
            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget>();
            if (index == -1 || !dataAccess.GetDataList(index, targets) || targets.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A shading scheme covers several windows. Connect the scheme's targets to _apertureSolarTargets_.");
                return;
            }

            WeatherData weatherData = Object<WeatherData>(dataAccess, "_weatherData_", out bool weatherSupplied, out bool weatherWrongType);
            if (weatherWrongType)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_weatherData_ is not SAM WeatherData.");
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

            double gridSize = Number(dataAccess, "_gridSize_", 0.5);
            double sunAngleStep = Number(dataAccess, "_sunAngleStep_", 2.0);
            bool recalculate = false;
            index = Params.IndexOfInputParam("_recalculate_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref recalculate);
            }

            int year = unwantedPeriod?.Year ?? wantedPeriod?.Year ?? SAMAnalyticalApertureIrradiance.DefaultYear(analyticalModel, weatherData);

            VerifiedShadingSchemeResult verified = SolarCreate.VerifiedShadingSchemeResult(
                analyticalModel, scheme, targets, year, out string message,
                weatherData, desirabilityStrategy, unwantedPeriod, wantedPeriod, gridSize, sunAngleStep, recalculate);

            if (verified == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message ?? "The scheme could not be verified.");
                return;
            }

            foreach (string warning in verified.Warnings)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
            }

            ShadingSchemePerformance performance = verified.Performance;

            // The existing single-aperture outputs are populated FROM THE SCHEME TOTALS, with the
            // aperture GUID empty and the status prefixed SCHEME, so a scheme result can never be
            // mistaken for a single-window one.
            ShadingPerformance total = new ShadingPerformance(
                Guid.Empty, scheme.Name,
                performance.AdmittedDirectEnergy, performance.AdmittedUnwantedEnergy, performance.AdmittedWantedEnergy,
                performance.DirectSolarIntercepted, performance.UnwantedSolarIntercepted, performance.WantedSolarBlocked,
                performance.UnattributedInterceptedEnergy, performance.MaterialFraction,
                new Dictionary<Guid, double>(), new Dictionary<Guid, string>());

            index = Params.IndexOfOutputParam("shadingPerformance");
            if (index != -1)
            {
                dataAccess.SetData(index, new GooSAMObject(total));
            }

            index = Params.IndexOfOutputParam("verificationSummary");
            if (index != -1)
            {
                dataAccess.SetData(index, verified.VerificationSummary);
            }

            index = Params.IndexOfOutputParam("status");
            if (index != -1)
            {
                dataAccess.SetData(index, "SCHEME " + SolarQuery.StatusText(verified.Status));
            }

            index = Params.IndexOfOutputParam("apertureGuid");
            if (index != -1)
            {
                dataAccess.SetData(index, string.Empty);
            }

            index = Params.IndexOfOutputParam("azimuth");
            if (index != -1)
            {
                dataAccess.SetData(index, double.NaN);
            }

            SetNumber(dataAccess, "baselineDirectSolar", total.AdmittedDirectEnergy);
            SetNumber(dataAccess, "directSolarIntercepted", total.DirectSolarIntercepted);
            SetNumber(dataAccess, "directShadingEfficiency", Query.Percentage(total.DirectShadingEfficiency));
            SetNumber(dataAccess, "unwantedSolarBlocked", Query.Percentage(total.UnwantedSolarBlocked));
            SetNumber(dataAccess, "wantedSolarRetained", Query.Percentage(total.WantedSolarRetained));
            SetNumber(dataAccess, "admittedUnwantedSolar", total.AdmittedUnwantedEnergy);
            SetNumber(dataAccess, "admittedWantedSolar", total.AdmittedWantedEnergy);
            SetNumber(dataAccess, "admittedNeutralSolar", total.AdmittedNeutralEnergy);
            SetNumber(dataAccess, "unwantedSolarIntercepted", total.UnwantedSolarIntercepted);
            SetNumber(dataAccess, "wantedSolarBlocked", total.WantedSolarBlocked);
            SetNumber(dataAccess, "neutralSolarIntercepted", total.NeutralSolarIntercepted);
            SetNumber(dataAccess, "unattributedEnergy", total.UnattributedInterceptedEnergy);

            index = Params.IndexOfOutputParam("accountingSummary");
            if (index != -1)
            {
                dataAccess.SetData(index, SolarQuery.AccountingSummary(total));
            }

            SetNumber(dataAccess, "materialFraction", total.MaterialFraction);

            // Element-level outputs follow the scheme's own element order.
            List<ShadingElement> elements = scheme.SchemeElements(targets);
            List<Guid> elementGuids = new List<Guid>();
            List<string> elementNames = new List<string>();
            List<double> elementEnergy = new List<double>();
            Dictionary<Guid, double> energyPerElement = performance.EnergyPerElement;
            foreach (ShadingElement element in elements ?? new List<ShadingElement>())
            {
                if (element == null)
                {
                    continue;
                }

                elementGuids.Add(element.Guid);
                elementNames.Add(element.Name);
                elementEnergy.Add(energyPerElement.TryGetValue(element.Guid, out double energy) ? energy : double.NaN);
            }

            index = Params.IndexOfOutputParam("elementNames");
            if (index != -1)
            {
                dataAccess.SetDataList(index, elementNames);
            }

            index = Params.IndexOfOutputParam("elementGuids");
            if (index != -1)
            {
                dataAccess.SetDataList(index, elementGuids.ConvertAll(x => x.ToString()));
            }

            index = Params.IndexOfOutputParam("elementEnergy");
            if (index != -1)
            {
                dataAccess.SetDataList(index, elementEnergy);
            }

            index = Params.IndexOfOutputParam("shadingGeometry");
            if (index != -1)
            {
                List<Rhino.Geometry.Brep> breps = new List<Rhino.Geometry.Brep>();
                foreach (ShadingElement element in elements ?? new List<ShadingElement>())
                {
                    SAM.Geometry.Spatial.Face3D face3D = element?.Face3D;
                    if (face3D != null)
                    {
                        Rhino.Geometry.Brep brep = SAM.Geometry.Rhino.Convert.ToRhino_Brep(face3D);
                        if (brep != null)
                        {
                            breps.Add(brep);
                        }
                    }
                }

                dataAccess.SetDataList(index, breps);
            }

            index = Params.IndexOfOutputParam("reusedPreviousCalculation");
            if (index != -1)
            {
                dataAccess.SetData(index, verified.ReusedPreviousCalculation);
            }

            index = Params.IndexOfOutputParam("schemeResult");
            if (index != -1)
            {
                dataAccess.SetData(index, new GooSAMObject(verified));
            }

            index = Params.IndexOfOutputParam("perApertureResults");
            if (index != -1)
            {
                List<GooSAMObject> perAperture = new List<GooSAMObject>();
                foreach (ShadingPerformance member in performance.PerAperture)
                {
                    perAperture.Add(new GooSAMObject(member));
                }

                dataAccess.SetDataList(index, perAperture);
            }

            index = Params.IndexOfOutputParam("successful");
            if (index != -1)
            {
                dataAccess.SetData(index, true);
            }
        }

        private void SetNumber(IGH_DataAccess dataAccess, string name, double value)
        {
            int index = Params.IndexOfOutputParam(name);
            if (index != -1)
            {
                dataAccess.SetData(index, value);
            }
        }
    }
}


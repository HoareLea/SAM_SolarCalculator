// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
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
    public class SAMAnalyticalRationaliseAwningGroup : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("9a2f5c81-3d47-4b6e-8f1a-2c7e6d4b0a93");

        /// <summary>
        /// The latest version of this component.
        ///
        /// 1.2.0 — each GroupedShadingDevice now RECORDS the grouping criteria it was formed
        /// under (_maximumGap_ and _headTolerance_), so SAMAnalytical.ShadingOperation
        /// re-establishes the exact original physical group instead of re-grouping under the
        /// defaults. Devices created before this version claim the defaults, which reproduces
        /// their previous behaviour exactly.
        /// </summary>
        public override string LatestComponentVersion => "1.2.0";

        /// <summary>Provides an Icon for the component.</summary>
        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        public SAMAnalyticalRationaliseAwningGroup()
          : base("SAMAnalytical.RationaliseAwningGroup", "SAMAnalytical.RationaliseAwningGroup",
              "SUMMARY\nSizes a RETRACTABLE FOLDING-ARM AWNING (the Dakar product preset) for one or more adjacent windows, against energy, not against the look of the ideal shape.\n\nONE PHYSICAL AWNING may span several selected apertures when they form a valid group and the product width permits it; when they do not, the selection is split deterministically into separate independent units - never one modularly joined awning.\n\nThe analysis uses ONE sloping canopy surface plus an optional front valance surface, in the FULLY DEPLOYED position, treated as OPAQUE. Deployment scheduling, automatic retraction, fabric solar transmittance, diffuse transmission, reflection, wind control and structural capacity are NOT modelled. This is solar-analysis geometry, not structural or fabrication design.\n\nINPUTS\n  _analyticalModel - the SAM Analytical Model.\n  _apertureSolarTargets - the windows, from ApertureSolarTargets. Input order does not matter: grouping is deterministic.\n  _productPreset_ - the product whose limits constrain the awning. Default: Dakar.\n  _projection_ - optional horizontal projection [m]. Empty = the analysis selects one of the preset's valid projections for each group width.\n  _tiltDegrees_ - optional manual lock [°], for verification or comparison. Empty (the normal mode) = the analysis selects the tilt between 5° and 40°. The selected value is the fixed installation tilt of the deployed fabric, not an hourly tracking angle.\n  _riseAboveHead_ - how far the back edge sits above the highest head [m]. Default 0.0 m.\n  _extensionBeyondJambs_ - how far the fabric runs past the outermost jambs on each side [m]. Default 0.15 m.\n  _valanceDepth_ - optional front valance depth [m]. Default 0.0 m (no valance); 0.21 m is the Dakar standard valance.\n  _maximumGap_ - largest horizontal gap between adjacent windows that still shares one awning [m]. Default 0.20 m.\n  _headTolerance_ - largest head-level difference within one group [m]. Default 0.02 m.\n  _weatherData_ / _unwantedPeriod_ / _wantedPeriod_ / _desirability_ - the brief, exactly as on RationaliseShading. Use the SAME brief throughout or the numbers will not compare.\n  _wantedSolarPenalty_ / _materialPenalty_ - as on RationaliseShading. DIMENSIONLESS. Defaults 1.0 and 0.1.\n  _gridSize_ / _sunAngleStep_ / _recalculate_ - as on ApertureIrradiance. Grid default 0.5 m, sun-angle step default 2°.\n  _maximumEvaluations_ - maximum awning designs evaluated per group. Default 400.\n  _run - nothing happens until this is true.\n  _mountingOffset_ - horizontal outward distance from the aperture plane to the awning mounting line [m]. Use this when the glazing is recessed and the awning is mounted on the external facade or soffit. Default 0.0 m.\n\nOUTPUTS\n  groupedShadingDevices - one device per group, carrying the deterministic group identity, the awning parameters and the product preset. The null device means NO SHADE and can be measured like any other.\n  shadingGeometry - the recommended awning surfaces per group: one canopy face, plus one valance face when the valance depth is not zero.\n  groupGuids / panelGuids - the deterministic group identity and its host wall, per group.\n  apertureGuids - the windows of each group, one branch per group, ordered left-to-right.\n  widths - awning width per group [m]: combined aperture envelope plus both side extensions.\n  projections - horizontal projection selected per group [m].\n  tiltDegrees - the tilt selected by the analysis per group [°], within the 5°-40° product range.\n  valanceDepths - valance depth per group [m].\n  requiredWallBrackets - wall brackets per group: 2 for widths up to and including 4.1 m, 3 above.\n  groupPerformances - the measured GROUP performance: energies summed over the member windows, percentages from the summed totals, one shared material charge.\n  performanceByAperture - the measured performance of each window within its group, each keeping its own aperture GUID.\n  designSummaries - the engineering answer per group in one line.\n  status - OK / NO SHADE / WARNING / NOT EVALUATED, per group.\n  reusedPreviousCalculation - true when no ray casting was needed to set up.\n  successful - successful?\n  mountingOffsets - horizontal outward distance from the aperture plane to the awning mounting line per group [m].\n  groupedAwningResults - the full result per group: the device, the group identity, the measured performance and the search record. Feed these into AssembleShadingSchemes.\n\nNOTES\nONE UNIT, MEASURED AGAINST ALL ITS WINDOWS. The canopy is built once from the group frame and every member window is measured against the same surfaces with the correct shared-cache offsets. Material is charged once per physical awning, never once per window.\nTHE FABRIC IS MODELLED DEPLOYED AND OPAQUE. A real awning can retract; that does not reduce the wanted-solar loss the analysis reports, and deployment control is a documented future extension.\nWINDOWS GROUP ONLY WHEN THEY CAN SHARE ONE UNIT: same wall, coplanar, outward normals the same way, heads aligned within _headTolerance_, gaps no larger than _maximumGap_, and the combined width (including extensions) within the product maximum. Everything else is split into independent units.\nNO SHADE IS A SUCCESSFUL ANSWER. When nothing beats leaving the group unshaded, the null device is returned and measured honestly; a group that could not be measured reports NOT EVALUATED and is never dressed up as a recommendation.\n\nEXAMPLE\nApertureSolarTargets → RationaliseAwningGroup._apertureSolarTargets (all the windows of one wall, _run = true) → shadingGeometry to Bake.\nRationaliseAwningGroup.groupedShadingDevices → ShadingOperation._shadingDevice - with the group's windows on _apertureSolarTargets and one SolarControlProfile per window on _controlProfiles - to measure what the awning actually does over the weather year.\nRationaliseAwningGroup.groupedAwningResults → AssembleShadingSchemes._groupedAwningResults_ - to rank the grouped awning against the conventional families and the No Shade baseline.",
              "SAM", "Solar")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooAnalyticalModelParam() { Name = "_analyticalModel", NickName = "_analyticalModel", Description = "SAM Analytical Model", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooApertureSolarTargetParam() { Name = "_apertureSolarTargets", NickName = "_apertureSolarTargets", Description = "The windows, from SAMAnalytical.ApertureSolarTargets. One physical awning may span several adjacent windows when they form a valid group", Access = GH_ParamAccess.list }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_String preset = new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "_productPreset_", NickName = "_productPreset_", Description = "The product whose limits constrain the awning.\nAvailable preset: Dakar (allowed projections 1.6, 2.1, 2.6, 3.1, 3.6 m; tilt 5° to 40°; maximum width 6.0 m; minimum width = projection + 0.4 m)", Access = GH_ParamAccess.item };
                preset.SetPersistentData("Dakar");
                result.Add(new GH_SAMParam(preset, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_projection_", NickName = "_projection_", Description = "Optional horizontal projection [m].\nEmpty = the analysis selects one of the preset's valid projections for each group width", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_tiltDegrees_", NickName = "_tiltDegrees_", Description = "Optional manual tilt lock [°], for verification or comparison.\nEmpty (the normal mode) = the analysis selects the tilt between 5° and 40°.\nThe selected value is the fixed installation tilt of the deployed fabric, not an hourly tracking angle", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(Number("_riseAboveHead_", "How far the back edge of the fabric sits above the highest head [m].\nDefault 0.0 m", 0.0), ParamVisibility.Binding));
                result.Add(new GH_SAMParam(Number("_extensionBeyondJambs_", "How far the fabric runs past the outermost jambs on each side [m].\nDefault 0.15 m", 0.15), ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_valanceDepth_", NickName = "_valanceDepth_", Description = "Optional front valance depth [m].\nDefault 0.0 m (no valance). 0.21 m is the Dakar standard valance", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(Number("_maximumGap_", "Largest horizontal gap between adjacent windows that still shares one awning [m].\nDefault 0.20 m.\nRecorded on each groupedShadingDevice, so downstream nodes re-establish the exact same group", 0.20), ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(Number("_headTolerance_", "Largest head-level difference between windows of one group [m].\nDefault 0.02 m.\nRecorded on each groupedShadingDevice, so downstream nodes re-establish the exact same group", 0.02), ParamVisibility.Voluntary));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_weatherData_", NickName = "_weatherData_", Description = "SAM WeatherData (hourly).\nSupplied weather wins; otherwise the weather attached to the model", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_unwantedPeriod_", NickName = "_unwantedPeriod_", Description = "Hours whose solar should be blocked.\nDefault: summer", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_wantedPeriod_", NickName = "_wantedPeriod_", Description = "Hours whose solar should be kept.\nDefault: winter", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_desirability_", NickName = "_desirability_", Description = "A full desirability strategy. When supplied it overrides the two periods", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));

                result.Add(new GH_SAMParam(Number("_wantedSolarPenalty_",
                    "How many kWh of unwanted solar blocked are worth one kWh of wanted solar lost. DIMENSIONLESS WEIGHT.\nALLOWED: any finite number from 0 upwards. NORMAL: 0.5 to 2.0. DEFAULT: 1.0.\nRAISE IT and wanted solar is protected harder, so shallower awnings win. It changes which measured design wins, never a measured energy.\nNEGATIVE IS REFUSED.", 1.0), ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(Number("_materialPenalty_",
                    "How strongly the search penalises additional fabric area for a small further gain. DIMENSIONLESS WEIGHT.\nALLOWED: any finite number from 0 upwards. NORMAL: 0 to 1.0. DEFAULT: 0.1.\nRAISE IT and the recommended awning SHRINKS; past a point nothing beats leaving the group unshaded and the answer is NO SHADE.\nNEGATIVE IS REFUSED.", 0.1), ParamVisibility.Voluntary));

                result.Add(new GH_SAMParam(Number("_gridSize_", "The analysis grid size [m]. Keep it the same as the targets.\nDefault 0.5 m", 0.5), ParamVisibility.Binding));
                result.Add(new GH_SAMParam(Number("_sunAngleStep_", "How finely similar sun positions are grouped [°].\nDefault 2°", 2.0), ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Integer maximumEvaluations = new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "_maximumEvaluations_", NickName = "_maximumEvaluations_", Description = "Maximum awning designs evaluated per group.\nDefault 400. Values of 0 or less use the default", Access = GH_ParamAccess.item };
                maximumEvaluations.SetPersistentData(400);
                result.Add(new GH_SAMParam(maximumEvaluations, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean recalculate = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_recalculate_", NickName = "_recalculate_", Description = "Force the solar calculation to be redone even when it could be reused.\nDefault false", Access = GH_ParamAccess.item };
                recalculate.SetPersistentData(false);
                result.Add(new GH_SAMParam(recalculate, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean run = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_run", NickName = "_run", Description = "Nothing is calculated until this is true", Access = GH_ParamAccess.item };
                run.SetPersistentData(false);
                result.Add(new GH_SAMParam(run, ParamVisibility.Binding));

                // Appended AFTER every pre-existing input so saved definitions keep their positional
                // parameter indices (the legacy parameter reader restores parameters by position).
                result.Add(new GH_SAMParam(Number("_mountingOffset_", "Horizontal outward distance from the aperture plane to the awning mounting line [m].\nUse this when the glazing is recessed and the awning is mounted on the external facade or soffit.\nDefault 0.0 m.\nPositive values move the whole awning outward without changing its product projection", 0.0), ParamVisibility.Binding));

                return result.ToArray();
            }
        }

        protected override GH_SAMParam[] Outputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "groupedShadingDevices", NickName = "groupedShadingDevices", Description = "One device per group, carrying the deterministic group identity, the awning parameters and the product preset.\nThe null device means NO SHADE.\nWire into SAMAnalytical.ShadingOperation._shadingDevice (with the group's windows on _apertureSolarTargets and one SolarControlProfile per window) to measure the awning's annual operation: requested, deployed and wind-retracted hours and the intercepted energy", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Brep() { Name = "shadingGeometry", NickName = "shadingGeometry", Description = "The recommended awning surfaces per group: one canopy face, plus one valance face when the valance depth is not zero.\nEmpty when no shading is recommended", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "groupGuids", NickName = "groupGuids", Description = "The deterministic group identity, per group", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "apertureGuids", NickName = "apertureGuids", Description = "The windows of each group, one branch per group, ordered left-to-right", Access = GH_ParamAccess.tree }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "panelGuids", NickName = "panelGuids", Description = "The host wall of each group", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "widths", NickName = "widths", Description = "Awning width per group [m]: combined aperture envelope plus both side extensions", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "projections", NickName = "projections", Description = "Horizontal projection selected per group [m]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tiltDegrees", NickName = "tiltDegrees", Description = "The tilt selected by the analysis per group [°], within the 5°-40° product range", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "valanceDepths", NickName = "valanceDepths", Description = "Valance depth per group [m]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "requiredWallBrackets", NickName = "requiredWallBrackets", Description = "Wall brackets per group: 2 for widths up to and including 4.1 m, 3 above", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "groupPerformances", NickName = "groupPerformances", Description = "The measured GROUP performance: energies summed over the member windows, percentages from the summed totals, one shared material charge", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "performanceByAperture", NickName = "performanceByAperture", Description = "The measured performance of each window within its group, each keeping its own aperture GUID", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "designSummaries", NickName = "designSummaries", Description = "The engineering answer per group in one line: what to build, its size, unwanted solar intercepted, wanted solar retained and wanted solar blocked", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "status", NickName = "status", Description = "OK / NO SHADE / WARNING / NOT EVALUATED, per group", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "reusedPreviousCalculation", NickName = "reusedPreviousCalculation", Description = "True when no ray casting was needed to set up", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "successful", NickName = "successful", Description = "Successful?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                // Appended AFTER every pre-existing output so saved definitions keep their positional
                // parameter indices (the legacy parameter reader restores parameters by position).
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "mountingOffsets", NickName = "mountingOffsets", Description = "Horizontal outward distance from the aperture plane to the awning mounting line per group [m], so the physical mounting condition is visible and auditable", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "groupedAwningResults", NickName = "groupedAwningResults", Description = "The full GroupedAwningResult per group - the device, the group identity, the measured performance and the search record. Feed these into SAMAnalytical.AssembleShadingSchemes", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
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

        /// <summary>An optional number input: null when nothing is connected or the value is not a number.</summary>
        private double? OptionalNumber(IGH_DataAccess dataAccess, string name)
        {
            int index = Params.IndexOfInputParam(name);
            if (index == -1)
            {
                return null;
            }

            double value = double.NaN;
            return dataAccess.GetData(index, ref value) && !double.IsNaN(value) ? (double?)value : null;
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

        /// <summary>The shared group device surfaces as Rhino breps, in group order.</summary>
        internal static List<Brep> Geometry(GroupedAwningResult groupedAwningResult)
        {
            List<Brep> result = new List<Brep>();

            List<ShadingElement> elements = groupedAwningResult?.Device?.ShadingElements(groupedAwningResult.Group);
            if (elements == null)
            {
                return result;
            }

            foreach (ShadingElement element in elements)
            {
                SAM.Geometry.Spatial.Face3D face3D = element?.Face3D;
                if (face3D == null)
                {
                    continue;
                }

                Brep brep = SAM.Geometry.Rhino.Convert.ToRhino_Brep(face3D);
                if (brep != null)
                {
                    result.Add(brep);
                }
            }

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

            index = Params.IndexOfInputParam("_apertureSolarTargets");
            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget>();
            if (index == -1 || !dataAccess.GetDataList(index, targets) || targets.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply at least one aperture solar target from SAMAnalytical.ApertureSolarTargets.");
                return;
            }

            string presetName = "Dakar";
            index = Params.IndexOfInputParam("_productPreset_");
            if (index != -1)
            {
                string supplied = presetName;
                if (dataAccess.GetData(index, ref supplied) && !string.IsNullOrWhiteSpace(supplied))
                {
                    presetName = supplied.Trim();
                }
            }

            if (!string.Equals(presetName, "Dakar", StringComparison.OrdinalIgnoreCase))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Format("_productPreset_ '{0}' is not supported. The available preset is Dakar.", presetName));
                return;
            }

            AwningSpecification specification = AwningSpecification.Dakar;

            double? projection = OptionalNumber(dataAccess, "_projection_");
            double? tiltDegrees = OptionalNumber(dataAccess, "_tiltDegrees_");
            double? valanceDepth = OptionalNumber(dataAccess, "_valanceDepth_") ?? 0.0; // default: no valance

            double riseAboveHead = Number(dataAccess, "_riseAboveHead_", 0.0);
            double mountingOffset = Number(dataAccess, "_mountingOffset_", 0.0);
            double extensionBeyondJambs = Number(dataAccess, "_extensionBeyondJambs_", 0.15);
            double maximumGap = Number(dataAccess, "_maximumGap_", 0.20);
            double headTolerance = Number(dataAccess, "_headTolerance_", 0.02);

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

            double wantedSolarPenalty = Number(dataAccess, "_wantedSolarPenalty_", 1.0);
            double materialPenalty = Number(dataAccess, "_materialPenalty_", 0.1);
            double gridSize = Number(dataAccess, "_gridSize_", 0.5);
            double sunAngleStep = Number(dataAccess, "_sunAngleStep_", 2.0);

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

            if (ShadingObjective.Validity(wantedSolarPenalty) == PenaltyValidity.Invalid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Format(
                    "_wantedSolarPenalty_ must be a finite number of zero or more (it is {0}). It is a dimensionless weight: 1.0 trades one kWh of wanted solar for one kWh of unwanted solar blocked. A negative value would reward destroying the solar you asked to keep; to design for maximum gain, swap _unwantedPeriod_ and _wantedPeriod_ instead.",
                    wantedSolarPenalty));
                return;
            }

            if (ShadingObjective.Validity(materialPenalty) == PenaltyValidity.Invalid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Format(
                    "_materialPenalty_ must be a finite number of zero or more (it is {0}). It is a dimensionless weight: 0 sizes on energy alone, 0.1 mildly prefers the leaner design. A negative value would reward buying fabric and would return the largest awning the bounds allow.",
                    materialPenalty));
                return;
            }

            if (ShadingObjective.Validity(wantedSolarPenalty) == PenaltyValidity.Extreme)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, string.Format(
                    "_wantedSolarPenalty_ {0} is far above the normal 0.5-2.0 range. The result is valid, but at this weight almost any loss of wanted solar is refused and the answer is usually 'build nothing' regardless of the exact value.",
                    wantedSolarPenalty));
            }

            if (ShadingObjective.Validity(materialPenalty) == PenaltyValidity.Extreme)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, string.Format(
                    "_materialPenalty_ {0} is far above the normal 0-1 range. The result is valid, but at this weight material outweighs any plausible energy gain and the answer is usually 'build nothing' regardless of the exact value.",
                    materialPenalty));
            }

            int maximumEvaluations = 400;
            index = Params.IndexOfInputParam("_maximumEvaluations_");
            if (index != -1)
            {
                int maximumEvaluations_Temp = maximumEvaluations;
                if (dataAccess.GetData(index, ref maximumEvaluations_Temp) && maximumEvaluations_Temp > 0)
                {
                    maximumEvaluations = maximumEvaluations_Temp;
                }
            }

            bool recalculate = false;
            index = Params.IndexOfInputParam("_recalculate_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref recalculate);
            }

            int year = unwantedPeriod?.Year ?? wantedPeriod?.Year ?? SAMAnalyticalApertureIrradiance.DefaultYear(analyticalModel, weatherData);

            List<Guid> apertureGuids = new List<Guid>();
            foreach (ApertureSolarTarget target in targets)
            {
                if (target != null && !apertureGuids.Contains(target.ApertureGuid))
                {
                    apertureGuids.Add(target.ApertureGuid);
                }
            }

            ShadingObjective objective = new ShadingObjective(wantedSolarPenalty, materialPenalty);

            List<GroupedAwningResult> results = SolarCreate.AwningGroupResults(
                analyticalModel, apertureGuids, year, out string message, out bool reusedPreviousCalculation,
                weatherData, desirabilityStrategy, unwantedPeriod, wantedPeriod, specification,
                projection, tiltDegrees, riseAboveHead, extensionBeyondJambs, valanceDepth,
                maximumGap, headTolerance, gridSize, sunAngleStep, recalculate, objective, maximumEvaluations, mountingOffset);

            if (results == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message ?? "The awning analysis could not be set up for this model.");
                return;
            }

            if (!reusedPreviousCalculation && !recalculate)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Geometry changed; solar calculation was updated.");
            }

            foreach (GroupedAwningResult result in results)
            {
                if (result?.Warnings == null)
                {
                    continue;
                }

                foreach (string warning in result.Warnings)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
                }
            }

            index = Params.IndexOfOutputParam("groupedShadingDevices");
            if (index != -1)
            {
                // The DEVICE, not the whole result: this output is declared as one grouped shading
                // device per group, and the single-aperture component's shadingDevice output means
                // the same thing. Emitting the result object here would give downstream nodes
                // something that is not a device on a socket that promises one.
                dataAccess.SetDataList(index, results.ConvertAll(x => x?.Device == null ? null : new GooSAMObject(x.Device)));
            }

            index = Params.IndexOfOutputParam("shadingGeometry");
            if (index != -1)
            {
                List<Brep> geometry = new List<Brep>();
                foreach (GroupedAwningResult result in results)
                {
                    geometry.AddRange(Geometry(result));
                }

                dataAccess.SetDataList(index, geometry);
            }

            index = Params.IndexOfOutputParam("groupGuids");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.Group?.GroupGuid.ToString()));
            }

            index = Params.IndexOfOutputParam("apertureGuids");
            if (index != -1)
            {
                GH_Structure<GH_String> dataTree = new GH_Structure<GH_String>();
                int branch = 0;
                foreach (GroupedAwningResult result in results)
                {
                    GH_Path path = new GH_Path(branch++);
                    foreach (Guid guid in result.Group?.ApertureGuids ?? new List<Guid>())
                    {
                        dataTree.Append(new GH_String(guid.ToString()), path);
                    }
                }

                dataAccess.SetDataTree(index, dataTree);
            }

            index = Params.IndexOfOutputParam("panelGuids");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.Group?.PanelGuid.ToString()));
            }

            index = Params.IndexOfOutputParam("widths");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.Width));
            }

            index = Params.IndexOfOutputParam("projections");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.Projection));
            }

            index = Params.IndexOfOutputParam("tiltDegrees");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.TiltDegrees));
            }

            index = Params.IndexOfOutputParam("valanceDepths");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.ValanceDepth));
            }

            index = Params.IndexOfOutputParam("mountingOffsets");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.MountingOffset));
            }

            index = Params.IndexOfOutputParam("requiredWallBrackets");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.RequiredWallBrackets));
            }

            index = Params.IndexOfOutputParam("groupPerformances");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => new GooSAMObject(x?.Performance)));
            }

            index = Params.IndexOfOutputParam("performanceByAperture");
            if (index != -1)
            {
                List<GooSAMObject> performances = new List<GooSAMObject>();
                foreach (GroupedAwningResult result in results)
                {
                    foreach (ShadingPerformance performance in result?.Performance?.PerAperture ?? new List<ShadingPerformance>())
                    {
                        performances.Add(new GooSAMObject(performance));
                    }
                }

                dataAccess.SetDataList(index, performances);
            }

            index = Params.IndexOfOutputParam("designSummaries");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.DesignSummary));
            }

            index = Params.IndexOfOutputParam("status");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => x.StatusText));
            }

            index = Params.IndexOfOutputParam("reusedPreviousCalculation");
            if (index != -1)
            {
                dataAccess.SetData(index, reusedPreviousCalculation);
            }

            index = Params.IndexOfOutputParam("groupedAwningResults");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results.ConvertAll(x => new GooSAMObject(x)));
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, true);
            }
        }
    }
}

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
    public class SAMAnalyticalRationaliseShading : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7f3c9a10-5b28-4e63-9a41-6c0d2e7b1106");

        /// <summary>
        /// The latest version of this component.
        ///
        /// 1.0.1 — shadingDevice now carries the aperture it was designed for, so VerifyShading can
        /// refuse a device that belongs to another window instead of measuring it and reporting a
        /// number that is right about the wrong design. A "build nothing" answer is now emitted as
        /// the null device rather than as the least-bad candidate, which is available separately on
        /// bestCandidateDevice. Added designSummary / status / apertureGuid / azimuth so a
        /// ten-window run reads as a table; objectiveScore demoted out of the headline.
        ///
        /// 1.0.2 — element spacing is now capped at the MINIMUM FEATURE SIZE (two analysis cells)
        /// rather than one, so the search can no longer ride the sampling limit and a result
        /// produced inside its own reliable bounds comes back without a resolution warning. The
        /// penalties refuse negative values outright and remark on extreme ones. benefit / harm are
        /// renamed unwantedSolarIntercepted / wantedSolarBlocked, and neutralSolarIntercepted is
        /// added so the three parts sum to what the device stops.
        ///
        /// 1.0.3 — a warning is raised when the winning device's element count sits exactly on the
        /// analysis-grid resolution cap: the grid may have limited the count and it should be
        /// confirmed on a finer grid. Nothing else changes — the search itself is untouched.
        /// </summary>
        public override string LatestComponentVersion => "1.0.3";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        public SAMAnalyticalRationaliseShading()
          : base("SAMAnalytical.RationaliseShading", "SAMAnalytical.RationaliseShading",
              "SUMMARY\nProduces a BUILDABLE shading device for one window and sizes it against energy, not against the look of the ideal shape. Every candidate is built as real geometry and ray-traced through the same engine that verifies the final answer.\n\nFamilies: Overhang, HorizontalLouvres, VerticalFins, EggCrate, Retractable Awning.\n*Each candidate is a full ray-tracing run; comparing all five families on one window takes seconds to minutes.*\n\nINPUTS\n  _analyticalModel — the SAM Analytical Model.\n  _apertureSolarTarget — the window, from ApertureSolarTargets.\n  _shadingPotentialField_ — the map from ShadingPotentialField. Optional: it gives the search a physically sensible starting depth. It never decides the answer.\n  _typologies_ — families to try, by name. Empty = every family capable of acting on this window's problem.\n  _weatherData_ / _unwantedPeriod_ / _wantedPeriod_ / _desirability_ — the brief, exactly as on ShadingPotentialField. Use the SAME brief throughout or the numbers will not compare.\n  _wantedSolarPenalty_ — how important preserving wanted solar is, relative to blocking unwanted solar. DIMENSIONLESS. Default 1.0.\n      1.0  equal energy weighting: losing 1 kWh of wanted solar costs exactly what gaining 1 kWh of blocked unwanted solar earns.\n      2.0  strongly protect wanted/winter solar: losing 1 kWh of wanted solar now needs about 2 kWh of unwanted solar blocked to justify it, so the search favours shallower devices.\n      0.5  prioritise blocking unwanted solar: losing 1 kWh of wanted solar only costs 0.5 kWh in the objective, so the search favours deeper ones.\n      Change it and the recommended DEPTH changes; it does not change any measured energy.\n  _materialPenalty_ — how strongly the search penalises additional device area for a small further gain. DIMENSIONLESS. Default 0.1.\n      0 sizes on energy alone and tends to return the largest device that still helps at all; 0.1 quietly prefers the leaner of two near-equal designs; raise it when buildability or a leaner device matters more than the last few kWh.\n  _optimise_ — true (default) searches each family's sizes for the best. False returns the quicker seeded candidate sweep instead.\n  _maximumEvaluations_ — maximum number of design options evaluated for each shading family. Default 400.\n  _gridSize_ / _sunAngleStep_ / _recalculate_ — as on ApertureIrradiance.\n  _run — nothing happens until this is true.\n\nOUTPUTS (best family first)\n  shadingDevice — the RECOMMENDED device, tagged with the aperture it was designed for. Feed it to VerifyShading. When nothing beats leaving the window alone this is the null device, which verifies honestly rather than failing.\n  shadingGeometry — the recommended device as surfaces. Empty when no shading is recommended.\n  designSummary — the whole answer in one line, e.g. '180° | Overhang | Depth 0.57 m | 83.2% unwanted blocked | 95.6% wanted retained | 308 kWh unwanted solar intercepted | 6 kWh wanted solar blocked'.\n  status — OK / NO SHADE / WARNING / NOT EVALUATED.\n  apertureGuid / azimuth — which window this result is about, so a ten-window run stays readable.\n  bestCandidateDevice — the least-bad candidate, always supplied. DIAGNOSTIC, not a recommendation.\n  optimisedShadingResults — the full result per family (optimised mode only).\n  typologies — family names in ranked order.\n  parameterNames / parameterValues — the sizes chosen, per family. Depths and offsets in m, counts as whole elements, tilts in degrees.\n  objectiveScore — Benefit − penalty × Harm − penalty × Cost [kWh]. Zero means 'build nothing'.\n  unwantedSolarIntercepted — the Benefit term: unwanted direct solar the device intercepts [kWh].\n  wantedSolarBlocked — the Harm term: wanted direct solar the device destroys [kWh].\n  neutralSolarIntercepted — direct solar the brief claimed neither way [kWh].\n  cost — material priced in [kWh].\n  materialFraction — device area / window area.\n  unwantedSolarBlocked — [%], unavailable (NaN) when there is no unwanted solar.\n  wantedSolarRetained — [%], unavailable (NaN) when there is no wanted solar.\n  directSolarIntercepted — total direct beam the device stops [kWh].\n  evaluations / iterations — how much searching it took.\n  termination — why the search stopped.\n  recommendsNoShading — true when nothing beats leaving the window alone.\n  reusedPreviousCalculation / successful.\n\nNOTES\nREAD THE PHYSICAL NUMBERS, NOT ONLY THE SCORE. Two families often finish within a fraction of a percent of each other with very different material quantities; which one is right is a judgement about the brief, and the score cannot make it for you.\nSCORE ZERO IS MEANINGFUL. Building nothing scores exactly zero, so a negative score means the device is worse than no device. When that is the best available, recommendsNoShading is set — and 'no shading is worth building here' is a SUCCESSFUL engineering answer, not a failed run. It is reported as status NO SHADE, shadingDevice carries the null device so it can still be verified, and the design that lost is on bestCandidateDevice. A run that could not be measured at all reports NOT EVALUATED instead, and is never dressed up as a recommendation.\nElement spacing finer than the analysis grid cannot be resolved: the node caps it and warns when a device gets close. Reduce _gridSize_ to justify a finer device.\nNo perforated or translucent screens: the engine is opaque-or-clear, and a porosity factor would produce screen-like numbers that are not a screen's.\nEach window is treated on its own — a device here does not shade its neighbour.\n\nEXAMPLE\nApertureSolarTargets → RationaliseShading._apertureSolarTarget, with the SAME brief as everywhere else: AnalysisPeriod → _unwantedPeriod_ / _wantedPeriod_, or SolarControlProfile.externalDesirability → _desirability_.\nShadingPotentialField.shadingPotentialField → RationaliseShading._shadingPotentialField_ — optional: it only seeds the search's starting depth.\nRationaliseShading.shadingDevice → VerifyShading._shadingDevice — measure the recommendation for this window.\nRationaliseShading.optimisedShadingResults → AssembleShadingSchemes._optimisedShadingResults_ — rank every family against the grouped awnings and the No Shade baseline.",
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
                result.Add(new GH_SAMParam(new GooShadingPotentialFieldParam() { Name = "_shadingPotentialField_", NickName = "_shadingPotentialField_", Description = "The shading potential map. Optional: gives the search a sensible starting depth", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_String typologies = new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "_typologies_", NickName = "_typologies_", Description = "Families to try: Overhang, HorizontalLouvres, VerticalFins, EggCrate, RetractableAwning.\nEmpty = every family capable of acting on this window's problem", Access = GH_ParamAccess.list, Optional = true };
                result.Add(new GH_SAMParam(typologies, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_weatherData_", NickName = "_weatherData_", Description = "SAM WeatherData (hourly).\nSupplied weather wins; otherwise the weather attached to the model", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_unwantedPeriod_", NickName = "_unwantedPeriod_", Description = "Hours whose solar should be blocked.\nDefault: summer", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_wantedPeriod_", NickName = "_wantedPeriod_", Description = "Hours whose solar should be kept.\nDefault: winter", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_desirability_", NickName = "_desirability_", Description = "A full desirability strategy. When supplied it overrides the two periods", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));

                result.Add(new GH_SAMParam(Number("_wantedSolarPenalty_",
                    "How many kWh of unwanted solar blocked are worth one kWh of wanted solar lost. DIMENSIONLESS WEIGHT.\n\n"
                    + "ALLOWED: any finite number from 0 upwards. There is no upper limit.\n"
                    + "NORMAL: 0.5 to 2.0.\n"
                    + "DEFAULT: 1.0 — an even trade, which assumes nothing about the brief.\n\n"
                    + "RAISE IT and wanted solar is protected harder, so the search favours SHALLOWER devices and accepts less unwanted solar blocked. Lower it and it favours deeper ones.\n"
                    + "It changes which measured design WINS. It never changes a measured energy.\n\n"
                    + "Above 10 the answer is usually 'build nothing' whatever the exact value.\n"
                    + "NEGATIVE IS REFUSED: it would pay the search to destroy the solar you asked to keep. To design for maximum gain, swap _unwantedPeriod_ and _wantedPeriod_ instead.", 1.0), ParamVisibility.Voluntary));

                result.Add(new GH_SAMParam(Number("_materialPenalty_",
                    "How strongly the search penalises additional device area for a small further gain. DIMENSIONLESS WEIGHT: the share of the window's whole admitted beam charged per unit of (device area / window area).\n\n"
                    + "ALLOWED: any finite number from 0 upwards. There is no upper limit.\n"
                    + "NORMAL: 0 to 1.0.\n"
                    + "DEFAULT: 0.1 — a mild preference for the leaner of two near-equal designs, small enough not to override the energy answer.\n\n"
                    + "RAISE IT and the recommended device SHRINKS; past a point nothing beats leaving the window alone and the answer becomes NO SHADE, which is a real answer and not a failure. 0 sizes on energy alone and tends to return the largest device that still helps at all.\n\n"
                    + "Above 10 the answer is 'build nothing' whatever the exact value.\n"
                    + "NEGATIVE IS REFUSED: it would reward adding material, returning the largest device the bounds allow.", 0.1), ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean optimise = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_optimise_", NickName = "_optimise_", Description = "True (default) searches each family's sizes. False runs the quicker seeded candidate sweep", Access = GH_ParamAccess.item };
                optimise.SetPersistentData(true);
                result.Add(new GH_SAMParam(optimise, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Integer maximumEvaluations = new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "_maximumEvaluations_", NickName = "_maximumEvaluations_", Description = "Maximum number of design options evaluated for each shading family.\nDefault 400. Values of 0 or less use the default", Access = GH_ParamAccess.item };
                maximumEvaluations.SetPersistentData(400);
                result.Add(new GH_SAMParam(maximumEvaluations, ParamVisibility.Voluntary));

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
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "shadingDevice", NickName = "shadingDevice", Description = "The RECOMMENDED device, carrying the aperture it was designed for. Feed it to SAMAnalytical.VerifyShading.\nWhen nothing beats leaving the window alone this is the null device, which verifies honestly as 0 % blocked / 100 % retained", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Brep() { Name = "shadingGeometry", NickName = "shadingGeometry", Description = "The recommended device as surfaces. Empty when no shading is recommended", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "designSummary", NickName = "designSummary", Description = "The engineering answer in one line: what to build, its size, unwanted solar intercepted, wanted solar retained, and wanted solar blocked", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "status", NickName = "status", Description = "OK / NO SHADE / WARNING / NOT EVALUATED. Readable across many apertures at once", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "apertureGuid", NickName = "apertureGuid", Description = "The aperture this result belongs to", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "azimuth", NickName = "azimuth", Description = "Compass direction the window faces [°]", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "bestCandidateDevice", NickName = "bestCandidateDevice", Description = "The least-bad candidate, ALWAYS supplied — diagnostic geometry, not a recommendation.\nWhen no shading is recommended this is what the search would have built, so the recommendation can be checked rather than taken on trust", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "optimisedShadingResults", NickName = "optimisedShadingResults", Description = "Full result per family, best first (optimised mode only)", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "typologies", NickName = "typologies", Description = "Family names, best first", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "parameterNames", NickName = "parameterNames", Description = "Size parameter names of the winning device", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "parameterValues", NickName = "parameterValues", Description = "Sizes of the winning device: depths and offsets [m], counts, tilts [°]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "objectiveScore", NickName = "objectiveScore", Description = "Benefit − penalty × Harm − penalty × Cost [kWh], per family. Zero = build nothing.\nA comparison aid, NOT the headline: two designs a fraction of a percent apart in score can do very different things", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                // Named for the PHYSICAL QUANTITY, not for the role it plays in the objective.
                // "benefit 46 kWh" was read during manual testing as "this saves 46 kWh"; it is the
                // unwanted beam the device intercepts, which is a narrower and checkable claim.
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "unwantedSolarIntercepted", NickName = "unwantedSolarIntercepted", Description = "Unwanted direct solar the device intercepts [kWh]. The objective's Benefit term", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "wantedSolarBlocked", NickName = "wantedSolarBlocked", Description = "Wanted direct solar the device destroys [kWh]. The objective's Harm term", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "neutralSolarIntercepted", NickName = "neutralSolarIntercepted", Description = "Direct solar the device intercepts that the brief claimed neither way [kWh].\n\ndirectSolarIntercepted = unwantedSolarIntercepted + wantedSolarBlocked + neutralSolarIntercepted", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "cost", NickName = "cost", Description = "Material priced in [kWh]", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "materialFraction", NickName = "materialFraction", Description = "Device area / window area", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "unwantedSolarBlocked", NickName = "unwantedSolarBlocked", Description = "Unwanted solar blocked [%]. NaN when there is no unwanted solar", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "wantedSolarRetained", NickName = "wantedSolarRetained", Description = "Wanted solar retained [%]. NaN when there is no wanted solar", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "directSolarIntercepted", NickName = "directSolarIntercepted", Description = "Direct beam the device stops [kWh]", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "evaluations", NickName = "evaluations", Description = "Candidates evaluated per family", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "iterations", NickName = "iterations", Description = "Search iterations per family", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "termination", NickName = "termination", Description = "Why the search stopped, per family", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "recommendsNoShading", NickName = "recommendsNoShading", Description = "True when nothing beats leaving the window alone", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "reusedPreviousCalculation", NickName = "reusedPreviousCalculation", Description = "True when no ray casting was needed to set up", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
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

        /// <summary>The device as Rhino surfaces, in build order.</summary>
        internal static List<Brep> Geometry(IShadingTypology typology, ApertureSolarTarget target)
        {
            List<Brep> result = new List<Brep>();

            List<ShadingElement> elements = typology?.ShadingElements(target);
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

            index = Params.IndexOfInputParam("_apertureSolarTarget");
            ApertureSolarTarget inputTarget = null;
            if (index == -1 || !dataAccess.GetData(index, ref inputTarget) || inputTarget == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply an aperture solar target from SAMAnalytical.ApertureSolarTargets.");
                return;
            }

            ShadingPotentialField field = null;
            index = Params.IndexOfInputParam("_shadingPotentialField_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref field);
            }

            List<string> typologyNames = new List<string>();
            index = Params.IndexOfInputParam("_typologies_");
            if (index != -1)
            {
                List<string> supplied = new List<string>();
                if (dataAccess.GetDataList(index, supplied))
                {
                    foreach (string name in supplied)
                    {
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }

                        // "Retractable Awning" (the canvas label) and "RetractableAwning" (the
                        // stable typology key) name the same family.
                        string familyName = name.Trim().Replace(" ", "");
                        if (SolarCreate.ShadingTypology(familyName) == null)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Format("'{0}' is not a shading family. Use Overhang, HorizontalLouvres, VerticalFins, EggCrate or RetractableAwning.", name));
                            return;
                        }

                        typologyNames.Add(familyName);
                    }
                }
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

            double wantedSolarPenalty = Number(dataAccess, "_wantedSolarPenalty_", 1.0);
            double materialPenalty = Number(dataAccess, "_materialPenalty_", 0.1);
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

            // The penalties are dimensionless weights on POSITIVE quantities that the score
            // SUBTRACTS, so a negative value flips the meaning of its own term: a negative wanted
            // penalty pays the search to destroy the solar the brief asked it to keep, and a
            // negative material penalty pays it to buy material. Refused rather than clamped —
            // reading -2 as 0 would answer a question nobody asked. To maximise gain instead, swap
            // the wanted and unwanted periods.
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
                    "_materialPenalty_ must be a finite number of zero or more (it is {0}). It is a dimensionless weight: 0 sizes on energy alone, 0.1 mildly prefers the leaner design. A negative value would reward buying material and would return the largest device the bounds allow.",
                    materialPenalty));
                return;
            }

            // Valid but far outside normal use. The answer is still sound, so this is a remark and
            // not an error — but at these weights the recommendation usually stops responding to the
            // value at all, and that is worth knowing before it is read as a design finding.
            if (ShadingObjective.Validity(wantedSolarPenalty) == PenaltyValidity.Extreme)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, string.Format(
                    "_wantedSolarPenalty_ {0} is far above the normal 0.5–2.0 range. The result is valid, but at this weight almost any loss of wanted solar is refused and the answer is usually 'build nothing' regardless of the exact value.",
                    wantedSolarPenalty));
            }

            if (ShadingObjective.Validity(materialPenalty) == PenaltyValidity.Extreme)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, string.Format(
                    "_materialPenalty_ {0} is far above the normal 0–1 range. The result is valid, but at this weight material outweighs any plausible energy gain and the answer is usually 'build nothing' regardless of the exact value.",
                    materialPenalty));
            }

            bool optimise = true;
            index = Params.IndexOfInputParam("_optimise_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref optimise);
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

            if (field != null && field.ApertureGuid != inputTarget.ApertureGuid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The shading potential map belongs to a different aperture and was ignored. The search still runs, starting from the family defaults.");
                field = null;
            }

            ShadingObjective objective = new ShadingObjective(wantedSolarPenalty, materialPenalty);

            List<IShadingTypology> devices = new List<IShadingTypology>();
            List<OptimisedShadingResult> optimisedResults = new List<OptimisedShadingResult>();
            List<ShadingPerformance> performances = new List<ShadingPerformance>();

            if (optimise)
            {
                optimisedResults = Optimise.ShadingDevice(
                    setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability, setup.Context.ContextOccluders,
                    objective, field, typologyNames.Count == 0 ? null : typologyNames, maximumEvaluations, setup.CellIndexOffset);

                if (optimisedResults == null || optimisedResults.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No shading family can act on this window's solar problem: no unwanted solar reaches it over the hours requested. No device is recommended.");
                    SetNoDevice(dataAccess, setup.Target, "No shading family can act on this window's solar problem", ShadingDesignStatus.NoShading);
                    return;
                }

                // Every family failing to produce a MEASURED candidate is a fault in the setup, not
                // an engineering answer, and must not be reported as "no shading needed".
                if (optimisedResults.TrueForAll(x => x.Termination == ShadingOptimisationTermination.EvaluationFailed))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No candidate could be measured on this window: the aperture, the solar calculation and the candidate geometry do not describe the same analysis points. Check that _gridSize_ is the same value used for the targets and that the target came from THIS model.");
                    SetNoDevice(dataAccess, setup.Target, "Not evaluated", ShadingDesignStatus.NotEvaluated);
                    return;
                }

                foreach (OptimisedShadingResult optimisedResult in optimisedResults)
                {
                    devices.Add(optimisedResult.Typology());
                }
            }
            else
            {
                List<string> names = typologyNames;
                if (names.Count == 0)
                {
                    names = SolarQuery.EligibleShadingTypologies(setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability, out double _, out double _) ?? new List<string>();
                }

                if (field == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_shadingPotentialField_ is required when _optimise_ is false: the candidate sweep is seeded from the map.");
                    return;
                }

                foreach (string name in names)
                {
                    IShadingTypology device = SolarCreate.RationalisedShading(field, setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability, setup.Context.ContextOccluders, name, out ShadingPerformance performance, wantedSolarPenalty, materialPenalty, setup.CellIndexOffset);
                    if (device == null || performance == null)
                    {
                        continue;
                    }

                    devices.Add(device);
                    performances.Add(performance);
                }

                if (devices.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No buildable device could be produced for this window under the brief given.");
                    SetNoDevice(dataAccess, setup.Target, "No buildable device under this brief", ShadingDesignStatus.NoShading);
                    return;
                }

                // Best first, by the same objective the optimised path reports.
                List<int> order = new List<int>();
                for (int i = 0; i < devices.Count; i++)
                {
                    order.Add(i);
                }

                order.Sort((a, b) => objective.Score(performances[b]).CompareTo(objective.Score(performances[a])));

                List<IShadingTypology> orderedDevices = new List<IShadingTypology>();
                List<ShadingPerformance> orderedPerformances = new List<ShadingPerformance>();
                foreach (int i in order)
                {
                    orderedDevices.Add(devices[i]);
                    orderedPerformances.Add(performances[i]);
                }

                devices = orderedDevices;
                performances = orderedPerformances;
            }

            IShadingTypology best = devices.Count == 0 ? null : devices[0];

            // Element spacing the analysis grid cannot resolve would be reported as a triumph.
            ShadingResolutionState resolutionState = ShadingResolutionState.Resolved;
            if (best != null)
            {
                resolutionState = SolarQuery.ShadingResolution(best, setup.Target, gridSize, out string resolutionMessage, out double _);
                if (resolutionState == ShadingResolutionState.BelowResolutionLimit)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, resolutionMessage);
                }
                else if (resolutionState == ShadingResolutionState.NearResolutionLimit)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, resolutionMessage);
                }
            }

            bool recommendsNoShading = optimise && optimisedResults.Count != 0 && optimisedResults[0].RecommendsNoShading;
            if (recommendsNoShading)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No device beats leaving this window unshaded. That is a successful answer, not a failure: shadingDevice carries the null device so it can be verified, and the least-bad candidate is on bestCandidateDevice so the recommendation can be checked rather than taken on trust.");
            }

            // Distinct from the spacing warning above: a winner whose element count sits exactly on
            // the grid-resolution cap may have been stopped by the grid rather than by the design.
            // Checked only for the RECOMMENDED device — a rejected candidate riding the cap is not a
            // recommendation and does not need the reader's attention.
            bool gridResolutionCapReached = false;
            if (optimise && optimisedResults.Count != 0 && best != null && !recommendsNoShading)
            {
                gridResolutionCapReached = SolarQuery.GridResolutionCapReached(optimisedResults[0], out string capMessage);
                if (gridResolutionCapReached)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, capMessage);
                }
            }

            // The RECOMMENDATION and the DIAGNOSTIC are different things and are kept on different
            // wires. Handing the least-bad candidate out as "the device" is how a rejected design
            // ends up built.
            IShadingTypology recommended = recommendsNoShading ? new NoShading() : best;

            index = Params.IndexOfOutputParam("shadingDevice");
            if (index != -1)
            {
                dataAccess.SetData(index, recommended == null ? null : new GooSAMObject(new ShadingDevice(setup.Target.ApertureGuid, recommended)));
            }

            index = Params.IndexOfOutputParam("shadingGeometry");
            if (index != -1)
            {
                dataAccess.SetDataList(index, Geometry(recommended, setup.Target));
            }

            index = Params.IndexOfOutputParam("bestCandidateDevice");
            if (index != -1)
            {
                dataAccess.SetData(index, best == null ? null : new GooSAMObject(new ShadingDevice(setup.Target.ApertureGuid, best)));
            }

            index = Params.IndexOfOutputParam("designSummary");
            if (index != -1)
            {
                dataAccess.SetData(index, optimise && optimisedResults.Count != 0
                    ? SolarQuery.DesignSummary(optimisedResults[0], setup.Target.Azimuth)
                    : SweepSummary(best, performances.Count == 0 ? null : performances[0], setup.Target.Azimuth));
            }

            index = Params.IndexOfOutputParam("status");
            if (index != -1)
            {
                ShadingDesignStatus status = optimise && optimisedResults.Count != 0
                    ? SolarQuery.DesignStatus(optimisedResults[0], resolutionState)
                    : (resolutionState == ShadingResolutionState.Resolved ? ShadingDesignStatus.Ok : ShadingDesignStatus.Warning);

                // A device that rode the grid-resolution cap is a "check before building" answer
                // too. NO SHADE still outranks: it is only promoted when a device is recommended.
                if (status == ShadingDesignStatus.Ok && gridResolutionCapReached)
                {
                    status = ShadingDesignStatus.Warning;
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

            index = Params.IndexOfOutputParam("optimisedShadingResults");
            if (index != -1)
            {
                dataAccess.SetDataList(index, optimisedResults.ConvertAll(x => new GooSAMObject(x)));
            }

            index = Params.IndexOfOutputParam("typologies");
            if (index != -1)
            {
                dataAccess.SetDataList(index, devices.ConvertAll(x => x.Name));
            }

            index = Params.IndexOfOutputParam("parameterNames");
            if (index != -1)
            {
                dataAccess.SetDataList(index, best?.ParameterNames);
            }

            index = Params.IndexOfOutputParam("parameterValues");
            if (index != -1)
            {
                dataAccess.SetDataList(index, best?.ParameterNames?.ConvertAll(x => best.GetParameter(x)));
            }

            SetList(dataAccess, "objectiveScore", optimise, optimisedResults, performances, x => x.ObjectiveScore, x => objective.Score(x));
            SetList(dataAccess, "unwantedSolarIntercepted", optimise, optimisedResults, performances, x => x.UnwantedSolarIntercepted, x => x.UnwantedSolarIntercepted);
            SetList(dataAccess, "wantedSolarBlocked", optimise, optimisedResults, performances, x => x.WantedSolarBlocked, x => x.WantedSolarBlocked);
            SetList(dataAccess, "neutralSolarIntercepted", optimise, optimisedResults, performances, x => x.NeutralSolarIntercepted, x => x.NeutralSolarIntercepted);
            SetList(dataAccess, "cost", optimise, optimisedResults, performances, x => x.Cost, x => objective.Cost(x));
            SetList(dataAccess, "materialFraction", optimise, optimisedResults, performances, x => x.MaterialFraction, x => x.MaterialFraction);
            SetList(dataAccess, "unwantedSolarBlocked", optimise, optimisedResults, performances, x => Query.Percentage(x.UnwantedSolarBlocked), x => Query.Percentage(x.UnwantedSolarBlocked));
            SetList(dataAccess, "wantedSolarRetained", optimise, optimisedResults, performances, x => Query.Percentage(x.WantedSolarRetained), x => Query.Percentage(x.WantedSolarRetained));
            SetList(dataAccess, "directSolarIntercepted", optimise, optimisedResults, performances, x => x.DirectSolarIntercepted, x => x.DirectSolarIntercepted);

            index = Params.IndexOfOutputParam("evaluations");
            if (index != -1)
            {
                dataAccess.SetDataList(index, optimisedResults.ConvertAll(x => x.Evaluations));
            }

            index = Params.IndexOfOutputParam("iterations");
            if (index != -1)
            {
                dataAccess.SetDataList(index, optimisedResults.ConvertAll(x => x.Iterations));
            }

            index = Params.IndexOfOutputParam("termination");
            if (index != -1)
            {
                dataAccess.SetDataList(index, optimisedResults.ConvertAll(x => x.Termination.ToString()));
            }

            index = Params.IndexOfOutputParam("recommendsNoShading");
            if (index != -1)
            {
                dataAccess.SetData(index, recommendsNoShading);
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
        /// The identity and status outputs for a run that produced no device at all.
        ///
        /// An early return that leaves every wire empty is the reason a batch of ten windows becomes
        /// unreadable: the branches that failed look identical to the branches that were never asked
        /// to run. The aperture, its azimuth, a status and a reason are always written, even when
        /// there is no design to report.
        /// </summary>
        private void SetNoDevice(IGH_DataAccess dataAccess, ApertureSolarTarget target, string reason, ShadingDesignStatus status)
        {
            int index = Params.IndexOfOutputParam("shadingDevice");
            if (index != -1 && status == ShadingDesignStatus.NoShading)
            {
                // Nothing to build IS the answer here, so it goes out as the verifiable null device.
                dataAccess.SetData(index, new GooSAMObject(new ShadingDevice(target.ApertureGuid, new NoShading())));
            }

            index = Params.IndexOfOutputParam("designSummary");
            if (index != -1)
            {
                dataAccess.SetData(index, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0}° | {1}", target.Azimuth, reason));
            }

            index = Params.IndexOfOutputParam("status");
            if (index != -1)
            {
                dataAccess.SetData(index, SolarQuery.StatusText(status));
            }

            index = Params.IndexOfOutputParam("apertureGuid");
            if (index != -1)
            {
                dataAccess.SetData(index, target.ApertureGuid.ToString());
            }

            index = Params.IndexOfOutputParam("azimuth");
            if (index != -1)
            {
                dataAccess.SetData(index, target.Azimuth);
            }

            index = Params.IndexOfOutputParam("recommendsNoShading");
            if (index != -1)
            {
                dataAccess.SetData(index, status == ShadingDesignStatus.NoShading);
            }
        }

        /// <summary>The one-line story for the seeded-sweep path, which has no OptimisedShadingResult.</summary>
        private static string SweepSummary(IShadingTypology typology, ShadingPerformance performance, double azimuth)
        {
            if (typology == null || performance == null)
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0}° | no device", azimuth);
            }

            return SolarQuery.VerificationSummary(performance, azimuth);
        }

        /// <summary>The same metric read from whichever path produced the devices.</summary>
        private void SetList(IGH_DataAccess dataAccess, string name, bool optimise, List<OptimisedShadingResult> optimisedResults, List<ShadingPerformance> performances, Func<OptimisedShadingResult, double> fromOptimised, Func<ShadingPerformance, double> fromPerformance)
        {
            int index = Params.IndexOfOutputParam(name);
            if (index == -1)
            {
                return;
            }

            dataAccess.SetDataList(index, optimise
                ? optimisedResults.ConvertAll(x => fromOptimised(x))
                : performances.ConvertAll(x => fromPerformance(x)));
        }
    }
}


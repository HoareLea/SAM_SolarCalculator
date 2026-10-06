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
    public class SAMAnalyticalCompareShading : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("cc379ffd-68df-4544-add8-e787c841a062");

        /// <summary>The latest version of this component.</summary>
        public override string LatestComponentVersion => "1.0.0";

        /// <summary>Provides an Icon for the component.</summary>
        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        public SAMAnalyticalCompareShading()
          : base("SAMAnalytical.CompareShading", "SAMAnalytical.CompareShading",
              "SUMMARY\nVerifies every shading scheme against the SAME aperture set and SAME analysis basis, ranks them by the verified score under ONE objective, and reports the whole comparison.\n\nTHE SOFTWARE RANKS; THE ENGINEER SELECTS. Rank 1 is the ANALYTICAL LEADER - the option with the highest verified score under the stated model, objective and resolution. It is NOT an automatic engineering selection: choosing a scheme to build is the separate downstream step SAMAnalytical.SelectShadingScheme, which records who chose what and why. The ranking is never suppressed, even when the recommendation confidence is low (see recommendationStatus).\n\nWhole-scheme verification measures the COMPLETE scheme against EVERY aperture at once, so an overhang over one window that also shades its neighbour is credited on the neighbour - a per-window sum cannot see that.\n\nOUTPUTS\n  comparisonResult - the full result: rows, ranking, analytical leader, recommendation status and the report.\n  topRankedVerifiedScheme / topRankedScheme / topRankedGeometry - the analytical leader (arithmetic rank 1), always populated when a rankable option exists, at every recommendation status.\n  reportMarkdown - the full report, glossary included. reportCsv - one row per supplied scheme.\n  recommendationStatus - READY / PROVISIONAL / INDETERMINATE / NO SHADING RECOMMENDED / NO DECISION: whether an engineer should ACT on rank 1.\n  openChecks / requiredBeforeFreeze - what remains open, and the actionable steps that close it.\n\nEXAMPLE\nAssembleShadingSchemes.shadingSchemes → CompareShading._shadingSchemes, with the same AnalyticalModel and the same ApertureSolarTargets list the schemes were built from, and the SAME brief as the searches.\nCompareShading.comparisonResult → SelectShadingScheme._comparisonResult.\nRead reportMarkdown for the full ranked report; recommendationStatus says whether an engineer should act on rank 1.",
              "SAM", "Solar")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooAnalyticalModelParam() { Name = "_analyticalModel", NickName = "_analyticalModel", Description = "SAM Analytical Model", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooApertureSolarTargetParam() { Name = "_apertureSolarTargets", NickName = "_apertureSolarTargets", Description = "The windows. Every scheme must cover exactly this set", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_shadingSchemes", NickName = "_shadingSchemes", Description = "The schemes from SAMAnalytical.AssembleShadingSchemes", Access = GH_ParamAccess.list }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_wantedSolarPenalty_", NickName = "_wantedSolarPenalty_", Description = "How many kWh of unwanted solar blocked are worth one kWh of wanted solar lost. DIMENSIONLESS WEIGHT.\nALLOWED: any finite number from 0 upwards. NORMAL: 0.5 to 2.0. DEFAULT: 1.0.\nRAISE IT and wanted solar is protected harder, so shallower devices win. It changes which measured design wins, never a measured energy.\nNEGATIVE IS REFUSED.", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_materialPenalty_", NickName = "_materialPenalty_", Description = "How strongly the comparison penalises material. DIMENSIONLESS WEIGHT.\nALLOWED: any finite number from 0 upwards. NORMAL: 0 to 1.0. DEFAULT: 0.1.\nRAISE IT and leaner designs win; past a point nothing beats building nothing and No Shade is the analytical leader.\nNEGATIVE IS REFUSED.", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "_materialCostReference_", NickName = "_materialCostReference_", Description = "Which admitted energy the material cost is measured against: AdmittedDirectEnergy (default) or AdmittedUnwantedEnergy", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_weatherData_", NickName = "_weatherData_", Description = "SAM WeatherData (hourly).\nSupplied weather wins; otherwise the weather attached to the model", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_unwantedPeriod_", NickName = "_unwantedPeriod_", Description = "Hours whose solar should be blocked.\nDefault: summer", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_wantedPeriod_", NickName = "_wantedPeriod_", Description = "Hours whose solar should be kept.\nDefault: winter", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_desirability_", NickName = "_desirability_", Description = "A full desirability strategy. When supplied it overrides the two periods", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number gridSize = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_gridSize_", NickName = "_gridSize_", Description = "The analysis grid size [m]. Keep it the same as the targets.\nDefault 0.5 m", Access = GH_ParamAccess.item };
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

                return result.ToArray();
            }
        }

        protected override GH_SAMParam[] Outputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "comparisonResult", NickName = "comparisonResult", Description = "The full comparison result: rows, ranking, analytical leader, recommendation status and the report", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "topRankedVerifiedScheme", NickName = "topRankedVerifiedScheme", Description = "The verified result of the ANALYTICAL LEADER (arithmetic rank 1)", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "topRankedScheme", NickName = "topRankedScheme", Description = "The analytical leader scheme", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Brep() { Name = "topRankedGeometry", NickName = "topRankedGeometry", Description = "The analytical leader's geometry, rebuilt from its placements", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "rankedVerifiedSchemes", NickName = "rankedVerifiedSchemes", Description = "The verified result of every RANKED scheme, in ranked order. Schemes that could not be ranked are reported in the result and the report, not here", Access = GH_ParamAccess.list }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "ranks", NickName = "ranks", Description = "The rank of each option, in ranked order", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "topRanked", NickName = "topRanked", Description = "True on the analytical leader (rank 1)", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "optionNames", NickName = "optionNames", Description = "One name per option, in ranked order", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "statuses", NickName = "statuses", Description = "OK / OK (provisional) / NO SHADE / INCOMPARABLE / NOT EVALUATED / NOT RANKABLE, in ranked order", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "physicalDeviceCounts", NickName = "physicalDeviceCounts", Description = "Physical units per option", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "apertureCounts", NickName = "apertureCounts", Description = "Apertures per option", Access = GH_ParamAccess.list }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "baselineDirectSolar", NickName = "baselineDirectSolar", Description = "Admitted direct solar per option [kWh]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "unwantedSolarIntercepted", NickName = "unwantedSolarIntercepted", Description = "Unwanted solar intercepted per option [kWh]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "wantedSolarBlocked", NickName = "wantedSolarBlocked", Description = "Wanted solar blocked per option [kWh]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "unwantedSolarBlocked", NickName = "unwantedSolarBlocked", Description = "Unwanted solar blocked per option [%]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "wantedSolarRetained", NickName = "wantedSolarRetained", Description = "Wanted solar retained per option [%]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "physicalDeviceAreas", NickName = "physicalDeviceAreas", Description = "Shading material per option [m2]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "materialFractions", NickName = "materialFractions", Description = "Device area / total window area per option (a fraction, not a percentage)", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "benefits", NickName = "benefits", Description = "Benefit per option [kWh]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "harms", NickName = "harms", Description = "Harm per option [kWh]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "costs", NickName = "costs", Description = "Cost per option [kWh]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "objectiveScores", NickName = "objectiveScores", Description = "Verified score per option [kWh]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "scoreDeltaToTopRanked", NickName = "scoreDeltaToTopRanked", Description = "Score below the analytical leader per option [kWh]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "scoreDeltaToNoShade", NickName = "scoreDeltaToNoShade", Description = "Score above or below No Shade per option [kWh]", Access = GH_ParamAccess.list }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "recommendationStatus", NickName = "recommendationStatus", Description = "READY / PROVISIONAL / INDETERMINATE / NO SHADING RECOMMENDED / NO DECISION - whether an engineer should ACT on rank 1. READY is reserved for a comparison where the required convergence checks have been confirmed; a single-grid comparison without such confirmation remains PROVISIONAL.", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "recommendationSummary", NickName = "recommendationSummary", Description = "The Engineering Recommendation block of the report", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "openChecks", NickName = "openChecks", Description = "One line per unresolved condition", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "requiredBeforeFreeze", NickName = "requiredBeforeFreeze", Description = "The actionable inverse of openChecks", Access = GH_ParamAccess.list }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "reportMarkdown", NickName = "reportMarkdown", Description = "The full comparison report, glossary included", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "reportCsv", NickName = "reportCsv", Description = "One row per supplied scheme", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "reportGlossary", NickName = "reportGlossary", Description = "The glossary alone, from the same generator as the report's", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "comparisonComplete", NickName = "comparisonComplete", Description = "False when any supplied scheme could not be ranked", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "baselineAnchored", NickName = "baselineAnchored", Description = "True when the No Shade scheme anchored the baseline check", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "gridConvergenceConfirmed", NickName = "gridConvergenceConfirmed", Description = "False unless a finer-grid repeat was run and compared", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
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

        /// <summary>
        /// Whether an input actually carries data — the only honest reading of "supplied". A value
        /// equal to the library default (e.g. mu = 0.1) that an engineer explicitly connected is a
        /// confirmed project value, not a default; the report's provenance block must not label it
        /// DEFAULT.
        /// </summary>
        private bool InputSupplied(IGH_DataAccess dataAccess, string name)
        {
            int index = Params.IndexOfInputParam(name);
            return index != -1 && Params.Input[index] != null && Params.Input[index].SourceCount > 0;
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

        internal static List<Brep> SchemeGeometry(ShadingScheme scheme, List<ApertureSolarTarget> targets)
        {
            List<Brep> result = new List<Brep>();
            if (scheme == null)
            {
                return result;
            }

            List<ShadingElement> elements = scheme.SchemeElements(targets);
            foreach (ShadingElement element in elements ?? new List<ShadingElement>())
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

            index = Params.IndexOfInputParam("_shadingSchemes");
            List<GH_ObjectWrapper> schemeWrappers = new List<GH_ObjectWrapper>();
            List<ShadingScheme> schemes = new List<ShadingScheme>();
            if (index == -1 || !dataAccess.GetDataList(index, schemeWrappers))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply shading schemes from SAMAnalytical.AssembleShadingSchemes.");
                return;
            }

            int schemeSupplied = schemeWrappers.Count;
            foreach (GH_ObjectWrapper wrapper in schemeWrappers)
            {
                ShadingScheme scheme = Query.Value<ShadingScheme>(wrapper);
                if (scheme != null)
                {
                    schemes.Add(scheme);
                }
            }

            if (schemes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, schemeSupplied == 0
                    ? "Please supply shading schemes from SAMAnalytical.AssembleShadingSchemes."
                    : "None of the entries on _shadingSchemes is a shading scheme, so the comparison has nothing to rank. Connect the shadingSchemes output of SAMAnalytical.AssembleShadingSchemes.");
                return;
            }

            if (schemes.Count < schemeSupplied)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Format("{0} of the {1} entries on _shadingSchemes are not shading schemes and were ignored.", schemeSupplied - schemes.Count, schemeSupplied));
            }

            double wantedSolarPenalty = Number(dataAccess, "_wantedSolarPenalty_", 1.0);
            double materialPenalty = Number(dataAccess, "_materialPenalty_", 0.1);

            string referenceText = "AdmittedDirectEnergy";
            index = Params.IndexOfInputParam("_materialCostReference_");
            if (index != -1)
            {
                string supplied = referenceText;
                if (dataAccess.GetData(index, ref supplied) && !string.IsNullOrWhiteSpace(supplied))
                {
                    referenceText = supplied.Trim();
                }
            }

            MaterialCostReference materialCostReference;
            if (string.Equals(referenceText, "AdmittedUnwantedEnergy", StringComparison.OrdinalIgnoreCase))
            {
                materialCostReference = MaterialCostReference.AdmittedUnwantedEnergy;
            }
            else if (string.Equals(referenceText, "AdmittedDirectEnergy", StringComparison.OrdinalIgnoreCase))
            {
                materialCostReference = MaterialCostReference.AdmittedDirectEnergy;
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Format("_materialCostReference_ '{0}' is not recognised. Use AdmittedDirectEnergy or AdmittedUnwantedEnergy.", referenceText));
                return;
            }

            WeatherData weatherData = Object<WeatherData>(dataAccess, "_weatherData_", out bool weatherSupplied, out bool weatherWrongType);
            if (weatherWrongType)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_weatherData_ is not SAM WeatherData.");
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
                    "_wantedSolarPenalty_ must be a finite number of zero or more (it is {0}). A negative value would reward destroying the solar you asked to keep.",
                    wantedSolarPenalty));
                return;
            }

            if (ShadingObjective.Validity(materialPenalty) == PenaltyValidity.Invalid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Format(
                    "_materialPenalty_ must be a finite number of zero or more (it is {0}). A negative value would reward buying material.",
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

            bool recalculate = false;
            index = Params.IndexOfInputParam("_recalculate_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref recalculate);
            }

            ShadingComparisonResult result = SolarCreate.ShadingComparison(
                analyticalModel, targets, schemes, wantedSolarPenalty, materialPenalty, materialCostReference, out string message,
                weatherData, desirabilityStrategy, unwantedPeriod, wantedPeriod, gridSize, sunAngleStep, recalculate,
                wantedSolarPenaltySupplied: InputSupplied(dataAccess, "_wantedSolarPenalty_"),
                materialPenaltySupplied: InputSupplied(dataAccess, "_materialPenalty_"),
                desirabilitySupplied: strategySupplied || unwantedSupplied || wantedSupplied);

            if (result == null || result.Message != null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, result?.Message ?? message ?? "The comparison could not be run.");
                return;
            }

            foreach (ShadingComparisonRow row in result.Rows)
            {
                foreach (string warning in row.Warnings)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, row.OptionName + ": " + warning);
                }
            }

            index = Params.IndexOfOutputParam("comparisonResult");
            if (index != -1)
            {
                dataAccess.SetData(index, new GooSAMObject(result));
            }

            VerifiedShadingSchemeResult topVerified = result.TopRankedVerifiedScheme;
            index = Params.IndexOfOutputParam("topRankedVerifiedScheme");
            if (index != -1)
            {
                dataAccess.SetData(index, topVerified == null ? null : new GooSAMObject(topVerified));
            }

            index = Params.IndexOfOutputParam("topRankedScheme");
            if (index != -1)
            {
                dataAccess.SetData(index, topVerified?.Scheme == null ? null : new GooSAMObject(topVerified.Scheme));
            }

            index = Params.IndexOfOutputParam("topRankedGeometry");
            if (index != -1)
            {
                dataAccess.SetDataList(index, SchemeGeometry(topVerified?.Scheme, targets));
            }

            index = Params.IndexOfOutputParam("rankedVerifiedSchemes");
            if (index != -1)
            {
                // The output contract is "every VERIFIED scheme, in RANKED order": rows that could
                // not be ranked (incomparable, not evaluated, not rankable) are reported elsewhere
                // and are excluded here so the list matches its name.
                List<GooSAMObject> verifiedSchemes = new List<GooSAMObject>();
                foreach (ShadingComparisonRow row in result.Rows)
                {
                    if (row.VerifiedResult != null && row.Rank >= 1)
                    {
                        verifiedSchemes.Add(new GooSAMObject(row.VerifiedResult));
                    }
                }

                dataAccess.SetDataList(index, verifiedSchemes);
            }

            index = Params.IndexOfOutputParam("ranks");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.Rank));
            }

            index = Params.IndexOfOutputParam("topRanked");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.TopRanked));
            }

            index = Params.IndexOfOutputParam("optionNames");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.OptionName));
            }

            index = Params.IndexOfOutputParam("statuses");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => StatusText(x)));
            }

            index = Params.IndexOfOutputParam("physicalDeviceCounts");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.PhysicalDeviceCount));
            }

            index = Params.IndexOfOutputParam("apertureCounts");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.ApertureCount));
            }

            index = Params.IndexOfOutputParam("baselineDirectSolar");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.BaselineDirectSolar));
            }

            index = Params.IndexOfOutputParam("unwantedSolarIntercepted");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.UnwantedSolarIntercepted));
            }

            index = Params.IndexOfOutputParam("wantedSolarBlocked");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.WantedSolarBlocked));
            }

            index = Params.IndexOfOutputParam("unwantedSolarBlocked");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => Query.Percentage(x.UnwantedSolarBlocked)));
            }

            index = Params.IndexOfOutputParam("wantedSolarRetained");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => Query.Percentage(x.WantedSolarRetained)));
            }

            index = Params.IndexOfOutputParam("physicalDeviceAreas");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.PhysicalDeviceArea));
            }

            index = Params.IndexOfOutputParam("materialFractions");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.MaterialFraction));
            }

            index = Params.IndexOfOutputParam("benefits");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.Benefit));
            }

            index = Params.IndexOfOutputParam("harms");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.Harm));
            }

            index = Params.IndexOfOutputParam("costs");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.Cost));
            }

            index = Params.IndexOfOutputParam("objectiveScores");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.ObjectiveScore));
            }

            index = Params.IndexOfOutputParam("scoreDeltaToTopRanked");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.ScoreDeltaToTopRanked));
            }

            index = Params.IndexOfOutputParam("scoreDeltaToNoShade");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.Rows.ConvertAll(x => x.ScoreDeltaToNoShade));
            }

            index = Params.IndexOfOutputParam("recommendationStatus");
            if (index != -1)
            {
                dataAccess.SetData(index, result.RecommendationStatus.ToString());
            }

            index = Params.IndexOfOutputParam("recommendationSummary");
            if (index != -1)
            {
                dataAccess.SetData(index, RecommendationBlock(result.ReportMarkdown));
            }

            index = Params.IndexOfOutputParam("openChecks");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.OpenChecks);
            }

            index = Params.IndexOfOutputParam("requiredBeforeFreeze");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result.RequiredBeforeFreeze);
            }

            index = Params.IndexOfOutputParam("reportMarkdown");
            if (index != -1)
            {
                dataAccess.SetData(index, result.ReportMarkdown);
            }

            index = Params.IndexOfOutputParam("reportCsv");
            if (index != -1)
            {
                dataAccess.SetData(index, result.ReportCsv);
            }

            index = Params.IndexOfOutputParam("reportGlossary");
            if (index != -1)
            {
                dataAccess.SetData(index, result.ReportGlossary);
            }

            index = Params.IndexOfOutputParam("comparisonComplete");
            if (index != -1)
            {
                dataAccess.SetData(index, result.ComparisonComplete);
            }

            index = Params.IndexOfOutputParam("baselineAnchored");
            if (index != -1)
            {
                dataAccess.SetData(index, result.BaselineAnchored);
            }

            index = Params.IndexOfOutputParam("gridConvergenceConfirmed");
            if (index != -1)
            {
                dataAccess.SetData(index, result.GridConvergenceConfirmed);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, true);
            }
        }

        private static string StatusText(ShadingComparisonRow row)
        {
            switch (row.Status)
            {
                case ShadingComparisonStatus.Ranked:
                    return row.DesignBudgetExhausted ? "OK (provisional)" : "OK";
                case ShadingComparisonStatus.NoShadeBaseline: return "NO SHADE";
                case ShadingComparisonStatus.Incomparable: return "INCOMPARABLE";
                case ShadingComparisonStatus.NotEvaluated: return "NOT EVALUATED";
                case ShadingComparisonStatus.NotRankable: return "NOT RANKABLE";
                default: return "UNDEFINED";
            }
        }

        private static string RecommendationBlock(string reportMarkdown)
        {
            if (reportMarkdown == null)
            {
                return null;
            }

            int start = reportMarkdown.IndexOf("## 1. Engineering Recommendation", StringComparison.Ordinal);
            int end = reportMarkdown.IndexOf("## 2. Scope and Analysis Basis", StringComparison.Ordinal);
            if (start == -1 || end == -1 || end <= start)
            {
                return null;
            }

            return reportMarkdown.Substring(start, end - start).TrimEnd();
        }
    }
}

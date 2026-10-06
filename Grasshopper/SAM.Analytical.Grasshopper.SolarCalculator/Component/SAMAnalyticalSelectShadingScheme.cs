// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using SAM.Analytical.Grasshopper.SolarCalculator.Properties;
using SAM.Analytical.SolarCalculator;
using SAM.Core.Grasshopper;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.Grasshopper.SolarCalculator
{
    public class SAMAnalyticalSelectShadingScheme : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("b3a71e42-9c06-4d5f-a3e9-1f8d2c4b6e05");

        /// <summary>The latest version of this component.</summary>
        public override string LatestComponentVersion => "1.0.0";

        /// <summary>Provides an Icon for the component.</summary>
        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        public SAMAnalyticalSelectShadingScheme()
          : base("SAMAnalytical.SelectShadingScheme", "SAMAnalytical.SelectShadingScheme",
              "SUMMARY\nTHE ENGINEERING DECISION: the explicit downstream step where an engineer chooses which shading scheme to build.\n\nThe comparison (SAMAnalytical.CompareShading) ranks; it does not preselect. Connect a scheme from AssembleShadingSchemes - identified by its SchemeGuid, never by name or rank, because ranks can change after recalculation while the scheme identity is deterministic - and this records who chose what and why.\n\nRULES\n  Only a verified, comparable and rankable scheme may be selected. An incomparable option must be re-run on the comparison basis first; a not-rankable one must have its missing material or scoring information resolved.\n  A reason is MANDATORY whenever the selected scheme is not rank 1; optional when it is.\n  The selection never changes scores, ranks or robustness calculations. It records a project decision based on criteria additional to the comparison objective - capital cost, maintenance, aesthetics, planning, structure, installation, product availability, operation and controls.\n  Where the alternative is preferred because lambda, mu or the desirability brief does not represent the project, revise those inputs and rerun the comparison rather than recording a manual override.\n  A NOT SEPARABLE result keeps its deterministic ranks for audit but produces no single analytical recommendation; the engineer may still select an option explicitly, with a reason.\n\nOUTPUTS\n  selectionDecision - the full decision record, including the comparison snapshot and the final report.\n  selectedScheme / selectedGeometry - the chosen scheme and its geometry.\n  selectedRank - the scheme's analytical rank.\n  selectionAlignment - AgreesWithLeader / DepartsFromLeader / LeaderNotDistinguishable.\n  decisionSummary - the ANALYTICAL RESULT / ENGINEERING DECISION block.\n  finalReportMarkdown - the comparison report with the engineering decision embedded.\n\nEXAMPLE\nCompareShading.comparisonResult → SelectShadingScheme._comparisonResult.\nAssembleShadingSchemes.shadingSchemes → SelectShadingScheme._shadingScheme - the scheme to build, matched by SchemeGuid, so the choice survives recalculation.\n_selectionReason_ is mandatory whenever the choice is not rank 1.\nSelectShadingScheme.selectedScheme → VerifyShading._shadingDevice (with _apertureSolarTargets_) to measure the chosen scheme. Do not wire it into ShadingOperation: a scheme is a whole-scope proposal, not one device, and ShadingOperation refuses it.",
              "SAM", "Solar")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_comparisonResult", NickName = "_comparisonResult", Description = "The comparisonResult from SAMAnalytical.CompareShading", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_shadingScheme", NickName = "_shadingScheme", Description = "The scheme to build, from SAMAnalytical.AssembleShadingSchemes. Matched by SchemeGuid", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "_selectionReason_", NickName = "_selectionReason_", Description = "Why this option. Optional when it is rank 1; MANDATORY for any other rank - the report carries an engineering decision rather than only a choice", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));

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
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "selectionDecision", NickName = "selectionDecision", Description = "The full decision record: the comparison snapshot, the chosen scheme identity, the alignment, the reason and the final report", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "selectedScheme", NickName = "selectedScheme", Description = "The chosen shading scheme", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Brep() { Name = "selectedGeometry", NickName = "selectedGeometry", Description = "The chosen scheme's geometry, rebuilt from its placements", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "selectedRank", NickName = "selectedRank", Description = "The scheme's analytical rank", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "selectionAlignment", NickName = "selectionAlignment", Description = "AgreesWithLeader / DepartsFromLeader / LeaderNotDistinguishable", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "decisionSummary", NickName = "decisionSummary", Description = "The ANALYTICAL RESULT / ENGINEERING DECISION block, with the alignment and - when the choice departs from rank 1 - the trade-off against the analytical leader", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "finalReportMarkdown", NickName = "finalReportMarkdown", Description = "The comparison report with the engineering decision embedded", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
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

            index = Params.IndexOfInputParam("_comparisonResult");
            GH_ObjectWrapper comparisonWrapper = null;
            ShadingComparisonResult comparisonResult = null;
            if (index == -1 || !dataAccess.GetData(index, ref comparisonWrapper) || comparisonWrapper?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply the comparisonResult from SAMAnalytical.CompareShading.");
                return;
            }

            comparisonResult = Query.Value<ShadingComparisonResult>(comparisonWrapper);
            if (comparisonResult == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_comparisonResult is not a shading comparison result.");
                return;
            }

            index = Params.IndexOfInputParam("_shadingScheme");
            GH_ObjectWrapper schemeWrapper = null;
            ShadingScheme scheme = null;
            if (index == -1 || !dataAccess.GetData(index, ref schemeWrapper) || schemeWrapper?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply the shading scheme to select, from SAMAnalytical.AssembleShadingSchemes.");
                return;
            }

            scheme = Query.Value<ShadingScheme>(schemeWrapper);
            if (scheme == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_shadingScheme is not a shading scheme.");
                return;
            }

            string reason = null;
            index = Params.IndexOfInputParam("_selectionReason_");
            if (index != -1)
            {
                string supplied = null;
                if (dataAccess.GetData(index, ref supplied) && !string.IsNullOrWhiteSpace(supplied))
                {
                    reason = supplied.Trim();
                }
            }

            ShadingSelectionDecision decision = comparisonResult.SelectShadingScheme(scheme, reason);

            if (!decision.Successful)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, decision.Message);
                return;
            }

            index = Params.IndexOfOutputParam("selectionDecision");
            if (index != -1)
            {
                dataAccess.SetData(index, new GooSAMObject(decision));
            }

            index = Params.IndexOfOutputParam("selectedScheme");
            if (index != -1)
            {
                dataAccess.SetData(index, new GooSAMObject(scheme));
            }

            index = Params.IndexOfOutputParam("selectedGeometry");
            if (index != -1)
            {
                dataAccess.SetDataList(index, Geometry(scheme, comparisonResult.Targets));
            }

            index = Params.IndexOfOutputParam("selectedRank");
            if (index != -1)
            {
                dataAccess.SetData(index, decision.SelectedRank);
            }

            index = Params.IndexOfOutputParam("selectionAlignment");
            if (index != -1)
            {
                dataAccess.SetData(index, decision.SelectionAlignment.ToString());
            }

            index = Params.IndexOfOutputParam("decisionSummary");
            if (index != -1)
            {
                dataAccess.SetData(index, decision.DecisionSummary);
            }

            index = Params.IndexOfOutputParam("finalReportMarkdown");
            if (index != -1)
            {
                dataAccess.SetData(index, decision.FinalReportMarkdown);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, true);
            }
        }

        internal static List<Brep> Geometry(ShadingScheme scheme, List<ApertureSolarTarget> targets)
        {
            return SAMAnalyticalCompareShading.SchemeGeometry(scheme, targets);
        }
    }
}

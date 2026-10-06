// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// The ENGINEERING SELECTION: the explicit downstream record of which scheme an engineer chose
    /// to build, separate from the analytical ranking.
    ///
    /// THE SOFTWARE RANKS; THE ENGINEER DECIDES AND RECORDS WHY. The comparison always reports the
    /// analytical leader (arithmetic rank 1); this decision records the option the engineer
    /// actually selected, identified by SchemeGuid — never by name or rank index, because ranks can
    /// change after recalculation while the scheme identity is deterministic.
    ///
    /// RULES.
    ///   - Only a verified, comparable and rankable scheme may be selected.
    ///   - A reason is MANDATORY whenever the selected scheme is not rank 1; optional otherwise.
    ///   - The selection never changes scores, ranks or robustness calculations.
    ///   - If a recalculation removes or changes the selected SchemeGuid, the old selection is
    ///     invalidated rather than silently re-pointed at the option now occupying the same rank.
    /// </summary>
    public class ShadingSelectionDecision : IJSAMObject, ISolarObject
    {
        private ShadingComparisonResult comparisonResult;
        private Guid selectedSchemeGuid;
        private int selectedRank = -1;
        private ShadingSelectionAlignment selectionAlignment = ShadingSelectionAlignment.None;
        private string selectionReason;
        private string decisionSummary;
        private string finalReportMarkdown;
        private bool successful;
        private string message;

        public ShadingSelectionDecision()
        {
        }

        public ShadingSelectionDecision(ShadingSelectionDecision shadingSelectionDecision)
        {
            if (shadingSelectionDecision != null)
            {
                comparisonResult = shadingSelectionDecision.comparisonResult == null ? null : new ShadingComparisonResult(shadingSelectionDecision.comparisonResult);
                selectedSchemeGuid = shadingSelectionDecision.selectedSchemeGuid;
                selectedRank = shadingSelectionDecision.selectedRank;
                selectionAlignment = shadingSelectionDecision.selectionAlignment;
                selectionReason = shadingSelectionDecision.selectionReason;
                decisionSummary = shadingSelectionDecision.decisionSummary;
                finalReportMarkdown = shadingSelectionDecision.finalReportMarkdown;
                successful = shadingSelectionDecision.successful;
                message = shadingSelectionDecision.message;
            }
        }

        public ShadingSelectionDecision(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>The comparison the selection was made against (snapshot).</summary>
        public ShadingComparisonResult ComparisonResult { get { return comparisonResult == null ? null : new ShadingComparisonResult(comparisonResult); } internal set { comparisonResult = value == null ? null : new ShadingComparisonResult(value); } }

        public Guid SelectedSchemeGuid { get { return selectedSchemeGuid; } internal set { selectedSchemeGuid = value; } }

        /// <summary>The selected scheme's analytical rank; -1 when the scheme could not be ranked.</summary>
        public int SelectedRank { get { return selectedRank; } internal set { selectedRank = value; } }

        public ShadingSelectionAlignment SelectionAlignment { get { return selectionAlignment; } internal set { selectionAlignment = value; } }

        /// <summary>The engineer's recorded reason. Empty only for rank 1 selections.</summary>
        public string SelectionReason { get { return selectionReason; } internal set { selectionReason = value; } }

        public string DecisionSummary { get { return decisionSummary; } internal set { decisionSummary = value; } }

        /// <summary>The comparison report with the engineering decision embedded.</summary>
        public string FinalReportMarkdown { get { return finalReportMarkdown; } internal set { finalReportMarkdown = value; } }

        public bool Successful { get { return successful; } internal set { successful = value; } }

        /// <summary>Null on success; the refusal reason otherwise.</summary>
        public string Message { get { return message; } internal set { message = value; } }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            comparisonResult = jObject.ContainsKey("ComparisonResult") ? new ShadingComparisonResult(jObject["ComparisonResult"] as JsonObject) : null;
            if (jObject.ContainsKey("SelectedSchemeGuid")) { Guid.TryParse(jObject["SelectedSchemeGuid"]?.GetValue<string>(), out selectedSchemeGuid); }
            if (jObject.ContainsKey("SelectedRank")) { selectedRank = jObject["SelectedRank"]?.GetValue<int>() ?? -1; }
            if (jObject.ContainsKey("SelectionAlignment")) { Enum.TryParse(jObject["SelectionAlignment"]?.GetValue<string>(), out selectionAlignment); }
            if (jObject.ContainsKey("SelectionReason")) { selectionReason = jObject["SelectionReason"]?.GetValue<string>(); }
            if (jObject.ContainsKey("DecisionSummary")) { decisionSummary = jObject["DecisionSummary"]?.GetValue<string>(); }
            if (jObject.ContainsKey("FinalReportMarkdown")) { finalReportMarkdown = jObject["FinalReportMarkdown"]?.GetValue<string>(); }
            if (jObject.ContainsKey("Successful")) { successful = jObject["Successful"]?.GetValue<bool>() ?? false; }
            if (jObject.ContainsKey("Message")) { message = jObject["Message"]?.GetValue<string>(); }
            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            if (comparisonResult != null) { jObject.Add("ComparisonResult", comparisonResult.ToJsonObject()); }
            jObject.Add("SelectedSchemeGuid", selectedSchemeGuid.ToString());
            jObject.Add("SelectedRank", selectedRank);
            jObject.Add("SelectionAlignment", selectionAlignment.ToString());
            if (selectionReason != null) { jObject.Add("SelectionReason", selectionReason); }
            if (decisionSummary != null) { jObject.Add("DecisionSummary", decisionSummary); }
            if (finalReportMarkdown != null) { jObject.Add("FinalReportMarkdown", finalReportMarkdown); }
            jObject.Add("Successful", successful);
            if (message != null) { jObject.Add("Message", message); }
            return jObject;
        }
    }
}

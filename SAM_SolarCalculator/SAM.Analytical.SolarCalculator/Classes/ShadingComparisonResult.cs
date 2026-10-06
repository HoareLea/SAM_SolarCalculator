// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>One decision-facing input of the comparison, with where its value came from and how much the ranking hinges on it.</summary>
    public class ShadingProjectInput : IJSAMObject, ISolarObject
    {
        private string name;
        private string valueText;
        private string source;
        private string classification;

        public ShadingProjectInput(string name, string valueText, string source, string classification)
        {
            this.name = name;
            this.valueText = valueText;
            this.source = source;
            this.classification = classification;
        }

        public ShadingProjectInput(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public string Name { get { return name; } }

        public string ValueText { get { return valueText; } }

        /// <summary>DEFAULT / PROJECT / SUPPLIED.</summary>
        public string Source { get { return source; } }

        /// <summary>decision-sensitive / decision-stable / not sensitivity-tested / coarser than recommended.</summary>
        public string Classification { get { return classification; } }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("Name")) { name = jObject["Name"]?.GetValue<string>(); }
            if (jObject.ContainsKey("ValueText")) { valueText = jObject["ValueText"]?.GetValue<string>(); }
            if (jObject.ContainsKey("Source")) { source = jObject["Source"]?.GetValue<string>(); }
            if (jObject.ContainsKey("Classification")) { classification = jObject["Classification"]?.GetValue<string>(); }
            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            if (name != null) { jObject.Add("Name", name); }
            if (valueText != null) { jObject.Add("ValueText", valueText); }
            if (source != null) { jObject.Add("Source", source); }
            if (classification != null) { jObject.Add("Classification", classification); }
            return jObject;
        }
    }

    /// <summary>
    /// The complete comparison: every supplied scheme verified on one basis, the deterministic
    /// ranking, the analytical leader (arithmetic rank 1 — NOT an engineering selection), the
    /// recommendation status, and the generated report.
    ///
    /// THE SOFTWARE RANKS; THE ENGINEER SELECTS. Rank 1 is the analytical leader and is always
    /// populated when a rankable row exists, at every recommendation status including
    /// <see cref="ShadingRecommendationStatus.Indeterminate"/>. Choosing a scheme to build is a
    /// separate downstream step (SelectShadingScheme) which records who chose what and why. A manual
    /// selection never changes scores, ranks or robustness calculations.
    /// </summary>
    public class ShadingComparisonResult : IJSAMObject, ISolarObject
    {
        private List<ShadingComparisonRow> rows = new List<ShadingComparisonRow>();
        private ShadingComparisonOutcome outcome = ShadingComparisonOutcome.Undefined;
        private ShadingRecommendationStatus recommendationStatus = ShadingRecommendationStatus.Undefined;
        private bool incomplete;
        private int tieBreakLevel;
        private bool baselineAnchored;
        private ShadingAnalysisSignature referenceSignature;
        private ShadingObjective objective;
        private ShadingAnalysisHours hours;
        private int suppliedCount;
        private int verifiedCount;
        private int rankedCount;
        private int incomparableCount;
        private int notEvaluatedCount;
        private int notRankableCount;
        private List<string> openChecks = new List<string>();
        private List<string> requiredBeforeFreeze = new List<string>();
        private List<ShadingProjectInput> projectInputs = new List<ShadingProjectInput>();
        private string message;

        // The display context the report needs, carried so the report (and the downstream selection
        // final report) is fully regenerable from this object alone.
        private List<ApertureSolarTarget> targets = new List<ApertureSolarTarget>();
        private string modelName;
        private string siteDescription;
        private string weatherDescription;

        // Report output: deterministic per run (I11), but never serialised — the version stamp is a
        // build property, and the structured form must stay byte-stable across builds (§12.10).
        private string reportMarkdown;
        private string reportCsv;
        private string reportGlossary;

        public ShadingComparisonResult()
        {
        }

        public ShadingComparisonResult(ShadingComparisonResult shadingComparisonResult)
        {
            if (shadingComparisonResult != null)
            {
                rows = shadingComparisonResult.rows.ConvertAll(x => x == null ? null : new ShadingComparisonRow(x));
                outcome = shadingComparisonResult.outcome;
                recommendationStatus = shadingComparisonResult.recommendationStatus;
                incomplete = shadingComparisonResult.incomplete;
                tieBreakLevel = shadingComparisonResult.tieBreakLevel;
                baselineAnchored = shadingComparisonResult.baselineAnchored;
                referenceSignature = shadingComparisonResult.referenceSignature == null ? null : new ShadingAnalysisSignature(shadingComparisonResult.referenceSignature);
                objective = shadingComparisonResult.objective == null ? null : new ShadingObjective(shadingComparisonResult.objective);
                hours = shadingComparisonResult.hours == null ? null : new ShadingAnalysisHours(shadingComparisonResult.hours);
                suppliedCount = shadingComparisonResult.suppliedCount;
                verifiedCount = shadingComparisonResult.verifiedCount;
                rankedCount = shadingComparisonResult.rankedCount;
                incomparableCount = shadingComparisonResult.incomparableCount;
                notEvaluatedCount = shadingComparisonResult.notEvaluatedCount;
                notRankableCount = shadingComparisonResult.notRankableCount;
                openChecks = new List<string>(shadingComparisonResult.openChecks);
                requiredBeforeFreeze = new List<string>(shadingComparisonResult.requiredBeforeFreeze);
                projectInputs = shadingComparisonResult.projectInputs.ConvertAll(x => x == null ? null : new ShadingProjectInput(x.Name, x.ValueText, x.Source, x.Classification));
                message = shadingComparisonResult.message;
                targets = shadingComparisonResult.targets.ConvertAll(x => x == null ? null : new ApertureSolarTarget(x));
                modelName = shadingComparisonResult.modelName;
                siteDescription = shadingComparisonResult.siteDescription;
                weatherDescription = shadingComparisonResult.weatherDescription;
                reportMarkdown = shadingComparisonResult.reportMarkdown;
                reportCsv = shadingComparisonResult.reportCsv;
                reportGlossary = shadingComparisonResult.reportGlossary;
            }
        }

        public ShadingComparisonResult(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>Every supplied scheme, ranked block first (best first) then the non-ranked block (§11.3).</summary>
        public List<ShadingComparisonRow> Rows { get { return rows.ConvertAll(x => x == null ? null : new ShadingComparisonRow(x)); } internal set { rows = value.ConvertAll(x => x == null ? null : new ShadingComparisonRow(x)); } }

        public ShadingComparisonRow Row(Guid schemeGuid)
        {
            return rows.Find(x => x != null && x.SchemeGuid == schemeGuid);
        }

        /// <summary>The analytical leader row: arithmetic rank 1. Null when no row is rankable.</summary>
        public ShadingComparisonRow TopRankedRow { get { return rows.Find(x => x != null && x.TopRanked); } }

        /// <summary>The analytical leader's verified result. Null when no row is rankable.</summary>
        public VerifiedShadingSchemeResult TopRankedVerifiedScheme
        {
            get
            {
                ShadingComparisonRow row = TopRankedRow;
                return row?.VerifiedResult;
            }
        }

        public Guid TopRankedSchemeGuid
        {
            get
            {
                ShadingComparisonRow row = TopRankedRow;
                return row == null ? Guid.Empty : row.SchemeGuid;
            }
        }

        public ShadingComparisonOutcome Outcome { get { return outcome; } internal set { outcome = value; } }

        public ShadingRecommendationStatus RecommendationStatus { get { return recommendationStatus; } internal set { recommendationStatus = value; } }

        /// <summary>True when any Incomparable / NotEvaluated / NotRankable row exists.</summary>
        public bool Incomplete { get { return incomplete; } internal set { incomplete = value; } }

        /// <summary>Of the top-ranked row against the runner-up; 0 when there is no tie to break.</summary>
        public int TieBreakLevel { get { return tieBreakLevel; } internal set { tieBreakLevel = value; } }

        /// <summary>True when the No Shade scheme was present and its baseline anchored the check.</summary>
        public bool BaselineAnchored { get { return baselineAnchored; } internal set { baselineAnchored = value; } }

        /// <summary>The basis every ranked row was measured on.</summary>
        public ShadingAnalysisSignature ReferenceSignature { get { return referenceSignature == null ? null : new ShadingAnalysisSignature(referenceSignature); } internal set { referenceSignature = value == null ? null : new ShadingAnalysisSignature(value); } }

        /// <summary>The objective every row was scored under.</summary>
        public ShadingObjective Objective { get { return objective == null ? null : new ShadingObjective(objective); } internal set { objective = value == null ? null : new ShadingObjective(value); } }

        public ShadingAnalysisHours Hours { get { return hours == null ? null : new ShadingAnalysisHours(hours); } internal set { hours = value == null ? null : new ShadingAnalysisHours(value); } }

        public int SuppliedCount { get { return suppliedCount; } internal set { suppliedCount = value; } }

        public int VerifiedCount { get { return verifiedCount; } internal set { verifiedCount = value; } }

        public int RankedCount { get { return rankedCount; } internal set { rankedCount = value; } }

        public int IncomparableCount { get { return incomparableCount; } internal set { incomparableCount = value; } }

        public int NotEvaluatedCount { get { return notEvaluatedCount; } internal set { notEvaluatedCount = value; } }

        public int NotRankableCount { get { return notRankableCount; } internal set { notRankableCount = value; } }

        /// <summary>One entry per open check that made the recommendation Provisional.</summary>
        public List<string> OpenChecks { get { return new List<string>(openChecks); } internal set { openChecks = new List<string>(value ?? new List<string>()); } }

        /// <summary>The actionable inverse of OpenChecks: one numbered step per check.</summary>
        public List<string> RequiredBeforeFreeze { get { return new List<string>(requiredBeforeFreeze); } internal set { requiredBeforeFreeze = new List<string>(value ?? new List<string>()); } }

        /// <summary>The decision-facing inputs: value, source and sensitivity classification.</summary>
        public List<ShadingProjectInput> ProjectInputs { get { return projectInputs.ConvertAll(x => x == null ? null : new ShadingProjectInput(x.Name, x.ValueText, x.Source, x.Classification)); } internal set { projectInputs = value.ConvertAll(x => x == null ? null : new ShadingProjectInput(x.Name, x.ValueText, x.Source, x.Classification)); } }

        /// <summary>The scope targets, so the report is fully regenerable from this object alone.</summary>
        public List<ApertureSolarTarget> Targets { get { return targets.ConvertAll(x => x == null ? null : new ApertureSolarTarget(x)); } internal set { targets = value.ConvertAll(x => x == null ? null : new ApertureSolarTarget(x)); } }

        public string ModelName { get { return modelName; } internal set { modelName = value; } }

        public string SiteDescription { get { return siteDescription; } internal set { siteDescription = value; } }

        public string WeatherDescription { get { return weatherDescription; } internal set { weatherDescription = value; } }

        /// <summary>Null on success; the refusal reason when the comparison could not be built at all.</summary>
        public string Message { get { return message; } internal set { message = value; } }

        public string ReportMarkdown { get { return reportMarkdown; } internal set { reportMarkdown = value; } }

        public string ReportCsv { get { return reportCsv; } internal set { reportCsv = value; } }

        public string ReportGlossary { get { return reportGlossary; } internal set { reportGlossary = value; } }

        public bool ComparisonComplete { get { return !incomplete; } }

        /// <summary>False unless a finer-grid repeat was actually run and compared — which the comparison does not automate.</summary>
        public bool GridConvergenceConfirmed { get { return false; } }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            rows = new List<ShadingComparisonRow>();
            if (jObject.ContainsKey("Rows") && jObject["Rows"] is JsonArray rowsArray)
            {
                foreach (JsonNode node in rowsArray)
                {
                    if (node is JsonObject rowObject)
                    {
                        rows.Add(new ShadingComparisonRow(rowObject));
                    }
                }
            }

            if (jObject.ContainsKey("Outcome")) { Enum.TryParse(jObject["Outcome"]?.GetValue<string>(), out outcome); }
            if (jObject.ContainsKey("RecommendationStatus")) { Enum.TryParse(jObject["RecommendationStatus"]?.GetValue<string>(), out recommendationStatus); }
            if (jObject.ContainsKey("Incomplete")) { incomplete = jObject["Incomplete"]?.GetValue<bool>() ?? false; }
            if (jObject.ContainsKey("TieBreakLevel")) { tieBreakLevel = jObject["TieBreakLevel"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("BaselineAnchored")) { baselineAnchored = jObject["BaselineAnchored"]?.GetValue<bool>() ?? false; }
            referenceSignature = jObject.ContainsKey("ReferenceSignature") ? new ShadingAnalysisSignature(jObject["ReferenceSignature"] as JsonObject) : null;
            objective = jObject.ContainsKey("Objective") ? new ShadingObjective(jObject["Objective"] as JsonObject) : null;
            hours = jObject.ContainsKey("Hours") ? new ShadingAnalysisHours(jObject["Hours"] as JsonObject) : null;
            if (jObject.ContainsKey("SuppliedCount")) { suppliedCount = jObject["SuppliedCount"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("VerifiedCount")) { verifiedCount = jObject["VerifiedCount"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("RankedCount")) { rankedCount = jObject["RankedCount"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("IncomparableCount")) { incomparableCount = jObject["IncomparableCount"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("NotEvaluatedCount")) { notEvaluatedCount = jObject["NotEvaluatedCount"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("NotRankableCount")) { notRankableCount = jObject["NotRankableCount"]?.GetValue<int>() ?? default; }

            openChecks = ReadStrings(jObject, "OpenChecks");
            requiredBeforeFreeze = ReadStrings(jObject, "RequiredBeforeFreeze");

            projectInputs = new List<ShadingProjectInput>();
            if (jObject.ContainsKey("ProjectInputs") && jObject["ProjectInputs"] is JsonArray inputsArray)
            {
                foreach (JsonNode node in inputsArray)
                {
                    if (node is JsonObject inputObject)
                    {
                        projectInputs.Add(new ShadingProjectInput(inputObject));
                    }
                }
            }

            targets = new List<ApertureSolarTarget>();
            if (jObject.ContainsKey("Targets") && jObject["Targets"] is JsonArray targetsArray)
            {
                foreach (JsonNode node in targetsArray)
                {
                    if (node is JsonObject targetObject)
                    {
                        targets.Add(new ApertureSolarTarget(targetObject));
                    }
                }
            }

            if (jObject.ContainsKey("ModelName")) { modelName = jObject["ModelName"]?.GetValue<string>(); }
            if (jObject.ContainsKey("SiteDescription")) { siteDescription = jObject["SiteDescription"]?.GetValue<string>(); }
            if (jObject.ContainsKey("WeatherDescription")) { weatherDescription = jObject["WeatherDescription"]?.GetValue<string>(); }
            if (jObject.ContainsKey("Message")) { message = jObject["Message"]?.GetValue<string>(); }

            return true;
        }

        private static List<string> ReadStrings(JsonObject jObject, string name)
        {
            List<string> result = new List<string>();
            if (jObject.ContainsKey(name) && jObject[name] is JsonArray jArray)
            {
                foreach (JsonNode node in jArray)
                {
                    result.Add(node?.GetValue<string>());
                }
            }

            return result;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            JsonArray rowsArray = new JsonArray();
            foreach (ShadingComparisonRow row in rows)
            {
                rowsArray.Add(row?.ToJsonObject());
            }
            jObject.Add("Rows", rowsArray);

            jObject.Add("Outcome", outcome.ToString());
            jObject.Add("RecommendationStatus", recommendationStatus.ToString());
            jObject.Add("Incomplete", incomplete);
            jObject.Add("TieBreakLevel", tieBreakLevel);
            jObject.Add("BaselineAnchored", baselineAnchored);
            if (referenceSignature != null) { jObject.Add("ReferenceSignature", referenceSignature.ToJsonObject()); }
            if (objective != null) { jObject.Add("Objective", objective.ToJsonObject()); }
            if (hours != null) { jObject.Add("Hours", hours.ToJsonObject()); }
            jObject.Add("SuppliedCount", suppliedCount);
            jObject.Add("VerifiedCount", verifiedCount);
            jObject.Add("RankedCount", rankedCount);
            jObject.Add("IncomparableCount", incomparableCount);
            jObject.Add("NotEvaluatedCount", notEvaluatedCount);
            jObject.Add("NotRankableCount", notRankableCount);

            JsonArray checksArray = new JsonArray();
            foreach (string check in openChecks)
            {
                checksArray.Add(check);
            }
            jObject.Add("OpenChecks", checksArray);

            JsonArray freezeArray = new JsonArray();
            foreach (string step in requiredBeforeFreeze)
            {
                freezeArray.Add(step);
            }
            jObject.Add("RequiredBeforeFreeze", freezeArray);

            JsonArray inputsArray = new JsonArray();
            foreach (ShadingProjectInput input in projectInputs)
            {
                inputsArray.Add(input?.ToJsonObject());
            }
            jObject.Add("ProjectInputs", inputsArray);

            JsonArray targetsArray = new JsonArray();
            foreach (ApertureSolarTarget target in targets)
            {
                targetsArray.Add(target?.ToJsonObject());
            }
            jObject.Add("Targets", targetsArray);

            if (modelName != null) { jObject.Add("ModelName", modelName); }
            if (siteDescription != null) { jObject.Add("SiteDescription", siteDescription); }
            if (weatherDescription != null) { jObject.Add("WeatherDescription", weatherDescription); }
            if (message != null) { jObject.Add("Message", message); }
            return jObject;
        }
    }
}

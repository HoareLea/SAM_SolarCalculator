// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>How one supplied scheme fared in the comparison.</summary>
    public enum ShadingComparisonStatus
    {
        Undefined,

        /// <summary>Verified, comparable, score usable.</summary>
        Ranked,

        /// <summary>The zero-device row; also ranked, score exactly 0.</summary>
        NoShadeBaseline,

        /// <summary>Verified on a different analysis basis. ComparabilityReason set.</summary>
        Incomparable,

        /// <summary>Verification failed.</summary>
        NotEvaluated,

        /// <summary>Verified and comparable, but the score cannot be used.</summary>
        NotRankable,
    }

    /// <summary>The arithmetic outcome of the comparison — rank 1, whatever an engineer later chooses.</summary>
    public enum ShadingComparisonOutcome
    {
        Undefined,

        /// <summary>A rankable option exists; rank 1 is the analytical leader.</summary>
        TopRanked,

        /// <summary>The No Shade baseline is rank 1: build nothing.</summary>
        NoShadeTopRanked,

        /// <summary>Nothing was comparable and rankable.</summary>
        NoComparableOptions,
    }

    /// <summary>
    /// Whether an engineer should ACT on rank 1. The ranking itself is never suppressed — rank 1
    /// always answers the factual question "which supplied option has the highest verified score
    /// under the stated model, objective and resolution?" — this status says how much to trust it.
    /// </summary>
    public enum ShadingRecommendationStatus
    {
        Undefined,

        /// <summary>
        /// Rank 1 is suitable to take forward on the evidence modelled. Reserved for a comparison
        /// where the required convergence checks have been confirmed; a single-grid comparison
        /// without such confirmation remains <see cref="Provisional"/>.
        /// </summary>
        Ready,

        /// <summary>Rank 1 stands, but one or more material checks remain open.</summary>
        Provisional,

        /// <summary>A ranking exists mathematically, but the analysis cannot defensibly distinguish the leading options.</summary>
        Indeterminate,

        /// <summary>No Shade is rank 1: the recommendation is to build nothing.</summary>
        NoShadingRecommended,

        /// <summary>Nothing was comparable and rankable.</summary>
        NoDecision,
    }

    /// <summary>How the engineer's selection sits against the analytical leader.</summary>
    public enum ShadingSelectionAlignment
    {
        /// <summary>No selection recorded.</summary>
        None,

        /// <summary>The engineer chose the analytical leader (rank 1).</summary>
        AgreesWithLeader,

        /// <summary>The engineer chose a lower-ranked, still rankable option.</summary>
        DepartsFromLeader,

        /// <summary>The leading options could not be separated; the selection is an explicit engineering choice.</summary>
        LeaderNotDistinguishable,
    }

    /// <summary>
    /// One scheme's row in the comparison: every quantity the report prints, in the order the CSV
    /// prints them. The verified energies come from the whole-scheme measurement; the design-time
    /// fields are provenance, never ranking inputs.
    /// </summary>
    public class ShadingComparisonRow : IJSAMObject, ISolarObject
    {
        // Identity and status
        private int rank = -1;
        private bool topRanked;
        private bool engineerSelected;
        private Guid schemeGuid;
        private string optionName;
        private string designMethod;
        private string typologyNames;
        private string productPreset;
        private ShadingComparisonStatus status = ShadingComparisonStatus.Undefined;
        private string comparabilityReason;
        private int physicalDeviceCount;
        private int apertureCount;
        private List<Guid> apertureGuids = new List<Guid>();

        // Energies
        private double baselineDirectSolar = double.NaN;
        private double admittedUnwantedSolar = double.NaN;
        private double admittedWantedSolar = double.NaN;
        private double admittedNeutralSolar = double.NaN;
        private double directSolarIntercepted = double.NaN;
        private double unwantedSolarIntercepted = double.NaN;
        private double wantedSolarBlocked = double.NaN;
        private double neutralSolarIntercepted = double.NaN;
        private double unattributedEnergy = double.NaN;
        private double directShadingEfficiency = double.NaN;
        private double unwantedSolarBlocked = double.NaN;
        private double wantedSolarRetained = double.NaN;

        // Material
        private double physicalDeviceArea = double.NaN;
        private double materialFraction = double.NaN;
        private bool materialAvailable;

        // Objective
        private double benefit = double.NaN;
        private double wantedSolarPenalty = double.NaN;
        private double harm = double.NaN;
        private double materialPenalty = double.NaN;
        private double cost = double.NaN;
        private double objectiveScore = double.NaN;
        private double designTimeScore = double.NaN;
        private bool designObjectiveMatchesComparisonObjective;
        private double scoreDeltaToTopRanked = double.NaN;
        private double scoreDeltaToNoShade = double.NaN;
        private int tieBreakLevel;
        private double breakEvenWantedSolarPenalty = double.NaN;
        private double breakEvenMaterialPenalty = double.NaN;
        private double unwantedInterceptedPerGrossArea = double.NaN;
        private double objectiveScorePerGrossArea = double.NaN;

        // Design provenance
        private int designEvaluations;
        private string designTermination;
        private bool designBudgetExhausted;
        private List<string> warnings = new List<string>();

        // Carried with the row for the report
        private VerifiedShadingSchemeResult verifiedResult;

        public ShadingComparisonRow()
        {
        }

        public ShadingComparisonRow(ShadingComparisonRow shadingComparisonRow)
        {
            if (shadingComparisonRow == null)
            {
                return;
            }

            rank = shadingComparisonRow.rank;
            topRanked = shadingComparisonRow.topRanked;
            engineerSelected = shadingComparisonRow.engineerSelected;
            schemeGuid = shadingComparisonRow.schemeGuid;
            optionName = shadingComparisonRow.optionName;
            designMethod = shadingComparisonRow.designMethod;
            typologyNames = shadingComparisonRow.typologyNames;
            productPreset = shadingComparisonRow.productPreset;
            status = shadingComparisonRow.status;
            comparabilityReason = shadingComparisonRow.comparabilityReason;
            physicalDeviceCount = shadingComparisonRow.physicalDeviceCount;
            apertureCount = shadingComparisonRow.apertureCount;
            apertureGuids = new List<Guid>(shadingComparisonRow.apertureGuids);

            baselineDirectSolar = shadingComparisonRow.baselineDirectSolar;
            admittedUnwantedSolar = shadingComparisonRow.admittedUnwantedSolar;
            admittedWantedSolar = shadingComparisonRow.admittedWantedSolar;
            admittedNeutralSolar = shadingComparisonRow.admittedNeutralSolar;
            directSolarIntercepted = shadingComparisonRow.directSolarIntercepted;
            unwantedSolarIntercepted = shadingComparisonRow.unwantedSolarIntercepted;
            wantedSolarBlocked = shadingComparisonRow.wantedSolarBlocked;
            neutralSolarIntercepted = shadingComparisonRow.neutralSolarIntercepted;
            unattributedEnergy = shadingComparisonRow.unattributedEnergy;
            directShadingEfficiency = shadingComparisonRow.directShadingEfficiency;
            unwantedSolarBlocked = shadingComparisonRow.unwantedSolarBlocked;
            wantedSolarRetained = shadingComparisonRow.wantedSolarRetained;

            physicalDeviceArea = shadingComparisonRow.physicalDeviceArea;
            materialFraction = shadingComparisonRow.materialFraction;
            materialAvailable = shadingComparisonRow.materialAvailable;

            benefit = shadingComparisonRow.benefit;
            wantedSolarPenalty = shadingComparisonRow.wantedSolarPenalty;
            harm = shadingComparisonRow.harm;
            materialPenalty = shadingComparisonRow.materialPenalty;
            cost = shadingComparisonRow.cost;
            objectiveScore = shadingComparisonRow.objectiveScore;
            designTimeScore = shadingComparisonRow.designTimeScore;
            designObjectiveMatchesComparisonObjective = shadingComparisonRow.designObjectiveMatchesComparisonObjective;
            scoreDeltaToTopRanked = shadingComparisonRow.scoreDeltaToTopRanked;
            scoreDeltaToNoShade = shadingComparisonRow.scoreDeltaToNoShade;
            tieBreakLevel = shadingComparisonRow.tieBreakLevel;
            breakEvenWantedSolarPenalty = shadingComparisonRow.breakEvenWantedSolarPenalty;
            breakEvenMaterialPenalty = shadingComparisonRow.breakEvenMaterialPenalty;
            unwantedInterceptedPerGrossArea = shadingComparisonRow.unwantedInterceptedPerGrossArea;
            objectiveScorePerGrossArea = shadingComparisonRow.objectiveScorePerGrossArea;

            designEvaluations = shadingComparisonRow.designEvaluations;
            designTermination = shadingComparisonRow.designTermination;
            designBudgetExhausted = shadingComparisonRow.designBudgetExhausted;
            warnings = new List<string>(shadingComparisonRow.warnings);
            verifiedResult = shadingComparisonRow.verifiedResult;
        }

        public ShadingComparisonRow(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        // ----------------------------------------------------------------- identity ----

        /// <summary>1-based rank among rankable rows; -1 when the row is not ranked.</summary>
        public int Rank { get { return rank; } internal set { rank = value; } }

        /// <summary>True when this row is the analytical leader: arithmetic rank 1. NOT an engineering selection.</summary>
        public bool TopRanked { get { return topRanked; } internal set { topRanked = value; } }

        /// <summary>True when an engineer has explicitly selected this scheme downstream (SelectShadingScheme).</summary>
        public bool EngineerSelected { get { return engineerSelected; } internal set { engineerSelected = value; } }

        public Guid SchemeGuid { get { return schemeGuid; } internal set { schemeGuid = value; } }

        public string OptionName { get { return optionName; } internal set { optionName = value; } }

        public string DesignMethod { get { return designMethod; } internal set { designMethod = value; } }

        public string TypologyNames { get { return typologyNames; } internal set { typologyNames = value; } }

        public string ProductPreset { get { return productPreset; } internal set { productPreset = value; } }

        public ShadingComparisonStatus Status { get { return status; } internal set { status = value; } }

        public string ComparabilityReason { get { return comparabilityReason; } internal set { comparabilityReason = value; } }

        public int PhysicalDeviceCount { get { return physicalDeviceCount; } internal set { physicalDeviceCount = value; } }

        public int ApertureCount { get { return apertureCount; } internal set { apertureCount = value; } }

        public List<Guid> ApertureGuids { get { return new List<Guid>(apertureGuids); } internal set { apertureGuids = new List<Guid>(value ?? new List<Guid>()); } }

        // ---------------------------------------------------------------- energies ----

        public double BaselineDirectSolar { get { return baselineDirectSolar; } internal set { baselineDirectSolar = value; } }

        public double AdmittedUnwantedSolar { get { return admittedUnwantedSolar; } internal set { admittedUnwantedSolar = value; } }

        public double AdmittedWantedSolar { get { return admittedWantedSolar; } internal set { admittedWantedSolar = value; } }

        public double AdmittedNeutralSolar { get { return admittedNeutralSolar; } internal set { admittedNeutralSolar = value; } }

        public double DirectSolarIntercepted { get { return directSolarIntercepted; } internal set { directSolarIntercepted = value; } }

        public double UnwantedSolarIntercepted { get { return unwantedSolarIntercepted; } internal set { unwantedSolarIntercepted = value; } }

        public double WantedSolarBlocked { get { return wantedSolarBlocked; } internal set { wantedSolarBlocked = value; } }

        public double NeutralSolarIntercepted { get { return neutralSolarIntercepted; } internal set { neutralSolarIntercepted = value; } }

        public double UnattributedEnergy { get { return unattributedEnergy; } internal set { unattributedEnergy = value; } }

        public double DirectShadingEfficiency { get { return directShadingEfficiency; } internal set { directShadingEfficiency = value; } }

        public double UnwantedSolarBlocked { get { return unwantedSolarBlocked; } internal set { unwantedSolarBlocked = value; } }

        public double WantedSolarRetained { get { return wantedSolarRetained; } internal set { wantedSolarRetained = value; } }

        // ---------------------------------------------------------------- material ----

        public double PhysicalDeviceArea { get { return physicalDeviceArea; } internal set { physicalDeviceArea = value; } }

        /// <summary>Dimensionless fraction — NOT a percentage. Routinely exceeds 1.0.</summary>
        public double MaterialFraction { get { return materialFraction; } internal set { materialFraction = value; } }

        public bool MaterialAvailable { get { return materialAvailable; } internal set { materialAvailable = value; } }

        // --------------------------------------------------------------- objective ----

        public double Benefit { get { return benefit; } internal set { benefit = value; } }

        public double WantedSolarPenalty { get { return wantedSolarPenalty; } internal set { wantedSolarPenalty = value; } }

        public double Harm { get { return harm; } internal set { harm = value; } }

        public double MaterialPenalty { get { return materialPenalty; } internal set { materialPenalty = value; } }

        public double Cost { get { return cost; } internal set { cost = value; } }

        public double ObjectiveScore { get { return objectiveScore; } internal set { objectiveScore = value; } }

        public double DesignTimeScore { get { return designTimeScore; } internal set { designTimeScore = value; } }

        public bool DesignObjectiveMatchesComparisonObjective { get { return designObjectiveMatchesComparisonObjective; } internal set { designObjectiveMatchesComparisonObjective = value; } }

        public double ScoreDeltaToTopRanked { get { return scoreDeltaToTopRanked; } internal set { scoreDeltaToTopRanked = value; } }

        /// <summary>NaN when no No Shade row exists — not 0.</summary>
        public double ScoreDeltaToNoShade { get { return scoreDeltaToNoShade; } internal set { scoreDeltaToNoShade = value; } }

        /// <summary>0 = no tie; else the comparator level that separated this row from the next.</summary>
        public int TieBreakLevel { get { return tieBreakLevel; } internal set { tieBreakLevel = value; } }

        public double BreakEvenWantedSolarPenalty { get { return breakEvenWantedSolarPenalty; } internal set { breakEvenWantedSolarPenalty = value; } }

        public double BreakEvenMaterialPenalty { get { return breakEvenMaterialPenalty; } internal set { breakEvenMaterialPenalty = value; } }

        public double UnwantedInterceptedPerGrossArea { get { return unwantedInterceptedPerGrossArea; } internal set { unwantedInterceptedPerGrossArea = value; } }

        public double ObjectiveScorePerGrossArea { get { return objectiveScorePerGrossArea; } internal set { objectiveScorePerGrossArea = value; } }

        // ------------------------------------------------------- design provenance ----

        public int DesignEvaluations { get { return designEvaluations; } internal set { designEvaluations = value; } }

        public string DesignTermination { get { return designTermination; } internal set { designTermination = value; } }

        public bool DesignBudgetExhausted { get { return designBudgetExhausted; } internal set { designBudgetExhausted = value; } }

        public List<string> Warnings { get { return new List<string>(warnings); } internal set { warnings = new List<string>(value ?? new List<string>()); } }

        /// <summary>The verified result this row reports. Carried, not serialised; null for a deserialised row.</summary>
        public VerifiedShadingSchemeResult VerifiedResult { get { return verifiedResult; } internal set { verifiedResult = value; } }

        // ---------------------------------------------------------------- JSON ----

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("Rank")) { rank = jObject["Rank"]?.GetValue<int>() ?? -1; }
            if (jObject.ContainsKey("TopRanked")) { topRanked = jObject["TopRanked"]?.GetValue<bool>() ?? false; }
            if (jObject.ContainsKey("EngineerSelected")) { engineerSelected = jObject["EngineerSelected"]?.GetValue<bool>() ?? false; }
            if (jObject.ContainsKey("SchemeGuid")) { Guid.TryParse(jObject["SchemeGuid"]?.GetValue<string>(), out schemeGuid); }
            if (jObject.ContainsKey("OptionName")) { optionName = jObject["OptionName"]?.GetValue<string>(); }
            if (jObject.ContainsKey("DesignMethod")) { designMethod = jObject["DesignMethod"]?.GetValue<string>(); }
            if (jObject.ContainsKey("TypologyNames")) { typologyNames = jObject["TypologyNames"]?.GetValue<string>(); }
            if (jObject.ContainsKey("ProductPreset")) { productPreset = jObject["ProductPreset"]?.GetValue<string>(); }
            if (jObject.ContainsKey("Status")) { Enum.TryParse(jObject["Status"]?.GetValue<string>(), out status); }
            if (jObject.ContainsKey("ComparabilityReason")) { comparabilityReason = jObject["ComparabilityReason"]?.GetValue<string>(); }
            if (jObject.ContainsKey("PhysicalDeviceCount")) { physicalDeviceCount = jObject["PhysicalDeviceCount"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("ApertureCount")) { apertureCount = jObject["ApertureCount"]?.GetValue<int>() ?? default; }

            apertureGuids = new List<Guid>();
            if (jObject.ContainsKey("ApertureGuids") && jObject["ApertureGuids"] is JsonArray guidsArray)
            {
                foreach (JsonNode node in guidsArray)
                {
                    if (Guid.TryParse(node?.GetValue<string>(), out Guid guid))
                    {
                        apertureGuids.Add(guid);
                    }
                }
            }

            baselineDirectSolar = Read(jObject, "BaselineDirectSolar");
            admittedUnwantedSolar = Read(jObject, "AdmittedUnwantedSolar");
            admittedWantedSolar = Read(jObject, "AdmittedWantedSolar");
            admittedNeutralSolar = Read(jObject, "AdmittedNeutralSolar");
            directSolarIntercepted = Read(jObject, "DirectSolarIntercepted");
            unwantedSolarIntercepted = Read(jObject, "UnwantedSolarIntercepted");
            wantedSolarBlocked = Read(jObject, "WantedSolarBlocked");
            neutralSolarIntercepted = Read(jObject, "NeutralSolarIntercepted");
            unattributedEnergy = Read(jObject, "UnattributedEnergy");
            directShadingEfficiency = Read(jObject, "DirectShadingEfficiency");
            unwantedSolarBlocked = Read(jObject, "UnwantedSolarBlocked");
            wantedSolarRetained = Read(jObject, "WantedSolarRetained");

            physicalDeviceArea = Read(jObject, "PhysicalDeviceArea");
            materialFraction = Read(jObject, "MaterialFraction");
            if (jObject.ContainsKey("MaterialAvailable")) { materialAvailable = jObject["MaterialAvailable"]?.GetValue<bool>() ?? false; }

            benefit = Read(jObject, "Benefit");
            wantedSolarPenalty = Read(jObject, "WantedSolarPenalty");
            harm = Read(jObject, "Harm");
            materialPenalty = Read(jObject, "MaterialPenalty");
            cost = Read(jObject, "Cost");
            objectiveScore = Read(jObject, "ObjectiveScore");
            designTimeScore = Read(jObject, "DesignTimeScore");
            if (jObject.ContainsKey("DesignObjectiveMatchesComparisonObjective")) { designObjectiveMatchesComparisonObjective = jObject["DesignObjectiveMatchesComparisonObjective"]?.GetValue<bool>() ?? false; }
            scoreDeltaToTopRanked = Read(jObject, "ScoreDeltaToTopRanked");
            scoreDeltaToNoShade = Read(jObject, "ScoreDeltaToNoShade");
            if (jObject.ContainsKey("TieBreakLevel")) { tieBreakLevel = jObject["TieBreakLevel"]?.GetValue<int>() ?? default; }
            breakEvenWantedSolarPenalty = Read(jObject, "BreakEvenWantedSolarPenalty");
            breakEvenMaterialPenalty = Read(jObject, "BreakEvenMaterialPenalty");
            unwantedInterceptedPerGrossArea = Read(jObject, "UnwantedInterceptedPerGrossArea");
            objectiveScorePerGrossArea = Read(jObject, "ObjectiveScorePerGrossArea");

            if (jObject.ContainsKey("DesignEvaluations")) { designEvaluations = jObject["DesignEvaluations"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("DesignTermination")) { designTermination = jObject["DesignTermination"]?.GetValue<string>(); }
            if (jObject.ContainsKey("DesignBudgetExhausted")) { designBudgetExhausted = jObject["DesignBudgetExhausted"]?.GetValue<bool>() ?? false; }

            warnings = new List<string>();
            if (jObject.ContainsKey("Warnings") && jObject["Warnings"] is JsonArray warningsArray)
            {
                foreach (JsonNode node in warningsArray)
                {
                    warnings.Add(node?.GetValue<string>());
                }
            }

            return true;
        }

        private static double Read(JsonObject jObject, string name)
        {
            return jObject.ContainsKey(name) ? (jObject[name]?.GetValue<double>() ?? double.NaN) : double.NaN;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            jObject.Add("Rank", rank);
            jObject.Add("TopRanked", topRanked);
            jObject.Add("EngineerSelected", engineerSelected);
            jObject.Add("SchemeGuid", schemeGuid.ToString());
            if (optionName != null) { jObject.Add("OptionName", optionName); }
            if (designMethod != null) { jObject.Add("DesignMethod", designMethod); }
            if (typologyNames != null) { jObject.Add("TypologyNames", typologyNames); }
            if (productPreset != null) { jObject.Add("ProductPreset", productPreset); }
            jObject.Add("Status", status.ToString());
            if (comparabilityReason != null) { jObject.Add("ComparabilityReason", comparabilityReason); }
            jObject.Add("PhysicalDeviceCount", physicalDeviceCount);
            jObject.Add("ApertureCount", apertureCount);

            JsonArray guidsArray = new JsonArray();
            foreach (Guid guid in apertureGuids)
            {
                guidsArray.Add(guid.ToString());
            }
            jObject.Add("ApertureGuids", guidsArray);

            AddFinite(jObject, "BaselineDirectSolar", baselineDirectSolar);
            AddFinite(jObject, "AdmittedUnwantedSolar", admittedUnwantedSolar);
            AddFinite(jObject, "AdmittedWantedSolar", admittedWantedSolar);
            AddFinite(jObject, "AdmittedNeutralSolar", admittedNeutralSolar);
            AddFinite(jObject, "DirectSolarIntercepted", directSolarIntercepted);
            AddFinite(jObject, "UnwantedSolarIntercepted", unwantedSolarIntercepted);
            AddFinite(jObject, "WantedSolarBlocked", wantedSolarBlocked);
            AddFinite(jObject, "NeutralSolarIntercepted", neutralSolarIntercepted);
            AddFinite(jObject, "UnattributedEnergy", unattributedEnergy);
            AddFinite(jObject, "DirectShadingEfficiency", directShadingEfficiency);
            AddFinite(jObject, "UnwantedSolarBlocked", unwantedSolarBlocked);
            AddFinite(jObject, "WantedSolarRetained", wantedSolarRetained);

            AddFinite(jObject, "PhysicalDeviceArea", physicalDeviceArea);
            AddFinite(jObject, "MaterialFraction", materialFraction);
            jObject.Add("MaterialAvailable", materialAvailable);

            AddFinite(jObject, "Benefit", benefit);
            AddFinite(jObject, "WantedSolarPenalty", wantedSolarPenalty);
            AddFinite(jObject, "Harm", harm);
            AddFinite(jObject, "MaterialPenalty", materialPenalty);
            AddFinite(jObject, "Cost", cost);
            AddFinite(jObject, "ObjectiveScore", objectiveScore);
            AddFinite(jObject, "DesignTimeScore", designTimeScore);
            jObject.Add("DesignObjectiveMatchesComparisonObjective", designObjectiveMatchesComparisonObjective);
            AddFinite(jObject, "ScoreDeltaToTopRanked", scoreDeltaToTopRanked);
            AddFinite(jObject, "ScoreDeltaToNoShade", scoreDeltaToNoShade);
            jObject.Add("TieBreakLevel", tieBreakLevel);
            AddFinite(jObject, "BreakEvenWantedSolarPenalty", breakEvenWantedSolarPenalty);
            AddFinite(jObject, "BreakEvenMaterialPenalty", breakEvenMaterialPenalty);
            AddFinite(jObject, "UnwantedInterceptedPerGrossArea", unwantedInterceptedPerGrossArea);
            AddFinite(jObject, "ObjectiveScorePerGrossArea", objectiveScorePerGrossArea);

            jObject.Add("DesignEvaluations", designEvaluations);
            if (designTermination != null) { jObject.Add("DesignTermination", designTermination); }
            jObject.Add("DesignBudgetExhausted", designBudgetExhausted);

            JsonArray warningsArray = new JsonArray();
            foreach (string warning in warnings)
            {
                warningsArray.Add(warning);
            }
            jObject.Add("Warnings", warningsArray);

            return jObject;
        }

        /// <summary>Non-finite doubles are OMITTED from the JSON (absent = NaN on read), so the serialised form is always writable and byte-stable.</summary>
        private static void AddFinite(JsonObject jObject, string name, double value)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value))
            {
                jObject.Add(name, value);
            }
        }
    }
}

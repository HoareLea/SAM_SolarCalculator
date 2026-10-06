// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>One glossary row: what a quantity is, and the specific misreading it invites.</summary>
    public class GlossaryEntry
    {
        public GlossaryEntry(string variable, string unit, string meaning, string watchOutFor)
        {
            Variable = variable;
            Unit = unit;
            Meaning = meaning;
            WatchOutFor = watchOutFor;
        }

        public string Variable { get; }

        public string Unit { get; }

        public string Meaning { get; }

        /// <summary>The load-bearing column: names the specific misreading, never restates the definition.</summary>
        public string WatchOutFor { get; }
    }

    public static partial class Query
    {
        /// <summary>
        /// The report glossary: one entry per quantity the comparison prints, keyed by field name
        /// (and by enum member name for status words), grouped for reading. A test reflects over
        /// ShadingComparisonRow, ShadingComparisonStatus and ShadingOptimisationTermination and
        /// fails when a field is added without an entry — that is what stops the glossary rotting.
        /// </summary>
        public static Dictionary<string, GlossaryEntry> ShadingReportGlossary()
        {
            Dictionary<string, GlossaryEntry> result = new Dictionary<string, GlossaryEntry>();

            void Add(string group, string key, string variable, string unit, string meaning, string watchOutFor)
            {
                result[key] = new GlossaryEntry(variable, unit, meaning, watchOutFor);
            }

            // Scope and basis ---------------------------------------------------------
            Add("Scope", "ApertureGuids", "Aperture", "—", "One window, identified by its GUID from the model.", "Only apertures on sun-exposed external panels are analysed. A window in a wall shared by two spaces is internal and never appears.");
            Add("Scope", "ApertureCount", "Aperture count", "count", "How many windows the comparison covers.", "One physical device may span several apertures; the device count is a different number.");
            Add("Scope", "SchemeGuid", "Scheme GUID", "—", "Deterministic identity of the complete scheme.", "Selection is by SchemeGuid, never by name or rank — ranks change after recalculation, the identity does not.");
            Add("Scope", "OptionName", "Option name", "—", "The option name the scheme competes under.", "—");
            Add("Scope", "DesignMethod", "Design method", "—", "Which workflow produced the geometry: RationaliseShading, RationaliseAwningGroup or Baseline.", "—");
            Add("Scope", "TypologyNames", "Typology names", "—", "The built families in the scheme.", "—");
            Add("Scope", "ProductPreset", "Product preset", "—", "The product preset the grouped device was constrained by.", "—");
            Add("Scope", "Grid size", "Grid size", "m", "Spacing between analysis sample points. Smaller = finer, slower.", "A device feature finer than the grid cannot be seen. Two options compared at different grid sizes are not comparable.");
            Add("Scope", "Sun-angle step", "Sun-angle step", "°", "How finely sun positions through the year are grouped before tracing.", "Must match between options being compared.");
            Add("Scope", "Desirability brief", "Desirability brief", "—", "Which hours' sun you want blocked and which kept. Default: summer unwanted, winter wanted.", "The single most important assumption in the report. Everything labelled 'unwanted' or 'wanted' derives from it.");
            Add("Scope", "Signature hash", "Signature hash", "—", "A fingerprint of the whole analysis basis.", "Same signature = measured on the same basis, may be compared. Different = the report says which field differed.");
            Add("Scope", "ComparabilityReason", "Comparability reason", "—", "Which analysis-basis field differed, for an option that could not be ranked.", "Fixes the basis, not the option: re-run the option on the comparison grid, brief and weather.");

            // Hours ------------------------------------------------------------------
            Add("Hours", "Weather timeline hours", "Weather timeline hours", "h", "Every hour in the analysis year. 8 760, or 8 784 in a leap year.", "—");
            Add("Hours", "Evaluated hours", "Evaluated hours", "h", "Hours with usable weather data.", "Timeline = Evaluated + Missing.");
            Add("Hours", "Sun above horizon", "Sun above horizon", "h", "Evaluated hours when the sun was up anywhere.", "Not window-specific.");
            Add("Hours", "Sun in front of the facade", "Sun in front of the facade", "h", "Sun-up hours when the sun was geometrically outside at least one window — it could have reached the glass if nothing were in the way.", "NOT a theoretical maximum. The horizon cut still applies. It ignores BUILDINGS, not physics.");
            Add("Hours", "Removed by surroundings", "Removed by surroundings", "h, kWh", "How much beam the neighbouring buildings take away before any device is considered.", "Separates a window that is dim because of orientation from one dim because something stands in front of it.");
            Add("Hours", "BeamAdmittingHours", "Beam reaching the scope", "h", "Sun-up hours when beam actually reached at least one window, context in place, no device.", "THE DENOMINATOR OF EVERY ENERGY IN THE REPORT.");
            Add("Hours", "unwanted / wanted / neither hours", "unwanted / wanted / neither hours", "h", "The split of the beam hours by the brief.", "Counted per hour, not per sun position — the same sun altitude occurs in June and December.");

            // Energies ---------------------------------------------------------------
            Add("Energies", "BaselineDirectSolar", "Admitted direct solar (baseline)", "kWh", "Direct sun the windows receive with surroundings in place and no device.", "Not the sun falling on the facade — sun already blocked by another building is excluded, because a device cannot claim credit for it.");
            Add("Energies", "AdmittedUnwantedSolar", "Admitted unwanted", "kWh", "The part arriving in the hours the brief wants blocked.", "On a WSW facade wanted is small — a west window gets little useful winter sun.");
            Add("Energies", "AdmittedWantedSolar", "Admitted wanted", "kWh", "The part arriving in the hours the brief wants kept.", "—");
            Add("Energies", "AdmittedNeutralSolar", "Admitted neither (neutral)", "kWh", "The remainder: spring and autumn beam under the default brief.", "Its absence makes it look as though energy has gone missing. It has not — this is it.");
            Add("Energies", "DirectSolarIntercepted", "Direct solar intercepted", "kWh", "Total direct sun the device stops.", "Stopped, not redirected. No reflection is modelled.");
            Add("Energies", "UnwantedSolarIntercepted", "Unwanted solar intercepted", "kWh", "The part you asked it to stop. THE BENEFIT.", "Beam intercepted — NOT 'energy saved'.");
            Add("Energies", "WantedSolarBlocked", "Wanted solar blocked", "kWh", "The part you asked it to keep. THE PRICE OF THE DESIGN.", "In the score it is SUBTRACTED; adding it would reward destroying the winter sun.");
            Add("Energies", "NeutralSolarIntercepted", "Neutral solar intercepted", "kWh", "The part the brief claimed neither way.", "Absent from the score, but required here or the energies do not add up.");
            Add("Energies", "Still admitted", "Still admitted", "kWh", "What reaches the aperture after the device: baseline − intercepted.", "NOT room solar gain, cooling load or energy consumption. No transmission, no diffuse, no thermal model.");
            Add("Energies", "UnattributedEnergy", "Unattributed energy", "kWh", "Beam the baseline says arrived but the geometry says was stopped by something other than the device.", "SHOULD ALWAYS BE 0. Anything else invalidates the row's totals.");

            // Percentages -------------------------------------------------------------
            Add("Percentages", "DirectShadingEfficiency", "Direct shading efficiency", "%", "Of all direct sun, how much is stopped, wanted or not.", "A blunt measure. A device blocking everything scores 100 % and may be a poor design.");
            Add("Percentages", "UnwantedSolarBlocked", "Unwanted solar blocked", "%", "Of the unwanted sun the window would have received, how much is stopped.", "Higher is better. Denominator is ADMITTED unwanted, not all unwanted sun in the sky.");
            Add("Percentages", "WantedSolarRetained", "Wanted solar retained", "%", "Of the wanted sun the window would have received, how much still gets through.", "HIGHER IS BETTER — this runs OPPOSITE to the line above it, which is the easiest misreading in the table. A fixed awning blocking 98 % of summer sun blocks the winter sun too.");
            Add("Percentages", "n/a", "n/a", "—", "No denominator — no unwanted sun, no wanted sun, or nothing admitted.", "Never 0 % or 100 %. An unavailable metric reading as a number is worse than no metric.");

            // Material ----------------------------------------------------------------
            Add("Material", "PhysicalDeviceCount", "Devices", "count", "Separate physical units you buy and install.", "One grouped awning over three windows is 1, not 3.");
            Add("Material", "PhysicalDeviceArea", "Device area", "m²", "Total shading material, each element counted once.", "Two overlapping devices both count — you buy both. Never a geometric union of shadows.");
            Add("Material", "MaterialFraction", "Material fraction", "—", "Device area ÷ total window area.", "NOT A PERCENTAGE — routinely exceeds 1. A deep awning is legitimately larger than its windows.");
            Add("Material", "MaterialAvailable", "Material available", "bool", "Whether the material quantity could be measured.", "False with μ > 0 makes the option NOT RANKABLE — unknown material is not free material.");

            // Objective and score -----------------------------------------------------
            Add("Objective", "Benefit", "Benefit", "kWh", "Unwanted intercepted.", "See UnwantedSolarIntercepted.");
            Add("Objective", "Harm", "Harm", "kWh", "Wanted blocked.", "See WantedSolarBlocked.");
            Add("Objective", "Cost", "Cost", "kWh", "Material priced as energy so it can be added to the energy terms.", "PRICED IN kWh SO IT CAN BE ADDED TO THE ENERGY TERMS. NOT A PRICE.");
            Add("Objective", "WantedSolarPenalty", "λ — wanted solar penalty", "—", "How many kWh of unwanted sun blocked is worth one kWh of wanted sun lost. Default 1.0.", "Changes no measured energy — only which measured design wins. Working range 0.5–2.0.");
            Add("Objective", "MaterialPenalty", "μ — material penalty", "—", "How much admitted sun you will give up per unit of material fraction. Default 0.1.", "0 sizes on energy alone. Raising it shrinks the device and eventually returns NO SHADE — a real answer.");
            Add("Objective", "ObjectiveScore", "Objective score", "kWh", "Benefit − λ×Harm − μ×Cost. Higher is better; 0 is 'build nothing'.", "A SUMMARY that mixes three quantities through weightings the reader may not have chosen.");
            Add("Objective", "DesignTimeScore", "Design-time score", "kWh", "Recorded while geometry was searched, window by window.", "NEVER RANKS. Cannot see cross-shading.");
            Add("Objective", "DesignObjectiveMatchesComparisonObjective", "Design objective matches", "bool", "Whether the geometry was optimised under the same λ, μ and reference as the comparison.", "False = the geometry was RE-SCORED, not re-optimised, under the comparison objective.");
            Add("Objective", "ScoreDeltaToTopRanked", "Δ to analytical leader", "kWh", "This option's score below the analytical leader.", "A small delta means the choice is close — see the robustness section.");
            Add("Objective", "ScoreDeltaToNoShade", "Δ to No Shade", "kWh", "This option's score above or below building nothing.", "n/a when no No Shade row exists — not 0.");
            Add("Objective", "BreakEvenWantedSolarPenalty", "Break-even λ", "—", "The λ at which this option would overtake the analytical leader.", "NOT A RE-OPTIMISATION — at that value the challenger would also have been sized differently.");
            Add("Objective", "BreakEvenMaterialPenalty", "Break-even μ", "—", "The μ at which this option would overtake the analytical leader.", "See above.");
            Add("Objective", "STABLE / MARGINAL / SENSITIVE", "STABLE / MARGINAL / SENSITIVE", "—", "How far the nearest flip sits from the weighting in use: ≥ 100 % stable, < 25 % sensitive.", "About the CHOICE BETWEEN SUPPLIED OPTIONS, not about insensitivity to the brief.");
            Add("Objective", "TieBreakLevel", "Tie-break level", "0–6", "Which rule separated equal scores: 1 score, 2 area, 3 harm, 4 count, 5 No Shade first, 6 identity.", "Resolved at 5 or 6 = engineering-equivalent; choose on grounds outside this report.");
            Add("Objective", "UnwantedInterceptedPerGrossArea", "Unwanted intercepted per m²", "kWh/m²·yr", "Unwanted intercepted divided by total aperture area.", "Divided by GROSS APERTURE area, not floor area — do not compare against floor-normalised benchmarks.");
            Add("Objective", "ObjectiveScorePerGrossArea", "Objective score per m²", "kWh/m²·yr", "Objective score divided by total aperture area.", "The unit that compares against published benchmarks.");

            // Resolution and confidence ----------------------------------------------
            Add("Resolution", "Design-grade recommendation", "Design-grade recommendation", "m", "The coarsest grid at which this workflow's validation evidence holds for this aperture set.", "GUIDANCE, NOT A REQUIREMENT. Never changes the calculation, and not a convergence guarantee.");
            Add("Resolution", "Adequate / MARGINAL", "Adequate / MARGINAL", "—", "Whether a window has enough cells to represent its geometry.", "A MARGINAL window can produce a plausible-looking recommendation that is a sampling artefact.");
            Add("Resolution", "Resolved / NearResolutionLimit / BelowResolutionLimit", "Resolved / NearResolutionLimit / BelowResolutionLimit", "—", "Whether the grid can see the device's repeated elements. Below limit = at most one sample per gap.", "BELOW THE LIMIT THE ELEMENT-LEVEL NUMBERS ARE NOT EVIDENCE — they are decided by where the samples happen to fall.");
            Add("Resolution", "Grid resolution cap reached", "Grid resolution cap reached", "—", "The winning element count sits on the maximum the grid allows, not the family's own maximum.", "THE GRID CHOSE THE DESIGN, NOT THE ENGINEER. The family may be under-represented in the ranking.");
            Add("Resolution", "Sun-group quantisation indicator", "Sun-group quantisation indicator", "%", "Documented mean absolute error from grouping sun positions instead of tracing every hour. ~1.5 % at 2°.", "A SCREENING INDICATOR, NOT AN UNCERTAINTY BOUND, and it excludes grid error. Available at 1°, 2° and 5° only — never interpolated.");
            Add("Resolution", "WELL ABOVE / COMPARABLE TO / WITHIN", "WELL ABOVE / COMPARABLE TO / WITHIN", "—", "Where the rank-1/rank-2 margin sits against that indicator.", "WELL ABOVE does NOT mean converged. WITHIN means the options cannot be distinguished on this evidence.");
            Add("Resolution", "Grid convergence", "Grid convergence", "—", "Whether a finer-grid repeat has confirmed the ranking.", "NOT CONFIRMED is the default state of this report and is not implied by any other number here.");

            // Status words ------------------------------------------------------------
            Add("Status", "Undefined", "Undefined", "—", "The value has not been set.", "An Undefined status in a report is a defect in the run, not an answer.");
            Add("Status", "Rank", "Rank", "1-based", "The deterministic arithmetic order under the comparison objective.", "NOT an engineering selection — the engineer's choice is recorded separately. −1 = could not be ranked.");
            Add("Status", "Status", "Status", "—", "How the row fared in the comparison.", "NOT EVALUATED is a fault; NO SHADE is a successful answer. Never interchangeable.");
            Add("Status", "TopRanked", "Top ranked", "bool", "True on the analytical leader: arithmetic rank 1 among rankable options.", "NOT an engineering selection. The engineer's explicit choice is recorded separately.");
            Add("Status", "EngineerSelected", "Engineer selected", "bool", "True on the option an engineer explicitly selected downstream.", "A selection never changes scores, ranks or robustness figures.");
            Add("Status", "Ranked", "OK", "—", "Verified, comparable, ranked, nothing needing attention.", "—");
            Add("Status", "NoShadeBaseline", "NO SHADE", "—", "The zero-device reference row, also ranked, score exactly 0.", "The anchor every other baseline is checked against.");
            Add("Status", "Incomparable", "INCOMPARABLE", "—", "Verified on a different analysis basis. Shown with the differing field named.", "Never ranked, never silently dropped.");
            Add("Status", "NotEvaluated", "NOT EVALUATED", "—", "Nothing could be measured. A fault.", "NEVER 'no shading needed'. NOT EVALUATED vs NO SHADE: a fault versus a successful answer.");
            Add("Status", "NotRankable", "NOT RANKABLE", "—", "Verified and comparable, but the score cannot be formed. Measured energies still shown.", "Usually unknown material with μ > 0. Set μ to 0 to rank on energy alone.");
            Add("Status", "OK (provisional)", "OK (provisional)", "—", "Ranked, but a family exhausted its search budget or was limited by the grid. Best SEEN, not converged.", "Raise the budget or refine the grid before treating the family as final.");
            Add("Status", "DesignBudgetExhausted", "Design budget exhausted", "bool", "The family hit its evaluation budget: the geometry is the best SEEN, not the best available.", "THE BEST DESIGN SEEN, NOT THE BEST AVAILABLE.");
            Add("Status", "DesignEvaluations", "Candidates evaluated", "count", "Distinct candidate geometries actually measured for the family.", "—");
            Add("Status", "DesignTermination", "Design termination", "—", "Why the design search stopped.", "EvaluationBudgetExhausted = provisional; EvaluationFailed = nothing was measurable.");
            Add("Status", "Warnings", "Warnings", "—", "Per-row notes: resolution caps, budget exhaustion, baseline disagreements.", "Read them before trusting the row.");
            Add("Status", "VerifiedResult", "Verified result", "object", "The verified scheme result behind this row.", "The structured evidence; the row fields are its printed form.");

            // Search diagnostics ------------------------------------------------------
            Add("Search", "StepBelowGranularity", "StepBelowGranularity", "—", "The search converged: the step fell below every parameter's granularity.", "The healthy ending.");
            Add("Search", "EvaluationBudgetExhausted", "EvaluationBudgetExhausted", "—", "Ran out of budget before converging. Best seen, not best available.", "Raise _maximumEvaluations_ and re-run before treating that family as final.");
            Add("Search", "NoBeneficialCandidate", "NoBeneficialCandidate", "—", "Every candidate scored below zero; building nothing wins.", "A real answer.");
            Add("Search", "NothingToSearch", "NothingToSearch", "—", "Every parameter was fixed, or the typology produced no geometry.", "—");
            Add("Search", "EvaluationFailed", "EvaluationFailed", "—", "Nothing could be measured.", "A fault, reported as NOT EVALUATED.");

            // Recommendation status ---------------------------------------------------
            Add("Status", "Ready", "READY", "—", "Rank 1 is suitable to take forward on the evidence modelled. Rare — it requires confirmed grid convergence.", "—");
            Add("Status", "Provisional", "PROVISIONAL", "—", "Rank 1 stands, but material checks remain open. They are listed, with the actions that would close them.", "—");
            Add("Status", "Indeterminate", "INDETERMINATE", "—", "A ranking exists mathematically, but the analysis cannot defensibly distinguish the leading options.", "RANK 1 IS STILL REPORTED; the advice is what changes. The engineer may select explicitly, with a reason.");
            Add("Status", "NoShadingRecommended", "NO SHADING RECOMMENDED", "—", "No Shade is rank 1.", "A successful engineering answer, not a failure.");
            Add("Status", "NoDecision", "NO DECISION", "—", "Nothing was comparable and rankable.", "—");

            // Selection ---------------------------------------------------------------
            Add("Status", "None", "None", "—", "No engineering selection has been recorded.", "The comparison ranks; the engineer selects. Until a selection is recorded, the report says so explicitly.");
            Add("Status", "AgreesWithLeader", "Agrees with leader", "—", "The engineering selection is the analytical leader.", "—");
            Add("Status", "DepartsFromLeader", "Departs from leader", "—", "The engineer chose a lower-ranked option, with a recorded reason.", "The analytical ranking is unchanged — this records a project decision on criteria the objective does not model.");
            Add("Status", "LeaderNotDistinguishable", "Leader not distinguishable", "—", "The leading options could not be separated; the selection is an explicit engineering choice.", "—");

            return result;
        }

        /// <summary>The glossary rendered as Markdown, grouped for reading. Inside reportMarkdown, always.</summary>
        public static string ShadingGlossaryMarkdown()
        {
            Dictionary<string, GlossaryEntry> entries = ShadingReportGlossary();
            StringBuilder stringBuilder = new StringBuilder();

            stringBuilder.AppendLine("## Variables explained");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("Plain-English meaning of every quantity in the report: unit, what it is, and the misreading it invites. Rendered inside the report, always.");
            stringBuilder.AppendLine();

            string[] groups = new string[]
            {
                "Scope and basis", "Hours", "Energies — direct beam only, kWh", "Percentages",
                "Material", "Objective and score", "Resolution and confidence", "Status words",
                "Recommendation status", "Search diagnostics",
            };

            foreach (string group in groups)
            {
                stringBuilder.AppendLine();
                stringBuilder.Append("### ").AppendLine(group);
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("| Variable | Unit | What it means | Watch out for |");
                stringBuilder.AppendLine("|---|---|---|---|");

                foreach (KeyValuePair<string, GlossaryEntry> pair in entries)
                {
                    GlossaryEntry entry = pair.Value;
                    if (entry == null || GroupOf(pair.Key) != group)
                    {
                        continue;
                    }

                    stringBuilder.Append("| **").Append(entry.Variable).Append("** | ")
                        .Append(Escape(entry.Unit)).Append(" | ")
                        .Append(Escape(entry.Meaning)).Append(" | ")
                        .Append(Escape(entry.WatchOutFor)).AppendLine(" |");
                }
            }

            return stringBuilder.ToString();
        }

        private static string GroupOf(string key)
        {
            switch (key)
            {
                case "ApertureGuids": case "ApertureCount": case "SchemeGuid": case "OptionName": case "DesignMethod":
                case "TypologyNames": case "ProductPreset": case "ComparabilityReason":
                    return "Scope and basis";
                case "BaselineDirectSolar": case "AdmittedUnwantedSolar": case "AdmittedWantedSolar": case "AdmittedNeutralSolar":
                case "DirectSolarIntercepted": case "UnwantedSolarIntercepted": case "WantedSolarBlocked":
                case "NeutralSolarIntercepted": case "UnattributedEnergy":
                    return "Energies — direct beam only, kWh";
                case "DirectShadingEfficiency": case "UnwantedSolarBlocked": case "WantedSolarRetained":
                    return "Percentages";
                case "PhysicalDeviceCount": case "PhysicalDeviceArea": case "MaterialFraction": case "MaterialAvailable":
                    return "Material";
                case "Benefit": case "Harm": case "Cost": case "WantedSolarPenalty": case "MaterialPenalty":
                case "ObjectiveScore": case "DesignTimeScore": case "DesignObjectiveMatchesComparisonObjective":
                case "ScoreDeltaToTopRanked": case "ScoreDeltaToNoShade": case "BreakEvenWantedSolarPenalty":
                case "BreakEvenMaterialPenalty": case "TieBreakLevel":
                case "UnwantedInterceptedPerGrossArea": case "ObjectiveScorePerGrossArea":
                    return "Objective and score";
                case "Rank": case "Status": case "TopRanked": case "EngineerSelected": case "Ranked": case "NoShadeBaseline":
                case "Incomparable": case "NotEvaluated": case "NotRankable": case "OK (provisional)":
                case "DesignBudgetExhausted": case "DesignEvaluations": case "DesignTermination": case "Warnings":
                case "VerifiedResult": case "AgreesWithLeader": case "DepartsFromLeader": case "LeaderNotDistinguishable":
                    return "Status words";
                case "Ready": case "Provisional": case "Indeterminate": case "NoShadingRecommended": case "NoDecision":
                    return "Recommendation status";
                case "StepBelowGranularity": case "EvaluationBudgetExhausted": case "NoBeneficialCandidate":
                case "NothingToSearch": case "EvaluationFailed":
                    return "Search diagnostics";
                default:
                    return "Scope and basis";
            }
        }

        private static string Escape(string text)
        {
            return (text ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }
    }
}

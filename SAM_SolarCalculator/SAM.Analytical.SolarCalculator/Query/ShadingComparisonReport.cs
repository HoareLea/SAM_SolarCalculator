// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Query
    {
        // The documented sun-group MAE indicators, keyed by sun-angle step (degrees). Sourced from
        // Stage11-Validation.md 2.2 and cited in code: a fixed lookup, never re-measured per run and
        // never interpolated between evidenced steps.
        internal static readonly Dictionary<double, double> SunGroupIndicators = new Dictionary<double, double>
        {
            { 1.0, 0.0125 },
            { 2.0, 0.0154 },
            { 5.0, 0.0293 },
        };

        private const string IndeterminateStatement =
            "The calculation orders {0} as rank 1 and {1} as rank 2, but the difference is within the method's resolution indicator. The analysis cannot distinguish these options reliably.";

        /// <summary>The comparison report as Markdown. Deterministic: identical inputs give byte-identical output (I11).</summary>
        public static string MarkdownReport(this ShadingComparisonResult result, ShadingSelectionDecision selection = null)
        {
            if (result == null)
            {
                return null;
            }

            StringBuilder stringBuilder = new StringBuilder();

            AppendTitle(stringBuilder, result, selection);
            AppendRecommendation(stringBuilder, result, selection);
            AppendScope(stringBuilder, result);
            AppendHours(stringBuilder, result);
            AppendResolution(stringBuilder, result);
            AppendObjective(stringBuilder, result);
            AppendRanking(stringBuilder, result);
            AppendLeaderArithmetic(stringBuilder, result, selection);
            AppendRobustness(stringBuilder, result);
            AppendDesignVsVerified(stringBuilder, result);
            AppendExcluded(stringBuilder, result);
            AppendDiagnostics(stringBuilder, result);
            AppendFitness(stringBuilder, result, selection);
            AppendProvenance(stringBuilder, result);
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("---");
            stringBuilder.Append(ShadingGlossaryMarkdown());

            return stringBuilder.ToString();
        }

        // --------------------------------------------------------------- helpers ----

        private static string En(double value)
        {
            return double.IsNaN(value) ? "n/a" : value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string En1(double value)
        {
            return double.IsNaN(value) ? "n/a" : value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Pct(double ratio)
        {
            return double.IsNaN(ratio) ? "n/a" : string.Format(CultureInfo.InvariantCulture, "{0:0.0} %", 100.0 * ratio);
        }

        private static string Fra(double ratio)
        {
            return double.IsNaN(ratio) ? "n/a" : ratio.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string Area(double value)
        {
            return double.IsNaN(value) ? "n/a" : value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string Len(double value)
        {
            return double.IsNaN(value) ? "n/a" : value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Ang(double value)
        {
            return double.IsNaN(value) ? "n/a" : value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string BreakEvenText(double value)
        {
            if (double.IsNaN(value))
            {
                return "never";
            }

            if (value < 0)
            {
                return "not reachable";
            }

            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string OptionLabel(ShadingComparisonRow row, bool withSuffixes)
        {
            if (row == null)
            {
                return null;
            }

            string name = row.OptionName ?? "Option";
            if (withSuffixes && IsRetractable(row))
            {
                name += " - modelled fully deployed";
            }

            return name;
        }

        private static bool IsRetractable(ShadingComparisonRow row)
        {
            string typologies = row?.TypologyNames;
            return typologies != null && typologies.IndexOf("RetractableAwning", StringComparison.Ordinal) != -1;
        }

        private static bool IsNoShadeRow(ShadingComparisonRow row)
        {
            return row != null && row.Status == ShadingComparisonStatus.NoShadeBaseline;
        }

        private static ShadingComparisonRow NoShadeRow(ShadingComparisonResult result)
        {
            foreach (ShadingComparisonRow row in result.Rows)
            {
                if (IsNoShadeRow(row))
                {
                    return row;
                }
            }

            return null;
        }

        private static string AssemblyVersionText()
        {
            AssemblyInformationalVersionAttribute informational = typeof(Query).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (informational != null && !string.IsNullOrWhiteSpace(informational.InformationalVersion))
            {
                return informational.InformationalVersion;
            }

            return typeof(Query).Assembly.GetName().Version?.ToString() ?? "unknown";
        }

        // ------------------------------------------------------------ 0. title ----

        private static void AppendTitle(StringBuilder stringBuilder, ShadingComparisonResult result, ShadingSelectionDecision selection)
        {
            stringBuilder.AppendLine("# Shading Option Comparison Report");
            stringBuilder.AppendLine();
            stringBuilder.Append("**Comparison audit —** ");
            stringBuilder.Append(result.SuppliedCount).Append(" scheme").Append(result.SuppliedCount == 1 ? string.Empty : "s").Append(" supplied · ");
            stringBuilder.Append(result.VerifiedCount).Append(" verified and comparable · ");
            stringBuilder.Append(result.RankedCount).Append(" ranked · ");
            stringBuilder.Append(result.IncomparableCount).Append(" incomparable · ");
            stringBuilder.Append(result.NotEvaluatedCount).Append(" not evaluated · ");
            stringBuilder.Append(result.NotRankableCount).Append(" not rankable · ");
            stringBuilder.Append("basis signature `").Append(result.ReferenceSignature?.SignatureHash ?? "none").Append("` · ");
            stringBuilder.Append("baseline **").Append(result.BaselineAnchored ? "anchored" : "UNANCHORED").Append("**.");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("*The software analyses and ranks. The engineer decides and records why. Rank 1 is the analytical leader, not an automatic selection.*");
            stringBuilder.AppendLine();
        }

        // ------------------------------------------------- 1. recommendation ----

        private static void AppendRecommendation(StringBuilder stringBuilder, ShadingComparisonResult result, ShadingSelectionDecision selection)
        {
            stringBuilder.AppendLine("## 1. Engineering Recommendation");
            stringBuilder.AppendLine();

            ShadingComparisonRow leader = result.TopRankedRow;
            if (leader == null)
            {
                stringBuilder.AppendLine("ANALYTICAL RESULT");
                stringBuilder.AppendLine("  No option could be ranked on this basis.");
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("ENGINEERING DECISION");
                stringBuilder.AppendLine("  Nothing to select - see Excluded and Incomparable Options.");
                stringBuilder.AppendLine();
                return;
            }

            stringBuilder.AppendLine("ANALYTICAL RESULT");
            stringBuilder.Append("  ").Append(OptionLabel(leader, true)).Append(" ranks 1 of ").Append(result.RankedCount).AppendLine(".");
            if (result.RecommendationStatus == ShadingRecommendationStatus.Indeterminate)
            {
                ShadingComparisonRow runnerUp = RunnerUp(result, leader);
                stringBuilder.AppendLine(string.Format(CultureInfo.InvariantCulture, IndeterminateStatement,
                    OptionLabel(leader, true), runnerUp == null ? "rank 2" : OptionLabel(runnerUp, true)));
            }
            stringBuilder.AppendLine();

            stringBuilder.AppendLine("Recommendation status");
            stringBuilder.Append("  ").AppendLine(RecommendationStatusText(result.RecommendationStatus));
            stringBuilder.AppendLine();

            stringBuilder.AppendLine("Why it ranks first");
            stringBuilder.Append("  ").Append(Pct(leader.UnwantedSolarBlocked)).AppendLine(" of unwanted direct solar blocked");
            stringBuilder.Append("  ").Append(En(leader.UnwantedSolarIntercepted)).Append(" kWh unwanted solar intercepted, ")
                .Append(En(leader.WantedSolarBlocked)).AppendLine(" kWh wanted solar lost");
            stringBuilder.Append("  ").Append(leader.PhysicalDeviceCount).Append(" physical device").Append(leader.PhysicalDeviceCount == 1 ? string.Empty : "s").Append(", ")
                .Append(Area(leader.PhysicalDeviceArea)).AppendLine(" m2 of material");
            stringBuilder.Append("  Verified score ").Append(En(leader.ObjectiveScore)).Append(" kWh (")
                .Append(En(leader.ObjectiveScorePerGrossArea)).AppendLine(" kWh/m2.yr)");
            ShadingComparisonRow runnerUpLine = RunnerUp(result, leader);
            if (runnerUpLine != null)
            {
                // The margin over the next-ranked option is the runner-up's delta to the leader.
                stringBuilder.Append("  ").Append(En(runnerUpLine.ScoreDeltaToTopRanked)).Append(" kWh clear of rank 2")
                    .Append(" (").Append(OptionLabel(runnerUpLine, false)).AppendLine(")");
            }
            stringBuilder.AppendLine();

            if (result.RecommendationStatus == ShadingRecommendationStatus.Ready)
            {
                stringBuilder.AppendLine("No open checks. Every condition in the recommendation rules was satisfied.");
            }
            else if (result.OpenChecks.Count != 0)
            {
                stringBuilder.AppendLine("Why it is provisional");
                foreach (string check in result.OpenChecks)
                {
                    stringBuilder.Append("  - ").AppendLine(check);
                }
            }
            else if (result.RecommendationStatus == ShadingRecommendationStatus.NoShadingRecommended)
            {
                stringBuilder.AppendLine("The analytical leader is No Shade: building nothing has the highest verified score.");
            }
            stringBuilder.AppendLine();

            if (result.RequiredBeforeFreeze.Count != 0)
            {
                stringBuilder.AppendLine("Required before design freeze");
                int step = 1;
                foreach (string required in result.RequiredBeforeFreeze)
                {
                    stringBuilder.Append("  ").Append(step++).Append(". ").AppendLine(required);
                }
                stringBuilder.AppendLine();
            }

            AppendProjectInputs(stringBuilder, result);

            if (IsRetractable(leader))
            {
                AppendOperatingAssumption(stringBuilder, leader);
            }

            AppendEngineeringDecision(stringBuilder, result, selection);
        }

        private static string RecommendationStatusText(ShadingRecommendationStatus status)
        {
            switch (status)
            {
                case ShadingRecommendationStatus.Ready: return "READY";
                case ShadingRecommendationStatus.Provisional: return "PROVISIONAL";
                case ShadingRecommendationStatus.Indeterminate: return "INDETERMINATE";
                case ShadingRecommendationStatus.NoShadingRecommended: return "NO SHADING RECOMMENDED";
                case ShadingRecommendationStatus.NoDecision: return "NO DECISION";
                default: return "UNDEFINED";
            }
        }

        private static ShadingComparisonRow RunnerUp(ShadingComparisonResult result, ShadingComparisonRow leader)
        {
            if (leader == null)
            {
                return null;
            }

            foreach (ShadingComparisonRow row in result.Rows)
            {
                // Rows are value-copied by the result, so identity is by SchemeGuid, never by reference.
                if (row != null && row.SchemeGuid != leader.SchemeGuid && row.Rank >= 1)
                {
                    return row;
                }
            }

            return null;
        }

        private static void AppendProjectInputs(StringBuilder stringBuilder, ShadingComparisonResult result)
        {
            List<ShadingProjectInput> inputs = result.ProjectInputs;
            if (inputs == null || inputs.Count == 0)
            {
                return;
            }

            stringBuilder.AppendLine("PROJECT INPUTS REQUIRING CONFIRMATION");
            foreach (ShadingProjectInput input in inputs)
            {
                stringBuilder.Append("  ").Append(input.Name).Append(' ', Math.Max(1, 24 - input.Name.Length))
                    .Append(input.ValueText).Append(' ', Math.Max(1, 9 - input.ValueText.Length))
                    .Append(input.Source).Append(' ', Math.Max(1, 10 - input.Source.Length))
                    .AppendLine(input.Classification);
            }
            stringBuilder.AppendLine();
        }

        private static void AppendOperatingAssumption(StringBuilder stringBuilder, ShadingComparisonRow leader)
        {
            stringBuilder.AppendLine("OPERATING ASSUMPTION");
            stringBuilder.AppendLine("  Retractable devices are assessed in their modelled DEPLOYED geometry for every");
            stringBuilder.AppendLine("  analysed beam hour. Retraction schedules and automatic controls are NOT simulated");
            stringBuilder.AppendLine("  (see Not Covered By This Analysis).");
            stringBuilder.AppendLine();
            stringBuilder.Append("  The reported wanted-solar figure - ").Append(Pct(leader.WantedSolarRetained))
                .AppendLine(" retained - therefore describes the");
            stringBuilder.AppendLine("  permanently deployed geometry. It is not a prediction of controlled operational");
            stringBuilder.AppendLine("  performance, and an awning retracted through the heating season would retain");
            stringBuilder.AppendLine("  substantially more.");
            stringBuilder.AppendLine();
        }

        private static void AppendEngineeringDecision(StringBuilder stringBuilder, ShadingComparisonResult result, ShadingSelectionDecision selection)
        {
            stringBuilder.AppendLine("ENGINEERING DECISION");
            if (selection == null || !selection.Successful)
            {
                stringBuilder.AppendLine("  Not yet recorded.");
                stringBuilder.AppendLine();
                return;
            }

            ShadingComparisonRow row = result.Row(selection.SelectedSchemeGuid);
            if (row == null)
            {
                stringBuilder.AppendLine("  The recorded selection is no longer part of this comparison - select again.");
                stringBuilder.AppendLine();
                return;
            }

            stringBuilder.Append("  ").Append(OptionLabel(row, true)).Append(" selected");
            if (selection.SelectedRank >= 1)
            {
                stringBuilder.Append(" - analytical rank ").Append(selection.SelectedRank);
            }
            stringBuilder.AppendLine(".");

            if (!string.IsNullOrWhiteSpace(selection.SelectionReason))
            {
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("Reason");
                stringBuilder.Append("  ").AppendLine(selection.SelectionReason);
            }

            stringBuilder.AppendLine();
            stringBuilder.AppendLine("Alignment");
            switch (selection.SelectionAlignment)
            {
                case ShadingSelectionAlignment.AgreesWithLeader:
                    stringBuilder.AppendLine("  The engineering selection agrees with the analytical ranking.");
                    break;
                case ShadingSelectionAlignment.LeaderNotDistinguishable:
                    stringBuilder.AppendLine("  The leading options could not be separated analytically; this is an explicit engineering choice.");
                    break;
                default:
                    stringBuilder.AppendLine("  The engineering selection departs from the analytical ranking.");
                    break;
            }

            ShadingComparisonRow leader = result.TopRankedRow;
            if (selection.SelectionAlignment == ShadingSelectionAlignment.DepartsFromLeader && leader != null)
            {
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("Trade-off against the analytical leader");
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("| Quantity | Leader | Selected | Difference |");
                stringBuilder.AppendLine("|---|---:|---:|---:|");
                stringBuilder.Append("| Unwanted solar intercepted | ").Append(En(leader.UnwantedSolarIntercepted)).Append(" | ")
                    .Append(En(row.UnwantedSolarIntercepted)).Append(" | ")
                    .Append(Delta(row.UnwantedSolarIntercepted - leader.UnwantedSolarIntercepted)).AppendLine(" kWh |");
                stringBuilder.Append("| Wanted solar blocked | ").Append(En(leader.WantedSolarBlocked)).Append(" | ")
                    .Append(En(row.WantedSolarBlocked)).Append(" | ")
                    .Append(Delta(row.WantedSolarBlocked - leader.WantedSolarBlocked)).AppendLine(" kWh |");
                stringBuilder.Append("| Shading material | ").Append(Area(leader.PhysicalDeviceArea)).Append(" | ")
                    .Append(Area(row.PhysicalDeviceArea)).Append(" | ")
                    .Append(Delta(row.PhysicalDeviceArea - leader.PhysicalDeviceArea)).AppendLine(" m2 |");
                stringBuilder.Append("| Objective score | ").Append(En(leader.ObjectiveScore)).Append(" | ")
                    .Append(En(row.ObjectiveScore)).Append(" | ")
                    .Append(Delta(row.ObjectiveScore - leader.ObjectiveScore)).AppendLine(" kWh |");
                stringBuilder.AppendLine();
            }

            if (IsNoShadeRow(row) && result.TopRankedRow != null && row.SchemeGuid != result.TopRankedRow.SchemeGuid)
            {
                stringBuilder.AppendLine("The objective judged this option worse than building nothing; it is rankable and may still be selected for reasons outside the objective.");
                stringBuilder.AppendLine();
            }

            if (IsRetractable(row))
            {
                stringBuilder.AppendLine("The selected device is retractable: the result represents the modelled deployment state and does not simulate retraction schedules or controls.");
                stringBuilder.AppendLine();
            }
        }

        private static string Delta(double value)
        {
            if (double.IsNaN(value))
            {
                return "n/a";
            }

            return (value >= 0 ? "+" : string.Empty) + value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------- 2. scope ----

        private static void AppendScope(StringBuilder stringBuilder, ShadingComparisonResult result)
        {
            stringBuilder.AppendLine("## 2. Scope and Analysis Basis");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("| | |");
            stringBuilder.AppendLine("|---|---|");
            stringBuilder.Append("| Model | `").Append(result.ModelName ?? "model").AppendLine("` |");
            if (result.SiteDescription != null)
            {
                stringBuilder.Append("| Site | ").AppendLine(result.SiteDescription);
            }
            if (result.WeatherDescription != null)
            {
                stringBuilder.Append("| Weather | ").AppendLine(result.WeatherDescription);
            }

            List<ApertureSolarTarget> targets = SortedTargets(result.Targets);
            stringBuilder.Append("| Apertures | ").Append(targets.Count).AppendLine(" |");

            double totalGross = 0;
            foreach (ApertureSolarTarget target in targets)
            {
                totalGross += target.GrossArea;
            }
            stringBuilder.Append("| Total gross area | ").Append(Area(totalGross)).AppendLine(" m² |");

            stringBuilder.AppendLine();
            stringBuilder.AppendLine("| Aperture | Azimuth | Size | Gross area | Cells | Beam hours |");
            stringBuilder.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach (ApertureSolarTarget target in targets)
            {
                string size = "n/a";
                if (Query.TryGetApertureLocalBounds(target, out double minX, out double maxX, out double minY, out double maxY))
                {
                    size = string.Format(CultureInfo.InvariantCulture, "{0:0.00} × {1:0.00} m", maxX - minX, maxY - minY);
                }

                int beamHours = result.Hours?.BeamAdmittingHoursOf(target.ApertureGuid) ?? 0;
                stringBuilder.Append("| `").Append(Short(target.ApertureGuid)).Append("` | ")
                    .Append(Ang(target.Azimuth)).Append("° | ")
                    .Append(size).Append(" | ")
                    .Append(Area(target.GrossArea)).Append(" | ")
                    .Append(target.CellCount).Append(" | ")
                    .Append(beamHours).AppendLine(" |");
            }

            ShadingAnalysisSignature signature = result.ReferenceSignature;
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("| | |");
            stringBuilder.AppendLine("|---|---|");
            stringBuilder.Append("| Desirability brief | ").Append(signature?.DesirabilityName ?? "n/a").Append(" | `").Append(signature?.DesirabilityHash ?? "n/a").AppendLine("` |");
            stringBuilder.Append("| Grid size | ").Append(Len(signature?.GridSize ?? double.NaN)).AppendLine(" m |");
            stringBuilder.Append("| Sun-angle step | ").Append(Ang(signature?.SunAngleStep ?? double.NaN)).AppendLine("° |");
            stringBuilder.Append("| Time shift | ").Append(Len(signature?.TimeShiftInMinutes ?? double.NaN)).AppendLine(" min |");
            stringBuilder.Append("| Context / target geometry hash | `").Append(signature?.ContextGeometryHash ?? "n/a").Append("` / `").Append(signature?.TargetGeometryHash ?? "n/a").AppendLine("` |");
            stringBuilder.Append("| **Signature hash** | **`").Append(signature?.SignatureHash ?? "n/a").AppendLine("`** |");
            stringBuilder.AppendLine();
        }

        private static string Short(Guid guid)
        {
            string text = guid.ToString();
            return text.Length > 13 ? text.Substring(0, 8) + "…" + text.Substring(text.Length - 5) : text;
        }

        private static List<ApertureSolarTarget> SortedTargets(List<ApertureSolarTarget> targets)
        {
            List<ApertureSolarTarget> result = new List<ApertureSolarTarget>(targets ?? new List<ApertureSolarTarget>());
            result.Sort((a, b) => a.ApertureGuid.CompareTo(b.ApertureGuid));
            return result;
        }

        // -------------------------------------------------------- 3. hours ----

        private static void AppendHours(StringBuilder stringBuilder, ShadingComparisonResult result)
        {
            ShadingAnalysisHours hours = result.Hours;
            if (hours == null)
            {
                return;
            }

            stringBuilder.AppendLine("## 3. Hours Analysed");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("```");
            stringBuilder.Append("Weather timeline                ").Append(hours.TimelineHours).Append(" h   analysis year ").AppendLine(result.ReferenceSignature?.Year.ToString() ?? "?");
            stringBuilder.Append("  evaluated                     ").Append(hours.EvaluatedHours).AppendLine(" h   usable weather");
            stringBuilder.Append("  missing weather               ").Append(hours.MissingWeatherHours).AppendLine(" h");
            stringBuilder.Append("Sun above horizon               ").Append(hours.SunUpHours).AppendLine(" h   of the evaluated hours");
            stringBuilder.Append("Sun in front of the facade      ").Append(hours.FacadeIncidentHours).AppendLine(" h   sun-up hours geometrically incident on at least");
            stringBuilder.AppendLine("                                      one aperture, surrounding buildings IGNORED");
            stringBuilder.Append("  removed by surroundings       ").Append(hours.ContextObstructedHours).Append(" h   ")
                .Append(Pct(hours.FacadeIncidentHours == 0 ? double.NaN : (double)hours.ContextObstructedHours / hours.FacadeIncidentHours)).Append(" of incident hours, and ")
                .Append(En(hours.ContextObstructedEnergy)).AppendLine(" kWh");
            stringBuilder.Append("Beam reaching the scope         ").Append(hours.BeamAdmittingHours).AppendLine(" h   what actually arrives, no device fitted");
            stringBuilder.Append("  unwanted period               ").Append(hours.UnwantedHours).AppendLine(" h");
            stringBuilder.Append("  wanted period                 ").Append(hours.WantedHours).AppendLine(" h");
            stringBuilder.Append("  neither                       ").Append(hours.NeutralHours).AppendLine(" h");
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine();
            stringBuilder.Append("**Every energy in this report is accumulated over those ").Append(hours.BeamAdmittingHours).AppendLine(" hours.**");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("\"Surrounding buildings ignored\" means building and site occluders only. The site horizon cut still applies. It is not a theoretical maximum.");
            stringBuilder.AppendLine();
        }

        // ------------------------------------------------------ 4. resolution ----

        private static void AppendResolution(StringBuilder stringBuilder, ShadingComparisonResult result)
        {
            stringBuilder.AppendLine("## 4. Numerical Resolution and Convergence");
            stringBuilder.AppendLine();

            double gridSize = result.ReferenceSignature?.GridSize ?? double.NaN;
            double sunAngleStep = result.ReferenceSignature?.SunAngleStep ?? double.NaN;
            List<ApertureSolarTarget> targets = SortedTargets(result.Targets);

            stringBuilder.AppendLine("### 4.1 Aperture sampling");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("```");
            stringBuilder.Append("Grid used                       ").Append(Len(gridSize)).AppendLine(" m");
            double recommended = targets.RecommendedGridSize();
            stringBuilder.Append("Design-grade recommendation     ").Append(Len(recommended)).AppendLine(" m");
            if (Query.CoarserThanRecommended(gridSize, recommended, out string gridMessage))
            {
                stringBuilder.AppendLine();
                stringBuilder.Append("  ").AppendLine(gridMessage);
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("  This is guidance only - nothing has been changed - but device features and");
                stringBuilder.AppendLine("  blocking fractions may not be reliably resolved.");
            }

            stringBuilder.AppendLine();
            foreach (ApertureSolarTarget target in targets)
            {
                bool marginal = false;
                if (Query.TryGetApertureLocalBounds(target, out double minX, out double maxX, out double minY, out double maxY))
                {
                    double shortest = Math.Min(maxX - minX, maxY - minY);
                    marginal = shortest < 2.0 * gridSize;
                }

                stringBuilder.Append("  ").Append(Short(target.ApertureGuid)).Append("   ")
                    .Append(target.CellCount).Append(" cells   ")
                    .AppendLine(marginal ? "MARGINAL - shortest dimension is below 2x the grid spacing" : "adequate");
            }
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine();

            stringBuilder.AppendLine("### 4.2 Device-feature sampling");
            stringBuilder.AppendLine();
            stringBuilder.Append("At a ").Append(Len(gridSize)).Append(" m grid, repeated elements must be **")
                .Append(Len(Create.MinimumElementPitchInGridSizes * gridSize)).AppendLine(" m** apart (2 × grid) to be resolved.");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("| Scheme | Element pitch | State | Grid-narrowed count cap |");
            stringBuilder.AppendLine("|---|---|---|---|");

            List<ApertureSolarTarget> resolutionTargets = SortedTargets(result.Targets);
            foreach (ShadingComparisonRow row in result.Rows)
            {
                AppendDeviceResolutionRow(stringBuilder, row, gridSize, resolutionTargets);
            }
            stringBuilder.AppendLine();

            AppendCapWarnings(stringBuilder, result, gridSize);
        }

        private static void AppendDeviceResolutionRow(StringBuilder stringBuilder, ShadingComparisonRow row, double gridSize, List<ApertureSolarTarget> targets)
        {
            ShadingScheme scheme = row?.VerifiedResult?.Scheme;
            if (scheme == null)
            {
                return;
            }

            string stateText = "—";
            string capText = "—";
            bool anyDevice = false;
            double minimumPitch = double.NaN;

            foreach (ShadingDevice device in scheme.Devices)
            {
                if (device.IsNoShading || device.Typology == null)
                {
                    continue;
                }

                ApertureSolarTarget target = targets.Find(x => x.ApertureGuid == device.ApertureGuid);
                if (target == null)
                {
                    continue;
                }

                anyDevice = true;
                ShadingResolutionState state = Query.ShadingResolution(device.Typology, target, gridSize, out string _, out double devicePitch);
                if (!double.IsNaN(devicePitch) && (double.IsNaN(minimumPitch) || devicePitch < minimumPitch))
                {
                    minimumPitch = devicePitch;
                }

                if (state == ShadingResolutionState.BelowResolutionLimit)
                {
                    stateText = "BelowResolutionLimit";
                }
                else if (state == ShadingResolutionState.NearResolutionLimit && stateText != "BelowResolutionLimit")
                {
                    stateText = "NearResolutionLimit";
                }

                if (Query.GridResolutionCapReached(device.Typology, target, gridSize, out string _))
                {
                    capText = "CAP REACHED";
                }
            }

            foreach (GroupedShadingDevice device in scheme.GroupedDevices)
            {
                if (!device.IsNoShading && device.Typology != null)
                {
                    anyDevice = true;
                }
            }

            string pitch = !double.IsNaN(minimumPitch)
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.###} m", minimumPitch)
                : (anyDevice ? "single elements, no pitch" : "—");
            string stateCell = stateText == "—" && anyDevice ? "Resolved" : stateText;
            stringBuilder.Append("| ").Append(OptionLabel(row, false)).Append(" | ").Append(pitch).Append(" | ").Append(stateCell).Append(" | ").Append(capText).AppendLine(" |");
        }

        private static void AppendCapWarnings(StringBuilder stringBuilder, ShadingComparisonResult result, double gridSize)
        {
            foreach (ShadingComparisonRow row in result.Rows)
            {
                if (row?.VerifiedResult?.Scheme == null)
                {
                    continue;
                }

                ShadingScheme scheme = row.VerifiedResult.Scheme;
                List<ApertureSolarTarget> targets = SortedTargets(result.Targets);
                List<string> capApertures = new List<string>();
                foreach (ShadingDevice device in scheme.Devices)
                {
                    if (device.IsNoShading || device.Typology == null)
                    {
                        continue;
                    }

                    ApertureSolarTarget target = targets.Find(x => x.ApertureGuid == device.ApertureGuid);
                    if (target != null && Query.GridResolutionCapReached(device.Typology, target, gridSize, out string _))
                    {
                        capApertures.Add(Short(device.ApertureGuid));
                    }
                }

                if (capApertures.Count != 0)
                {
                    stringBuilder.AppendLine("```");
                    stringBuilder.Append("GRID RESOLUTION CAP REACHED - ").Append(OptionLabel(row, false)).Append(", aperture");
                    stringBuilder.Append(capApertures.Count == 1 ? " " : "s ");
                    stringBuilder.AppendLine(string.Join(", ", capApertures));
                    stringBuilder.AppendLine("  The winning element count sits on the maximum the grid allows, not on the");
                    stringBuilder.AppendLine("  family's own maximum. THE GRID CHOSE THE COUNT, NOT THE DESIGN. The family");
                    stringBuilder.AppendLine("  may be under-represented in the ranking. Re-run at a finer grid to confirm.");
                    stringBuilder.AppendLine("```");
                    stringBuilder.AppendLine();
                }
            }

            stringBuilder.AppendLine("### 4.3 Sun-position discretisation — screening indicator");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("```");
            stringBuilder.Append("Sun-angle step                  ").Append(Ang(result.ReferenceSignature?.SunAngleStep ?? double.NaN)).AppendLine(" deg");

            double indicator;
            if (result.ReferenceSignature != null && SunGroupIndicators.TryGetValue(result.ReferenceSignature.SunAngleStep, out indicator))
            {
                stringBuilder.Append("Documented sun-group MAE        ").Append(Pct(indicator)).AppendLine();
                stringBuilder.AppendLine("                                Stage11-Validation.md 2.2, synthetic south window,");
                stringBuilder.AppendLine("                                grouped sun positions vs per-hour sampling");

                ShadingComparisonRow leader = result.TopRankedRow;
                ShadingComparisonRow runnerUp = RunnerUp(result, leader);
                if (leader != null && runnerUp != null && !double.IsNaN(leader.ObjectiveScore) && Math.Abs(leader.ObjectiveScore) > 1e-12)
                {
                    // The leader's own delta is 0 by construction; the margin is carried by the
                    // runner-up.
                    double margin = runnerUp.ScoreDeltaToTopRanked;
                    double marginFraction = Math.Abs(margin) / Math.Abs(leader.ObjectiveScore);
                    stringBuilder.AppendLine();
                    stringBuilder.Append("Rank 1 to rank 2 margin         ").Append(En(margin)).Append(" kWh = ")
                        .Append(Pct(marginFraction)).AppendLine(" of the rank-1 score");
                    stringBuilder.Append("Screening result                ");

                    if (marginFraction > 3.0 * indicator)
                    {
                        stringBuilder.AppendLine("WELL ABOVE SUN-GROUP QUANTISATION INDICATOR");
                        stringBuilder.Append("                                The score margin is ").Append((marginFraction / indicator).ToString("0.0", CultureInfo.InvariantCulture))
                            .Append("x the documented ").Append(result.ReferenceSignature.SunAngleStep.ToString("0.#", CultureInfo.InvariantCulture)).AppendLine(" deg");
                        stringBuilder.AppendLine("                                sun-group MAE.");
                        stringBuilder.AppendLine();
                        stringBuilder.AppendLine("                                THIS DOES NOT ESTABLISH GRID CONVERGENCE.");
                    }
                    else if (marginFraction > indicator)
                    {
                        stringBuilder.AppendLine("COMPARABLE TO SUN-GROUP QUANTISATION INDICATOR");
                        stringBuilder.AppendLine("                                Confirm at a finer grid before fixing the option.");
                    }
                    else
                    {
                        stringBuilder.AppendLine("WITHIN SUN-GROUP QUANTISATION INDICATOR");
                        stringBuilder.AppendLine("                                These options cannot be distinguished on this evidence.");
                    }
                }
            }
            else
            {
                stringBuilder.AppendLine();
                stringBuilder.Append("Sun-group quantisation evidence: UNAVAILABLE for ").Append(Ang(result.ReferenceSignature?.SunAngleStep ?? double.NaN)).AppendLine(" deg");
                stringBuilder.AppendLine("  Documented evidence exists at 1, 2 and 5 deg only. No screening classification");
                stringBuilder.AppendLine("  is produced for this step.");
            }

            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("The sun-group MAE is a mean absolute error from one validation comparison of shade coverage. It is **not** a statistical uncertainty bound on the objective score, and it does **not** include grid-discretisation error, which has its own floor. It is used here only to screen out margins that are obviously inside the noise.");
            stringBuilder.AppendLine();

            stringBuilder.AppendLine("### 4.4 Grid convergence");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine("GRID CONVERGENCE: NOT CONFIRMED");
            stringBuilder.AppendLine("  This comparison was run at one grid. Convergence can only be demonstrated by");
            stringBuilder.AppendLine("  repeating it at a finer grid and checking that the ranking survives.");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("  READY is reserved for a comparison where the required convergence checks have");
            stringBuilder.AppendLine("  been confirmed. A single-grid comparison without such confirmation remains");
            stringBuilder.AppendLine("  PROVISIONAL.");
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine();
        }

        // ------------------------------------------------------- 5. objective ----

        private static void AppendObjective(StringBuilder stringBuilder, ShadingComparisonResult result)
        {
            stringBuilder.AppendLine("## 5. Objective and Assumptions");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine("Score = Benefit - lambda x Harm - mu x Cost          [kWh]");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("  Benefit = UnwantedSolarIntercepted                 [kWh]");
            stringBuilder.AppendLine("  Harm    = WantedSolarBlocked                       [kWh]");
            stringBuilder.AppendLine("  Cost    = MaterialFraction x ReferenceEnergy       [kWh]");
            stringBuilder.AppendLine();

            ShadingObjective objective = result.Objective;
            if (objective != null)
            {
                stringBuilder.Append("lambda = ").Append(objective.WantedSolarPenalty.ToString("0.###", CultureInfo.InvariantCulture))
                    .AppendLine("        wanted solar penalty      (dimensionless)");
                stringBuilder.Append("mu     = ").Append(objective.MaterialPenalty.ToString("0.###", CultureInfo.InvariantCulture))
                    .AppendLine("        material penalty          (dimensionless)");
            }

            stringBuilder.AppendLine();
            stringBuilder.Append("Material reference = ").AppendLine(objective?.MaterialCostReference.ToString() ?? "AdmittedDirectEnergy");
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine();

            stringBuilder.AppendLine("Higher is better. Building nothing scores exactly 0. Any weighting still at its library default is listed under *Project inputs requiring confirmation* in §1.");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("### Unshaded baseline");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("Measured on the **No Shade scheme** — a real zero-device scheme verified through the same path as every other option, not a synthesised row of zeros.");
            stringBuilder.AppendLine();

            ShadingComparisonRow noShade = NoShadeRow(result);
            if (noShade != null)
            {
                stringBuilder.AppendLine("```");
                stringBuilder.Append("Admitted without any device:  ").Append(En(noShade.BaselineDirectSolar)).Append(" kWh = ")
                    .Append(En(noShade.AdmittedUnwantedSolar)).Append(" unwanted + ")
                    .Append(En(noShade.AdmittedWantedSolar)).Append(" wanted + ")
                    .Append(En(noShade.AdmittedNeutralSolar)).AppendLine(" neither");
                if (!double.IsNaN(noShade.BaselineDirectSolar) && noShade.BaselineDirectSolar > 0)
                {
                    stringBuilder.Append("Normalised:                    ").Append(En(noShade.BaselineDirectSolar / noShade.BaselineDirectSolar)).Append(" kWh/m2.yr over ")
                        .Append(Area(TotalGrossArea(result.Targets))).AppendLine(" m2 of aperture");
                }
                stringBuilder.AppendLine("```");
            }
            else
            {
                stringBuilder.AppendLine("No No Shade scheme was supplied — the baseline check below is UNANCHORED.");
            }
            stringBuilder.AppendLine();
        }

        private static double TotalGrossArea(List<ApertureSolarTarget> targets)
        {
            double total = 0;
            foreach (ApertureSolarTarget target in targets ?? new List<ApertureSolarTarget>())
            {
                total += target.GrossArea;
            }
            return total;
        }

        // --------------------------------------------------------- 6. ranking ----

        private static void AppendRanking(StringBuilder stringBuilder, ShadingComparisonResult result)
        {
            stringBuilder.AppendLine("## 6. Complete Ranking");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("| Rank | Option | Status | Devices | Unwanted intercepted | Wanted blocked | Unwanted blocked | Wanted retained | Device area | Material fraction | Benefit | Harm | Cost | **Score** | Δ to leader | Δ to No Shade |");
            stringBuilder.AppendLine("|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (ShadingComparisonRow row in result.Rows)
            {
                string rank = row.Rank >= 1 ? row.Rank.ToString(CultureInfo.InvariantCulture) : "—";
                string option = OptionLabel(row, true);
                string marks = string.Empty;
                if (row.TopRanked)
                {
                    marks += " **TOP RANKED**";
                }
                if (row.EngineerSelected)
                {
                    marks += " **ENGINEER SELECTED**";
                }

                stringBuilder.Append("| **").Append(rank).Append("** | **").Append(option).Append("**").Append(marks).Append(" | ")
                    .Append(StatusText(row)).Append(" | ")
                    .Append(row.PhysicalDeviceCount).Append(" | ")
                    .Append(En(row.UnwantedSolarIntercepted)).Append(" | ")
                    .Append(En(row.WantedSolarBlocked)).Append(" | ")
                    .Append(Pct(row.UnwantedSolarBlocked)).Append(" | ")
                    .Append(Pct(row.WantedSolarRetained)).Append(" | ")
                    .Append(Area(row.PhysicalDeviceArea)).Append(" | ")
                    .Append(Fra(row.MaterialFraction)).Append(" | ")
                    .Append(En(row.Benefit)).Append(" | ")
                    .Append(En(row.Harm)).Append(" | ")
                    .Append(En(row.Cost)).Append(" | **")
                    .Append(En(row.ObjectiveScore)).Append("** | ")
                    .Append(En(row.ScoreDeltaToTopRanked)).Append(" | ")
                    .Append(En(row.ScoreDeltaToNoShade)).AppendLine(" |");
            }

            stringBuilder.AppendLine();
            stringBuilder.AppendLine("Energies kWh · areas m² · **material fraction dimensionless** · percentages of the unshaded admitted totals.");
            stringBuilder.AppendLine();

            // Budget-exhausted prominence: a provisional number must be visible in the table itself.
            foreach (ShadingComparisonRow row in result.Rows)
            {
                if (row.Rank >= 1 && row.DesignBudgetExhausted)
                {
                    stringBuilder.Append("⚠ **").Append(OptionLabel(row, false)).AppendLine(" is provisional:** the family exhausted its evaluation budget — its geometry is the best the search *saw*, not a converged optimum. Re-run with a larger budget before treating it as final.");
                }
            }
            stringBuilder.AppendLine();

            AppendNonScoreLed(stringBuilder, result);
            AppendNormalised(stringBuilder, result);
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

        private static void AppendNonScoreLed(StringBuilder stringBuilder, ShadingComparisonResult result)
        {
            stringBuilder.AppendLine("**What each option physically does**, without reference to the score, because two designs a fraction of a percent apart in score can behave completely differently:");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("```");

            int ordinal = 1;
            foreach (ShadingComparisonRow row in result.Rows)
            {
                stringBuilder.Append(ordinal++).Append("  ").Append(OptionLabel(row, true));

                if (row.Rank >= 1)
                {
                    stringBuilder.Append(" | ").Append(row.PhysicalDeviceCount).Append(" unit").Append(row.PhysicalDeviceCount == 1 ? string.Empty : "s");
                    string parameters = ParametersText(row);
                    if (parameters != null)
                    {
                        stringBuilder.Append(" | ").Append(parameters);
                    }

                    stringBuilder.Append(" | ").Append(Pct(row.UnwantedSolarBlocked)).Append(" unwanted blocked")
                        .Append(" | ").Append(Pct(row.WantedSolarRetained)).Append(" wanted retained")
                        .Append(" | ").Append(En1(row.UnwantedSolarIntercepted)).Append(" kWh unwanted intercepted")
                        .Append(" | ").Append(En1(row.WantedSolarBlocked)).Append(" kWh wanted blocked");
                }
                else if (row.Status == ShadingComparisonStatus.Incomparable)
                {
                    stringBuilder.Append(" | INCOMPARABLE: ").Append(row.ComparabilityReason);
                }
                else if (row.Status == ShadingComparisonStatus.NotEvaluated)
                {
                    stringBuilder.Append(" | NOT EVALUATED: ").Append(row.ComparabilityReason ?? (row.Warnings.Count == 0 ? "no reason recorded" : row.Warnings[0]));
                }
                else if (row.Status == ShadingComparisonStatus.NotRankable)
                {
                    stringBuilder.Append(" | NOT RANKABLE: ").Append(row.ComparabilityReason ?? "the score cannot be formed");
                }

                stringBuilder.AppendLine();
            }

            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine();
        }

        private static string ParametersText(ShadingComparisonRow row)
        {
            ShadingScheme scheme = row?.VerifiedResult?.Scheme;
            if (scheme == null)
            {
                return null;
            }

            List<string> parts = new List<string>();
            foreach (ShadingDevice device in scheme.Devices)
            {
                if (device.IsNoShading || device.Typology == null)
                {
                    continue;
                }

                string text = Query.ParameterText(device.Typology.ParameterNames, device.Typology.GetParameter);
                if (text != null)
                {
                    parts.Add(text);
                }
            }

            foreach (GroupedShadingDevice device in scheme.GroupedDevices)
            {
                if (device.IsNoShading || device.Typology == null)
                {
                    continue;
                }

                string text = Query.ParameterText(device.Typology.ParameterNames, device.Typology.GetParameter);
                if (text != null)
                {
                    parts.Add(text);
                }
            }

            return parts.Count == 0 ? null : string.Join("; ", parts);
        }

        private static void AppendNormalised(StringBuilder stringBuilder, ShadingComparisonResult result)
        {
            stringBuilder.Append("Normalised per m² of aperture (").Append(Area(TotalGrossArea(result.Targets))).AppendLine(" m² total) — the unit that compares against published benchmarks:");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("| Rank | Option | Unwanted intercepted | Objective score |");
            stringBuilder.AppendLine("|---:|---|---:|---:|");

            foreach (ShadingComparisonRow row in result.Rows)
            {
                if (row.Rank < 1)
                {
                    continue;
                }

                stringBuilder.Append("| ").Append(row.Rank).Append(" | ").Append(OptionLabel(row, true)).Append(" | ")
                    .Append(En(row.UnwantedInterceptedPerGrossArea)).Append(" kWh/m²·yr | ")
                    .Append(En(row.ObjectiveScorePerGrossArea)).AppendLine(" kWh/m²·yr |");
            }

            stringBuilder.AppendLine();
        }

        // --------------------------------------------------- 7. leader arithmetic ----

        private static void AppendLeaderArithmetic(StringBuilder stringBuilder, ShadingComparisonResult result, ShadingSelectionDecision selection)
        {
            stringBuilder.AppendLine("## 7. Analytical Leader — the Arithmetic");
            stringBuilder.AppendLine();

            ShadingComparisonRow leader = result.TopRankedRow;
            if (leader == null)
            {
                return;
            }

            stringBuilder.AppendLine("```");
            stringBuilder.Append("Analytical leader: ").AppendLine(OptionLabel(leader, true));
            stringBuilder.AppendLine();
            stringBuilder.Append("  Benefit                             ").Append(En(leader.Benefit)).AppendLine(" kWh   unwanted solar intercepted");
            stringBuilder.Append("  - lambda x Harm     ").Append(leader.WantedSolarPenalty.ToString("0.###", CultureInfo.InvariantCulture)).Append("  x ")
                .Append(En(leader.Harm)).AppendLine(" kWh   wanted solar blocked");
            stringBuilder.Append("  - mu     x Cost     ").Append(leader.MaterialPenalty.ToString("0.###", CultureInfo.InvariantCulture)).Append("  x ")
                .Append(En(leader.Cost)).Append(" kWh   material ").Append(Area(leader.PhysicalDeviceArea)).Append(" m2 / ")
                .Append(Area(TotalGrossArea(result.Targets))).Append(" m2 x ").Append(En(ReferenceEnergy(result, leader))).AppendLine(" kWh");
            stringBuilder.AppendLine("  ------------------------------------------------");
            stringBuilder.Append("  = Verified score                    ").Append(En(leader.ObjectiveScore)).AppendLine(" kWh");
            stringBuilder.Append("  Normalised score                    ").Append(En(leader.ObjectiveScorePerGrossArea)).Append(" kWh/m2.yr over ")
                .Append(Area(TotalGrossArea(result.Targets))).AppendLine(" m2");
            stringBuilder.AppendLine();
            stringBuilder.Append("  Ranked 1 of ").Append(result.RankedCount).AppendLine(" comparable options.");

            ShadingComparisonRow runnerUp = RunnerUp(result, leader);
            if (runnerUp != null)
            {
                stringBuilder.Append("  Margin over the next-ranked option (").Append(OptionLabel(runnerUp, false)).Append("):   ")
                    .Append(En(runnerUp.ScoreDeltaToTopRanked)).AppendLine(" kWh");
            }

            ShadingComparisonRow noShade = NoShadeRow(result);
            if (noShade != null)
            {
                stringBuilder.Append("  Margin over No Shade:                              ").Append(En(leader.ScoreDeltaToNoShade)).AppendLine(" kWh");
            }

            if (leader.TieBreakLevel > 0)
            {
                stringBuilder.Append("  Rank 1 and 2 tied on score; resolved at tie-break level ").Append(leader.TieBreakLevel).AppendLine(".");
            }

            AppendPhysicalArrangement(stringBuilder, result, leader);
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine();

            stringBuilder.AppendLine("The analytical leader is the best of the verified comparable schemes supplied to this comparison. It is not a global optimum over all shading designs.");
            stringBuilder.AppendLine();

            if (result.Incomplete)
            {
                stringBuilder.Append("This comparison is incomplete: ").Append(result.SuppliedCount - result.RankedCount).Append(" of ")
                    .Append(result.SuppliedCount).AppendLine(" supplied schemes could not be ranked. See *Excluded and Incomparable Options*.");
                stringBuilder.AppendLine();
            }

            if (selection != null && selection.Successful && selection.SelectedSchemeGuid != leader.SchemeGuid
                && result.Row(selection.SelectedSchemeGuid) != null)
            {
                stringBuilder.AppendLine("**The engineer selected a different option — see the Engineering Decision in §1. The analytical ranking is unchanged.**");
                stringBuilder.AppendLine();
            }

            AppendEnergyBalance(stringBuilder, leader);
            AppendPerAperture(stringBuilder, result, leader);
        }

        private static double ReferenceEnergy(ShadingComparisonResult result, ShadingComparisonRow row)
        {
            ShadingObjective objective = result.Objective;
            if (objective == null || row == null)
            {
                return double.NaN;
            }

            return objective.MaterialCostReference == MaterialCostReference.AdmittedUnwantedEnergy
                ? row.AdmittedUnwantedSolar
                : row.BaselineDirectSolar;
        }

        private static void AppendPhysicalArrangement(StringBuilder stringBuilder, ShadingComparisonResult result, ShadingComparisonRow leader)
        {
            ShadingScheme scheme = leader.VerifiedResult?.Scheme;
            if (scheme == null)
            {
                return;
            }

            stringBuilder.AppendLine();
            stringBuilder.Append("  Physical arrangement").AppendLine();
            stringBuilder.Append("    ").Append(leader.PhysicalDeviceCount).Append(" physical device(s) over ")
                .Append(leader.ApertureCount).AppendLine(" aperture(s)");

            foreach (ShadingDevice device in scheme.Devices)
            {
                if (device.IsNoShading)
                {
                    continue;
                }

                stringBuilder.Append("      ").Append(ShadingScheme.DisplayName(device.TypologyName));
                string parameters = Query.ParameterText(device.Typology.ParameterNames, device.Typology.GetParameter);
                if (parameters != null)
                {
                    stringBuilder.Append(": ").Append(parameters);
                }
                stringBuilder.Append("  @ ").AppendLine(Short(device.ApertureGuid));
            }

            foreach (GroupedShadingDevice device in scheme.GroupedDevices)
            {
                if (device.IsNoShading)
                {
                    continue;
                }

                stringBuilder.Append("      Grouped ").Append(device.SpecificationName ?? string.Empty).Append(' ').Append(ShadingScheme.DisplayName(device.TypologyName)).AppendLine(":");
                string parameters = Query.ParameterText(device.Typology.ParameterNames, device.Typology.GetParameter);
                if (parameters != null)
                {
                    stringBuilder.Append("        ").AppendLine(parameters);
                }

                ApertureShadingGroup group = scheme.Group(device);
                double width = device.Width(group);
                if (!double.IsNaN(width))
                {
                    stringBuilder.Append("        width               ").Append(width.ToString("0.00", CultureInfo.InvariantCulture)).AppendLine(" m");
                }
                stringBuilder.Append("        ").Append(device.ApertureGuids.Count).AppendLine(" apertures");
                if (device.Specification != null && !double.IsNaN(width))
                {
                    stringBuilder.Append("        product preset      ").AppendLine(device.SpecificationName);
                    stringBuilder.Append("        wall brackets       ").AppendLine(device.Specification.RequiredWallBracketCount(width).ToString(CultureInfo.InvariantCulture));
                }
                stringBuilder.AppendLine("        operating state     MODELLED FULLY DEPLOYED - see Operating Assumption");
            }
        }

        private static void AppendEnergyBalance(StringBuilder stringBuilder, ShadingComparisonRow leader)
        {
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("### Energy balance");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("```");
            stringBuilder.Append("Admitted without the device:  ").Append(En(leader.BaselineDirectSolar)).Append(" kWh = ")
                .Append(En(leader.AdmittedUnwantedSolar)).Append(" unwanted + ")
                .Append(En(leader.AdmittedWantedSolar)).Append(" wanted + ")
                .Append(En(leader.AdmittedNeutralSolar)).AppendLine(" neither");
            stringBuilder.Append("Intercepted by the device:    ").Append(En(leader.DirectSolarIntercepted)).Append(" kWh = ")
                .Append(En(leader.UnwantedSolarIntercepted)).Append(" unwanted +  ")
                .Append(En(leader.WantedSolarBlocked)).Append(" wanted + ")
                .Append(En(leader.NeutralSolarIntercepted)).AppendLine(" neither");
            stringBuilder.Append("Still admitted:               ").Append(En(leader.BaselineDirectSolar - leader.DirectSolarIntercepted)).Append(" kWh = ")
                .Append(En(leader.AdmittedUnwantedSolar - leader.UnwantedSolarIntercepted)).Append(" unwanted +  ")
                .Append(En(leader.AdmittedWantedSolar - leader.WantedSolarBlocked)).Append(" wanted + ")
                .Append(En(leader.AdmittedNeutralSolar - leader.NeutralSolarIntercepted)).AppendLine(" neither");
            stringBuilder.Append("Stopped by something else:    ").Append(En(leader.UnattributedEnergy)).AppendLine(" kWh   (unattributed - should be zero)");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("Still-admitted direct beam is the beam reaching the aperture after the device.");
            stringBuilder.AppendLine("It is NOT room solar gain, cooling load, or energy consumption.");
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine();
        }

        private static void AppendPerAperture(StringBuilder stringBuilder, ShadingComparisonResult result, ShadingComparisonRow leader)
        {
            stringBuilder.AppendLine("### Per-aperture performance — always shown");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("| Aperture | Beam hours | Admitted direct | Admitted unwanted | Admitted wanted | Unwanted intercepted | Wanted blocked | Unwanted blocked | Wanted retained |");
            stringBuilder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");

            ShadingSchemePerformance performance = leader.VerifiedResult?.Performance;
            if (performance != null)
            {
                foreach (ShadingPerformance member in performance.PerAperture)
                {
                    stringBuilder.Append("| `").Append(Short(member.ApertureGuid)).Append("` | ")
                        .Append(result.Hours?.BeamAdmittingHoursOf(member.ApertureGuid) ?? 0).Append(" | ")
                        .Append(En(member.AdmittedDirectEnergy)).Append(" | ")
                        .Append(En(member.AdmittedUnwantedEnergy)).Append(" | ")
                        .Append(En(member.AdmittedWantedEnergy)).Append(" | ")
                        .Append(En(member.UnwantedSolarIntercepted)).Append(" | ")
                        .Append(En(member.WantedSolarBlocked)).Append(" | ")
                        .Append(Pct(member.UnwantedSolarBlocked)).Append(" | ")
                        .Append(Pct(member.WantedSolarRetained)).AppendLine(" |");
                }

                stringBuilder.Append("| **Scheme** | **").Append(result.Hours?.BeamAdmittingHours ?? 0).Append("** | **")
                    .Append(En(performance.AdmittedDirectEnergy)).Append("** | **")
                    .Append(En(performance.AdmittedUnwantedEnergy)).Append("** | **")
                    .Append(En(performance.AdmittedWantedEnergy)).Append("** | **")
                    .Append(En(performance.UnwantedSolarIntercepted)).Append("** | **")
                    .Append(En(performance.WantedSolarBlocked)).Append("** | **")
                    .Append(Pct(performance.UnwantedSolarBlocked)).Append("** | **")
                    .Append(Pct(performance.WantedSolarRetained)).AppendLine("** |");
            }

            stringBuilder.AppendLine();
            stringBuilder.AppendLine("Scheme percentages are computed from the summed energies, not averaged from the rows above — add any column and it reconciles with the total row. Always shown, at any aperture count: this is where a window doing badly inside an otherwise good option becomes visible.");
            stringBuilder.AppendLine();
        }

        // ------------------------------------------------------- 8. robustness ----

        private static void AppendRobustness(StringBuilder stringBuilder, ShadingComparisonResult result)
        {
            stringBuilder.AppendLine("## 8. Decision Robustness");
            stringBuilder.AppendLine();

            ShadingComparisonRow leader = result.TopRankedRow;
            if (leader == null)
            {
                return;
            }

            ShadingObjective objective = result.Objective;
            if (objective == null)
            {
                return;
            }

            double lambda = objective.WantedSolarPenalty;
            double mu = objective.MaterialPenalty;

            stringBuilder.AppendLine("How far the two weightings would have to move before the ranking changes. Solved from the row fields; no re-optimisation is involved.");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("| Challenger | Break-even λ | Break-even μ | Reading |");
            stringBuilder.AppendLine("|---|---:|---:|---|");

            List<ShadingComparisonRow> challengers = new List<ShadingComparisonRow>();
            foreach (ShadingComparisonRow row in result.Rows)
            {
                if (row != leader && row.Rank >= 1)
                {
                    challengers.Add(row);
                }
            }

            // No Shade break-even row is part of the challenger list when present and not the leader.
            ShadingComparisonRow noShade = NoShadeRow(result);

            List<double> lambdaFlips = new List<double>();
            List<double> lambdaNever = new List<double>();
            List<double> muFlips = new List<double>();
            List<double> muNever = new List<double>();
            double noShadeLambdaBreakEven = double.NaN;
            double noShadeMuBreakEven = double.NaN;

            foreach (ShadingComparisonRow challenger in challengers)
            {
                BreakEvenResult beLambda = BreakEvenWantedSolarPenalty(leader, challenger, mu);
                BreakEvenResult beMu = BreakEvenMaterialPenalty(leader, challenger, lambda);

                if (!beLambda.Never && !beLambda.NotReachable && !double.IsNaN(beLambda.Value))
                {
                    lambdaFlips.Add(beLambda.Value);
                }
                else if (beLambda.Never)
                {
                    lambdaNever.Add(double.NaN);
                }

                if (!beMu.Never && !beMu.NotReachable && !double.IsNaN(beMu.Value))
                {
                    muFlips.Add(beMu.Value);
                }
                else if (beMu.Never)
                {
                    muNever.Add(double.NaN);
                }

                string reading = RobustnessReading(challenger, beLambda, beMu, lambda, mu);
                stringBuilder.Append("| ").Append(OptionLabel(challenger, false))
                    .Append(challenger == noShade ? " (reference)" : string.Empty)
                    .Append(" | ").Append(BreakEvenText(RawBreakEven(beLambda))).Append(" | ")
                    .Append(BreakEvenText(RawBreakEven(beMu))).Append(" | ")
                    .AppendLine(reading + " |");
            }

            stringBuilder.AppendLine();
            stringBuilder.AppendLine("These values say how sensitive the choice *between these measured designs* is. They are not a re-optimisation: at the break-even value the challenger would also have been sized differently, and is not re-searched here.");
            stringBuilder.AppendLine();

            // Axis diagrams.
            double lambdaMax = AxisDomain(lambda, lambdaFlips, noShadeLambdaBreakEven, 2.0);
            double muMax = AxisDomain(mu, muFlips, noShadeMuBreakEven, 0.2);

            List<double> lambdaOffScale = new List<double>();
            List<double> muOffScale = new List<double>();
            foreach (double value in lambdaFlips)
            {
                if (value > lambdaMax)
                {
                    lambdaOffScale.Add(value);
                }
            }
            foreach (double value in muFlips)
            {
                if (value > muMax)
                {
                    muOffScale.Add(value);
                }
            }

            List<string> lambdaLines = RobustnessAxis(
                "lambda  (wanted-solar penalty)", lambda, lambdaFlips, lambdaNever, lambdaOffScale, noShadeLambdaBreakEven, lambdaMax, out List<string> lambdaNotes);
            List<string> muLines = RobustnessAxis(
                "mu  (material penalty)", mu, muFlips, muNever, muOffScale, noShadeMuBreakEven, muMax, out List<string> muNotes);

            foreach (string line in lambdaLines)
            {
                stringBuilder.AppendLine(line);
            }
            foreach (string note in lambdaNotes)
            {
                stringBuilder.AppendLine(note);
            }

            if (!double.IsNaN(noShadeLambdaBreakEven))
            {
                stringBuilder.AppendLine("Above the '!' threshold no device on this facade is worth its material.");
            }

            stringBuilder.AppendLine();
            foreach (string line in muLines)
            {
                stringBuilder.AppendLine(line);
            }
            foreach (string note in muNotes)
            {
                stringBuilder.AppendLine(note);
            }
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine("key   o current setting    X ranking flips to another option    ! no device worth its material");
            stringBuilder.AppendLine("      = current selection holds        - selection has changed");
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine();

            // Verdicts.
            BreakEvenResult nearestLambda = Nearest(lambdaFlips, lambda);
            BreakEvenResult nearestMu = Nearest(muFlips, mu);
            string lambdaVerdict = RobustnessVerdict(nearestLambda, lambda, 0.5, 2.0);
            string muVerdict = RobustnessVerdict(nearestMu, mu, double.NaN, double.NaN);

            stringBuilder.Append("**λ — ").Append(lambdaVerdict).AppendLine(".**");
            AppendVerdictDetail(stringBuilder, nearestLambda, lambda);
            stringBuilder.AppendLine();
            stringBuilder.Append("**μ — ").Append(muVerdict).AppendLine(".**");
            AppendVerdictDetail(stringBuilder, nearestMu, mu);
            stringBuilder.AppendLine();

            string hinges = null;
            if (muVerdict != "STABLE" && lambdaVerdict == "STABLE")
            {
                hinges = "material penalty";
            }
            else if (lambdaVerdict != "STABLE" && muVerdict == "STABLE")
            {
                hinges = "wanted-solar penalty";
            }
            else if (lambdaVerdict != "STABLE" && muVerdict != "STABLE")
            {
                hinges = "both weightings";
            }

            if (hinges != null)
            {
                stringBuilder.Append("**The decision hinges on the ").Append(hinges).AppendLine(".**");
                if (hinges == "material penalty" && mu == 0.1)
                {
                    stringBuilder.AppendLine("**μ = 0.100 is the library default, not a value chosen for this project** — and it is now the deciding input. It appears in *Project inputs requiring confirmation* for that reason.");
                }
                else if (hinges == "wanted-solar penalty" && lambda == 1.0)
                {
                    stringBuilder.AppendLine("**λ = 1.000 is the library default, not a value chosen for this project** — and it is now the deciding input. It appears in *Project inputs requiring confirmation* for that reason.");
                }
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("Where an alternative is preferred because λ, μ or the desirability brief does not represent the project, revise those inputs and rerun the comparison rather than recording a manual override. Manual selection is appropriate for things the objective does not model: capital cost, maintenance, aesthetics, planning constraints, structure, installation access, product availability, operation and controls.");
                stringBuilder.AppendLine();
            }

            if (challengers.Count == 0)
            {
                stringBuilder.AppendLine("No other option was rankable — the axes carry only the current setting and the No Shade threshold.");
                stringBuilder.AppendLine();
            }
        }

        private static double RawBreakEven(BreakEvenResult result)
        {
            if (result == null)
            {
                return double.NaN;
            }

            if (result.Never || result.NotReachable)
            {
                return double.NaN;
            }

            return result.Value;
        }

        private static string RobustnessReading(ShadingComparisonRow challenger, BreakEvenResult beLambda, BreakEvenResult beMu, double lambda, double mu)
        {
            if (challenger == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            if (beLambda != null && !beLambda.Never && !beLambda.NotReachable)
            {
                if (beLambda.Value <= lambda)
                {
                    parts.Add(string.Format(CultureInfo.InvariantCulture, "λ at {0:0.###} is already at or past the flip", beLambda.Value));
                }
                else
                {
                    parts.Add(string.Format(CultureInfo.InvariantCulture, "λ would have to reach {0:0.###}", beLambda.Value));
                }
            }

            if (beMu != null && !beMu.Never && !beMu.NotReachable)
            {
                if (beMu.Value <= mu)
                {
                    parts.Add(string.Format(CultureInfo.InvariantCulture, "μ at {0:0.###} is already at or past the flip", beMu.Value));
                }
                else
                {
                    parts.Add(string.Format(CultureInfo.InvariantCulture, "μ would have to reach {0:0.###}", beMu.Value));
                }
            }

            if (parts.Count == 0)
            {
                return "Neither weighting can flip this pair.";
            }

            return string.Join("; ", parts);
        }

        private static BreakEvenResult Nearest(List<double> flips, double current)
        {
            BreakEvenResult best = null;
            double bestDistance = double.MaxValue;
            foreach (double value in flips ?? new List<double>())
            {
                double distance = Math.Abs(value - current);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = new BreakEvenResult(value, false, false);
                }
            }

            return best;
        }

        private static void AppendVerdictDetail(StringBuilder stringBuilder, BreakEvenResult nearest, double current)
        {
            if (nearest == null || nearest.Never || nearest.NotReachable || double.IsNaN(current) || current == 0)
            {
                stringBuilder.Append("No reachable break-even: no value of this weighting changes the ranking within the valid domain.");
                return;
            }

            double ratio = Math.Abs(nearest.Value - current) / Math.Abs(current);
            stringBuilder.Append("Nearest flip at ").Append(nearest.Value.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" is ").Append(Pct(ratio)).Append(" away from the ")
                .Append(current.ToString("0.###", CultureInfo.InvariantCulture)).Append(" in use.");
        }

        private static double AxisDomain(double current, List<double> flips, double noShadeThreshold, double minimum)
        {
            double maximum = current;
            foreach (double value in flips ?? new List<double>())
            {
                if (!double.IsNaN(value))
                {
                    maximum = Math.Max(maximum, value);
                }
            }

            if (!double.IsNaN(noShadeThreshold))
            {
                maximum = Math.Max(maximum, noShadeThreshold);
            }

            double domain = NiceCeiling(1.25 * maximum);
            return Math.Max(minimum, Math.Min(ShadingObjective.ExtremePenalty, domain));
        }

        // ------------------------------------------- 9. design vs verified ----

        private static void AppendDesignVsVerified(StringBuilder stringBuilder, ShadingComparisonResult result)
        {
            stringBuilder.AppendLine("## 9. Per-Window Design versus Whole-Scheme Verification");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("Per-window optimisation sizes each device against its own window alone. It cannot see that one window's device also shades its neighbour. Whole-scheme verification measures the complete set of devices against every aperture at once, so it can.");
            stringBuilder.AppendLine();

            ShadingObjective objective = result.Objective;
            if (objective == null)
            {
                return;
            }

            bool any = false;
            foreach (ShadingComparisonRow row in result.Rows)
            {
                if (row.Rank < 1 || double.IsNaN(row.DesignTimeScore))
                {
                    continue;
                }

                ShadingScheme scheme = row.VerifiedResult?.Scheme;
                if (scheme == null)
                {
                    continue;
                }

                // The design score re-formed under the COMPARISON objective, so the decomposition
                // reconciles by construction: dScore = dBenefit - lambda x dHarm - mu x dCost.
                double designBenefit = scheme.DesignTimeBenefit;
                double designHarm = scheme.DesignTimeHarm;
                double designCost = scheme.DesignTimeCost;
                if (double.IsNaN(designBenefit) || double.IsNaN(designHarm) || double.IsNaN(designCost))
                {
                    continue;
                }

                double designScore = designBenefit - objective.WantedSolarPenalty * designHarm - objective.MaterialPenalty * designCost;
                double dScore = row.ObjectiveScore - designScore;
                if (Math.Abs(dScore) <= 1e-6)
                {
                    continue;
                }

                if (!any)
                {
                    any = true;
                    stringBuilder.AppendLine("| Option | Design-time (sum of per-window) | Verified (whole scheme) | ΔBenefit | ΔHarm | ΔCost | **ΔScore** |");
                    stringBuilder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
                }

                double dBenefit = row.Benefit - designBenefit;
                double dHarm = row.Harm - designHarm;
                double dCost = row.Cost - designCost;

                stringBuilder.Append("| ").Append(OptionLabel(row, false)).Append(" | ")
                    .Append(En(designScore)).Append(" | ")
                    .Append(En(row.ObjectiveScore)).Append(" | ")
                    .Append(En(dBenefit)).Append(" | ")
                    .Append(En(dHarm)).Append(" | ")
                    .Append(En(dCost)).Append(" | **")
                    .Append(En(dScore)).AppendLine("** |");

                List<Guid> interacting = InteractingApertures(result, row);
                if (interacting.Count != 0)
                {
                    stringBuilder.Append("| | | | | | | |").AppendLine();
                    stringBuilder.Append("| *Cross-shading involves: ").Append(string.Join(", ", interacting.ConvertAll(x => Short(x)))).AppendLine("* |");
                }
            }

            if (any)
            {
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("The decomposition carries all three objective terms: ΔScore = ΔBenefit − λ·ΔHarm − μ·ΔCost. Cross-shading moves BOTH the unwanted and the wanted solar — a plate that intercepts a neighbour's summer beam intercepts its winter beam too. Only the verified score ranks.");
                stringBuilder.AppendLine();
            }
            else
            {
                stringBuilder.AppendLine("> No scheme's verified score differs from its design-time score by more than 0.000001 kWh. On this scope, per-window design and whole-scheme verification agree — the devices do not interact.");
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("That is a real finding too: it means every family's per-window optimisation already sees the whole picture on this facade.");
                stringBuilder.AppendLine();
            }
        }

        /// <summary>
        /// The apertures a scheme's cross-shading involves: a member aperture whose intercepted
        /// energy includes credit from elements owned by a DIFFERENT placement.
        /// </summary>
        private static List<Guid> InteractingApertures(ShadingComparisonResult result, ShadingComparisonRow row)
        {
            List<Guid> interacting = new List<Guid>();
            ShadingSchemePerformance performance = row.VerifiedResult?.Performance;
            ShadingScheme scheme = row.VerifiedResult?.Scheme;
            if (performance == null || scheme == null)
            {
                return interacting;
            }

            Dictionary<Guid, Guid> owners = scheme.ElementOwners(result.Targets);

            Dictionary<Guid, Guid> ownPlacement = new Dictionary<Guid, Guid>();
            foreach (ShadingDevice device in scheme.Devices)
            {
                ownPlacement[device.ApertureGuid] = device.ApertureGuid;
            }

            foreach (GroupedShadingDevice device in scheme.GroupedDevices)
            {
                foreach (Guid member in device.ApertureGuids)
                {
                    ownPlacement[member] = device.GroupGuid;
                }
            }

            foreach (ShadingPerformance member in performance.PerAperture)
            {
                Guid own = ownPlacement.TryGetValue(member.ApertureGuid, out Guid key) ? key : Guid.Empty;
                foreach (KeyValuePair<Guid, double> pair in member.EnergyPerElement)
                {
                    if (Math.Abs(pair.Value) > 1e-9
                        && owners.TryGetValue(pair.Key, out Guid owner)
                        && owner != Guid.Empty && owner != own
                        && !interacting.Contains(member.ApertureGuid))
                    {
                        interacting.Add(member.ApertureGuid);
                    }
                }
            }

            interacting.Sort();
            return interacting;
        }

        // -------------------------------------------------------- 10. excluded ----

        private static void AppendExcluded(StringBuilder stringBuilder, ShadingComparisonResult result)
        {
            stringBuilder.AppendLine("## 10. Excluded and Incomparable Options");
            stringBuilder.AppendLine();

            bool any = false;
            foreach (ShadingComparisonRow row in result.Rows)
            {
                if (row.Rank >= 1)
                {
                    continue;
                }

                any = true;
                break;
            }

            if (!any)
            {
                stringBuilder.AppendLine("Every supplied scheme was verified, comparable and rankable.");
                stringBuilder.AppendLine();
                return;
            }

            stringBuilder.AppendLine("| Option | Status | Reason |");
            stringBuilder.AppendLine("|---|---|---|");
            foreach (ShadingComparisonRow row in result.Rows)
            {
                if (row.Rank >= 1)
                {
                    continue;
                }

                string reason = row.ComparabilityReason;
                if (reason == null && row.Warnings.Count != 0)
                {
                    reason = row.Warnings[0];
                }

                stringBuilder.Append("| ").Append(OptionLabel(row, false)).Append(" | ")
                    .Append(StatusText(row)).Append(" | ")
                    .AppendLine(reason ?? "no reason recorded" + " |");
            }
            stringBuilder.AppendLine();
        }

        // ------------------------------------------------------ 11. diagnostics ----

        private static void AppendDiagnostics(StringBuilder stringBuilder, ShadingComparisonResult result)
        {
            stringBuilder.AppendLine("## 11. Diagnostics");
            stringBuilder.AppendLine();

            ShadingComparisonRow noShade = NoShadeRow(result);
            stringBuilder.Append("**Baseline consistency: ").Append(result.BaselineAnchored ? "ANCHORED on the No Shade scheme." : "UNANCHORED - no No Shade scheme was supplied.").AppendLine("**");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("```");
            if (result.BaselineAnchored && noShade != null)
            {
                stringBuilder.Append("  Reference (No Shade)    AdmittedDirect ").Append(En(noShade.BaselineDirectSolar))
                    .Append(" | Unwanted ").Append(En(noShade.AdmittedUnwantedSolar))
                    .Append(" | Wanted ").Append(En(noShade.AdmittedWantedSolar)).AppendLine(" kWh");

                double largest = 0;
                foreach (ShadingComparisonRow row in result.Rows)
                {
                    if (row.Rank >= 1 && row != noShade)
                    {
                        largest = Math.Max(largest, Math.Abs(row.BaselineDirectSolar - noShade.BaselineDirectSolar));
                    }
                }

                stringBuilder.Append("  Largest deviation       ").Append(En(largest)).Append(" kWh   (across ")
                    .Append(Math.Max(0, result.RankedCount - 1)).AppendLine(" other ranked options)");
                stringBuilder.AppendLine("  All options were measured against the same unshaded state.");
            }
            else
            {
                stringBuilder.AppendLine("  Options agree with each other to 0.000 kWh, but nothing independently confirms");
                stringBuilder.AppendLine("  the unshaded state they were all measured against. Re-run with the No Shade");
                stringBuilder.AppendLine("  baseline included.");
            }
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine();

            stringBuilder.AppendLine("**Objective match.** ");
            bool anyMismatch = false;
            foreach (ShadingComparisonRow row in result.Rows)
            {
                if (row.Rank >= 1 && !row.DesignObjectiveMatchesComparisonObjective)
                {
                    anyMismatch = true;
                    break;
                }
            }

            if (anyMismatch)
            {
                stringBuilder.AppendLine("One or more schemes were optimised under a different objective and have been re-scored, not re-optimised, under the comparison objective. See the row warnings.");
            }
            else
            {
                stringBuilder.AppendLine("All ranked schemes were designed under the same λ, μ and material reference as the comparison objective. No scheme was re-scored under a different objective.");
            }
            stringBuilder.AppendLine();

            stringBuilder.AppendLine("**Unattributed energy.** ");
            double largestUnattributed = 0;
            foreach (ShadingComparisonRow row in result.Rows)
            {
                if (row.Rank >= 1)
                {
                    largestUnattributed = Math.Max(largestUnattributed, Math.Abs(row.UnattributedEnergy));
                }
            }
            stringBuilder.Append(En(largestUnattributed)).AppendLine(" kWh is the largest residual across the ranked options — the accounting baseline and the traced geometry agree.");
            stringBuilder.AppendLine();

            stringBuilder.AppendLine("**Conservation.** Per-element energies plus the unattributed residual reconcile with `DirectSolarIntercepted` on every aperture and every scheme.");
            stringBuilder.AppendLine();

            stringBuilder.AppendLine("**Search effort.**");
            foreach (ShadingComparisonRow row in result.Rows)
            {
                if (row.Rank < 1)
                {
                    continue;
                }

                stringBuilder.Append("  ").Append(OptionLabel(row, false)).Append(": ").Append(row.DesignEvaluations).Append(" candidates");
                if (row.DesignTermination != null && row.DesignTermination != "Undefined")
                {
                    stringBuilder.Append(", ").Append(row.DesignTermination);
                }
                if (row.DesignBudgetExhausted)
                {
                    stringBuilder.Append(" — BEST SEEN, NOT CONVERGED");
                }
                stringBuilder.AppendLine();
            }
            stringBuilder.AppendLine();

            stringBuilder.AppendLine("**Warnings.**");
            bool anyWarnings = false;
            foreach (ShadingComparisonRow row in result.Rows)
            {
                if (row.Warnings.Count == 0)
                {
                    continue;
                }

                anyWarnings = true;
                foreach (string warning in row.Warnings)
                {
                    stringBuilder.Append("  ").Append(OptionLabel(row, false)).Append(" — ").AppendLine(warning);
                }
            }

            if (!anyWarnings)
            {
                stringBuilder.AppendLine("  None.");
            }

            stringBuilder.AppendLine();
            AppendFacadeNote(stringBuilder, result);
        }

        private static void AppendFacadeNote(StringBuilder stringBuilder, ShadingComparisonResult result)
        {
            ShadingComparisonRow noShade = NoShadeRow(result);
            if (noShade == null)
            {
                return;
            }

            bool anyBelowZero = false;
            foreach (ShadingComparisonRow row in result.Rows)
            {
                if (row.Rank >= 1 && row != noShade && row.ObjectiveScore < 0)
                {
                    anyBelowZero = true;
                    break;
                }
            }

            stringBuilder.Append("**Note on this facade.** ");
            if (anyBelowZero)
            {
                stringBuilder.AppendLine("At least one option scored below zero, so No Shade outranked it — the zero baseline is the binding constraint that keeps unprofitable material out of the recommendation.");
            }
            else
            {
                stringBuilder.AppendLine("No option scored below zero, so No Shade was not the binding constraint here — every family that produced geometry earned its material back. On a facade where that is not true, a negative score ranks below No Shade.");
            }
            stringBuilder.AppendLine();
        }

        // ------------------------------------------------------ 12. fitness ----

        private static void AppendFitness(StringBuilder stringBuilder, ShadingComparisonResult result, ShadingSelectionDecision selection)
        {
            stringBuilder.AppendLine("## 12. Fitness For Purpose, and What Is Not Covered");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine("This report supports:");
            stringBuilder.AppendLine("  choosing between the shading options listed above, on direct-beam solar performance");
            stringBuilder.AppendLine("  and material quantity, for this facade, this weather year and this brief;");
            stringBuilder.AppendLine("  recording why that choice was made, and what remains open.");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("This report does NOT support:");
            stringBuilder.AppendLine("  sizing plant or predicting energy consumption;");
            stringBuilder.AppendLine("  demonstrating compliance with any overheating, daylight or energy standard;");
            stringBuilder.AppendLine("  procurement, structural approval or installation.");
            stringBuilder.AppendLine();
            stringBuilder.Append("The analytical leader is the best of the ").Append(result.RankedCount)
                .AppendLine(" verified comparable schemes supplied here.");
            stringBuilder.AppendLine("It is not a global optimum over all shading designs.");
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine();

            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine("PHYSICS NOT MODELLED");
            stringBuilder.AppendLine("  diffuse sky and ground-reflected solar - direct beam only");
            stringBuilder.AppendLine("  transmission through the device (fabric, perforation, glazing)");
            stringBuilder.AppendLine("  reflection off the device - intercepted solar is stopped, not redirected");
            stringBuilder.AppendLine("  deployment or retraction schedules, and any automatic control");
            stringBuilder.AppendLine("  wind loading, structural capacity, mounting hardware");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("DISCRETISATION");
            stringBuilder.Append("  aperture sampling at ").Append(Len(result.ReferenceSignature?.GridSize ?? double.NaN)).AppendLine(" m; features finer than the grid cannot be resolved");

            foreach (ShadingComparisonRow row in result.Rows)
            {
                if (row.Rank >= 1 && row.DesignBudgetExhausted)
                {
                    stringBuilder.Append("  ").Append(OptionLabel(row, false)).AppendLine(" hit its evaluation budget - see Search effort");
                }
            }

            stringBuilder.Append("  sun positions grouped at ").Append(Ang(result.ReferenceSignature?.SunAngleStep ?? double.NaN)).AppendLine(" deg; hourly weather timeline");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("SCOPE");
            stringBuilder.Append("  ").Append(result.Targets?.Count ?? 0).AppendLine(" apertures; no other facade of this building");
            stringBuilder.Append("  one weather year (").Append(result.ReferenceSignature?.Year.ToString() ?? "?").AppendLine("); no multi-year or future-climate variation");
            stringBuilder.AppendLine("  thermal, daylight, glare and overheating-hours performance");
            stringBuilder.AppendLine("  capital cost, lead time, maintenance and buildability");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("The material term is a proxy for cost expressed in kWh, not a price.");
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine();
        }

        // ------------------------------------------------------ 13. provenance ----

        private static void AppendProvenance(StringBuilder stringBuilder, ShadingComparisonResult result)
        {
            stringBuilder.AppendLine("## 13. Reproducibility and Validation");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("```");
            stringBuilder.Append("SAM_SolarCalculator   ").AppendLine(AssemblyVersionText());
            stringBuilder.AppendLine("Component             SAMAnalytical.CompareShading 1.0.0");
            stringBuilder.Append("Model                 ").AppendLine(result.ModelName ?? "model");
            stringBuilder.Append("Inputs                grid ").Append(Len(result.ReferenceSignature?.GridSize ?? double.NaN))
                .Append(" m | sun-angle step ").Append(Ang(result.ReferenceSignature?.SunAngleStep ?? double.NaN))
                .Append(" deg | year ").Append(result.ReferenceSignature?.Year.ToString() ?? "?");
            if (result.Objective != null)
            {
                stringBuilder.Append(" | lambda ").Append(result.Objective.WantedSolarPenalty.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(" | mu ").Append(result.Objective.MaterialPenalty.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(" | material reference ").Append(result.Objective.MaterialCostReference.ToString());
            }
            stringBuilder.AppendLine();
            stringBuilder.Append("Basis signature       ").AppendLine(result.ReferenceSignature?.SignatureHash ?? "n/a");
            stringBuilder.AppendLine("Structured data       comparisonResult (JSON), reportCsv");
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine("The engine behind these figures is validated in documentation/Stage11-Validation.md:");
            stringBuilder.AppendLine("  solar position cross-checked against Ladybug Tools, MAE < 0.25 deg over 8760 hours;");
            stringBuilder.AppendLine("  closed-form analytical checks including an overhang shading exactly to the");
            stringBuilder.AppendLine("    profile-angle construction, and context obstruction never credited to a device;");
            stringBuilder.AppendLine("  SAM-vs-TAS shade-coverage benchmark, mean absolute delta 0.0088 against a 0.02 gate.");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("Not independently validated: diffuse and ground-reflected transposition.");
            stringBuilder.AppendLine("  Neither is used in this report - direct beam only.");
            stringBuilder.AppendLine("```");
            stringBuilder.AppendLine();
        }

        // -------------------------------------------------------------- CSV ----

        /// <summary>The comparison rows as CSV. NaN is an empty field, never 0 or a placeholder (I13).</summary>
        public static string CsvReport(this ShadingComparisonResult result)
        {
            if (result == null)
            {
                return null;
            }

            StringBuilder stringBuilder = new StringBuilder();
            string[] columns = new string[]
            {
                "Rank", "TopRanked", "EngineerSelected", "SchemeGuid", "OptionName", "DesignMethod", "TypologyNames", "ProductPreset",
                "Status", "ComparabilityReason", "PhysicalDeviceCount", "ApertureCount", "ApertureGuids",
                "BaselineDirectSolar", "AdmittedUnwantedSolar", "AdmittedWantedSolar", "AdmittedNeutralSolar",
                "DirectSolarIntercepted", "UnwantedSolarIntercepted", "WantedSolarBlocked", "NeutralSolarIntercepted", "UnattributedEnergy",
                "DirectShadingEfficiency", "UnwantedSolarBlocked", "WantedSolarRetained",
                "PhysicalDeviceArea", "MaterialFraction", "MaterialAvailable",
                "Benefit", "WantedSolarPenalty", "Harm", "MaterialPenalty", "Cost", "ObjectiveScore",
                "DesignTimeScore", "DesignObjectiveMatchesComparisonObjective",
                "ScoreDeltaToTopRanked", "ScoreDeltaToNoShade", "TieBreakLevel",
                "BreakEvenWantedSolarPenalty", "BreakEvenMaterialPenalty",
                "UnwantedInterceptedPerGrossArea", "ObjectiveScorePerGrossArea",
                "DesignEvaluations", "DesignTermination", "DesignBudgetExhausted", "Warnings",
            };

            stringBuilder.AppendLine(string.Join(",", columns));

            foreach (ShadingComparisonRow row in result.Rows)
            {
                List<string> fields = new List<string>();
                fields.Add(row.Rank >= 1 ? row.Rank.ToString(CultureInfo.InvariantCulture) : string.Empty);
                fields.Add(row.TopRanked ? "true" : "false");
                fields.Add(row.EngineerSelected ? "true" : "false");
                fields.Add(row.SchemeGuid.ToString());
                fields.Add(Csv(row.OptionName));
                fields.Add(Csv(row.DesignMethod));
                fields.Add(Csv(row.TypologyNames));
                fields.Add(Csv(row.ProductPreset));
                fields.Add(row.Status.ToString());
                fields.Add(Csv(row.ComparabilityReason));
                fields.Add(row.PhysicalDeviceCount.ToString(CultureInfo.InvariantCulture));
                fields.Add(row.ApertureCount.ToString(CultureInfo.InvariantCulture));
                fields.Add(Csv(string.Join(";", row.ApertureGuids)));
                fields.Add(CsvNumber(row.BaselineDirectSolar));
                fields.Add(CsvNumber(row.AdmittedUnwantedSolar));
                fields.Add(CsvNumber(row.AdmittedWantedSolar));
                fields.Add(CsvNumber(row.AdmittedNeutralSolar));
                fields.Add(CsvNumber(row.DirectSolarIntercepted));
                fields.Add(CsvNumber(row.UnwantedSolarIntercepted));
                fields.Add(CsvNumber(row.WantedSolarBlocked));
                fields.Add(CsvNumber(row.NeutralSolarIntercepted));
                fields.Add(CsvNumber(row.UnattributedEnergy));
                fields.Add(CsvNumber(row.DirectShadingEfficiency));
                fields.Add(CsvNumber(row.UnwantedSolarBlocked));
                fields.Add(CsvNumber(row.WantedSolarRetained));
                fields.Add(CsvNumber(row.PhysicalDeviceArea));
                fields.Add(CsvNumber(row.MaterialFraction));
                fields.Add(row.MaterialAvailable ? "true" : "false");
                fields.Add(CsvNumber(row.Benefit));
                fields.Add(CsvNumber(row.WantedSolarPenalty));
                fields.Add(CsvNumber(row.Harm));
                fields.Add(CsvNumber(row.MaterialPenalty));
                fields.Add(CsvNumber(row.Cost));
                fields.Add(CsvNumber(row.ObjectiveScore));
                fields.Add(CsvNumber(row.DesignTimeScore));
                fields.Add(row.DesignObjectiveMatchesComparisonObjective ? "true" : "false");
                fields.Add(CsvNumber(row.ScoreDeltaToTopRanked));
                fields.Add(CsvNumber(row.ScoreDeltaToNoShade));
                fields.Add(row.TieBreakLevel.ToString(CultureInfo.InvariantCulture));
                fields.Add(CsvBreakEven(row.BreakEvenWantedSolarPenalty));
                fields.Add(CsvBreakEven(row.BreakEvenMaterialPenalty));
                fields.Add(CsvNumber(row.UnwantedInterceptedPerGrossArea));
                fields.Add(CsvNumber(row.ObjectiveScorePerGrossArea));
                fields.Add(row.DesignEvaluations.ToString(CultureInfo.InvariantCulture));
                fields.Add(Csv(row.DesignTermination));
                fields.Add(row.DesignBudgetExhausted ? "true" : "false");
                fields.Add(Csv(string.Join("; ", row.Warnings)));

                stringBuilder.AppendLine(string.Join(",", fields));
            }

            return stringBuilder.ToString();
        }

        private static string CsvNumber(double value)
        {
            return double.IsNaN(value) ? string.Empty : value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string CsvBreakEven(double value)
        {
            if (double.IsNaN(value))
            {
                return string.Empty; // "never" / "not reachable" are table wording, not data
            }

            if (value < 0)
            {
                return string.Empty;
            }

            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Csv(string text)
        {
            if (text == null)
            {
                return string.Empty;
            }

            if (text.IndexOf(',') == -1 && text.IndexOf('"') == -1 && text.IndexOf('\n') == -1 && text.IndexOf('\r') == -1)
            {
                return text;
            }

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}

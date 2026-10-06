// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// THE DOWNSTREAM SELECTION STEP: the engineer explicitly chooses which scheme to build,
        /// identified by SchemeGuid — never by option name or rank index, because ranks can change
        /// after recalculation while the scheme identity is deterministic.
        ///
        /// RULES.
        ///   - Only a verified, comparable and rankable scheme may be selected. An incomparable
        ///     option must be re-run on the comparison basis first; a not-rankable one must have its
        ///     missing material or scoring information resolved.
        ///   - A reason is MANDATORY whenever the selected scheme is not rank 1; optional otherwise.
        ///   - The selection never changes scores, ranks or robustness calculations.
        ///   - If the comparison no longer contains the selected SchemeGuid, the old selection is
        ///     invalidated rather than silently re-pointed at the option now occupying the same rank.
        ///   - A NOT SEPARABLE result keeps its deterministic ranks for audit, but produces no single
        ///     analytical recommendation; the engineer may still select explicitly, with a reason.
        /// </summary>
        /// <param name="comparisonResult">The comparison to select against.</param>
        /// <param name="scheme">The scheme to select. May be a copy — it is matched by SchemeGuid.</param>
        /// <param name="selectionReason">Why this option. Optional for rank 1, mandatory otherwise.</param>
        public static ShadingSelectionDecision SelectShadingScheme(this ShadingComparisonResult comparisonResult, ShadingScheme scheme, string selectionReason)
        {
            ShadingSelectionDecision decision = new ShadingSelectionDecision();

            if (comparisonResult == null)
            {
                decision.Message = "No comparison result was supplied. Run SAMAnalytical.CompareShading first and connect its comparisonResult.";
                return decision;
            }

            if (comparisonResult.RankedCount == 0 || comparisonResult.TopRankedRow == null)
            {
                decision.Message = "The comparison produced no rankable option, so nothing can be selected.";
                return decision;
            }

            if (scheme == null)
            {
                decision.Message = "No shading scheme was supplied. Connect the scheme to select from SAMAnalytical.AssembleShadingSchemes.";
                return decision;
            }

            ShadingComparisonRow row = comparisonResult.Row(scheme.SchemeGuid);
            if (row == null)
            {
                decision.Message = string.Format(CultureInfo.InvariantCulture,
                    "This scheme ({0}) is not part of this comparison. If the comparison has been recalculated, the previous selection is no longer valid - select again from the current ranked list.",
                    scheme.Name ?? scheme.SchemeGuid.ToString());
                return decision;
            }

            switch (row.Status)
            {
                case ShadingComparisonStatus.Incomparable:
                    decision.Message = string.Format(CultureInfo.InvariantCulture,
                        "{0} was verified on a different analysis basis and cannot be selected. Re-run this option on the comparison basis before selecting it. ({1})",
                        row.OptionName, row.ComparabilityReason);
                    return decision;

                case ShadingComparisonStatus.NotRankable:
                    decision.Message = string.Format(CultureInfo.InvariantCulture,
                        "{0} cannot be ranked and cannot be selected. Resolve the missing material or scoring information before selecting it. ({1})",
                        row.OptionName, row.ComparabilityReason);
                    return decision;

                case ShadingComparisonStatus.NotEvaluated:
                    decision.Message = string.Format(CultureInfo.InvariantCulture,
                        "{0} could not be evaluated and cannot be selected. ({1})",
                        row.OptionName, row.ComparabilityReason);
                    return decision;
            }

            bool isLeader = row.TopRanked;
            bool indeterminate = comparisonResult.RecommendationStatus == ShadingRecommendationStatus.Indeterminate;

            // An INDETERMINATE comparison has no single analytical recommendation: choosing either
            // leading option - rank 1 included - is an explicit engineering decision and needs a
            // reason, exactly like choosing a lower rank.
            bool requiresReason = !isLeader || indeterminate;
            if (requiresReason && string.IsNullOrWhiteSpace(selectionReason))
            {
                if (!isLeader)
                {
                    decision.Message = string.Format(CultureInfo.InvariantCulture,
                        "{0} is ranked {1}, not rank 1. Selecting a lower-ranked option requires a recorded reason, so the report carries an engineering decision rather than only a choice. Supply _selectionReason_.",
                        row.OptionName, row.Rank);
                }
                else
                {
                    decision.Message = string.Format(CultureInfo.InvariantCulture,
                        "{0} is rank 1, but the comparison could not separate the leading options analytically (INDETERMINATE). Choosing it is therefore an explicit engineering decision and requires a recorded reason. Supply _selectionReason_.",
                        row.OptionName);
                }

                return decision;
            }

            decision.SelectedSchemeGuid = scheme.SchemeGuid;
            decision.SelectedRank = row.Rank;
            decision.SelectionReason = string.IsNullOrWhiteSpace(selectionReason) ? null : selectionReason.Trim();

            if (indeterminate)
            {
                decision.SelectionAlignment = ShadingSelectionAlignment.LeaderNotDistinguishable;
            }
            else if (isLeader)
            {
                decision.SelectionAlignment = ShadingSelectionAlignment.AgreesWithLeader;
            }
            else
            {
                decision.SelectionAlignment = ShadingSelectionAlignment.DepartsFromLeader;
            }

            decision.ComparisonResult = comparisonResult;

            // Mark the selected row on the report snapshot (the Rows getter returns copies, so the
            // list is rebuilt and reassigned).
            ShadingComparisonResult reportResult = new ShadingComparisonResult(comparisonResult);
            List<ShadingComparisonRow> markedRows = reportResult.Rows;
            foreach (ShadingComparisonRow reportRow in markedRows)
            {
                reportRow.EngineerSelected = reportRow.SchemeGuid == scheme.SchemeGuid;
            }

            reportResult.Rows = markedRows;
            decision.ComparisonResult = reportResult;
            decision.Successful = true;
            decision.DecisionSummary = Summary(comparisonResult, row, decision);
            decision.FinalReportMarkdown = reportResult.MarkdownReport(decision);
            return decision;
        }

        private static string Summary(ShadingComparisonResult comparisonResult, ShadingComparisonRow row, ShadingSelectionDecision decision)
        {
            StringBuilder stringBuilder = new StringBuilder();
            ShadingComparisonRow leader = comparisonResult.TopRankedRow;

            stringBuilder.AppendLine("ANALYTICAL RESULT");
            stringBuilder.Append("  ").Append(leader.OptionName).Append(" ranks 1 of ").Append(comparisonResult.RankedCount).AppendLine(".");
            stringBuilder.AppendLine();

            stringBuilder.AppendLine("ENGINEERING DECISION");
            stringBuilder.Append("  ").Append(row.OptionName).Append(" selected");
            if (row.Rank >= 1)
            {
                stringBuilder.Append(" - analytical rank ").Append(row.Rank);
            }
            stringBuilder.AppendLine(".");

            if (!string.IsNullOrWhiteSpace(decision.SelectionReason))
            {
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("Reason");
                stringBuilder.Append("  ").AppendLine(decision.SelectionReason);
            }

            stringBuilder.AppendLine();
            stringBuilder.AppendLine("Alignment");
            switch (decision.SelectionAlignment)
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

            if (decision.SelectionAlignment == ShadingSelectionAlignment.DepartsFromLeader && leader != null)
            {
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("Trade-off against the analytical leader");
                stringBuilder.Append("  ").Append((row.UnwantedSolarIntercepted - leader.UnwantedSolarIntercepted).ToString("+0.000;-0.000", CultureInfo.InvariantCulture))
                    .AppendLine(" kWh unwanted solar intercepted");
                stringBuilder.Append("  ").Append((row.WantedSolarBlocked - leader.WantedSolarBlocked).ToString("+0.000;-0.000", CultureInfo.InvariantCulture))
                    .AppendLine(" kWh wanted solar blocked");
                stringBuilder.Append("  ").Append((row.PhysicalDeviceArea - leader.PhysicalDeviceArea).ToString("+0.000;-0.000", CultureInfo.InvariantCulture))
                    .AppendLine(" m2 shading material");
                stringBuilder.Append("  ").Append((row.ObjectiveScore - leader.ObjectiveScore).ToString("+0.000;-0.000", CultureInfo.InvariantCulture))
                    .AppendLine(" kWh objective score");
            }

            stringBuilder.AppendLine();
            stringBuilder.AppendLine("The engineering selection does not alter the analytical ranking. It records a project decision based on criteria additional to the comparison objective.");

            return stringBuilder.ToString();
        }
    }
}

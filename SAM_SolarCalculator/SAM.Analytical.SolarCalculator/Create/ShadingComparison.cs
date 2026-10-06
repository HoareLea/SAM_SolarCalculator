// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using SAM.Core.SolarCalculator;
using SAM.Weather;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// THE COMPARISON: verifies every supplied scheme against ONE solar context, ranks them by
        /// the verified scores under ONE objective, and produces the report.
        ///
        /// THE SOFTWARE RANKS; THE ENGINEER SELECTS. Rank 1 is the analytical leader — the option
        /// with the highest verified score under the stated model, objective and resolution — and is
        /// always populated when a rankable row exists. It is NOT an automatic engineering
        /// selection; choosing a scheme to build is the separate downstream SelectShadingScheme
        /// step, which records who chose what and why. The ranking is a total order and never
        /// suppressed, whatever the recommendation confidence.
        ///
        /// All schemes are verified against the SAME prepared context, so the visibility cache, cell
        /// ordering and brief are computed once. Every ranked row's score is computed from ONE
        /// supplied objective; design-time scores are provenance, never ranking inputs.
        /// </summary>
        /// <param name="analyticalModel">The model.</param>
        /// <param name="targets">The scope — every scheme must cover exactly this aperture set.</param>
        /// <param name="schemes">The schemes to compare, from AssembleShadingSchemes.</param>
        /// <param name="wantedSolarPenalty">λ.</param>
        /// <param name="materialPenalty">μ.</param>
        /// <param name="materialCostReference">Which admitted energy the material cost is measured against.</param>
        /// <param name="message">Null on success; an actionable sentence otherwise.</param>
        /// <param name="weatherData">Weather. Null = the weather attached to the model.</param>
        /// <param name="desirabilityStrategy">Explicit weighting. Wins over the periods when supplied.</param>
        /// <param name="unwantedPeriod">Hours whose solar should be blocked.</param>
        /// <param name="wantedPeriod">Hours whose solar should be preserved.</param>
        /// <param name="gridSize">Aperture analysis-grid size, m.</param>
        /// <param name="sunAngleStep">Angular resolution used to group similar sun positions, degrees.</param>
        /// <param name="recalculate">Force a rebuild of the solar calculation.</param>
        /// <param name="wantedSolarPenaltySupplied">True when the caller explicitly supplied λ (it is not the library default).</param>
        /// <param name="materialPenaltySupplied">True when the caller explicitly supplied μ.</param>
        /// <param name="desirabilitySupplied">True when the caller supplied a brief.</param>
        public static ShadingComparisonResult ShadingComparison(
            this AnalyticalModel analyticalModel,
            IEnumerable<ApertureSolarTarget> targets,
            IEnumerable<ShadingScheme> schemes,
            double wantedSolarPenalty,
            double materialPenalty,
            MaterialCostReference materialCostReference,
            out string message,
            WeatherData weatherData = null,
            IDesirabilityStrategy desirabilityStrategy = null,
            AnalysisPeriod unwantedPeriod = null,
            AnalysisPeriod wantedPeriod = null,
            double gridSize = 0.5,
            double sunAngleStep = 2.0,
            bool recalculate = false,
            bool wantedSolarPenaltySupplied = false,
            bool materialPenaltySupplied = false,
            bool desirabilitySupplied = false)
        {
            message = null;
            ShadingComparisonResult comparisonResult = new ShadingComparisonResult();

            ShadingObjective objective = new ShadingObjective(wantedSolarPenalty, materialPenalty, materialCostReference);
            comparisonResult.Objective = objective;

            if (ShadingObjective.Validity(wantedSolarPenalty) == PenaltyValidity.Invalid)
            {
                message = "_wantedSolarPenalty_ must be a finite number of zero or more. A negative value would reward destroying the solar you asked to keep.";
                comparisonResult.Message = message;
                comparisonResult.Outcome = ShadingComparisonOutcome.NoComparableOptions;
                comparisonResult.RecommendationStatus = ShadingRecommendationStatus.NoDecision;
                return comparisonResult;
            }

            if (ShadingObjective.Validity(materialPenalty) == PenaltyValidity.Invalid)
            {
                message = "_materialPenalty_ must be a finite number of zero or more. A negative value would reward buying material.";
                comparisonResult.Message = message;
                comparisonResult.Outcome = ShadingComparisonOutcome.NoComparableOptions;
                comparisonResult.RecommendationStatus = ShadingRecommendationStatus.NoDecision;
                return comparisonResult;
            }

            // ------------------------------------------------------------ inputs ----

            // Duplicates are REFUSED, never silently collapsed: an audit-oriented comparison must
            // not quietly change the supplied set, because a duplicated aperture would double-count
            // its energy in every scheme equally and a duplicated scheme would compete against
            // itself.
            List<ApertureSolarTarget> targetList = new List<ApertureSolarTarget>();
            HashSet<Guid> targetGuids = new HashSet<Guid>();
            foreach (ApertureSolarTarget target in targets ?? new List<ApertureSolarTarget>())
            {
                if (target == null)
                {
                    continue;
                }

                if (!targetGuids.Add(target.ApertureGuid))
                {
                    message = string.Format(CultureInfo.InvariantCulture,
                        "Aperture {0} was supplied more than once. A duplicated aperture would double-count its energy in every scheme equally, which hides rather than cancels the error. Remove the duplicate target.",
                        target.ApertureGuid);
                    comparisonResult.Message = message;
                    comparisonResult.Outcome = ShadingComparisonOutcome.NoComparableOptions;
                    comparisonResult.RecommendationStatus = ShadingRecommendationStatus.NoDecision;
                    return comparisonResult;
                }

                targetList.Add(target);
            }
            targetList.Sort((a, b) => a.ApertureGuid.CompareTo(b.ApertureGuid));
            comparisonResult.Targets = targetList;

            List<Guid> scope = targetList.ConvertAll(x => x.ApertureGuid);
            if (scope.Count == 0)
            {
                message = "No aperture solar targets were supplied.";
                comparisonResult.Message = message;
                comparisonResult.Outcome = ShadingComparisonOutcome.NoComparableOptions;
                comparisonResult.RecommendationStatus = ShadingRecommendationStatus.NoDecision;
                return comparisonResult;
            }

            List<ShadingScheme> schemeList = new List<ShadingScheme>();
            HashSet<Guid> schemeGuids = new HashSet<Guid>();
            foreach (ShadingScheme scheme in schemes ?? new List<ShadingScheme>())
            {
                if (scheme == null)
                {
                    continue;
                }

                // A scheme whose identity failed to resolve (e.g. reconstructed from corrupted JSON)
                // must be refused here, never silently ranked. It must never pass as the No Shade
                // baseline, which is a real zero-device scheme with its own deterministic identity.
                if (scheme.SchemeGuid == Guid.Empty)
                {
                    message = string.Format(CultureInfo.InvariantCulture,
                        "The scheme '{0}' has no resolved identity (its SchemeGuid is empty), so it cannot be ranked. It was not reconstructed from valid data — recreate it from its source inputs, or reload it from intact JSON.",
                        scheme.Name ?? "(unnamed)");
                    comparisonResult.Message = message;
                    comparisonResult.Outcome = ShadingComparisonOutcome.NoComparableOptions;
                    comparisonResult.RecommendationStatus = ShadingRecommendationStatus.NoDecision;
                    return comparisonResult;
                }

                if (!schemeGuids.Add(scheme.SchemeGuid))
                {
                    message = string.Format(CultureInfo.InvariantCulture,
                        "The scheme '{0}' ({1}) was supplied more than once. A duplicated scheme would compete against itself in the ranking. Remove the duplicate.",
                        scheme.Name, scheme.SchemeGuid);
                    comparisonResult.Message = message;
                    comparisonResult.Outcome = ShadingComparisonOutcome.NoComparableOptions;
                    comparisonResult.RecommendationStatus = ShadingRecommendationStatus.NoDecision;
                    return comparisonResult;
                }

                schemeList.Add(scheme);
            }
            schemeList.Sort((a, b) => a.SchemeGuid.CompareTo(b.SchemeGuid));

            comparisonResult.SuppliedCount = schemeList.Count;
            comparisonResult.ModelName = analyticalModel?.Name;

            if (schemeList.Count == 0)
            {
                message = "No shading schemes were supplied. Connect the output of SAMAnalytical.AssembleShadingSchemes.";
                comparisonResult.Message = message;
                comparisonResult.Outcome = ShadingComparisonOutcome.NoComparableOptions;
                comparisonResult.RecommendationStatus = ShadingRecommendationStatus.NoDecision;
                return comparisonResult;
            }

            if (Query.GridSizeValidity(gridSize, out string gridSizeMessage) != GridSizeValidity.Valid)
            {
                message = gridSizeMessage;
                comparisonResult.Message = message;
                comparisonResult.Outcome = ShadingComparisonOutcome.NoComparableOptions;
                comparisonResult.RecommendationStatus = ShadingRecommendationStatus.NoDecision;
                return comparisonResult;
            }

            if (sunAngleStep <= 0 || double.IsNaN(sunAngleStep))
            {
                message = "_sunAngleStep_ [°] must be greater than zero.";
                comparisonResult.Message = message;
                comparisonResult.Outcome = ShadingComparisonOutcome.NoComparableOptions;
                comparisonResult.RecommendationStatus = ShadingRecommendationStatus.NoDecision;
                return comparisonResult;
            }

            // ------------------------------------------------------------- context ----

            WeatherData effectiveWeather = weatherData ?? analyticalModel?.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            if (effectiveWeather == null)
            {
                message = "No weather data. Supply WeatherData, or attach it to the AnalyticalModel.";
                comparisonResult.Message = message;
                comparisonResult.Outcome = ShadingComparisonOutcome.NoComparableOptions;
                comparisonResult.RecommendationStatus = ShadingRecommendationStatus.NoDecision;
                return comparisonResult;
            }

            int year = unwantedPeriod?.Year ?? wantedPeriod?.Year ?? 0;
            if (year == 0)
            {
                foreach (WeatherYear weatherYear in effectiveWeather.WeatherYears ?? new List<WeatherYear>())
                {
                    if (weatherYear != null)
                    {
                        year = weatherYear.Year;
                        break;
                    }
                }
            }

            ApertureSolarContext context = ApertureSolarContext(analyticalModel, year, effectiveWeather, scope, gridSize, sunAngleStep, recalculate);
            if (context == null)
            {
                message = ApertureSolarContextFailureReason(analyticalModel, scope, gridSize, effectiveWeather)
                    ?? "The solar calculation could not be set up for this model.";
                comparisonResult.Message = message;
                comparisonResult.Outcome = ShadingComparisonOutcome.NoComparableOptions;
                comparisonResult.RecommendationStatus = ShadingRecommendationStatus.NoDecision;
                return comparisonResult;
            }

            IDesirabilityStrategy strategy = desirabilityStrategy;
            if (strategy == null)
            {
                strategy = unwantedPeriod == null && wantedPeriod == null
                    ? DefaultDesirabilityStrategy(context.Year, context.Location)
                    : new SeasonalDesirability(ReRoot(unwantedPeriod, context.Year), ReRoot(wantedPeriod, context.Year));
            }

            List<ApertureDesirability> desirabilities = new List<ApertureDesirability>();
            foreach (ApertureSolarTarget target in context.Targets)
            {
                ApertureDesirability desirability = ApertureDesirability(target, context.SolarVisibilityCache, strategy, context.WeatherData);
                if (desirability == null)
                {
                    message = string.Format(CultureInfo.InvariantCulture,
                        "The desirability weighting could not be built for aperture {0}.", target.ApertureGuid);
                    comparisonResult.Message = message;
                    comparisonResult.Outcome = ShadingComparisonOutcome.NoComparableOptions;
                    comparisonResult.RecommendationStatus = ShadingRecommendationStatus.NoDecision;
                    return comparisonResult;
                }

                desirabilities.Add(desirability);
            }

            ShadingAnalysisSignature signature = context.ShadingAnalysisSignature(strategy);
            comparisonResult.ReferenceSignature = signature;
            comparisonResult.Hours = context.ShadingAnalysisHours(desirabilities, strategy);

            Core.Location location = context.Location;
            comparisonResult.SiteDescription = location == null ? null : string.Format(CultureInfo.InvariantCulture,
                "{0} — {1:0.###} °N, {2:0.###} °E", location.Name, location.Latitude, location.Longitude);
            comparisonResult.WeatherDescription = effectiveWeather.Name ?? effectiveWeather.Description;

            // ----------------------------------------------------------- verification ----

            List<ShadingComparisonRow> rows = new List<ShadingComparisonRow>();
            foreach (ShadingScheme scheme in schemeList)
            {
                rows.Add(BuildRow(scheme, context, targetList, desirabilities, signature, objective));
            }

            // ---------------------------------------------------------------- ranking ----

            comparisonResult.BaselineAnchored = rows.Exists(x => x.Status == ShadingComparisonStatus.NoShadeBaseline);

            RankRows(rows, objective);

            List<ShadingComparisonRow> rankable = rows.FindAll(x => x.Rank >= 1);
            if (rankable.Count != 0)
            {
                // All row mutation happens on the row OBJECTS before the rows are copied into the
                // result, so the copies carry ranks, flags, deltas and break-evens.
                rankable[0].TopRanked = true;

                foreach (ShadingComparisonRow row in rankable)
                {
                    row.ScoreDeltaToTopRanked = rankable[0].ObjectiveScore - row.ObjectiveScore;
                }

                ShadingComparisonRow noShadeRow = rankable.Find(x => x.Status == ShadingComparisonStatus.NoShadeBaseline);
                foreach (ShadingComparisonRow row in rankable)
                {
                    // "Score above or below No Shade": positive when the option beats the baseline.
                    row.ScoreDeltaToNoShade = noShadeRow == null ? double.NaN : row.ObjectiveScore - noShadeRow.ObjectiveScore;
                }

                foreach (ShadingComparisonRow row in rankable)
                {
                    if (row == rankable[0])
                    {
                        row.BreakEvenWantedSolarPenalty = double.NaN;
                        row.BreakEvenMaterialPenalty = double.NaN;
                        continue;
                    }

                    BreakEvenResult beLambda = Query.BreakEvenWantedSolarPenalty(rankable[0], row, materialPenalty);
                    BreakEvenResult beMu = Query.BreakEvenMaterialPenalty(rankable[0], row, wantedSolarPenalty);
                    row.BreakEvenWantedSolarPenalty = beLambda.Never || beLambda.NotReachable ? double.NaN : beLambda.Value;
                    row.BreakEvenMaterialPenalty = beMu.Never || beMu.NotReachable ? double.NaN : beMu.Value;
                }
            }

            List<ShadingComparisonRow> ordered = new List<ShadingComparisonRow>(rankable);
            ordered.AddRange(rows.FindAll(x => x.Rank < 1));
            comparisonResult.Rows = ordered;

            comparisonResult.RankedCount = rankable.Count;
            // "Verified and comparable" — the audit-line wording — is exactly the rows the ranking
            // could see: Ranked + NoShadeBaseline + NotRankable. Incomparable rows are verified on
            // a different basis and must NOT inflate this count, or the audit line would contradict
            // its own incomparable figure.
            comparisonResult.VerifiedCount = rows.FindAll(x =>
                x.Status != ShadingComparisonStatus.NotEvaluated
                && x.Status != ShadingComparisonStatus.Incomparable).Count;
            comparisonResult.IncomparableCount = rows.FindAll(x => x.Status == ShadingComparisonStatus.Incomparable).Count;
            comparisonResult.NotEvaluatedCount = rows.FindAll(x => x.Status == ShadingComparisonStatus.NotEvaluated).Count;
            comparisonResult.NotRankableCount = rows.FindAll(x => x.Status == ShadingComparisonStatus.NotRankable).Count;
            comparisonResult.Incomplete = comparisonResult.SuppliedCount != comparisonResult.RankedCount;

            if (rankable.Count == 0)
            {
                comparisonResult.Outcome = ShadingComparisonOutcome.NoComparableOptions;
                comparisonResult.RecommendationStatus = ShadingRecommendationStatus.NoDecision;
                comparisonResult.Message = "No supplied scheme could be ranked on this basis.";
                comparisonResult.ReportMarkdown = comparisonResult.MarkdownReport(null);
                comparisonResult.ReportCsv = comparisonResult.CsvReport();
                comparisonResult.ReportGlossary = Query.ShadingGlossaryMarkdown();
                return comparisonResult;
            }

            ShadingComparisonRow topRow = rankable[0];

            // ---------------------------------------------------- recommendation status ----

            ShadingRecommendationStatus recommendationStatus = ShadingRecommendationStatus.Ready;
            List<string> openChecks = new List<string>();
            List<string> required = new List<string>();

            bool indeterminate = false;
            double indicator;
            if (Query.SunGroupIndicators.TryGetValue(sunAngleStep, out indicator)
                && rankable.Count >= 2
                && !double.IsNaN(topRow.ObjectiveScore) && Math.Abs(topRow.ObjectiveScore) > 1e-12)
            {
                // The runner-up's delta IS the margin: top - runner-up.
                double marginFraction = Math.Abs(rankable[1].ScoreDeltaToTopRanked) / Math.Abs(topRow.ObjectiveScore);
                if (marginFraction <= indicator)
                {
                    indeterminate = true;
                }
            }

            if (!indeterminate && topRow.Status != ShadingComparisonStatus.NoShadeBaseline)
            {
                AppendResolutionChecks(targetList, gridSize, rankable, openChecks, required);
                AppendBudgetChecks(rankable, openChecks, required);
                AppendConvergenceCheck(openChecks, required);
                if (comparisonResult.Incomplete)
                {
                    openChecks.Add(string.Format(CultureInfo.InvariantCulture,
                        "comparison is incomplete: {0} of {1} supplied schemes could not be ranked",
                        comparisonResult.SuppliedCount - comparisonResult.RankedCount, comparisonResult.SuppliedCount));
                    required.Add(string.Format(CultureInfo.InvariantCulture,
                        "resolve or accept the {0} unranked scheme(s)", comparisonResult.SuppliedCount - comparisonResult.RankedCount));
                }
            }

            recommendationStatus = DetermineRecommendationStatus(
                rankable.Count == 0,
                indeterminate,
                rankable.Count != 0 && topRow.Status == ShadingComparisonStatus.NoShadeBaseline,
                openChecks.Count);

            comparisonResult.Outcome = topRow.Status == ShadingComparisonStatus.NoShadeBaseline
                ? ShadingComparisonOutcome.NoShadeTopRanked
                : ShadingComparisonOutcome.TopRanked;
            comparisonResult.RecommendationStatus = recommendationStatus;
            comparisonResult.TieBreakLevel = rankable.Count >= 2 ? rankable[0].TieBreakLevel : 0;
            comparisonResult.OpenChecks = openChecks;
            comparisonResult.RequiredBeforeFreeze = required;
            comparisonResult.ProjectInputs = ProjectInputs(
                wantedSolarPenalty, materialPenalty, gridSize, targetList,
                wantedSolarPenaltySupplied, materialPenaltySupplied, desirabilitySupplied,
                rankable, topRow, rankable.Find(x => x.Status == ShadingComparisonStatus.NoShadeBaseline));

            // ------------------------------------------------------------------ report ----

            comparisonResult.ReportMarkdown = comparisonResult.MarkdownReport(null);
            comparisonResult.ReportCsv = comparisonResult.CsvReport();
            comparisonResult.ReportGlossary = Query.ShadingGlossaryMarkdown();

            return comparisonResult;
        }

        private static ShadingComparisonRow BuildRow(
            ShadingScheme scheme,
            ApertureSolarContext context,
            List<ApertureSolarTarget> targetList,
            List<ApertureDesirability> desirabilities,
            ShadingAnalysisSignature signature,
            ShadingObjective objective)
        {
            ShadingComparisonRow row = new ShadingComparisonRow();
            row.SchemeGuid = scheme.SchemeGuid;
            row.OptionName = scheme.Name;
            row.DesignMethod = scheme.DesignMethod;
            row.TypologyNames = string.Join(", ", scheme.TypologyNames);
            row.ProductPreset = scheme.ProductPreset;
            row.PhysicalDeviceCount = scheme.PhysicalDeviceCount;
            row.ApertureCount = scheme.ApertureGuids.Count;
            row.ApertureGuids = scheme.ApertureGuids;
            row.DesignEvaluations = scheme.DesignEvaluations;
            row.DesignTermination = scheme.DesignTermination.ToString();
            row.DesignBudgetExhausted = scheme.DesignBudgetExhausted;
            row.DesignTimeScore = scheme.DesignTimeScore;
            row.WantedSolarPenalty = objective.WantedSolarPenalty;
            row.MaterialPenalty = objective.MaterialPenalty;

            ShadingObjective designObjective = scheme.DesignObjective;
            row.DesignObjectiveMatchesComparisonObjective = designObjective != null
                && designObjective.WantedSolarPenalty == objective.WantedSolarPenalty
                && designObjective.MaterialPenalty == objective.MaterialPenalty
                && designObjective.MaterialCostReference == objective.MaterialCostReference;

            if (!row.DesignObjectiveMatchesComparisonObjective && designObjective != null)
            {
                row.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "Scheme geometry was optimised under a different objective (lambda={0}, mu={1}) and has been re-scored, not re-optimised, under the comparison objective (lambda={2}, mu={3}).",
                    designObjective.WantedSolarPenalty, designObjective.MaterialPenalty,
                    objective.WantedSolarPenalty, objective.MaterialPenalty));
            }

            VerifiedShadingSchemeResult verified = scheme.VerifiedShadingSchemeResult(context, targetList, desirabilities, signature, out string verificationMessage);
            if (verified == null)
            {
                row.Status = ShadingComparisonStatus.NotEvaluated;
                row.ComparabilityReason = verificationMessage;
                return row;
            }

            row.VerifiedResult = verified;

            if (!Query.Comparable(verified.Signature, signature, out string comparabilityReason))
            {
                row.Status = ShadingComparisonStatus.Incomparable;
                row.ComparabilityReason = comparabilityReason;
                return row;
            }

            if (verified.Status == ShadingDesignStatus.NotEvaluated)
            {
                row.Status = ShadingComparisonStatus.NotEvaluated;
                row.ComparabilityReason = verified.Warnings.Count == 0 ? "The scheme could not be verified." : verified.Warnings[0];
                return row;
            }

            ShadingSchemePerformance performance = verified.Performance;
            row.BaselineDirectSolar = performance.AdmittedDirectEnergy;
            row.AdmittedUnwantedSolar = performance.AdmittedUnwantedEnergy;
            row.AdmittedWantedSolar = performance.AdmittedWantedEnergy;
            row.AdmittedNeutralSolar = performance.AdmittedNeutralEnergy;
            row.DirectSolarIntercepted = performance.DirectSolarIntercepted;
            row.UnwantedSolarIntercepted = performance.UnwantedSolarIntercepted;
            row.WantedSolarBlocked = performance.WantedSolarBlocked;
            row.NeutralSolarIntercepted = performance.NeutralSolarIntercepted;
            row.UnattributedEnergy = performance.UnattributedInterceptedEnergy;
            row.DirectShadingEfficiency = performance.DirectShadingEfficiency;
            row.UnwantedSolarBlocked = performance.UnwantedSolarBlocked;
            row.WantedSolarRetained = performance.WantedSolarRetained;
            row.PhysicalDeviceArea = performance.PhysicalDeviceArea;
            row.MaterialFraction = performance.MaterialFraction;
            row.MaterialAvailable = performance.MaterialAvailable;
            row.Benefit = objective.Benefit(performance);
            row.Harm = objective.Harm(performance);
            row.Cost = objective.Cost(performance);
            row.ObjectiveScore = objective.Score(performance);

            double grossArea = TotalGrossArea(targetList);
            if (!double.IsNaN(grossArea) && grossArea > 0)
            {
                row.UnwantedInterceptedPerGrossArea = row.UnwantedSolarIntercepted / grossArea;
                row.ObjectiveScorePerGrossArea = row.ObjectiveScore / grossArea;
            }

            foreach (string warning in verified.Warnings)
            {
                row.Warnings.Add(warning);
            }

            row.Status = scheme.IsNoShade ? ShadingComparisonStatus.NoShadeBaseline : ShadingComparisonStatus.Ranked;

            if (!Rankable(row, objective))
            {
                row.Status = ShadingComparisonStatus.NotRankable;
                row.ComparabilityReason = RankabilityReason(row, objective);
            }

            return row;
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

        internal static bool Rankable(ShadingComparisonRow row, ShadingObjective objective)
        {
            if (row.Status != ShadingComparisonStatus.Ranked && row.Status != ShadingComparisonStatus.NoShadeBaseline)
            {
                return false;
            }

            if (double.IsNaN(row.ObjectiveScore))
            {
                return false;
            }

            return objective.MaterialPenalty == 0 || row.MaterialAvailable;
        }

        /// <summary>
        /// Applies the §11.3 total order in place: rankable rows sorted best-first, non-rankable
        /// rows appended ordered by (Status, OptionName), ranks and tie-break levels assigned. The
        /// comparator uses double.CompareTo throughout and NaN is excluded before sorting.
        /// </summary>
        internal static void RankRows(List<ShadingComparisonRow> rows, ShadingObjective objective)
        {
            List<ShadingComparisonRow> rankable = rows.FindAll(x => Rankable(x, objective));
            rankable.Sort(new ShadingRowComparer());

            List<ShadingComparisonRow> nonRankable = rows.FindAll(x => !Rankable(x, objective));
            nonRankable.Sort((a, b) =>
            {
                int order = a.Status.CompareTo(b.Status);
                return order != 0 ? order : string.CompareOrdinal(a.OptionName ?? string.Empty, b.OptionName ?? string.Empty);
            });

            int rank = 1;
            foreach (ShadingComparisonRow row in rankable)
            {
                row.Rank = rank++;
            }

            for (int i = 0; i < rankable.Count; i++)
            {
                ShadingComparisonRow row = rankable[i];
                row.TieBreakLevel = i + 1 < rankable.Count ? DecidingLevel(row, rankable[i + 1]) : 0;
            }

            rows.Clear();
            rows.AddRange(rankable);
            rows.AddRange(nonRankable);
        }

        /// <summary>
        /// The recommendation status rule (§11.4.1), extracted so the assignment table is
        /// unit-testable row by row. The ranking itself is never suppressed.
        /// </summary>
        internal static ShadingRecommendationStatus DetermineRecommendationStatus(bool noRankableRows, bool indeterminate, bool noShadeTop, int openCheckCount)
        {
            if (noRankableRows)
            {
                return ShadingRecommendationStatus.NoDecision;
            }

            if (indeterminate)
            {
                return ShadingRecommendationStatus.Indeterminate;
            }

            if (noShadeTop)
            {
                return ShadingRecommendationStatus.NoShadingRecommended;
            }

            return openCheckCount == 0
                ? ShadingRecommendationStatus.Ready
                : ShadingRecommendationStatus.Provisional;
        }

        private static string RankabilityReason(ShadingComparisonRow row, ShadingObjective objective)
        {
            if (double.IsNaN(row.ObjectiveScore))
            {
                return "The verified score is not a number, so the scheme cannot be ranked.";
            }

            if (objective.MaterialPenalty != 0 && !row.MaterialAvailable)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "MaterialFraction is unavailable (an element area is not a finite number) and the material penalty is {0} > 0. Measured energy is shown below; the scheme cannot be scored. Set the material penalty to 0 to rank on energy alone.",
                    objective.MaterialPenalty);
            }

            return "The scheme cannot be ranked.";
        }

        internal static int DecidingLevel(ShadingComparisonRow a, ShadingComparisonRow b)
        {
            if (a.ObjectiveScore.CompareTo(b.ObjectiveScore) != 0)
            {
                return 0; // no tie: level 1 decided outright
            }

            if (a.PhysicalDeviceArea.CompareTo(b.PhysicalDeviceArea) != 0)
            {
                return 2;
            }

            if (a.WantedSolarBlocked.CompareTo(b.WantedSolarBlocked) != 0)
            {
                return 3;
            }

            if (a.PhysicalDeviceCount.CompareTo(b.PhysicalDeviceCount) != 0)
            {
                return 4;
            }

            bool aBaseline = a.Status == ShadingComparisonStatus.NoShadeBaseline;
            bool bBaseline = b.Status == ShadingComparisonStatus.NoShadeBaseline;
            if (aBaseline != bBaseline)
            {
                return 5;
            }

            return 6;
        }

        private static void AppendResolutionChecks(List<ApertureSolarTarget> targets, double gridSize, List<ShadingComparisonRow> rankable, List<string> openChecks, List<string> required)
        {
            double recommended = targets.RecommendedGridSize();
            if (Query.CoarserThanRecommended(gridSize, recommended, out string gridMessage))
            {
                openChecks.Add(string.Format(CultureInfo.InvariantCulture,
                    "analysis grid {0:0.###} m; design-grade recommendation {1:0.###} m", gridSize, recommended));
                required.Add(string.Format(CultureInfo.InvariantCulture,
                    "repeat this comparison at {0:0.###} m and confirm the ranking holds", recommended));
            }

            // One check per FAMILY, not per device: a family with three capped devices is one open
            // item, not three.
            HashSet<string> resolutionFamilies = new HashSet<string>();
            HashSet<string> capFamilies = new HashSet<string>();

            foreach (ShadingComparisonRow row in rankable)
            {
                ShadingScheme scheme = row.VerifiedResult?.Scheme;
                if (scheme == null)
                {
                    continue;
                }

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

                    if (!resolutionFamilies.Contains(row.OptionName))
                    {
                        ShadingResolutionState state = Query.ShadingResolution(device.Typology, target, gridSize, out string _, out double _);
                        if (state != ShadingResolutionState.Resolved)
                        {
                            resolutionFamilies.Add(row.OptionName);
                            openChecks.Add(string.Format(CultureInfo.InvariantCulture,
                                "{0} has elements below or near the resolution limit on this grid", row.OptionName));
                            required.Add(string.Format(CultureInfo.InvariantCulture,
                                "re-run {0} at a finer grid so its repeated elements are resolved", row.OptionName));
                        }
                    }

                    if (!capFamilies.Contains(row.OptionName) && Query.GridResolutionCapReached(device.Typology, target, gridSize, out string _))
                    {
                        capFamilies.Add(row.OptionName);
                        openChecks.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0} sits on the grid-narrowed element-count maximum - the grid chose the count, not the design", row.OptionName));
                        required.Add(string.Format(CultureInfo.InvariantCulture,
                            "re-run {0} at a finer grid so the family's own maximum is reachable", row.OptionName));
                    }
                }
            }
        }

        private static void AppendBudgetChecks(List<ShadingComparisonRow> rankable, List<string> openChecks, List<string> required)
        {
            foreach (ShadingComparisonRow row in rankable)
            {
                if (row.DesignBudgetExhausted)
                {
                    openChecks.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0} exhausted its optimisation budget", row.OptionName));
                    required.Add(string.Format(CultureInfo.InvariantCulture,
                        "re-run {0} with sufficient optimisation budget", row.OptionName));
                }
            }
        }

        private static void AppendConvergenceCheck(List<string> openChecks, List<string> required)
        {
            openChecks.Add("grid convergence has not been demonstrated");
            required.Add("repeat this comparison at a finer grid and confirm the ranking survives");
        }

        /// <summary>
        /// The decision-facing input table. Internal so the DEFAULT-vs-PROJECT provenance rule is
        /// unit-testable directly: "supplied" means the caller actually connected a value, never
        /// whether the value happens to differ from the library default.
        /// </summary>
        internal static List<ShadingProjectInput> ProjectInputs(
            double wantedSolarPenalty,
            double materialPenalty,
            double gridSize,
            List<ApertureSolarTarget> targets,
            bool lambdaSupplied,
            bool muSupplied,
            bool desirabilitySupplied,
            List<ShadingComparisonRow> rankable,
            ShadingComparisonRow topRow,
            ShadingComparisonRow noShadeRow)
        {
            List<ShadingProjectInput> inputs = new List<ShadingProjectInput>();

            // Break-even verdicts for the two weightings.
            List<double> lambdaFlips = new List<double>();
            List<double> muFlips = new List<double>();
            foreach (ShadingComparisonRow row in rankable)
            {
                if (row == topRow)
                {
                    continue;
                }

                BreakEvenResult beLambda = Query.BreakEvenWantedSolarPenalty(topRow, row, materialPenalty);
                if (!beLambda.Never && !beLambda.NotReachable)
                {
                    lambdaFlips.Add(beLambda.Value);
                }

                BreakEvenResult beMu = Query.BreakEvenMaterialPenalty(topRow, row, wantedSolarPenalty);
                if (!beMu.Never && !beMu.NotReachable)
                {
                    muFlips.Add(beMu.Value);
                }
            }

            BreakEvenResult nearestLambda = null;
            double bestLambda = double.MaxValue;
            foreach (double value in lambdaFlips)
            {
                double distance = Math.Abs(value - wantedSolarPenalty);
                if (distance < bestLambda)
                {
                    bestLambda = distance;
                    nearestLambda = new BreakEvenResult(value, false, false);
                }
            }

            BreakEvenResult nearestMu = null;
            double bestMu = double.MaxValue;
            foreach (double value in muFlips)
            {
                double distance = Math.Abs(value - materialPenalty);
                if (distance < bestMu)
                {
                    bestMu = distance;
                    nearestMu = new BreakEvenResult(value, false, false);
                }
            }

            string lambdaVerdict = Query.RobustnessVerdict(nearestLambda, wantedSolarPenalty, 0.5, 2.0);
            string muVerdict = Query.RobustnessVerdict(nearestMu, materialPenalty, double.NaN, double.NaN);

            // An input that is BOTH at its library default AND decision-sensitive must be listed.
            // Supplied inputs are listed when decision-sensitive, as PROJECT.
            bool lambdaDefault = wantedSolarPenalty == 1.0 && !lambdaSupplied;
            bool muDefault = materialPenalty == 0.1 && !muSupplied;
            bool lambdaSensitive = lambdaVerdict != "STABLE";
            bool muSensitive = muVerdict != "STABLE";

            if (lambdaDefault)
            {
                inputs.Add(new ShadingProjectInput(
                    "Wanted solar penalty lambda", wantedSolarPenalty.ToString("0.000", CultureInfo.InvariantCulture),
                    "DEFAULT", lambdaSensitive ? "decision-sensitive" : "decision-stable over 0.5 - 2.0"));
            }
            else if (lambdaSupplied && lambdaSensitive)
            {
                inputs.Add(new ShadingProjectInput(
                    "Wanted solar penalty lambda", wantedSolarPenalty.ToString("0.000", CultureInfo.InvariantCulture),
                    "PROJECT", "decision-sensitive"));
            }

            if (muDefault)
            {
                inputs.Add(new ShadingProjectInput(
                    "Material penalty mu", materialPenalty.ToString("0.000", CultureInfo.InvariantCulture),
                    "DEFAULT", muSensitive ? "decision-sensitive" : "decision-stable"));
            }
            else if (muSupplied && muSensitive)
            {
                inputs.Add(new ShadingProjectInput(
                    "Material penalty mu", materialPenalty.ToString("0.000", CultureInfo.InvariantCulture),
                    "PROJECT", "decision-sensitive"));
            }

            if (!desirabilitySupplied)
            {
                inputs.Add(new ShadingProjectInput(
                    "Desirability brief", "Default", "DEFAULT", "not sensitivity-tested"));
            }

            double recommended = targets.RecommendedGridSize();
            if (Query.CoarserThanRecommended(gridSize, recommended, out string _))
            {
                inputs.Add(new ShadingProjectInput(
                    "Analysis grid", gridSize.ToString("0.000", CultureInfo.InvariantCulture) + " m",
                    "SUPPLIED", "coarser than recommended"));
            }

            return inputs;
        }

        /// <summary>The comparator of §11.3: score, then area, then harm, then count, then No Shade first, then SchemeGuid. Total order.</summary>
        internal class ShadingRowComparer : IComparer<ShadingComparisonRow>
        {
            public int Compare(ShadingComparisonRow a, ShadingComparisonRow b)
            {
                if (a == null || b == null)
                {
                    return (a == null ? 1 : 0) - (b == null ? 1 : 0);
                }

                int order = b.ObjectiveScore.CompareTo(a.ObjectiveScore); // descending
                if (order != 0)
                {
                    return order;
                }

                order = a.PhysicalDeviceArea.CompareTo(b.PhysicalDeviceArea); // ascending
                if (order != 0)
                {
                    return order;
                }

                order = a.WantedSolarBlocked.CompareTo(b.WantedSolarBlocked); // ascending
                if (order != 0)
                {
                    return order;
                }

                order = a.PhysicalDeviceCount.CompareTo(b.PhysicalDeviceCount); // ascending
                if (order != 0)
                {
                    return order;
                }

                bool aBaseline = a.Status == ShadingComparisonStatus.NoShadeBaseline;
                bool bBaseline = b.Status == ShadingComparisonStatus.NoShadeBaseline;
                if (aBaseline != bBaseline)
                {
                    return aBaseline ? -1 : 1; // baseline first
                }

                return a.SchemeGuid.CompareTo(b.SchemeGuid); // ordinal ascending
            }
        }
    }
}

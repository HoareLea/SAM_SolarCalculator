// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// Assembles the complete shading SCHEMES for a comparison: one per conventional family
        /// (all apertures of the scope, from the per-aperture rationalisation results), one for the
        /// grouped awning run, and — unless suppressed — the zero-device No Shade baseline.
        ///
        /// The scope is the aperture GUID set of <paramref name="targets"/>, sorted ordinal
        /// ascending. Every scheme covers exactly that set, which is what makes the schemes
        /// comparable on one basis.
        ///
        /// CONVENTIONAL FAMILIES. Results are grouped by <see cref="OptimisedShadingResult.TypologyName"/>.
        /// One family = one scheme over the whole scope. For each aperture:
        ///   - a device that beat the null device is used as a ShadingDevice;
        ///   - RecommendsNoShading contributes the NoShading null device, with the best candidate
        ///     recorded in the design diagnostics;
        ///   - EvaluationFailed, or no result at all for the aperture, makes the scheme
        ///     NotEvaluated with a reason naming the aperture — no other family's device is ever
        ///     substituted;
        ///   - a device below the resolution limit contributes a Warning.
        /// A family whose every aperture returned NoShading is still emitted as a distinct row,
        /// labelled "&lt;Family&gt; (all no shade)" — it is a different engineering statement
        /// ("this family was searched and lost") from the baseline.
        ///
        /// GROUPED AWNING. ALL groups from the ONE supplied run form ONE scheme. The union of the
        /// groups must equal the scope, groups must not overlap, and one product preset must apply;
        /// any violation makes that scheme NotEvaluated with a reason naming the aperture.
        ///
        /// NO SHADE BASELINE. When <paramref name="includeNoShade"/> is true (the default, and it
        /// SHOULD be left true), one real zero-device scheme named "No Shade" is emitted and
        /// verified through the identical path — the anchor every other scheme's baseline is
        /// checked against.
        /// </summary>
        /// <param name="targets">The scope: every scheme will cover exactly these apertures.</param>
        /// <param name="optimisedShadingResults">Per-aperture, per-family results from RationaliseShading.</param>
        /// <param name="groupedAwningResults">Per-group results from one RationaliseAwningGroup run.</param>
        /// <param name="includeNoShade">Emit the No Shade baseline scheme. Default true.</param>
        /// <param name="message">An actionable sentence on refusal; null on success.</param>
        public static List<ShadingScheme> ShadingSchemes(
            IEnumerable<ApertureSolarTarget> targets,
            IEnumerable<OptimisedShadingResult> optimisedShadingResults,
            IEnumerable<GroupedAwningResult> groupedAwningResults,
            bool includeNoShade,
            out string message)
        {
            message = null;

            // ------------------------------------------- the scope, sorted and de-duplicated ----
            List<ApertureSolarTarget> targetList = new List<ApertureSolarTarget>();
            if (targets != null)
            {
                foreach (ApertureSolarTarget target in targets)
                {
                    if (target != null)
                    {
                        targetList.Add(target);
                    }
                }
            }

            List<Guid> scope = new List<Guid>();
            foreach (ApertureSolarTarget target in targetList)
            {
                if (!scope.Contains(target.ApertureGuid))
                {
                    scope.Add(target.ApertureGuid);
                }
            }
            scope.Sort();

            if (targetList.Count != scope.Count)
            {
                message = string.Format(CultureInfo.InvariantCulture,
                    "A duplicated aperture was supplied: the scope contains {0} targets but only {1} distinct apertures. A duplicated aperture would double-count its energy in every scheme equally, which hides rather than cancels the error. Remove the duplicate target.",
                    targetList.Count, scope.Count);
                return null;
            }

            if (scope.Count == 0)
            {
                message = "No aperture solar targets were supplied, so no shading scheme can be assembled.";
                return null;
            }

            List<Guid> panelGuids = new List<Guid>();
            foreach (ApertureSolarTarget target in targetList)
            {
                if (!panelGuids.Contains(target.PanelGuid))
                {
                    panelGuids.Add(target.PanelGuid);
                }
            }
            panelGuids.Sort();

            List<ShadingScheme> result = new List<ShadingScheme>();

            // ------------------------------------------------------- the No Shade baseline ----
            if (includeNoShade)
            {
                result.Add(new ShadingScheme(
                    "No Shade", "Baseline", scope, panelGuids,
                    new List<ShadingDevice>(), new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                    ShadingDesignStatus.NoShading, new List<string>(), null,
                    new List<string> { "Zero-device baseline: the unshaded state, verified through the same path as every other option." }));
            }

            // --------------------------------------------------- conventional families ----
            // Per family, per aperture: the winning result. Duplicates for the same family+aperture
            // are resolved by the highest objective score so the outcome does not depend on input
            // order (a non-NaN score always wins over a NaN one).
            Dictionary<string, Dictionary<Guid, OptimisedShadingResult>> byFamily = new Dictionary<string, Dictionary<Guid, OptimisedShadingResult>>();
            foreach (OptimisedShadingResult optimisedShadingResult in optimisedShadingResults ?? new List<OptimisedShadingResult>())
            {
                if (optimisedShadingResult == null || string.IsNullOrEmpty(optimisedShadingResult.TypologyName))
                {
                    continue;
                }

                if (!byFamily.TryGetValue(optimisedShadingResult.TypologyName, out Dictionary<Guid, OptimisedShadingResult> perAperture))
                {
                    perAperture = new Dictionary<Guid, OptimisedShadingResult>();
                    byFamily[optimisedShadingResult.TypologyName] = perAperture;
                }

                if (perAperture.TryGetValue(optimisedShadingResult.ApertureGuid, out OptimisedShadingResult existing))
                {
                    double existingScore = existing.ObjectiveScore;
                    double candidateScore = optimisedShadingResult.ObjectiveScore;
                    if (!double.IsNaN(candidateScore) && (double.IsNaN(existingScore) || candidateScore > existingScore))
                    {
                        perAperture[optimisedShadingResult.ApertureGuid] = optimisedShadingResult;
                    }
                }
                else
                {
                    perAperture[optimisedShadingResult.ApertureGuid] = optimisedShadingResult;
                }
            }

            List<string> familyNames = new List<string>(byFamily.Keys);
            familyNames.Sort(StringComparer.Ordinal);

            foreach (string familyName in familyNames)
            {
                Dictionary<Guid, OptimisedShadingResult> perAperture = byFamily[familyName];

                List<ShadingDevice> devices = new List<ShadingDevice>();
                List<string> diagnostics = new List<string>();
                List<string> warnings = new List<string>();
                ShadingDesignStatus status = ShadingDesignStatus.Ok;
                ShadingObjective designObjective = null;
                bool missing = false;
                bool allNoShade = true;

                foreach (Guid apertureGuid in scope)
                {
                    ApertureSolarTarget target = Target(targetList, apertureGuid);
                    OptimisedShadingResult optimisedShadingResult = perAperture.TryGetValue(apertureGuid, out OptimisedShadingResult found) ? found : null;

                    if (optimisedShadingResult == null)
                    {
                        status = Worse(status, ShadingDesignStatus.NotEvaluated);
                        diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                            "No {0} result for aperture {1}. A family scheme must cover the whole scope; no other family's device was substituted.", familyName, apertureGuid));
                        missing = true;
                        continue;
                    }

                    designObjective = designObjective ?? optimisedShadingResult.Objective;

                    if (optimisedShadingResult.Termination == ShadingOptimisationTermination.EvaluationFailed)
                    {
                        status = Worse(status, ShadingDesignStatus.NotEvaluated);
                        diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0} @ {1}: evaluation failed - no candidate could be measured.", familyName, apertureGuid));
                        continue;
                    }

                    if (optimisedShadingResult.RecommendsNoShading)
                    {
                        devices.Add(new ShadingDevice(apertureGuid, new NoShading()));
                        status = Worse(status, ShadingDesignStatus.NoShading);

                        IShadingTypology bestCandidate = optimisedShadingResult.Typology();
                        string bestText = bestCandidate == null
                            ? "no best candidate recorded"
                            : string.Format(CultureInfo.InvariantCulture, "best candidate {0} scored {1:0.###} kWh",
                                ParameterText(bestCandidate), optimisedShadingResult.ObjectiveScore);
                        diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0} @ {1}: recommends NO SHADE ({2}, {3} candidates, {4}).",
                            familyName, apertureGuid, bestText, optimisedShadingResult.Evaluations, optimisedShadingResult.Termination));
                        continue;
                    }

                    IShadingTypology typology = optimisedShadingResult.Typology();
                    devices.Add(new ShadingDevice(apertureGuid, typology));
                    allNoShade = false;

                    ShadingResolutionState resolutionState = Query.ShadingResolution(typology, target, optimisedShadingResult.GridSize, out string resolutionMessage, out double _);
                    if (resolutionState != ShadingResolutionState.Resolved)
                    {
                        status = Worse(status, ShadingDesignStatus.Warning);
                        if (resolutionMessage != null)
                        {
                            warnings.Add(resolutionMessage);
                        }
                    }

                    if (Query.GridResolutionCapReached(optimisedShadingResult, out string capMessage))
                    {
                        status = Worse(status, ShadingDesignStatus.Warning);
                        if (capMessage != null)
                        {
                            warnings.Add(capMessage);
                        }
                    }

                    diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0} @ {1}: {2} candidates, {3}.",
                        familyName, apertureGuid, optimisedShadingResult.Evaluations, optimisedShadingResult.Termination));
                }

                string schemeName = familyName;
                if (!missing && allNoShade)
                {
                    schemeName = familyName + " (all no shade)";
                }

                // The design-time score is the SUM of the per-window design scores — a sum of
                // independent optimisations, deliberately kept distinct from the verified score.
                double designTimeScore = double.NaN;
                double designTimeBenefit = double.NaN;
                double designTimeHarm = double.NaN;
                double designTimeCost = double.NaN;
                int designEvaluations = 0;
                ShadingOptimisationTermination designTermination = ShadingOptimisationTermination.Undefined;
                if (!missing)
                {
                    double total = 0, benefitTotal = 0, harmTotal = 0, costTotal = 0;
                    foreach (Guid apertureGuid in scope)
                    {
                        OptimisedShadingResult optimisedShadingResult = perAperture[apertureGuid];
                        if (double.IsNaN(optimisedShadingResult.ObjectiveScore))
                        {
                            total = double.NaN;
                            break;
                        }

                        total += optimisedShadingResult.ObjectiveScore;
                        benefitTotal += optimisedShadingResult.Benefit;
                        harmTotal += optimisedShadingResult.Harm;
                        costTotal += optimisedShadingResult.Cost;
                        designEvaluations += optimisedShadingResult.Evaluations;
                        designTermination = MostConsequential(designTermination, optimisedShadingResult.Termination);
                    }
                    designTimeScore = total;
                    designTimeBenefit = benefitTotal;
                    designTimeHarm = harmTotal;
                    designTimeCost = costTotal;
                }

                ShadingScheme scheme = new ShadingScheme(
                    schemeName, "RationaliseShading", scope, panelGuids,
                    devices, new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                    status, warnings, designObjective, diagnostics);
                scheme.DesignTimeScore = designTimeScore;
                scheme.DesignTimeBenefit = designTimeBenefit;
                scheme.DesignTimeHarm = designTimeHarm;
                scheme.DesignTimeCost = designTimeCost;
                scheme.DesignEvaluations = designEvaluations;
                scheme.DesignTermination = designTermination;
                result.Add(scheme);
            }

            // ------------------------------------------------------ grouped awning ----
            List<GroupedAwningResult> groupedResults = new List<GroupedAwningResult>();
            if (groupedAwningResults != null)
            {
                foreach (GroupedAwningResult groupedAwningResult in groupedAwningResults)
                {
                    if (groupedAwningResult != null)
                    {
                        groupedResults.Add(groupedAwningResult);
                    }
                }
            }

            if (groupedResults.Count != 0)
            {
                List<GroupedShadingDevice> groupedDevices = new List<GroupedShadingDevice>();
                List<ApertureShadingGroup> groups = new List<ApertureShadingGroup>();
                List<string> diagnostics = new List<string>();
                List<string> warnings = new List<string>();
                ShadingDesignStatus status = ShadingDesignStatus.Ok;

                HashSet<Guid> covered = new HashSet<Guid>();
                string specificationName = null;
                string typologyName = null;
                bool invalid = false;

                foreach (GroupedAwningResult groupedAwningResult in groupedResults)
                {
                    GroupedShadingDevice device = groupedAwningResult.Device;
                    groupedDevices.Add(device == null ? null : new GroupedShadingDevice(device));
                    groups.Add(groupedAwningResult.Group == null ? null : new ApertureShadingGroup(groupedAwningResult.Group));

                    status = Worse(status, groupedAwningResult.Status);

                    foreach (string warning in groupedAwningResult.Warnings ?? new List<string>())
                    {
                        if (warning != null)
                        {
                            warnings.Add(warning);
                        }
                    }

                    diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                        "Group {0}: {1} ({2} candidates).", groupedAwningResult.Group?.GroupGuid, groupedAwningResult.DesignSummary, groupedAwningResult.Evaluations));

                    if (device != null && !device.IsNoShading)
                    {
                        if (specificationName == null)
                        {
                            specificationName = device.SpecificationName;
                            typologyName = device.TypologyName;
                        }
                        else if (specificationName != device.SpecificationName)
                        {
                            status = Worse(status, ShadingDesignStatus.NotEvaluated);
                            diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                                "Product presets differ across groups ({0} vs {1}). One grouped-awning scheme must come from one product run.", specificationName, device.SpecificationName));
                            invalid = true;
                        }
                    }

                    foreach (Guid member in groupedAwningResult.Group?.ApertureGuids ?? new List<Guid>())
                    {
                        if (!scope.Contains(member))
                        {
                            status = Worse(status, ShadingDesignStatus.NotEvaluated);
                            diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                                "Aperture {0} is covered by a grouped awning but is outside the comparison scope.", member));
                            invalid = true;
                        }
                        else if (covered.Contains(member))
                        {
                            status = Worse(status, ShadingDesignStatus.NotEvaluated);
                            diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                                "Aperture {0} is covered by two grouped awnings. Overlapping groups are refused; split the run.", member));
                            invalid = true;
                        }
                        else
                        {
                            covered.Add(member);
                        }
                    }
                }

                foreach (Guid apertureGuid in scope)
                {
                    if (!covered.Contains(apertureGuid))
                    {
                        status = Worse(status, ShadingDesignStatus.NotEvaluated);
                        diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                            "Aperture {0} is in the scope but covered by no grouped awning. The union of the groups must equal the scope.", apertureGuid));
                        invalid = true;
                    }
                }

                bool allNoShade = !invalid;
                foreach (GroupedShadingDevice device in groupedDevices)
                {
                    if (device != null && !device.IsNoShading)
                    {
                        allNoShade = false;
                        break;
                    }
                }

                string groupedName = "Grouped " + (specificationName ?? string.Empty) + " " + ShadingScheme.DisplayName(typologyName ?? "Awning");
                groupedName = groupedName.Trim();
                if (allNoShade)
                {
                    groupedName += " (all no shade)";
                }

                int groupedEvaluations = 0;
                foreach (GroupedAwningResult groupedAwningResult in groupedResults)
                {
                    groupedEvaluations += groupedAwningResult.Evaluations;
                }

                ShadingScheme groupedScheme = new ShadingScheme(
                    groupedName, "RationaliseAwningGroup", scope, panelGuids,
                    new List<ShadingDevice>(), groupedDevices, groups,
                    status, warnings, null, diagnostics);
                groupedScheme.DesignEvaluations = groupedEvaluations;
                result.Add(groupedScheme);
            }

            return result;
        }

        private static ApertureSolarTarget Target(List<ApertureSolarTarget> targets, Guid apertureGuid)
        {
            return targets.Find(x => x != null && x.ApertureGuid == apertureGuid);
        }

        /// <summary>The worst contribution in the order NotEvaluated &gt; Warning &gt; NoShading &gt; Ok.</summary>
        private static ShadingDesignStatus Worse(ShadingDesignStatus current, ShadingDesignStatus contribution)
        {
            return contribution > current ? contribution : current;
        }

        /// <summary>
        /// The most consequential search termination across a family: EvaluationFailed outranks
        /// everything (no numbers exist), EvaluationBudgetExhausted next (best seen, not converged),
        /// then the rest by enum order.
        /// </summary>
        private static ShadingOptimisationTermination MostConsequential(ShadingOptimisationTermination current, ShadingOptimisationTermination candidate)
        {
            int Rank(ShadingOptimisationTermination termination)
            {
                switch (termination)
                {
                    case ShadingOptimisationTermination.EvaluationFailed: return 5;
                    case ShadingOptimisationTermination.EvaluationBudgetExhausted: return 4;
                    case ShadingOptimisationTermination.NoBeneficialCandidate: return 3;
                    case ShadingOptimisationTermination.NothingToSearch: return 2;
                    case ShadingOptimisationTermination.StepBelowGranularity: return 1;
                    default: return 0;
                }
            }

            return Rank(candidate) > Rank(current) ? candidate : current;
        }

        private static string ParameterText(IShadingTypology typology)
        {
            if (typology == null)
            {
                return null;
            }

            List<string> parts = new List<string>();
            foreach (string name in typology.ParameterNames ?? new List<string>())
            {
                double value = typology.GetParameter(name);
                if (!double.IsNaN(value))
                {
                    parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:0.###}", name, value));
                }
            }

            return parts.Count == 0 ? null : string.Join(", ", parts);
        }
    }
}

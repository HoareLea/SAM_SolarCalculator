// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SAM.Geometry.Object.Spatial;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Optimise
    {
        /// <summary>
        /// The product-constrained awning search for ONE aperture group: a bounded, deterministic
        /// evaluation of the Dakar candidates, scored with the SAME objective and first-hit
        /// accounting as the mature single-aperture optimiser — not a rewrite of it.
        ///
        /// SEARCHED, when not fixed by the caller:
        ///
        ///   - every Dakar projection whose minimum width fits the group width;
        ///   - TiltDegrees from 5° to 40° inclusive: a 5° coarse sweep, then 1° refinement around
        ///     the winning coarse angle, confined to the product range. The returned angle is the
        ///     selected fixed installation tilt of the deployed fabric — a design outcome, not an
        ///     hourly tracking angle. The 1° final granularity is awning-specific: the global
        ///     TiltDegrees step of the louvre/fin families is unchanged.
        ///   - valance depth 0 and the preset standard depth, only when valance optimisation is
        ///     enabled (null); otherwise the caller's fixed value.
        ///
        /// RiseAboveHead, MountingOffset and the side extension stay fixed from the caller, which
        /// keeps the search small by design.
        ///
        /// REFUSED, NEVER CLAMPED. This method is public in its own right, so it validates the
        /// caller's fixed projection, tilt, valance, rise and extension ITSELF rather than assuming
        /// Create.AwningGroupResults already did. Every one is checked against both the product
        /// limits and the RetractableAwning parameter bounds, because ShadingTypology.Define clamps
        /// silently: unchecked, a fixed 50° tilt would build 40° of geometry and still report 50°.
        /// An invalid request comes back as NotEvaluated with the reason attached — never as a
        /// quietly resized awning, and never as NoShading, which would claim a measurement that
        /// never happened. A preset whose own limits are wider than the family bounds has the
        /// searched lattice confined to the intersection, and says so in the warnings.
        ///
        /// ONE PHYSICAL UNIT. The canopy and valance are built ONCE from the group frame; every
        /// member aperture is measured against the SAME element set through its own cell window and
        /// shared-cache offset. Material is charged once: the group material fraction is the shared
        /// device area over the sum of the member gross areas.
        ///
        /// WINNER. The existing total order applies — higher objective score, then lower material
        /// fraction, then the lexicographically smaller parameter vector — so the answer is
        /// reproducible. The null device scores exactly zero; when nothing beats it the result is a
        /// successful NO SHADE answer, never the least-bad awning dressed up as a recommendation.
        /// A group nothing could measure is a failure, reported as such, distinct from NO SHADE.
        /// </summary>
        /// <param name="group">The group, members ordered left-to-right.</param>
        /// <param name="baseVisibilityCache">Visibility with CONTEXT ONLY, shared by every member.</param>
        /// <param name="desirabilities">Per-member desirability, aligned to the group's member order.</param>
        /// <param name="contextOccluders">Existing context (takes effect through baseVisibilityCache).</param>
        /// <param name="objective">The objective. Null for the Stage 9 default.</param>
        /// <param name="specification">Product preset. Null = Dakar.</param>
        /// <param name="projection">Fixed projection [m], or null to search the valid preset projections.</param>
        /// <param name="tiltDegrees">Fixed deployment tilt [°], or null to select it by analysis.</param>
        /// <param name="riseAboveHead">Fixed rise above the head line [m].</param>
        /// <param name="extensionBeyondJambs">Fixed symmetric side extension [m].</param>
        /// <param name="valanceDepth">Fixed valance depth [m] (0 or the preset standard); null enables valance optimisation.</param>
        /// <param name="maximumEvaluations">Hard budget on distinct candidate evaluations for this group.</param>
        public static GroupedAwningResult RetractableAwningGroup(
            this ApertureShadingGroup group,
            SolarVisibilityCache baseVisibilityCache,
            List<ApertureDesirability> desirabilities,
            List<LinkedFace3D> contextOccluders,
            ShadingObjective objective = null,
            AwningSpecification specification = null,
            double? projection = null,
            double? tiltDegrees = null,
            double riseAboveHead = 0.0,
            double extensionBeyondJambs = 0.0,
            double? valanceDepth = null,
            int maximumEvaluations = 400)
        {
            return RetractableAwningGroup(
                group, baseVisibilityCache, desirabilities, contextOccluders,
                objective, specification, projection, tiltDegrees, riseAboveHead, extensionBeyondJambs,
                valanceDepth, maximumEvaluations, mountingOffset: 0.0);
        }

        /// <summary>
        /// The mounting-offset overload. The original twelve-parameter signature above is retained
        /// exactly for binary compatibility and forwards here with MountingOffset = 0.0; this
        /// thirteen-parameter signature is likewise retained and forwards to the grouping-criteria
        /// overload with the algorithm defaults — the only honest claim a criteria-less call can
        /// make about how its group was formed.
        /// </summary>
        /// <param name="group">The group, members ordered left-to-right.</param>
        /// <param name="baseVisibilityCache">Visibility with CONTEXT ONLY, shared by every member.</param>
        /// <param name="desirabilities">Per-member desirability, aligned to the group's member order.</param>
        /// <param name="contextOccluders">Existing context (takes effect through baseVisibilityCache).</param>
        /// <param name="objective">The objective. Null for the Stage 9 default.</param>
        /// <param name="specification">Product preset. Null = Dakar.</param>
        /// <param name="projection">Fixed projection [m], or null to search the valid preset projections.</param>
        /// <param name="tiltDegrees">Fixed deployment tilt [°], or null to select it by analysis.</param>
        /// <param name="riseAboveHead">Fixed rise above the head line [m].</param>
        /// <param name="extensionBeyondJambs">Fixed symmetric side extension [m].</param>
        /// <param name="valanceDepth">Fixed valance depth [m] (0 or the preset standard); null enables valance optimisation.</param>
        /// <param name="maximumEvaluations">Hard budget on distinct candidate evaluations for this group.</param>
        /// <param name="mountingOffset">Fixed horizontal outward distance from the aperture plane to the awning mounting line [m].</param>
        public static GroupedAwningResult RetractableAwningGroup(
            this ApertureShadingGroup group,
            SolarVisibilityCache baseVisibilityCache,
            List<ApertureDesirability> desirabilities,
            List<LinkedFace3D> contextOccluders,
            ShadingObjective objective,
            AwningSpecification specification,
            double? projection,
            double? tiltDegrees,
            double riseAboveHead,
            double extensionBeyondJambs,
            double? valanceDepth,
            int maximumEvaluations,
            double mountingOffset)
        {
            return RetractableAwningGroup(
                group, baseVisibilityCache, desirabilities, contextOccluders,
                objective, specification, projection, tiltDegrees, riseAboveHead, extensionBeyondJambs,
                valanceDepth, maximumEvaluations, mountingOffset,
                GroupedShadingDevice.DefaultMaximumGap, GroupedShadingDevice.DefaultHeadTolerance);
        }

        /// <summary>
        /// The grouping-criteria overload. The device records the criteria its group was formed
        /// under, so a downstream node can re-establish the EXACT original physical group rather
        /// than re-group the members under the algorithm defaults.
        /// </summary>
        /// <param name="group">The group, members ordered left-to-right.</param>
        /// <param name="baseVisibilityCache">Visibility with CONTEXT ONLY, shared by every member.</param>
        /// <param name="desirabilities">Per-member desirability, aligned to the group's member order.</param>
        /// <param name="contextOccluders">Existing context (takes effect through baseVisibilityCache).</param>
        /// <param name="objective">The objective. Null for the Stage 9 default.</param>
        /// <param name="specification">Product preset. Null = Dakar.</param>
        /// <param name="projection">Fixed projection [m], or null to search the valid preset projections.</param>
        /// <param name="tiltDegrees">Fixed deployment tilt [°], or null to select it by analysis.</param>
        /// <param name="riseAboveHead">Fixed rise above the head line [m].</param>
        /// <param name="extensionBeyondJambs">Fixed symmetric side extension [m].</param>
        /// <param name="valanceDepth">Fixed valance depth [m] (0 or the preset standard); null enables valance optimisation.</param>
        /// <param name="maximumEvaluations">Hard budget on distinct candidate evaluations for this group.</param>
        /// <param name="mountingOffset">Fixed horizontal outward distance from the aperture plane to the awning mounting line [m].</param>
        /// <param name="maximumGap">The largest horizontal gap between consecutive apertures that still shares one awning [m] — the criterion the group was formed under.</param>
        /// <param name="headTolerance">The largest head-level spread within one group [m] — the criterion the group was formed under.</param>
        public static GroupedAwningResult RetractableAwningGroup(
            this ApertureShadingGroup group,
            SolarVisibilityCache baseVisibilityCache,
            List<ApertureDesirability> desirabilities,
            List<LinkedFace3D> contextOccluders,
            ShadingObjective objective,
            AwningSpecification specification,
            double? projection,
            double? tiltDegrees,
            double riseAboveHead,
            double extensionBeyondJambs,
            double? valanceDepth,
            int maximumEvaluations,
            double mountingOffset,
            double maximumGap,
            double headTolerance)
        {
            specification = specification ?? AwningSpecification.Dakar;
            objective = objective ?? new ShadingObjective();

            if (group == null || baseVisibilityCache == null || group.Targets.Count == 0)
            {
                return null;
            }

            // FIXED INPUTS ARE REQUESTS, NOT SEARCH BOUNDS, and this method is public in its own
            // right. It validates them HERE rather than trusting Create.AwningGroupResults to have
            // done it, because the failure mode of trusting is silent: ShadingTypology.Define
            // CLAMPS into the family bounds, so a fixed tilt of 50° would build 40° of geometry
            // while the answer still reported 50°. A refused request is always better than a design
            // that disagrees with its own parameters.
            if (!ValidFixedInputs(group, specification, projection, tiltDegrees, riseAboveHead, extensionBeyondJambs, valanceDepth, mountingOffset, out string refusal))
            {
                return Refused(group, refusal);
            }

            // Member inputs, aligned to the group's own left-to-right order.
            Dictionary<Guid, ApertureDesirability> desirabilityMap = new Dictionary<Guid, ApertureDesirability>();
            foreach (ApertureDesirability desirability in desirabilities ?? new List<ApertureDesirability>())
            {
                if (desirability != null)
                {
                    desirabilityMap[desirability.ApertureGuid] = desirability;
                }
            }

            List<ApertureSolarTarget> targets = group.Targets;
            List<int> offsets = group.CellIndexOffsets;

            double width = group.Width + 2.0 * extensionBeyondJambs;

            // A specification is DATA and its limits need not agree with the RetractableAwning
            // parameter bounds — Dakar does, a caller's own preset need not. Any searched value the
            // family would clamp is dropped from the lattice and SAID SO, rather than quietly
            // evaluated as a different awning from the one the preset named.
            RetractableAwning familyBounds = new RetractableAwning();
            List<string> latticeWarnings = new List<string>();

            // The candidate lattice, product-constrained.
            List<double> projections = new List<double>();
            if (projection.HasValue)
            {
                projections.Add(projection.Value);
            }
            else
            {
                foreach (double allowed in specification.AllowedProjections)
                {
                    double minimum = specification.MinimumWidth(allowed);
                    if (double.IsNaN(minimum) || width < minimum * (1.0 - 1e-9))
                    {
                        continue;
                    }

                    if (!Within(familyBounds, "Projection", allowed, out double projectionMinimum, out double projectionMaximum))
                    {
                        latticeWarnings.Add(string.Format(CultureInfo.InvariantCulture,
                            "The {0} projection {1:0.###} m lies outside the RetractableAwning range of {2:0.###} m to {3:0.###} m and was not evaluated: the geometry would have been built at a different projection from the one reported.",
                            specification.Name, allowed, projectionMinimum, projectionMaximum));
                        continue;
                    }

                    projections.Add(allowed);
                }
            }

            List<double> valances = new List<double>();
            if (valanceDepth.HasValue)
            {
                valances.Add(valanceDepth.Value);
            }
            else
            {
                valances.Add(0.0);
                if (Within(familyBounds, "ValanceDepth", specification.StandardValanceDepth, out double valanceMinimum, out double valanceMaximum))
                {
                    valances.Add(specification.StandardValanceDepth);
                }
                else
                {
                    latticeWarnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "The {0} standard valance depth {1:0.###} m lies outside the RetractableAwning range of {2:0.###} m to {3:0.###} m and was not evaluated; only the no-valance state was searched.",
                        specification.Name, specification.StandardValanceDepth, valanceMinimum, valanceMaximum));
                }
            }

            // The coarse tilt sweep runs over the INTERSECTION of the product range and the family
            // bounds, for the same reason: a preset that allowed 0°–60° must not be answered with
            // silently clamped 5° and 40° geometry reported as 0° and 60°.
            familyBounds.TryGetBounds("TiltDegrees", out double familyTiltMinimum, out double familyTiltMaximum);
            double tiltMinimum = Math.Max(specification.MinimumTiltDegrees, familyTiltMinimum);
            double tiltMaximum = Math.Min(specification.MaximumTiltDegrees, familyTiltMaximum);
            if (!tiltDegrees.HasValue && (specification.MinimumTiltDegrees < familyTiltMinimum - 1e-9 || specification.MaximumTiltDegrees > familyTiltMaximum + 1e-9))
            {
                latticeWarnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "The {0} tilt range of {1:0.#}° to {2:0.#}° is wider than the RetractableAwning range of {3:0.#}° to {4:0.#}°; the search was confined to {5:0.#}° to {6:0.#}° so every reported tilt is the tilt that was built.",
                    specification.Name, specification.MinimumTiltDegrees, specification.MaximumTiltDegrees, familyTiltMinimum, familyTiltMaximum, tiltMinimum, tiltMaximum));
            }

            AwningEvaluator evaluator = new AwningEvaluator(group, targets, offsets, baseVisibilityCache, desirabilityMap, contextOccluders, objective, specification, riseAboveHead, extensionBeyondJambs, mountingOffset, maximumGap, headTolerance, latticeWarnings);

            // The null device first: measured like every other candidate, and the zero the rest must beat.
            AwningCandidate nullDevice = evaluator.Evaluate(null, double.NaN, 0.0);
            bool nullMeasured = nullDevice != null && nullDevice.Measurable;

            List<AwningCandidate> measured = new List<AwningCandidate>();

            foreach (double candidateProjection in projections)
            {
                foreach (double candidateValance in valances)
                {
                    if (evaluator.Evaluations >= maximumEvaluations)
                    {
                        break;
                    }

                    if (!tiltDegrees.HasValue)
                    {
                        // Coarse 5° sweep across the product range, then 1° refinement around the
                        // winning coarse angle, clamped to 5°–40°.
                        AwningCandidate bestCoarse = null;
                        for (double tilt = tiltMinimum; tilt <= tiltMaximum + 1e-9; tilt += 5.0)
                        {
                            if (evaluator.Evaluations >= maximumEvaluations)
                            {
                                break;
                            }

                            AwningCandidate candidate = evaluator.Evaluate(candidateProjection, tilt, candidateValance);
                            if (AwningIsBetter(candidate, bestCoarse))
                            {
                                bestCoarse = candidate;
                            }
                        }

                        AwningCandidate incumbent = bestCoarse;
                        if (incumbent != null && incumbent.Measurable)
                        {
                            while (evaluator.Evaluations < maximumEvaluations)
                            {
                                AwningCandidate bestProbe = null;
                                foreach (int sign in new int[] { 1, -1 })
                                {
                                    double probeTilt = Math.Round(incumbent.TiltDegrees) + sign;
                                    if (probeTilt < tiltMinimum || probeTilt > tiltMaximum)
                                    {
                                        continue;
                                    }

                                    AwningCandidate probe = evaluator.Evaluate(candidateProjection, probeTilt, candidateValance);
                                    if (AwningIsBetter(probe, bestProbe))
                                    {
                                        bestProbe = probe;
                                    }
                                }

                                if (!AwningIsBetter(bestProbe, incumbent))
                                {
                                    break;
                                }

                                incumbent = bestProbe;
                            }
                        }

                        if (incumbent != null && incumbent.Measurable && !double.IsNaN(incumbent.Score))
                        {
                            measured.Add(incumbent);
                        }
                    }
                    else
                    {
                        // A scoreless candidate is not a candidate. Measurable only says the members
                        // could be measured; the objective can still come back NaN (a brief with no
                        // unwanted hours leaves the benefit term undefined). Letting one into the
                        // ranking would put a NaN into the comparer — every comparison against it is
                        // false, which is not a total order — and List.Sort answers that by throwing.
                        AwningCandidate candidate = evaluator.Evaluate(candidateProjection, tiltDegrees.Value, candidateValance);
                        if (candidate != null && candidate.Measurable && !double.IsNaN(candidate.Score))
                        {
                            measured.Add(candidate);
                        }
                    }
                }
            }

            // The exact total order for ranking: score, then material, then parameters.
            measured.Sort(AwningCompareForRanking);
            if (measured.Count > 0)
            {
                AwningCandidate winner = measured[0];
                bool recommendsNoShading = !(winner.Score > 0);

                List<string> warnings = evaluator.Warnings(width);

                GroupedShadingDevice device;
                GroupedShadingDevice bestCandidateDevice;
                GroupedShadingPerformance performance;
                ShadingDesignStatus status;

                if (recommendsNoShading)
                {
                    device = evaluator.NullDevice();
                    bestCandidateDevice = evaluator.Device(winner);
                    performance = nullDevice?.Performance ?? null;
                    status = ShadingDesignStatus.NoShading;
                }
                else
                {
                    device = evaluator.Device(winner);
                    bestCandidateDevice = null;
                    performance = winner.Performance;
                    status = warnings.Count == 0 ? ShadingDesignStatus.Ok : ShadingDesignStatus.Warning;
                }

                return new GroupedAwningResult(
                    group, device, bestCandidateDevice, performance, status,
                    Summary(group, device, performance, recommendsNoShading, winner),
                    warnings, evaluator.Candidates(), evaluator.Evaluations);
            }

            if (nullMeasured)
            {
                // Every measurable candidate lost to the null device (or none existed to begin with,
                // e.g. the group width fits no Dakar projection): a successful NO SHADE answer.
                List<string> warnings = evaluator.Warnings(width);
                return new GroupedAwningResult(
                    group, evaluator.NullDevice(), null, nullDevice.Performance, ShadingDesignStatus.NoShading,
                    Summary(group, evaluator.NullDevice(), nullDevice.Performance, true, null),
                    warnings, evaluator.Candidates(), evaluator.Evaluations);
            }

            // Nothing could be measured at all: a fault, never "no shading is worth building".
            return new GroupedAwningResult(
                group, null, null, null, ShadingDesignStatus.NotEvaluated,
                string.Format(CultureInfo.InvariantCulture, "{0:0}° | Not evaluated | no candidate could be measured on this group", group.Targets[0].Azimuth),
                new List<string> { "No candidate could be measured on this group: the apertures, the solar calculation and the candidate geometry do not describe the same analysis points." },
                evaluator.Candidates(), evaluator.Evaluations);
        }

        /// <summary>
        /// Whether a value sits inside a RetractableAwning parameter's own declared bounds. The
        /// bounds are read off the family rather than restated here, so the two cannot drift apart.
        /// A non-finite value is never inside: Define would turn NaN into NaN geometry and the
        /// candidate would carry NaN corners into the ray engine.
        /// </summary>
        private static bool Within(RetractableAwning familyBounds, string parameterName, double value, out double minimum, out double maximum)
        {
            if (!familyBounds.TryGetBounds(parameterName, out minimum, out maximum))
            {
                return false;
            }

            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= minimum && value <= maximum;
        }

        /// <summary>
        /// Whether the caller's FIXED inputs describe a buildable request. Each is checked against
        /// BOTH the product limits and the RetractableAwning parameter bounds, because the two are
        /// separate contracts and only their intersection can be honestly reported: a value inside
        /// the product but outside the family would be clamped into different geometry, and a value
        /// inside the family but outside the product is not a saleable awning.
        ///
        /// Nothing is clamped and nothing is nudged to the nearest legal value — the message says
        /// what to change.
        /// </summary>
        private static bool ValidFixedInputs(ApertureShadingGroup group, AwningSpecification specification, double? projection, double? tiltDegrees, double riseAboveHead, double extensionBeyondJambs, double? valanceDepth, double mountingOffset, out string message)
        {
            if (!ValidAwningInputs(specification, projection, tiltDegrees, riseAboveHead, extensionBeyondJambs, valanceDepth, mountingOffset, out message))
            {
                return false;
            }

            // A fixed projection must also suit THIS group's width: the product minimum-width rule
            // is a property of the pair, not of the projection alone.
            if (projection.HasValue)
            {
                double width = group.Width + 2.0 * extensionBeyondJambs;
                if (!specification.IsValid(width, projection.Value, tiltDegrees ?? specification.MinimumTiltDegrees, out string widthMessage))
                {
                    message = widthMessage;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The group-INDEPENDENT half of the same validation, so the orchestration layer can refuse
        /// a bad request before paying for a solar context and both layers refuse it in the same
        /// words. Anything that depends on a particular group's width belongs to the caller.
        /// </summary>
        internal static bool ValidAwningInputs(AwningSpecification specification, double? projection, double? tiltDegrees, double riseAboveHead, double extensionBeyondJambs, double? valanceDepth, double mountingOffset, out string message)
        {
            message = null;
            specification = specification ?? AwningSpecification.Dakar;
            RetractableAwning familyBounds = new RetractableAwning();

            if (projection.HasValue)
            {
                if (!specification.IsProjectionAllowed(projection.Value))
                {
                    message = string.Format(CultureInfo.InvariantCulture,
                        "The requested projection {0:0.###} m is not an allowed {1} projection. Allowed nominal projections are {2} m. Leave the projection empty to let the analysis select one.",
                        projection.Value, specification.Name, FormatProjections(specification));
                    return false;
                }

                if (!Within(familyBounds, "Projection", projection.Value, out double projectionMinimum, out double projectionMaximum))
                {
                    message = string.Format(CultureInfo.InvariantCulture,
                        "The requested projection {0:0.###} m is outside the RetractableAwning range of {1:0.###} m to {2:0.###} m. It is refused rather than clamped, because clamping would build a different awning from the one reported.",
                        projection.Value, projectionMinimum, projectionMaximum);
                    return false;
                }
            }

            if (tiltDegrees.HasValue)
            {
                // NaN is checked EXPLICITLY: every ordinary comparison against NaN is false, so a
                // range test alone would wave it through and the canopy would be built from
                // tan(NaN).
                if (double.IsNaN(tiltDegrees.Value) || double.IsInfinity(tiltDegrees.Value))
                {
                    message = "The requested tilt is not a finite number of degrees. Supply a tilt within the product range, or leave it empty to let the analysis select it.";
                    return false;
                }

                if (tiltDegrees.Value < specification.MinimumTiltDegrees || tiltDegrees.Value > specification.MaximumTiltDegrees)
                {
                    message = string.Format(CultureInfo.InvariantCulture,
                        "The requested tilt {0:0.#}° is outside the {1} tilt range of {2:0.#}° to {3:0.#}° from horizontal. Leave the tilt empty to let the analysis select it.",
                        tiltDegrees.Value, specification.Name, specification.MinimumTiltDegrees, specification.MaximumTiltDegrees);
                    return false;
                }

                if (!Within(familyBounds, "TiltDegrees", tiltDegrees.Value, out double tiltMinimum, out double tiltMaximum))
                {
                    message = string.Format(CultureInfo.InvariantCulture,
                        "The requested tilt {0:0.#}° is outside the RetractableAwning range of {1:0.#}° to {2:0.#}°. It is refused rather than clamped, because clamping would build a different awning from the one reported.",
                        tiltDegrees.Value, tiltMinimum, tiltMaximum);
                    return false;
                }
            }

            if (valanceDepth.HasValue)
            {
                if (!specification.IsValidValanceDepth(valanceDepth.Value))
                {
                    message = string.Format(CultureInfo.InvariantCulture,
                        "The requested valance depth {0:0.###} m is not supported: {1} valances are either 0 or the standard {2:0.##} m.",
                        valanceDepth.Value, specification.Name, specification.StandardValanceDepth);
                    return false;
                }

                if (!Within(familyBounds, "ValanceDepth", valanceDepth.Value, out double valanceMinimum, out double valanceMaximum))
                {
                    message = string.Format(CultureInfo.InvariantCulture,
                        "The requested valance depth {0:0.###} m is outside the RetractableAwning range of {1:0.###} m to {2:0.###} m. It is refused rather than clamped, because clamping would build a different awning from the one reported.",
                        valanceDepth.Value, valanceMinimum, valanceMaximum);
                    return false;
                }
            }

            // Rise and extension are fixed by the caller and never searched, so nothing downstream
            // would ever notice them being clamped — and the extension is doubly dangerous, because
            // the group WIDTH, the product validation and the bracket count are all computed from
            // the requested value while the geometry would be built from the clamped one.
            if (!Within(familyBounds, "RiseAboveHead", riseAboveHead, out double riseMinimum, out double riseMaximum))
            {
                message = string.Format(CultureInfo.InvariantCulture,
                    "The requested rise above the head line, {0:0.###} m, is outside the RetractableAwning range of {1:0.###} m to {2:0.###} m.",
                    riseAboveHead, riseMinimum, riseMaximum);
                return false;
            }

            if (!Within(familyBounds, "ExtensionBeyondJambs", extensionBeyondJambs, out double extensionMinimum, out double extensionMaximum))
            {
                message = string.Format(CultureInfo.InvariantCulture,
                    "The requested extension beyond the jambs, {0:0.###} m per side, is outside the RetractableAwning range of {1:0.###} m to {2:0.###} m. The awning width, the product check and the bracket count are all measured from this value, so it is refused rather than clamped.",
                    extensionBeyondJambs, extensionMinimum, extensionMaximum);
                return false;
            }

            // MountingOffset is a project/building placement input, not a product limit: there is no
            // product maximum to check against, so the rule is finite and non-negative. It is refused
            // rather than clamped because Define would turn a negative offset into a silently moved
            // mounting line and an infinite one into an awning built at the wrong position.
            if (double.IsNaN(mountingOffset) || double.IsInfinity(mountingOffset))
            {
                message = "The requested mounting offset is not a finite number of metres. Supply the horizontal outward distance from the aperture plane to the awning mounting line, or 0.0 m to mount directly on the aperture plane.";
                return false;
            }

            if (mountingOffset < 0.0)
            {
                message = string.Format(CultureInfo.InvariantCulture,
                    "The requested mounting offset {0:0.###} m is negative. The mounting offset is the horizontal outward distance from the aperture plane to the awning mounting line and must be zero or more; use a positive value for a recessed aperture mounted on the external facade or soffit.",
                    mountingOffset);
                return false;
            }

            return true;
        }

        private static string FormatProjections(AwningSpecification specification)
        {
            return string.Join(", ", specification.AllowedProjections.ConvertAll(x => x.ToString("0.#", CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// A refused request: NOT EVALUATED with the reason attached. It is deliberately the same
        /// shape as "nothing could be measured" and deliberately NOT NoShading — the analysis did
        /// not find that building nothing was best, it never ran.
        /// </summary>
        private static GroupedAwningResult Refused(ApertureShadingGroup group, string reason)
        {
            double azimuth = group?.Targets?[0]?.Azimuth ?? double.NaN;

            StringBuilder stringBuilder = new StringBuilder();
            if (!double.IsNaN(azimuth))
            {
                stringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0:0}° | ", azimuth);
            }

            stringBuilder.Append("Not evaluated | ").Append(reason);

            return new GroupedAwningResult(
                group, null, null, null, ShadingDesignStatus.NotEvaluated,
                stringBuilder.ToString(), new List<string> { reason }, new List<AwningSearchCandidate>(), 0);
        }

        private static string Summary(ApertureShadingGroup group, GroupedShadingDevice device, GroupedShadingPerformance performance, bool noShading, AwningCandidate leastBad)
        {
            double azimuth = group?.Targets?[0]?.Azimuth ?? double.NaN;

            StringBuilder stringBuilder = new StringBuilder();
            if (!double.IsNaN(azimuth))
            {
                stringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0:0}° | ", azimuth);
            }

            if (performance == null)
            {
                stringBuilder.Append("Not evaluated");
                return stringBuilder.ToString();
            }

            if (noShading)
            {
                stringBuilder.Append("No shading | nothing beats leaving this group unshaded");
                if (leastBad != null && !double.IsNaN(leastBad.Score))
                {
                    stringBuilder.AppendFormat(CultureInfo.InvariantCulture, " | best candidate RetractableAwning scored {0:0.#} kWh", leastBad.Score);
                }

                return stringBuilder.ToString();
            }

            stringBuilder.Append("Retractable Awning");

            IShadingTypology typology = device?.Typology;
            if (typology != null)
            {
                stringBuilder.AppendFormat(CultureInfo.InvariantCulture,
                    " | Projection {0:0.##} m, TiltDegrees {1:0.#}°, MountingOffset {2:0.##} m, ValanceDepth {3:0.##} m, width {4:0.##} m",
                    typology.GetParameter("Projection"), typology.GetParameter("TiltDegrees"),
                    typology.GetParameter("MountingOffset"), typology.GetParameter("ValanceDepth"), device.Width(group));
            }

            AppendPercentage(stringBuilder, performance.UnwantedSolarBlocked, "unwanted blocked");
            AppendPercentage(stringBuilder, performance.WantedSolarRetained, "wanted retained");
            AppendEnergy(stringBuilder, performance.UnwantedSolarIntercepted, "unwanted solar intercepted");
            AppendEnergy(stringBuilder, performance.WantedSolarBlocked, "wanted solar blocked");

            return stringBuilder.ToString();
        }

        private static void AppendPercentage(StringBuilder stringBuilder, double ratio, string label)
        {
            stringBuilder.Append(" | ");
            if (double.IsNaN(ratio))
            {
                stringBuilder.Append("n/a ").Append(label);
                return;
            }

            stringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#}% {1}", 100.0 * ratio, label);
        }

        private static void AppendEnergy(StringBuilder stringBuilder, double kWh, string label)
        {
            stringBuilder.Append(" | ");
            if (double.IsNaN(kWh))
            {
                stringBuilder.Append("n/a ").Append(label);
                return;
            }

            stringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} kWh {1}", kWh, label);
        }

        /// <summary>The existing total order: score, then lower material, then the lexicographically smaller parameter vector.</summary>
        private static bool AwningIsBetter(AwningCandidate candidate, AwningCandidate incumbent)
        {
            if (candidate == null || !candidate.Measurable || double.IsNaN(candidate.Score))
            {
                return false;
            }

            if (incumbent == null || !incumbent.Measurable || double.IsNaN(incumbent.Score))
            {
                return true;
            }

            double tolerance = 1e-12 * Math.Max(1.0, Math.Max(Math.Abs(candidate.Score), Math.Abs(incumbent.Score)));
            if (candidate.Score > incumbent.Score + tolerance) { return true; }
            if (candidate.Score < incumbent.Score - tolerance) { return false; }

            double candidateCost = double.IsNaN(candidate.MaterialFraction) ? double.PositiveInfinity : candidate.MaterialFraction;
            double incumbentCost = double.IsNaN(incumbent.MaterialFraction) ? double.PositiveInfinity : incumbent.MaterialFraction;
            if (candidateCost < incumbentCost) { return true; }
            if (candidateCost > incumbentCost) { return false; }

            if (candidate.Projection < incumbent.Projection) { return true; }
            if (candidate.Projection > incumbent.Projection) { return false; }

            if (candidate.TiltDegrees < incumbent.TiltDegrees) { return true; }
            if (candidate.TiltDegrees > incumbent.TiltDegrees) { return false; }

            if (candidate.ValanceDepth < incumbent.ValanceDepth) { return true; }
            if (candidate.ValanceDepth > incumbent.ValanceDepth) { return false; }

            return false;
        }

        /// <summary>An EXACT total order for ranking, so List.Sort stays transitive and the list reads best-first.</summary>
        private static int AwningCompareForRanking(AwningCandidate x, AwningCandidate y)
        {
            if (ReferenceEquals(x, y)) { return 0; }
            if (x == null) { return y == null ? 0 : 1; }
            if (y == null) { return -1; }

            // NaN is mapped to the worst end of every key BEFORE comparing. A NaN left in place makes
            // both < and > false, so the comparer would report "equal" for pairs that are not, and
            // List.Sort detects the inconsistency by throwing rather than by sorting badly.
            int compare = Compare(Rank(x.Score), Rank(y.Score));
            if (compare != 0) { return -compare; } // higher score first

            compare = Compare(Cost(x.MaterialFraction), Cost(y.MaterialFraction));
            if (compare != 0) { return compare; }

            compare = Compare(Cost(x.Projection), Cost(y.Projection));
            if (compare != 0) { return compare; }

            compare = Compare(Cost(x.TiltDegrees), Cost(y.TiltDegrees));
            if (compare != 0) { return compare; }

            return Compare(Cost(x.ValanceDepth), Cost(y.ValanceDepth));
        }

        /// <summary>A score that sorts last when it does not exist.</summary>
        private static double Rank(double score)
        {
            return double.IsNaN(score) ? double.NegativeInfinity : score;
        }

        /// <summary>A tie-break key that sorts last when it does not exist.</summary>
        private static double Cost(double value)
        {
            return double.IsNaN(value) ? double.PositiveInfinity : value;
        }

        private static int Compare(double x, double y)
        {
            if (x < y) { return -1; }
            if (x > y) { return 1; }
            return 0;
        }

        /// <summary>One measured candidate: its parameters, its group performance and its score.</summary>
        private class AwningCandidate
        {
            public double Projection;
            public double TiltDegrees;
            public double ValanceDepth;
            public double MountingOffset = double.NaN;
            public double Score = double.NaN;
            public double MaterialFraction = double.NaN;
            public bool Measurable;
            public GroupedShadingPerformance Performance;
            public List<ShadingElement> Elements;
            public double SharedDeviceArea = double.NaN;
        }

        /// <summary>
        /// Builds the shared device geometry ONCE per candidate and measures every member aperture
        /// against the same element set. Memoised by (projection, tilt, valance) so a revisited
        /// point costs nothing and can never return a different answer.
        /// </summary>
        private class AwningEvaluator
        {
            private readonly ApertureShadingGroup group;
            private readonly List<ApertureSolarTarget> targets;
            private readonly List<int> offsets;
            private readonly SolarVisibilityCache baseVisibilityCache;
            private readonly Dictionary<Guid, ApertureDesirability> desirabilityMap;
            private readonly List<LinkedFace3D> contextOccluders;
            private readonly ShadingObjective objective;
            private readonly AwningSpecification specification;
            private readonly double riseAboveHead;
            private readonly double extensionBeyondJambs;
            private readonly double mountingOffset;
            private readonly double maximumGap;
            private readonly double headTolerance;
            private readonly List<string> latticeWarnings;
            private readonly Dictionary<string, AwningCandidate> cache = new Dictionary<string, AwningCandidate>();
            private readonly List<AwningCandidate> allEvaluated = new List<AwningCandidate>();

            public AwningEvaluator(ApertureShadingGroup group, List<ApertureSolarTarget> targets, List<int> offsets, SolarVisibilityCache baseVisibilityCache, Dictionary<Guid, ApertureDesirability> desirabilityMap, List<LinkedFace3D> contextOccluders, ShadingObjective objective, AwningSpecification specification, double riseAboveHead, double extensionBeyondJambs, double mountingOffset, double maximumGap, double headTolerance, List<string> latticeWarnings = null)
            {
                this.latticeWarnings = latticeWarnings ?? new List<string>();
                this.group = group;
                this.targets = targets;
                this.offsets = offsets;
                this.baseVisibilityCache = baseVisibilityCache;
                this.desirabilityMap = desirabilityMap;
                this.contextOccluders = contextOccluders ?? new List<LinkedFace3D>();
                this.objective = objective;
                this.specification = specification;
                this.riseAboveHead = riseAboveHead;
                this.extensionBeyondJambs = extensionBeyondJambs;
                this.mountingOffset = mountingOffset;
                this.maximumGap = maximumGap;
                this.headTolerance = headTolerance;
            }

            public int Evaluations { get { return cache.Count; } }

            public GroupedShadingDevice NullDevice()
            {
                return new GroupedShadingDevice(group.GroupGuid, group.PanelGuid, group.ApertureGuids, new NoShading(), specification, maximumGap, headTolerance);
            }

            public GroupedShadingDevice Device(AwningCandidate candidate)
            {
                if (candidate == null || candidate.Elements == null)
                {
                    return NullDevice();
                }

                RetractableAwning awning = new RetractableAwning(candidate.Projection, candidate.TiltDegrees, riseAboveHead, extensionBeyondJambs, candidate.ValanceDepth, mountingOffset);
                return new GroupedShadingDevice(group.GroupGuid, group.PanelGuid, group.ApertureGuids, awning, specification, maximumGap, headTolerance);
            }

            /// <summary>
            /// EVERY measurable candidate the search evaluated, best first under the exact ranking
            /// order — not just the refined incumbents — so the winner can be checked against the
            /// full search record rather than taken on trust.
            /// </summary>
            public List<AwningSearchCandidate> Candidates()
            {
                List<AwningCandidate> ranked = new List<AwningCandidate>(allEvaluated);
                ranked.Sort(AwningCompareForRanking);

                List<AwningSearchCandidate> result = new List<AwningSearchCandidate>();
                foreach (AwningCandidate candidate in ranked)
                {
                    result.Add(new AwningSearchCandidate("RetractableAwning", candidate.Projection, candidate.TiltDegrees, candidate.ValanceDepth, candidate.Score, candidate.MaterialFraction, candidate.Measurable));
                }

                return result;
            }

            /// <summary>
            /// Warnings attached to the answer: a group whose width exceeds the product maximum has
            /// no valid candidate at all. The host-bound fit is the caller's to check — it has the
            /// model — and is appended to the result separately.
            /// </summary>
            public List<string> Warnings(double width)
            {
                // Lattice points the family bounds excluded come first: they explain why the search
                // space is narrower than the preset the caller named.
                List<string> result = new List<string>(latticeWarnings);

                if (width > specification.MaximumWidth * (1.0 + 1e-9))
                {
                    result.Add(string.Format(CultureInfo.InvariantCulture,
                        "The group width of {0:0.###} m exceeds the maximum {1} width of {2:0.#} m: no valid awning exists for this group. Split the apertures into separate units.",
                        width, specification.Name, specification.MaximumWidth));
                }

                return result;
            }

            public AwningCandidate Evaluate(double? projection, double tiltDegrees, double valanceDepth)
            {
                if (!projection.HasValue || double.IsNaN(tiltDegrees) || double.IsNaN(valanceDepth))
                {
                    // The null device: no geometry at all, scored against the same members.
                    return EvaluateElements(new List<ShadingElement>(), "NoShading", double.NaN, double.NaN, valanceDepth, 0.0);
                }

                return Evaluate(projection.Value, tiltDegrees, valanceDepth);
            }

            public AwningCandidate Evaluate(double projection, double tiltDegrees, double valanceDepth)
            {
                // BUILD FIRST, THEN READ THE PARAMETERS BACK OFF THE FAMILY. ShadingTypology.Define
                // clamps into the declared bounds, so the value asked for and the value the
                // geometry was actually built from are not always the same number. Everything
                // downstream — the memoisation key, the candidate record, the 1° tilt refinement
                // and the device rebuilt from the winner — uses the BUILT value, so the search can
                // never report a design it did not measure, nor measure one geometry twice under
                // two different names.
                RetractableAwning awning = new RetractableAwning(projection, tiltDegrees, riseAboveHead, extensionBeyondJambs, valanceDepth, mountingOffset);
                double builtProjection = awning.GetParameter("Projection");
                double builtTiltDegrees = awning.GetParameter("TiltDegrees");
                double builtValanceDepth = awning.GetParameter("ValanceDepth");
                double builtMountingOffset = awning.GetParameter("MountingOffset");

                string key = Key(builtProjection, builtTiltDegrees, builtValanceDepth, builtMountingOffset);
                if (cache.TryGetValue(key, out AwningCandidate cached))
                {
                    return cached;
                }

                List<ShadingElement> elements = awning.ShadingElements(group.Plane, group.MinX, group.MaxX, group.MaxY);
                if (elements == null)
                {
                    AwningCandidate failed = new AwningCandidate { Projection = builtProjection, TiltDegrees = builtTiltDegrees, ValanceDepth = builtValanceDepth, MountingOffset = builtMountingOffset };
                    cache[key] = failed;
                    return failed;
                }

                double deviceArea = 0;
                foreach (ShadingElement element in elements)
                {
                    double area = element.Area;
                    if (!double.IsNaN(area))
                    {
                        deviceArea += area;
                    }
                }

                AwningCandidate candidate = EvaluateElements(elements, "RetractableAwning", builtProjection, builtTiltDegrees, builtValanceDepth, deviceArea);
                cache[key] = candidate;
                if (candidate.Measurable)
                {
                    allEvaluated.Add(candidate);
                }

                return candidate;
            }

            private AwningCandidate EvaluateElements(List<ShadingElement> elements, string typologyName, double projection, double tiltDegrees, double valanceDepth, double deviceArea)
            {
                AwningCandidate result = new AwningCandidate
                {
                    Projection = projection,
                    TiltDegrees = tiltDegrees,
                    ValanceDepth = valanceDepth,
                    MountingOffset = mountingOffset,
                    Elements = elements,
                    SharedDeviceArea = deviceArea,
                };

                List<ShadingPerformance> performances = new List<ShadingPerformance>();
                for (int i = 0; i < targets.Count; i++)
                {
                    ApertureSolarTarget target = targets[i];
                    int offset = i < offsets.Count ? offsets[i] : -1;
                    if (target == null || offset < 0 || !desirabilityMap.TryGetValue(target.ApertureGuid, out ApertureDesirability desirability))
                    {
                        return result; // not measurable
                    }

                    ShadingPerformance performance = Create.ShadingPerformance(
                        target, baseVisibilityCache, desirability, contextOccluders, elements, typologyName, offset);

                    if (performance == null)
                    {
                        return result; // not measurable
                    }

                    performances.Add(performance);
                }

                GroupedShadingPerformance grouped = new GroupedShadingPerformance(
                    group.GroupGuid, group.PanelGuid, typologyName, performances, deviceArea, group.TotalGrossArea);

                result.Performance = grouped;
                result.MaterialFraction = grouped.MaterialFraction;
                result.Score = objective.Score(grouped);
                result.Measurable = true;
                return result;
            }

            private static string Key(double projection, double tiltDegrees, double valanceDepth, double mountingOffset)
            {
                return string.Concat(
                    projection.ToString("R", CultureInfo.InvariantCulture), "|",
                    tiltDegrees.ToString("R", CultureInfo.InvariantCulture), "|",
                    valanceDepth.ToString("R", CultureInfo.InvariantCulture), "|",
                    mountingOffset.ToString("R", CultureInfo.InvariantCulture));
            }
        }
    }
}

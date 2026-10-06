// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// THE ACTIVE-BIN RULE for candidate scoring: the sun bins whose Benefit/Harm contribution
        /// can be non-zero under the existing accounting, as a per-bin mask aligned with the
        /// visibility cache's bins.
        ///
        /// The rule is the literal complement of the skip gate in ShadingPerformance's accounting
        /// loop, intersected with "carries wanted or unwanted energy":
        ///
        ///   active[b] = (unwanted[b] != 0 || wanted[b] != 0)
        ///            && !(|direct[b]| + |unwanted[b]| + |wanted[b]| &lt; 1e-12)
        ///
        /// WHY THE GATE IS PART OF THE RULE, AND WHY IT IS THE LOOP'S OWN EXPRESSION. A bin with a
        /// sub-gate non-zero unwanted energy is skipped by the accounting today and contributes
        /// exactly zero; tracing and scoring it under a bare "unwanted != 0 || wanted != 0" rule
        /// would ADD a spurious contribution and change scores. A bin whose wanted and unwanted
        /// energies are both exactly zero contributes exactly 0.0 to both accumulators whatever the
        /// attribution says, so it may be left untraced with no effect. The gate is therefore not a
        /// tuning choice: excluding it from the mask breaks bit-identity with the full accounting.
        /// The complement form (!(x &lt; 1e-12) rather than x &gt;= 1e-12) is written to stay
        /// consistent with the loop even if an energy were NaN (they are not, by construction — see
        /// ApertureDesirability — but the mask must never diverge from the loop it mirrors).
        ///
        /// The mask is computed ONCE per aperture/desirability pair and reused for every candidate:
        /// the arrays it reads are brief-independent after Stage 5, so nothing here is recomputed
        /// per candidate.
        ///
        /// INTERNAL ON PURPOSE. The only production caller is the Stage-9 optimiser, in this
        /// assembly; no consumer outside it needs the mask. The rule's contract is still pinned by
        /// the public paths: the equivalence and gate-edge tests exercise the mask through the
        /// public attribution builder and the unpruned/full comparison. Promote it to public only
        /// when an outside caller genuinely wants it.
        /// </summary>
        /// <param name="desirability">Stage 5 per-group energies for this aperture.</param>
        /// <param name="binCount">The visibility cache's bin count the mask must align with.</param>
        /// <returns>A bool per bin, null when the inputs cannot produce one.</returns>
        internal static bool[] ActiveDesirabilityBins(ApertureDesirability desirability, int binCount)
        {
            if (desirability == null || binCount <= 0)
            {
                return null;
            }

            double[] direct = desirability.DirectEnergyPerGroup;
            double[] unwanted = desirability.UnwantedEnergyPerGroup;
            double[] wanted = desirability.WantedEnergyPerGroup;
            if (direct == null || unwanted == null || wanted == null
                || direct.Length != binCount || unwanted.Length != binCount || wanted.Length != binCount)
            {
                return null;
            }

            bool[] result = new bool[binCount];
            for (int b = 0; b < binCount; b++)
            {
                result[b] = (unwanted[b] != 0 || wanted[b] != 0)
                    && !(Math.Abs(direct[b]) + Math.Abs(unwanted[b]) + Math.Abs(wanted[b]) < 1e-12);
            }

            return result;
        }

        /// <summary>
        /// Stage 8: the energy performance of a set of shading elements against one aperture,
        /// attributed to the element the sun physically reaches FIRST.
        ///
        /// The accounting rule, and the reason the baseline cache is a required argument:
        ///
        ///   V_A = the rays admitted with existing context in place and NO candidate
        ///         (exactly the Stage 0-4 lit bits of baseVisibilityCache)
        ///
        /// Only rays in V_A can be credited. For each of them the attribution cache — built over
        /// context PLUS the candidate elements — says which face the sun hits first. If it is one
        /// of the candidate's elements, that element is credited with the ray's energy. If it is a
        /// context face, the baseline and the attribution disagree about the same geometry, which
        /// should be impossible; that energy goes to UnattributedInterceptedEnergy rather than
        /// being folded into a total, so the discrepancy is visible instead of hidden.
        ///
        /// Because attribution is first-hit, overlapping elements never double-count: a ray stopped
        /// by a louvre that would also have met the fin behind it credits the louvre alone, and the
        /// per-element sum plus the residual reconciles exactly with DirectSolarIntercepted.
        ///
        /// Units: Stage 5 energies are kWh/m2 per sun group, multiplied here by analysis-cell area,
        /// so every energy is kWh.
        ///
        /// THE TWO CACHES ARE INDEXED DIFFERENTLY, and this is the only place the pairing is made.
        /// The visibility cache normally spans the WHOLE model's apertures, so this target's cells
        /// start at cellIndexOffset within it. The attribution cache is built for this target alone
        /// and is indexed locally from zero. Reading both at the same index — which is what this did
        /// before — silently scored one aperture against another aperture's admitted beam whenever a
        /// model had more than one window.
        /// </summary>
        /// <param name="target">The aperture.</param>
        /// <param name="baseVisibilityCache">Visibility with CONTEXT ONLY: defines what the candidate may be credited for. Addressed at cellIndexOffset + c.</param>
        /// <param name="attributionCache">First hit over context PLUS the candidate elements, same sun groups, THIS target's cells. Addressed at c.</param>
        /// <param name="desirability">Stage 5 per-group energies for this aperture.</param>
        /// <param name="shadingElements">The candidate. Only these Guids may be credited.</param>
        /// <param name="typologyName">Provenance label.</param>
        /// <param name="materialFraction">Device area / aperture gross area.</param>
        /// <param name="cellIndexOffset">This target's first cell index within the visibility cache's shared cell space.</param>
        public static ShadingPerformance ShadingPerformance(this ApertureSolarTarget target, SolarVisibilityCache baseVisibilityCache, SolarAttributionCache attributionCache, ApertureDesirability desirability, IEnumerable<ShadingElement> shadingElements, string typologyName, double materialFraction, int cellIndexOffset = 0)
        {
            return ShadingPerformanceCore(target, baseVisibilityCache, attributionCache, desirability, shadingElements, typologyName, materialFraction, cellIndexOffset, null);
        }

        /// <summary>
        /// THE PRUNED SCORING PATH. Identical accounting to the public overload above except that
        /// attribution is consulted ONLY at active bins (see <see cref="ActiveDesirabilityBins"/>);
        /// the mask is passed through, never recomputed here.
        ///
        /// WHAT THE PRUNING DOES NOT TOUCH, and why the score is bit-identical to the full path:
        ///
        ///   - the per-bin skip gate and the bin iteration order are unchanged, so every addition the
        ///     full path performs for Benefit/Harm is performed here in the same order;
        ///   - a masked (inactive) bin contributes exactly 0.0 to UnwantedSolarIntercepted and
        ///     WantedSolarBlocked whatever the attribution says — that is what the mask's definition
        ///     guarantees — and 0.0 additions cannot move a non-negative accumulator, so skipping
        ///     those reads changes no bit;
        ///   - the ADMITTED sums are accumulated before the mask check, over exactly the bins and
        ///     cells the full path uses. AdmittedDirectEnergy (and with it the material-cost
        ///     reference) must NOT shrink: pruning the admitted accounting would silently change
        ///     Score through Cost, which is the one mistake this ordering exists to prevent.
        ///
        /// The intercepted totals and the per-element credits are therefore INCOMPLETE by design —
        /// they under-count the neutral direct beam the candidate would stop. This overload exists
        /// for SCORING ONLY. A performance object it returns must never be reported: the optimiser
        /// re-measures its winner through the full path before the result is built.
        /// </summary>
        internal static ShadingPerformance ShadingPerformance(this ApertureSolarTarget target, SolarVisibilityCache baseVisibilityCache, SolarAttributionCache attributionCache, ApertureDesirability desirability, IEnumerable<ShadingElement> shadingElements, string typologyName, double materialFraction, int cellIndexOffset, bool[] activeBins)
        {
            return ShadingPerformanceCore(target, baseVisibilityCache, attributionCache, desirability, shadingElements, typologyName, materialFraction, cellIndexOffset, activeBins);
        }

        private static ShadingPerformance ShadingPerformanceCore(ApertureSolarTarget target, SolarVisibilityCache baseVisibilityCache, SolarAttributionCache attributionCache, ApertureDesirability desirability, IEnumerable<ShadingElement> shadingElements, string typologyName, double materialFraction, int cellIndexOffset, bool[] activeBins)
        {
            List<AnalysisCell> cells = target?.AnalysisCells;
            if (cells == null || cells.Count == 0 || baseVisibilityCache == null || attributionCache == null || desirability == null)
            {
                return null;
            }

            // The target's window must lie inside the visibility cache, and the attribution cache
            // must cover exactly that window. Anything else and the two are not describing the same
            // samples, which would produce plausible numbers about the wrong aperture.
            if (cellIndexOffset < 0 || cellIndexOffset + cells.Count > baseVisibilityCache.CellCount)
            {
                return null;
            }

            if (attributionCache.CellCount != cells.Count)
            {
                return null;
            }

            List<SunBin> bins = baseVisibilityCache.Bins;
            double[] directGroups = desirability.DirectEnergyPerGroup;
            double[] unwantedGroups = desirability.UnwantedEnergyPerGroup;
            double[] wantedGroups = desirability.WantedEnergyPerGroup;
            if (bins == null || directGroups == null || unwantedGroups == null || wantedGroups == null
                || directGroups.Length != bins.Count || unwantedGroups.Length != bins.Count || wantedGroups.Length != bins.Count)
            {
                return null;
            }

            if (attributionCache.BinCount != bins.Count)
            {
                return null;
            }

            // The mask is indexed by bin in the loop below, so it must cover exactly the bins this
            // call accounts for. A mask of any other length is a wiring error, not a shorter brief:
            // treating a missing tail as inactive would silently drop the Benefit/Harm at bins the
            // attribution did trace, which is precisely the divergence pruning must never introduce.
            // Rejected here, with the method's other shape guards, rather than thrown at the index.
            if (activeBins != null && activeBins.Length != bins.Count)
            {
                return null;
            }

            // Only these Guids may receive credit. Anything else hit first is context.
            Dictionary<Guid, double> energyPerElement = new Dictionary<Guid, double>();
            Dictionary<Guid, string> namePerElement = new Dictionary<Guid, string>();
            HashSet<Guid> candidateGuids = new HashSet<Guid>();
            foreach (ShadingElement element in shadingElements ?? new List<ShadingElement>())
            {
                if (element == null)
                {
                    continue;
                }

                candidateGuids.Add(element.Guid);
                energyPerElement[element.Guid] = 0.0;
                namePerElement[element.Guid] = element.Name;
            }

            double admittedDirect = 0, admittedUnwanted = 0, admittedWanted = 0;
            double interceptedDirect = 0, interceptedUnwanted = 0, blockedWanted = 0;
            double unattributed = 0;

            int cellCount = cells.Count;
            for (int b = 0; b < bins.Count; b++)
            {
                double direct = directGroups[b];
                double unwanted = unwantedGroups[b];
                double wanted = wantedGroups[b];
                if (Math.Abs(direct) + Math.Abs(unwanted) + Math.Abs(wanted) < 1e-12)
                {
                    continue;
                }

                for (int c = 0; c < cellCount; c++)
                {
                    // Shared cell space for the baseline, local cell space for the attribution.
                    if (!baseVisibilityCache.IsLit(b, cellIndexOffset + c))
                    {
                        continue; // context already blocked it: not the candidate's to claim
                    }

                    double area = cells[c]?.Area ?? 0;
                    if (area <= 0)
                    {
                        continue;
                    }

                    admittedDirect += area * direct;
                    admittedUnwanted += area * unwanted;
                    admittedWanted += area * wanted;

                    // THE PRUNING POINT: admitted accounting above is never pruned; only the
                    // attribution reads below are skipped for inactive bins.
                    if (activeBins != null && !activeBins[b])
                    {
                        continue;
                    }

                    int firstHit = attributionCache.FirstHitIndex(b, c);
                    if (firstHit < 0)
                    {
                        continue; // still reaches the aperture
                    }

                    interceptedDirect += area * direct;
                    interceptedUnwanted += area * unwanted;
                    blockedWanted += area * wanted;

                    Guid guid = attributionCache.FirstHitGuid(b, c);
                    if (candidateGuids.Contains(guid))
                    {
                        energyPerElement[guid] += area * direct;
                    }
                    else
                    {
                        // A base-admitted ray stopped by something that is not the candidate.
                        unattributed += area * direct;
                    }
                }
            }

            return new ShadingPerformance(
                target.ApertureGuid, typologyName,
                admittedDirect, admittedUnwanted, admittedWanted,
                interceptedDirect, interceptedUnwanted, blockedWanted,
                unattributed, materialFraction, energyPerElement, namePerElement);
        }

        /// <summary>
        /// Convenience overload: builds the attribution cache for context plus the candidate and
        /// evaluates in one call. The element order is context first, then the candidate, so the
        /// occluder table is stable across candidates that share the same context.
        ///
        /// Attribution is built for THIS TARGET'S CELLS ONLY — the window starting at
        /// cellIndexOffset — while the occluder set stays the whole model. Scoping the samples is
        /// what makes analysing one window of a 2 000-window project affordable; scoping the
        /// occluders would silently delete the rest of the building from the sun path, so it is not
        /// done here and must not be done anywhere.
        ///
        /// A device with NO elements is a legitimate candidate — it is the null device, "build
        /// nothing" — and is measured like any other: nothing is intercepted, and the admitted
        /// baseline is reported honestly.
        ///
        /// CONTEXT IS HONOURED THROUGH THE BASE VISIBILITY CACHE, NOT RE-TRACED. contextOccluders is
        /// still the physical surroundings and still decides everything — but it decides it once,
        /// when baseVisibilityCache was built, and this call does not trace it again.
        ///
        /// The identity that makes that exact: attribution is only ever consulted at samples the
        /// base cache reports as LIT, and "lit" means the same primitive, with the same tolerances
        /// and the same ray-start offset, already found no context face on that ray
        /// (Query.CellVisibility and Query.CellFirstHit are deliberate mirrors of each other). For
        /// those samples the first hit over context PLUS candidate is therefore either a candidate
        /// element or nothing — the context faces cannot be first, because they are not on the ray
        /// at all. Tracing them again can only reproduce an answer already paid for.
        ///
        /// It is also the difference between usable and unusable on a real project. Each attribution
        /// build projects every occluder onto a plane per sun group, and an optimiser evaluates
        /// dozens of candidates: on a 8 800-panel model, re-projecting the whole building for every
        /// candidate cost about 300 s for a single family on a single window. Nothing is removed
        /// from the physics — the building still shades the window, through the cache that measured
        /// it — and MultiApertureShadingTests asserts the two routes agree numerically rather than
        /// leaving the argument above to stand on its own.
        /// </summary>
        /// <param name="target">The aperture.</param>
        /// <param name="baseVisibilityCache">Visibility with CONTEXT ONLY: what the candidate may be credited for.</param>
        /// <param name="desirability">Stage 5 per-group energies for this aperture.</param>
        /// <param name="contextOccluders">The physical surroundings. Present for API symmetry and for callers that pass an unrelated base cache; the shading answer takes context from baseVisibilityCache.</param>
        /// <param name="typology">The candidate device.</param>
        /// <param name="cellIndexOffset">This target's first cell index within baseVisibilityCache. Use ApertureShadingSetup.CellIndexOffset; 0 when the cache covers this target alone.</param>
        public static ShadingPerformance ShadingPerformance(this ApertureSolarTarget target, SolarVisibilityCache baseVisibilityCache, ApertureDesirability desirability, List<LinkedFace3D> contextOccluders, IShadingTypology typology, int cellIndexOffset = 0)
        {
            List<ShadingElement> elements = typology?.ShadingElements(target);
            if (elements == null)
            {
                return null;
            }

            SolarAttributionCache attributionCache = CandidateAttributionCache(baseVisibilityCache, elements, target.AnalysisCells, cellIndexOffset);
            if (attributionCache == null)
            {
                return null;
            }

            return ShadingPerformance(target, baseVisibilityCache, attributionCache, desirability, elements, typology.Name, typology.MaterialFraction(target), cellIndexOffset);
        }

        /// <summary>
        /// The shared-elements path: measures an EXPLICIT element set against one aperture, without
        /// regenerating the device around it.
        ///
        /// This is what the grouped-awning analysis uses. A single physical awning spans several
        /// apertures, so its canopy is built ONCE from the group frame; every member aperture is
        /// then measured against that same element list (same Guids) through this overload, with
        /// its own cell window and shared-cache offset. Building the attribution per member is
        /// necessary — the cache addresses each member's cells separately — but the geometry is
        /// never duplicated or re-centred.
        ///
        /// The material fraction here is the per-member value (shared element area / THIS
        /// aperture's gross area); the group-level charge — shared area / total gross area — is
        /// computed once by GroupedShadingPerformance and must never be rebuilt by summing these.
        /// </summary>
        /// <param name="target">The member aperture.</param>
        /// <param name="baseVisibilityCache">Visibility with CONTEXT ONLY, spanning the whole model.</param>
        /// <param name="desirability">Stage 5 per-group energies for this aperture.</param>
        /// <param name="contextOccluders">Existing context (takes effect through baseVisibilityCache).</param>
        /// <param name="shadingElements">The shared device elements. Empty list = the null device, measured like any other.</param>
        /// <param name="typologyName">Provenance label.</param>
        /// <param name="cellIndexOffset">This target's first cell index within the shared cell space.</param>
        public static ShadingPerformance ShadingPerformance(this ApertureSolarTarget target, SolarVisibilityCache baseVisibilityCache, ApertureDesirability desirability, List<LinkedFace3D> contextOccluders, IEnumerable<ShadingElement> shadingElements, string typologyName, int cellIndexOffset = 0)
        {
            List<ShadingElement> elements = new List<ShadingElement>(shadingElements ?? new List<ShadingElement>());
            elements.RemoveAll(x => x == null);

            double elementArea = 0;
            foreach (ShadingElement element in elements)
            {
                double area = element.Area;
                if (!double.IsNaN(area))
                {
                    elementArea += area;
                }
            }

            double grossArea = target?.GrossArea ?? double.NaN;
            double materialFraction = double.IsNaN(grossArea) || grossArea <= 0 ? double.NaN : elementArea / grossArea;

            SolarAttributionCache attributionCache = CandidateAttributionCache(baseVisibilityCache, elements, target.AnalysisCells, cellIndexOffset);
            if (attributionCache == null)
            {
                return null;
            }

            return ShadingPerformance(target, baseVisibilityCache, attributionCache, desirability, elements, typologyName, materialFraction, cellIndexOffset);
        }

        /// <summary>
        /// First-hit attribution over the CANDIDATE'S faces alone, for the cell window belonging to
        /// one target. See the note above for why the context faces are not part of it.
        ///
        /// ONLY THE SAMPLES THE ACCOUNTING CAN READ ARE TRACED, and this is exact rather than an
        /// approximation. ShadingPerformance consults attribution strictly inside
        /// `if (baseVisibilityCache.IsLit(b, cellIndexOffset + c))` — a sample that context already
        /// shades is not the candidate's to claim, so its first hit is never asked for. Tracing it
        /// produces a value nothing reads. Skipping it therefore cannot change any reported number;
        /// what it changes is how much work a candidate costs, and on an aperture whose unwanted
        /// solar arrives from a narrow part of the sky — a north window, or any window in heavy
        /// context — most (sun group, sample) pairs are in that category.
        ///
        /// Measured on the MultiAzimuth fixture: 10.8 % of pairs are readable on the north window
        /// (a 9.2x reduction in rays traced) against 85.2 % on the south (1.2x). The saving is
        /// largest exactly where the old cost was least justified.
        /// </summary>
        internal static SolarAttributionCache CandidateAttributionCache(SolarVisibilityCache baseVisibilityCache, List<ShadingElement> elements, List<AnalysisCell> analysisCells, int cellIndexOffset, bool[] activeBins = null)
        {
            List<LinkedFace3D> occluders = new List<LinkedFace3D>();
            foreach (ShadingElement element in elements ?? new List<ShadingElement>())
            {
                LinkedFace3D linkedFace3D = element?.LinkedFace3D;
                if (linkedFace3D != null)
                {
                    occluders.Add(linkedFace3D);
                }
            }

            return Weather.SolarCalculator.Create.SolarAttributionCache(
                baseVisibilityCache, occluders, analysisCells, cellIndexOffset,
                Core.Tolerance.MacroDistance, Core.Tolerance.MacroDistance, Core.Tolerance.Angle, Core.Tolerance.Distance,
                true, activeBins);
        }
    }
}

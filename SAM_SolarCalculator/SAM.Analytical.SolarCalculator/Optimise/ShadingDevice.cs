// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Geometry.Object.Spatial;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Optimise
    {
        /// <summary>Optimises a single overhang: depth, rise above the head, extension past the jambs.</summary>
        public static OptimisedShadingResult Overhang(this ApertureSolarTarget target, SolarVisibilityCache baseVisibilityCache, ApertureDesirability desirability, List<LinkedFace3D> contextOccluders, ShadingObjective objective = null, ShadingPotentialField field = null, int maximumEvaluations = 400, int cellIndexOffset = 0)
        {
            return ShadingTypology(target, baseVisibilityCache, desirability, contextOccluders, "Overhang", objective, null, Seed(field, target, "Overhang", objective), maximumEvaluations, 3, cellIndexOffset);
        }

        /// <summary>Optimises a horizontal louvre array: depth, blade count, blade tilt.</summary>
        public static OptimisedShadingResult HorizontalLouvres(this ApertureSolarTarget target, SolarVisibilityCache baseVisibilityCache, ApertureDesirability desirability, List<LinkedFace3D> contextOccluders, ShadingObjective objective = null, ShadingPotentialField field = null, int maximumEvaluations = 400, int cellIndexOffset = 0)
        {
            return ShadingTypology(target, baseVisibilityCache, desirability, contextOccluders, "HorizontalLouvres", objective, null, Seed(field, target, "HorizontalLouvres", objective), maximumEvaluations, 3, cellIndexOffset);
        }

        /// <summary>Optimises a vertical fin array: depth, fin count, fin tilt.</summary>
        public static OptimisedShadingResult VerticalFins(this ApertureSolarTarget target, SolarVisibilityCache baseVisibilityCache, ApertureDesirability desirability, List<LinkedFace3D> contextOccluders, ShadingObjective objective = null, ShadingPotentialField field = null, int maximumEvaluations = 400, int cellIndexOffset = 0)
        {
            return ShadingTypology(target, baseVisibilityCache, desirability, contextOccluders, "VerticalFins", objective, null, Seed(field, target, "VerticalFins", objective), maximumEvaluations, 3, cellIndexOffset);
        }

        /// <summary>Optimises an egg crate: depth, louvre count, fin count.</summary>
        public static OptimisedShadingResult EggCrate(this ApertureSolarTarget target, SolarVisibilityCache baseVisibilityCache, ApertureDesirability desirability, List<LinkedFace3D> contextOccluders, ShadingObjective objective = null, ShadingPotentialField field = null, int maximumEvaluations = 400, int cellIndexOffset = 0)
        {
            return ShadingTypology(target, baseVisibilityCache, desirability, contextOccluders, "EggCrate", objective, null, Seed(field, target, "EggCrate", objective), maximumEvaluations, 3, cellIndexOffset);
        }

        /// <summary>
        /// Optimises a retractable folding-arm awning (deployed): projection, deployed tilt, rise
        /// above the head, extension past the jambs and valance depth.
        ///
        /// Projection is the horizontal reach, so the Stage 6 seed — an outward horizontal distance
        /// in the aperture frame — applies directly, without trigonometric conversion. The tilt is a
        /// free search variable within the product range, selected by the analysis; it is the fixed
        /// installation setting of the deployed fabric, not an hourly tracking angle.
        /// </summary>
        public static OptimisedShadingResult RetractableAwning(this ApertureSolarTarget target, SolarVisibilityCache baseVisibilityCache, ApertureDesirability desirability, List<LinkedFace3D> contextOccluders, ShadingObjective objective = null, ShadingPotentialField field = null, int maximumEvaluations = 400, int cellIndexOffset = 0)
        {
            return ShadingTypology(target, baseVisibilityCache, desirability, contextOccluders, "RetractableAwning", objective, null, Seed(field, target, "RetractableAwning", objective), maximumEvaluations, 3, cellIndexOffset);
        }

        /// <summary>
        /// Optimises every ELIGIBLE family for the aperture and returns them best first.
        ///
        /// Eligibility comes from Query.EligibleShadingTypologies, which decides from where the
        /// unwanted solar actually arrives rather than from the facade's compass bearing. Families
        /// that cannot act on this aperture's problem are not run at all: a losing score from a
        /// device that is physically incapable of helping reads like evidence and is not.
        ///
        /// The ordering is by the same total order the search itself uses — score, then less
        /// material, then lexicographically smaller parameters — so the ranking is reproducible and
        /// a tie between two families is broken the same way every time.
        /// </summary>
        /// <param name="target">The aperture.</param>
        /// <param name="baseVisibilityCache">Visibility with CONTEXT ONLY.</param>
        /// <param name="desirability">Stage 5 per-group energies for this aperture.</param>
        /// <param name="contextOccluders">Existing context.</param>
        /// <param name="objective">The objective. Null for the Stage 9 default.</param>
        /// <param name="field">The Stage 6 map, for the seeded depth. Optional.</param>
        /// <param name="typologyNames">Families to consider. Null means the eligible ones.</param>
        /// <param name="maximumEvaluations">Hard budget on distinct candidate evaluations per family.</param>
        /// <param name="cellIndexOffset">This target's first cell index within baseVisibilityCache when the cache spans the whole model.</param>
        /// <returns>Results best first. EMPTY when nothing is eligible, which is itself an answer.</returns>
        public static List<OptimisedShadingResult> ShadingDevice(this ApertureSolarTarget target, SolarVisibilityCache baseVisibilityCache, ApertureDesirability desirability, List<LinkedFace3D> contextOccluders, ShadingObjective objective = null, ShadingPotentialField field = null, IEnumerable<string> typologyNames = null, int maximumEvaluations = 400, int cellIndexOffset = 0)
        {
            if (target == null || baseVisibilityCache == null || desirability == null)
            {
                return null;
            }

            List<string> names;
            if (typologyNames != null)
            {
                names = new List<string>(typologyNames);
            }
            else
            {
                names = Query.EligibleShadingTypologies(target, baseVisibilityCache, desirability, out double _, out double _);
                if (names == null)
                {
                    return null;
                }
            }

            List<OptimisedShadingResult> result = new List<OptimisedShadingResult>();
            foreach (string name in names)
            {
                OptimisedShadingResult optimised = ShadingTypology(
                    target, baseVisibilityCache, desirability, contextOccluders, name,
                    objective, null, Seed(field, target, name, objective), maximumEvaluations, 3, cellIndexOffset);

                if (optimised != null)
                {
                    result.Add(optimised);
                }
            }

            // Descending by the search's own total order. A stable insertion sort keeps the
            // comparison logic in one place and the ordering reproducible.
            result.Sort(Compare);
            return result;
        }

        /// <summary>Descending by score, then ascending material, then ascending parameters.</summary>
        private static int Compare(OptimisedShadingResult x, OptimisedShadingResult y)
        {
            if (x == null || y == null)
            {
                return x == null ? (y == null ? 0 : 1) : -1;
            }

            double xScore = double.IsNaN(x.ObjectiveScore) ? double.NegativeInfinity : x.ObjectiveScore;
            double yScore = double.IsNaN(y.ObjectiveScore) ? double.NegativeInfinity : y.ObjectiveScore;

            double tolerance = 1e-12 * Math.Max(1.0, Math.Max(Math.Abs(xScore), Math.Abs(yScore)));
            if (xScore > yScore + tolerance) { return -1; }
            if (xScore < yScore - tolerance) { return 1; }

            double xMaterial = double.IsNaN(x.MaterialFraction) ? double.PositiveInfinity : x.MaterialFraction;
            double yMaterial = double.IsNaN(y.MaterialFraction) ? double.PositiveInfinity : y.MaterialFraction;
            if (xMaterial != yMaterial) { return xMaterial.CompareTo(yMaterial); }

            return string.CompareOrdinal(x.TypologyName, y.TypologyName);
        }

        /// <summary>
        /// A physically meaningful starting device from the Stage 6 field, or null when the field
        /// gives no crossing.
        ///
        /// The field's zero crossing along the row just above the head is the profile-angle
        /// construction D = H / tan(VSA) read off the actual climate and brief instead of assumed,
        /// so it puts the search down near a real device rather than at an arbitrary default. It is
        /// only a STARTING point: the coarse lattice covers the whole bounded range regardless, and
        /// the seed wins only if it scores best.
        /// </summary>
        private static IShadingTypology Seed(ShadingPotentialField field, ApertureSolarTarget target, string typologyName, ShadingObjective objective)
        {
            if (field == null)
            {
                return null;
            }

            double wantedSolarPenalty = objective == null ? 1.0 : objective.WantedSolarPenalty;
            double depth = Create.SeedOverhangDepth(field, target, wantedSolarPenalty);
            if (double.IsNaN(depth) || depth <= 0)
            {
                return null;
            }

            IShadingTypology result = Create.ShadingTypology(typologyName);

            // The awning's horizontal reach is its Projection, not a Depth: writing the seed into a
            // Depth parameter the family does not have would silently drop it.
            result?.SetParameter(typologyName == "RetractableAwning" ? "Projection" : "Depth", depth);
            return result;
        }
    }
}

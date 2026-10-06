// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Geometry.Object.Spatial;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// The Stage 8 fit score of one evaluated candidate.
        ///
        ///   Score = UnwantedSolarIntercepted
        ///         - wantedSolarPenalty  x WantedSolarBlocked
        ///         - materialPenalty     x MaterialFraction x AdmittedUnwantedEnergy
        ///
        /// Note the SIGNS, which are the easiest thing in this whole stage to get backwards. All
        /// three terms are POSITIVE quantities. UnwantedSolarIntercepted is the benefit and is
        /// added. WantedSolarBlocked is the wanted beam the device destroys — a loss — and is
        /// SUBTRACTED; adding it would reward a device for wrecking the winter sun. MaterialFraction
        /// is a cost and is subtracted, scaled by the admitted unwanted energy so the penalty is in
        /// kWh and comparable to the other two terms rather than being an arbitrary mix of units.
        ///
        /// The names say what the quantities are rather than which way they point, which is why
        /// they are WantedSolarBlocked and wantedSolarPenalty and not "harm".
        /// </summary>
        public static double ShadingFitScore(this ShadingPerformance performance, double wantedSolarPenalty = 1.0, double materialPenalty = 0.1)
        {
            if (performance == null)
            {
                return double.NaN;
            }

            double materialCost = performance.MaterialFraction;
            if (double.IsNaN(materialCost))
            {
                materialCost = 0;
            }

            return performance.UnwantedSolarIntercepted
                 - wantedSolarPenalty * performance.WantedSolarBlocked
                 - materialPenalty * materialCost * performance.AdmittedUnwantedEnergy;
        }

        /// <summary>
        /// The analytically seeded overhang depth: the depth at which the Stage 6 field's benefit
        /// along the voxel row just above the head crosses from positive to negative.
        ///
        /// This is the profile-angle construction D = H / tan(VSA_cut) read off the field instead of
        /// assumed: beyond that depth an overhang starts intercepting more wanted than unwanted
        /// beam, so extending it costs more than it earns. Seeding from the field rather than from
        /// a hard-coded dimension is what makes the rationalisation answer the actual climate,
        /// orientation and weighting rather than a rule of thumb.
        ///
        /// NaN when the field never crosses zero along that row — the caller must then fall back on
        /// a parameter sweep rather than pretend a seed exists.
        /// </summary>
        public static double SeedOverhangDepth(this ShadingPotentialField field, ApertureSolarTarget target, double wantedSolarPenalty = 1.0)
        {
            ShadingVolume volume = field?.Volume;
            Geometry.Spatial.Plane plane = target?.Plane;
            if (volume == null || plane == null)
            {
                return double.NaN;
            }

            if (!Query.TryGetApertureLocalBounds(target, out double _, out double _, out double _, out double maxY))
            {
                return double.NaN;
            }

            double voxelSize = volume.VoxelSize;
            double yRow = maxY + 0.5 * voxelSize;

            double previous = double.NaN;
            for (int k = 0; k < volume.CountZ; k++)
            {
                double best = double.MaxValue;
                int bestIndex = -1;
                for (int i = 0; i < volume.CountX; i++)
                {
                    for (int j = 0; j < volume.CountY; j++)
                    {
                        int index = volume.VoxelIndex(i, j, k);
                        if (!Query.TryGetApertureLocalCentre(target, volume, index, out double x, out double y, out double _))
                        {
                            continue;
                        }

                        double distance = Math.Abs(x) + Math.Abs(y - yRow);
                        if (distance < best)
                        {
                            best = distance;
                            bestIndex = index;
                        }
                    }
                }

                if (bestIndex < 0)
                {
                    continue;
                }

                double score = field.Score(bestIndex, wantedSolarPenalty);
                if (previous > 0 && score <= 0)
                {
                    return (k + 0.5) * voxelSize;
                }

                previous = score;
            }

            return double.NaN;
        }

        /// <summary>
        /// Rationalises a Stage 6/7 result into a buildable device: evaluates a bounded set of
        /// candidates of the given typology and returns the best-scoring one with its performance.
        ///
        /// The depth sweep is SEEDED from the field's own zero crossing where one exists, then
        /// sampled around it, rather than sweeping the whole bound blindly. Nothing here is a
        /// hard-coded dimension: the seed comes from the physics and the sweep is expressed as
        /// multiples of it.
        ///
        /// This is deliberately NOT Stage 9. It is a small, ordered, fully deterministic candidate
        /// evaluation — enough to produce a credible practical device and an honest ideal-versus-
        /// rationalised comparison, not a general optimiser.
        /// </summary>
        /// <param name="field">The Stage 6 map, for the seeded depth.</param>
        /// <param name="target">The aperture.</param>
        /// <param name="baseVisibilityCache">Visibility with CONTEXT ONLY.</param>
        /// <param name="desirability">Stage 5 per-group energies for this aperture.</param>
        /// <param name="contextOccluders">Existing context.</param>
        /// <param name="typologyName">Family to sweep.</param>
        /// <param name="performance">Performance of the winning candidate.</param>
        /// <param name="wantedSolarPenalty">Importance of preserving wanted solar, relative to blocking unwanted solar.</param>
        /// <param name="materialPenalty">Reluctance to buy device area for a small further gain.</param>
        /// <param name="cellIndexOffset">This target's first cell index within baseVisibilityCache when the cache spans the whole model.</param>
        public static IShadingTypology RationalisedShading(this ShadingPotentialField field, ApertureSolarTarget target, SolarVisibilityCache baseVisibilityCache, ApertureDesirability desirability, List<LinkedFace3D> contextOccluders, string typologyName, out ShadingPerformance performance, double wantedSolarPenalty = 1.0, double materialPenalty = 0.1, int cellIndexOffset = 0)
        {
            performance = null;
            if (field == null || target == null || baseVisibilityCache == null || desirability == null)
            {
                return null;
            }

            if (typologyName == "RetractableAwning")
            {
                // The awning's horizontal reach is a PROJECTION, not a depth, and its allowed values
                // are the product lattice — the seeded-depth machinery below does not apply.
                return RationalisedAwningSweep(target, baseVisibilityCache, desirability, contextOccluders, out performance, wantedSolarPenalty, materialPenalty, cellIndexOffset);
            }

            double seed = SeedOverhangDepth(field, target, wantedSolarPenalty);
            List<double> depths = new List<double>();
            if (double.IsNaN(seed) || seed <= 0)
            {
                // No crossing: fall back on a fixed, documented sweep rather than inventing a seed.
                depths.AddRange(new double[] { 0.15, 0.3, 0.45, 0.6, 0.9, 1.2 });
            }
            else
            {
                foreach (double multiple in new double[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 })
                {
                    depths.Add(seed * multiple);
                }
            }

            IShadingTypology best = null;
            double bestScore = double.NegativeInfinity;

            foreach (double depth in depths)
            {
                foreach (IShadingTypology candidate in Candidates(typologyName, depth))
                {
                    ShadingPerformance candidatePerformance = ShadingPerformance(target, baseVisibilityCache, desirability, contextOccluders, candidate, cellIndexOffset);
                    if (candidatePerformance == null)
                    {
                        continue;
                    }

                    double score = ShadingFitScore(candidatePerformance, wantedSolarPenalty, materialPenalty);
                    if (double.IsNaN(score) || score <= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    best = candidate;
                    performance = candidatePerformance;
                }
            }

            return best;
        }

        /// <summary>
        /// The quick candidate set for a retractable awning: the Dakar projection lattice crossed
        /// with the 5° tilt lattice, rise/extension/valance fixed at zero. The full product-
        /// constrained search lives in Optimise.AwningGroup; this keeps the seeded-sweep component
        /// path working for the new family.
        /// </summary>
        private static IShadingTypology RationalisedAwningSweep(ApertureSolarTarget target, SolarVisibilityCache baseVisibilityCache, ApertureDesirability desirability, List<LinkedFace3D> contextOccluders, out ShadingPerformance performance, double wantedSolarPenalty, double materialPenalty, int cellIndexOffset)
        {
            performance = null;

            IShadingTypology best = null;
            double bestScore = double.NegativeInfinity;

            foreach (double projection in AwningSpecification.Dakar.AllowedProjections)
            {
                for (double tilt = AwningSpecification.Dakar.MinimumTiltDegrees; tilt <= AwningSpecification.Dakar.MaximumTiltDegrees + 1e-9; tilt += 5.0)
                {
                    IShadingTypology candidate = new RetractableAwning(projection, tilt, 0.0, 0.0, 0.0);
                    ShadingPerformance candidatePerformance = ShadingPerformance(target, baseVisibilityCache, desirability, contextOccluders, candidate, cellIndexOffset);
                    if (candidatePerformance == null)
                    {
                        continue;
                    }

                    double score = ShadingFitScore(candidatePerformance, wantedSolarPenalty, materialPenalty);
                    if (double.IsNaN(score) || score <= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    best = candidate;
                    performance = candidatePerformance;
                }
            }

            return best;
        }

        /// <summary>The candidate set of one family at one depth, in a fixed order.</summary>
        private static List<IShadingTypology> Candidates(string typologyName, double depth)
        {
            List<IShadingTypology> result = new List<IShadingTypology>();

            if (typologyName == "Overhang")
            {
                result.Add(new Overhang(depth, 0.0, 0.0));
                result.Add(new Overhang(depth, 0.0, 0.2));
            }
            else if (typologyName == "HorizontalLouvres")
            {
                result.Add(new HorizontalLouvres(depth, 3, 0.0));
                result.Add(new HorizontalLouvres(depth, 5, 0.0));
                result.Add(new HorizontalLouvres(depth, 5, 20.0));
            }
            else if (typologyName == "VerticalFins")
            {
                result.Add(new VerticalFins(depth, 3, 0.0));
                result.Add(new VerticalFins(depth, 5, 0.0));
                result.Add(new VerticalFins(depth, 5, 20.0));
            }
            else if (typologyName == "EggCrate")
            {
                result.Add(new EggCrate(depth, 3, 3));
                result.Add(new EggCrate(depth, 4, 2));
            }

            return result;
        }
    }
}

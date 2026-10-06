// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// The design-grade analysis grid size, m: the coarsest grid at which the shading workflow's
        /// own validation evidence holds up.
        ///
        /// WHERE IT COMES FROM. It is an EMPIRICAL, design-grade guidance value, not an established
        /// universal physical constant and not a production constant of the geometry engine. It
        /// combines the Gate-4 / design-resolution evidence (measured shading-blocked error is
        /// acceptable at 0.25 m and grows materially at 0.5 m and 1.0 m) with the Kołobrzeg office
        /// convergence study, where a 0.60 x 1.39 m aperture recommended a very thin single vertical
        /// fin with ~88 % blocking at the 0.5 m default and ~0 % on 0.3 / 0.25 / 0.2 m grids — a
        /// plausible-looking recommendation that was a coarse-grid sampling artefact.
        ///
        /// WHAT IT IS FOR. <see cref="RecommendedGridSize(IEnumerable{ApertureSolarTarget}, double)"/>
        /// uses it as the ceiling of its recommendation. It is a resolution GUIDANCE figure, and it
        /// must not be described as the grid at which a shading solution is guaranteed to converge:
        /// the same study that supports 0.25 m also showed that one universal correct grid does not
        /// exist and that geometry alone cannot guarantee convergence. Convergence can only be
        /// confirmed by repeating an analysis at a finer grid and comparing.
        ///
        /// INTERNAL, DELIBERATELY. Because the value is revisable as validation evidence grows, it
        /// is not part of the public API and is not a C# <c>const</c> exposed for consumers to
        /// inline. The public behaviour is the recommendation itself, via
        /// <see cref="RecommendedGridSize(IEnumerable{ApertureSolarTarget}, double)"/>, not this
        /// implementation constant. (The test assembly reads it through friend access.)
        /// </summary>
        internal const double DesignGradeGridSize = 0.25;

        /// <summary>
        /// The recommended analysis grid size for a SELECTED APERTURE SET, m, based on aperture
        /// geometric representability and the current design-grade resolution guidance.
        ///
        ///   per aperture:  geometricRecommendation = shortestLocalDimension / 2
        ///   set:           recommendation = min(DesignGradeGridSize, smallest geometricRecommendation)
        ///
        /// clamped up to <see cref="MinimumGridSize(double)"/> when the apertures are so small that
        /// the geometric half-width would fall below the smallest grid any sample cell can exist on.
        ///
        /// The set recommendation is deliberately ONE number, not one per aperture: the current
        /// solar context and cache architecture is single-grid, and a shared grid must remain valid
        /// for the whole calculation, so the SMALLEST (governing) aperture decides.
        ///
        /// THIS IS GUIDANCE, NOT A REQUIREMENT. The recommendation never replaces or alters the
        /// engineer's supplied grid size — the supplied grid remains authoritative and the
        /// calculation continues with it. And it is not a convergence guarantee: it says nothing
        /// about what the shading solution would do on any particular grid, only what resolution
        /// the aperture geometry and the design-grade guidance suggest. Convergence can only be
        /// confirmed by repeating the analysis at a finer grid and comparing the results.
        ///
        /// The shortest dimension is the aperture's extent in its own plane — the smaller of the
        /// across-facade and up-slope spans of the local planar bounding box, via
        /// <see cref="TryGetApertureLocalBounds"/> — the same smallest robust geometry mechanism the
        /// resolution and optimisation rules already use. Apertures that expose no measurable
        /// extent are skipped; if none remain the recommendation is NaN.
        /// </summary>
        /// <param name="apertureSolarTargets">The selected apertures, as produced by ApertureSolarTargets.</param>
        /// <param name="tolerance_Area">Area tolerance the minimum grid size is derived from, m2.</param>
        /// <returns>Recommended grid size, m. NaN when there is no measurable aperture.</returns>
        public static double RecommendedGridSize(this IEnumerable<ApertureSolarTarget> apertureSolarTargets, double tolerance_Area = Core.Tolerance.MacroDistance)
        {
            if (apertureSolarTargets == null)
            {
                return double.NaN;
            }

            double smallestGeometric = double.NaN;
            foreach (ApertureSolarTarget target in apertureSolarTargets)
            {
                if (target == null || !Query.TryGetApertureLocalBounds(target, out double minX, out double maxX, out double minY, out double maxY))
                {
                    continue;
                }

                double spanAcross = maxX - minX;
                double spanUp = maxY - minY;
                if (double.IsNaN(spanAcross) || spanAcross <= 0 || double.IsNaN(spanUp) || spanUp <= 0)
                {
                    continue;
                }

                double geometric = Math.Min(spanAcross, spanUp) / 2.0;
                if (double.IsNaN(smallestGeometric) || geometric < smallestGeometric)
                {
                    smallestGeometric = geometric;
                }
            }

            if (double.IsNaN(smallestGeometric))
            {
                return double.NaN;
            }

            double result = Math.Min(DesignGradeGridSize, smallestGeometric);

            double minimum = MinimumGridSize(tolerance_Area);
            if (!double.IsNaN(minimum) && minimum > 0 && result < minimum)
            {
                result = minimum;
            }

            return result;
        }

        /// <summary>
        /// Whether a supplied analysis grid size is COARSER than a recommended grid size, and the
        /// sentence to show the engineer when it is.
        ///
        /// This is the warning half of the grid-resolution guidance: the recommendation itself never
        /// changes the calculation, so a run on a coarser grid must CONTINUE — and say, clearly,
        /// that its shading geometry may be under-resolved. The message deliberately does not say the
        /// result is wrong, and does not claim the recommended grid guarantees convergence: it says
        /// the result should be confirmed by refining the grid and comparing.
        ///
        /// The comparison is inclusive at the boundary, with the same relative nudge the resolution
        /// rules use: two arithmetic routes to the same length need not agree in the last bit, so a
        /// grid that merely equals the recommendation must not warn.
        /// </summary>
        /// <param name="gridSize">The supplied analysis grid size, m.</param>
        /// <param name="recommendedGridSize">The recommendation to compare against, m.</param>
        /// <param name="message">An actionable sentence, or null when the grid is at or finer than the recommendation.</param>
        public static bool CoarserThanRecommended(double gridSize, double recommendedGridSize, out string message)
        {
            message = null;

            if (double.IsNaN(gridSize) || double.IsNaN(recommendedGridSize) || gridSize <= 0 || recommendedGridSize <= 0)
            {
                return false;
            }

            if (gridSize <= recommendedGridSize * (1.0 + 1e-9))
            {
                return false;
            }

            message = string.Format(CultureInfo.InvariantCulture,
                "The selected analysis grid ({0:0.####} m) is coarser than the recommended grid size for the selected apertures ({1:0.####} m). Shading geometry may be under-resolved. This is guidance, not a requirement: the calculation continues at your grid size, and the recommendation does not guarantee convergence — confirm the result by re-running at a finer grid and comparing.",
                gridSize, recommendedGridSize);
            return true;
        }
    }
}

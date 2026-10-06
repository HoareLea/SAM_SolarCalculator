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
        /// Whether an optimised shading solution reached the ANALYSIS-GRID RESOLUTION CAP on its
        /// repeated-element count, and the sentence to show the engineer when it did.
        ///
        /// WHY THIS EXISTS, SEPARATELY FROM <see cref="ShadingResolution"/>. Stage 9 caps an
        /// optimised element count at what the analysis grid can resolve
        /// (<see cref="Create.ShadingParameters(IShadingTypology, double, ApertureSolarTarget, double)"/>,
        /// final maximum = min(typology-declared maximum, grid-resolution-derived maximum)). A winner
        /// that sits exactly on the FINAL maximum may have been stopped by the grid rather than by
        /// the design — or it may simply have reached the family's own maximum, which says nothing
        /// about resolution. Equality with the final maximum alone is therefore not enough. This
        /// warns ONLY when both hold:
        ///
        ///   1. the grid-resolution cap genuinely narrowed the typology's declared maximum, and
        ///   2. the winning element count equals that resolution-limited maximum.
        ///
        /// A winner that reaches an independent typology maximum carries no warning, and a winner
        /// below the narrowed cap carries none either — the search stopped short of the limit, so
        /// the grid did not decide the count.
        ///
        /// WHY THE RECORDED BOUNDS ARE THE SOURCE. The optimised result stores the parameter bounds
        /// the search was actually allowed (<see cref="OptimisedShadingResult.Bounds"/>), which are
        /// the capped ones by construction on the DEFAULT optimisation parameter path (an explicit
        /// caller-supplied parameter list bypasses the resolution cap). Comparing those against the
        /// typology's own declared bounds reuses the same numbers the search used, instead of
        /// recomputing the cap with a second, potentially inconsistent formula.
        /// </summary>
        /// <param name="optimisedShadingResult">The winning result of Optimise.ShadingDevice.</param>
        /// <param name="message">An actionable sentence, or null when there is nothing to report.</param>
        public static bool GridResolutionCapReached(this OptimisedShadingResult optimisedShadingResult, out string message)
        {
            message = null;

            if (optimisedShadingResult == null)
            {
                return false;
            }

            IShadingTypology typology = optimisedShadingResult.Typology();
            if (typology == null)
            {
                return false;
            }

            List<ShadingParameter> bounds = optimisedShadingResult.Bounds;
            if (bounds == null || bounds.Count == 0)
            {
                return false;
            }

            foreach (ShadingParameter bound in bounds)
            {
                string name = bound?.Name;
                if (name != "Count" && name != "LouvreCount" && name != "FinCount")
                {
                    continue;
                }

                if (!typology.TryGetBounds(name, out double _, out double declaredMaximum) || double.IsNaN(declaredMaximum))
                {
                    continue;
                }

                double cappedMaximum = bound.Maximum;

                // The cap did not narrow the family's own maximum, so reaching it is an ordinary
                // typology answer and says nothing about the grid.
                if (double.IsNaN(cappedMaximum) || cappedMaximum >= declaredMaximum - 1e-9)
                {
                    continue;
                }

                double winner = optimisedShadingResult.GetParameter(name);
                if (double.IsNaN(winner))
                {
                    continue;
                }

                // Counts are integers before any geometry is built; the nudge absorbs the two
                // arithmetic routes to the same count.
                if (Math.Abs(Math.Round(winner) - cappedMaximum) > 1e-9)
                {
                    continue;
                }

                message = string.Format(CultureInfo.InvariantCulture,
                    "The selected {0} solution reached the analysis-grid resolution limit ({1:0.####} m). The analysis grid may have limited the element count. Refine the grid and compare the result to confirm the recommendation.",
                    typology.Name, optimisedShadingResult.GridSize);
                return true;
            }

            return false;
        }

        /// <summary>
        /// The typology-level overload: whether a HAND-BUILT or scheme-carried device sits on the
        /// grid-resolution cap. The cap is recomputed from the typology's declared bounds and the
        /// Stage 9 search ranges (<see cref="Create.ShadingParameters"/>) instead of reading stored
        /// bounds — the same two conditions apply: the grid genuinely narrowed the family's maximum
        /// AND the device sits exactly on that narrowed maximum.
        /// </summary>
        public static bool GridResolutionCapReached(this IShadingTypology typology, ApertureSolarTarget target, double gridSize, out string message)
        {
            message = null;

            if (typology == null || target == null || double.IsNaN(gridSize) || gridSize <= 0)
            {
                return false;
            }

            List<ShadingParameter> parameters = Create.ShadingParameters(typology, double.NaN, target, gridSize);
            if (parameters == null || parameters.Count == 0)
            {
                return false;
            }

            foreach (ShadingParameter parameter in parameters)
            {
                string name = parameter?.Name;
                if (name != "Count" && name != "LouvreCount" && name != "FinCount")
                {
                    continue;
                }

                if (!typology.TryGetBounds(name, out double _, out double declaredMaximum) || double.IsNaN(declaredMaximum))
                {
                    continue;
                }

                double cappedMaximum = parameter.Maximum;
                if (double.IsNaN(cappedMaximum) || cappedMaximum >= declaredMaximum - 1e-9)
                {
                    continue;
                }

                double winner = typology.GetParameter(name);
                if (double.IsNaN(winner) || Math.Abs(Math.Round(winner) - cappedMaximum) > 1e-9)
                {
                    continue;
                }

                message = string.Format(CultureInfo.InvariantCulture,
                    "The selected {0} solution reached the analysis-grid resolution limit ({1:0.####} m). The analysis grid may have limited the element count. Refine the grid and compare the result to confirm the recommendation.",
                    typology.Name, gridSize);
                return true;
            }

            return false;
        }
    }
}

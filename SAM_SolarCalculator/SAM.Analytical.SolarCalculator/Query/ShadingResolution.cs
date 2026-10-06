// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>How a device's element spacing compares with the analysis grid it was measured on.</summary>
    public enum ShadingResolutionState
    {
        /// <summary>
        /// Nothing to say: either the device has no repeated elements, or its spacing is at or above
        /// the minimum feature size — <see cref="Create.MinimumElementPitchInGridSizes"/> cells.
        /// Every device Stage 9 proposes lands here, by construction.
        /// </summary>
        Resolved,

        /// <summary>
        /// Element spacing is below the minimum feature size but still wider than one cell: there is
        /// less than one sample per stripe period to spare, and the element-level numbers are near
        /// the limit of what the analysis can see.
        /// </summary>
        NearResolutionLimit,

        /// <summary>
        /// Element spacing is at or below one cell: there is at most ONE sample per stripe period, so
        /// the measured shaded fraction is decided by the phase between the device and the grid
        /// rather than by the device. The element-level numbers are not evidence.
        /// </summary>
        BelowResolutionLimit,
    }

    public static partial class Query
    {
        /// <summary>
        /// Whether a device's element spacing is something the analysis grid can actually see, and
        /// the sentence to show the engineer when it is not.
        ///
        /// Stage 9 found that this failure looks like a triumph: blades spaced more finely than the
        /// grid shade BETWEEN the sample points, and the measured result reads "100 % of unwanted
        /// solar blocked, 100 % of wanted solar retained" — a property of where the samples happen
        /// to fall, not of the device.
        ///
        /// THIS IS THE REPORTING SIDE OF ONE RULE, NOT A SECOND RULE. Stage 9's parameter cap
        /// (<see cref="Create.ShadingParameters(IShadingTypology, double, ApertureSolarTarget, double)"/>)
        /// keeps an OPTIMISED device at or above <see cref="Create.MinimumElementPitchInGridSizes"/>
        /// grid spacings, and this query warns below exactly that same figure. Stage 10.2 made the
        /// two agree: before it, the cap admitted pitch >= 1 x GridSize while this warned below
        /// 2 x GridSize, so an optimised device that rode its own cap — which it does on any aperture
        /// where more elements keep helping — was warned about every single time, and refining the
        /// grid never cleared the warning because the cap moved down with the grid. A warning here
        /// now means a device Stage 9 would not have proposed: a hand-typed one, or one measured on a
        /// coarser grid than it was designed against. Nothing was suppressed; the bound moved to
        /// where the evidence says it belongs.
        ///
        /// Element pitch is span / (count - 1) — the spacing the typologies actually build to — over
        /// the aperture's up-slope extent for louvres and its across-facade extent for fins. A
        /// single element has no pitch and is always resolved.
        /// </summary>
        /// <param name="typology">The device. Null returns Resolved with no message.</param>
        /// <param name="target">The aperture the device sits on, for its spans.</param>
        /// <param name="gridSize">The analysis grid size the numbers were produced at, m.</param>
        /// <param name="message">An actionable sentence, or null when there is nothing to report.</param>
        /// <param name="minimumPitch">The tightest element spacing found, m (NaN when there is none).</param>
        public static ShadingResolutionState ShadingResolution(this IShadingTypology typology, ApertureSolarTarget target, double gridSize, out string message, out double minimumPitch)
        {
            message = null;
            minimumPitch = double.NaN;

            if (typology == null || target == null || double.IsNaN(gridSize) || gridSize <= 0)
            {
                return ShadingResolutionState.Resolved;
            }

            if (!Query.TryGetApertureLocalBounds(target, out double minX, out double maxX, out double minY, out double maxY))
            {
                return ShadingResolutionState.Resolved;
            }

            double spanAcross = maxX - minX;
            double spanUp = maxY - minY;

            // Which counted parameter runs over which span. Louvre arrays stack UP the aperture;
            // fin arrays run ACROSS it. Anything else has no repeated element to space.
            List<Tuple<string, double>> counted = new List<Tuple<string, double>>();
            foreach (string name in typology.ParameterNames ?? new List<string>())
            {
                if (name == "Count")
                {
                    counted.Add(new Tuple<string, double>(name, typology.Name == "VerticalFins" ? spanAcross : spanUp));
                }
                else if (name == "LouvreCount")
                {
                    counted.Add(new Tuple<string, double>(name, spanUp));
                }
                else if (name == "FinCount")
                {
                    counted.Add(new Tuple<string, double>(name, spanAcross));
                }
            }

            foreach (Tuple<string, double> pair in counted)
            {
                double count = Math.Round(typology.GetParameter(pair.Item1));
                if (double.IsNaN(count) || count <= 1 || double.IsNaN(pair.Item2) || pair.Item2 <= 0)
                {
                    continue;
                }

                double pitch = pair.Item2 / (count - 1);
                if (double.IsNaN(minimumPitch) || pitch < minimumPitch)
                {
                    minimumPitch = pitch;
                }
            }

            if (double.IsNaN(minimumPitch))
            {
                return ShadingResolutionState.Resolved;
            }

            // A device sitting EXACTLY on the minimum feature size is inside the rule, so the
            // comparison is nudged by a relative epsilon: span / (count - 1) and
            // MinimumElementPitchInGridSizes x gridSize are two different arithmetic routes to the
            // same length, and they need not agree in the last bit.
            double minimumFeaturePitch = Create.MinimumElementPitchInGridSizes * gridSize;

            if (minimumPitch <= gridSize * (1.0 + 1e-9))
            {
                message = string.Format(
                    "The proposed shading spacing ({0:0.###} m) is at or below the analysis grid (GridSize {1:0.###} m), so there is at most one sample point per gap and the measured percentages are decided by where the samples happen to fall rather than by the device. Space the elements at least {2:0.###} m apart, or reduce GridSize to at most {3:0.###} m and recalculate, before trusting the element-level results.",
                    minimumPitch, gridSize, minimumFeaturePitch, minimumPitch / Create.MinimumElementPitchInGridSizes);
                return ShadingResolutionState.BelowResolutionLimit;
            }

            if (minimumPitch < minimumFeaturePitch * (1.0 - 1e-9))
            {
                message = string.Format(
                    "The proposed shading spacing ({0:0.###} m) is below the minimum feature size for this analysis ({1:0.###} m, i.e. {2:0.#} x GridSize {3:0.###} m). The element-level results are near the limit of what the analysis can see. Reduce GridSize to at most {4:0.###} m and recalculate to make them reliable.",
                    minimumPitch, minimumFeaturePitch, Create.MinimumElementPitchInGridSizes, gridSize, minimumPitch / Create.MinimumElementPitchInGridSizes);
                return ShadingResolutionState.NearResolutionLimit;
            }

            return ShadingResolutionState.Resolved;
        }
    }
}

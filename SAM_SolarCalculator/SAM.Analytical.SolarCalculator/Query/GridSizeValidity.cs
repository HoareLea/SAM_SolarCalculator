// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Globalization;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>Whether a requested analysis grid size can produce analysis samples at all.</summary>
    public enum GridSizeValidity
    {
        /// <summary>Usable. Whether a PARTICULAR aperture yields samples is a separate, geometric question.</summary>
        Valid,

        /// <summary>NaN or infinite. Not a length.</summary>
        NotANumber,

        /// <summary>Zero or negative. Not a length.</summary>
        NotPositive,

        /// <summary>
        /// Positive, but so fine that a full square sample cell would be smaller than the area
        /// tolerance the geometry engine works to — so EVERY cell is discarded, on every geometry.
        /// </summary>
        BelowAreaTolerance,
    }

    public static partial class Query
    {
        /// <summary>
        /// The smallest analysis grid size that can produce a sample cell at all, m.
        ///
        /// THIS IS ARITHMETIC, NOT AN OBSERVED FIGURE. Analysis cells are built by
        /// <see cref="Geometry.SolarCalculator.Query.AnalysisCells"/>, which clips a gridSize x
        /// gridSize rectangle against the aperture and DISCARDS any piece whose area falls below
        /// tolerance_Area. A cell in the interior of the opening is a full square of area
        /// gridSize^2, so the whole grid collapses to nothing the moment
        ///
        ///   gridSize^2 &lt; tolerance_Area      i.e.      gridSize &lt; sqrt(tolerance_Area)
        ///
        /// With SAM's default area tolerance of 1e-3 m^2 that limit is 0.0316 m — about 32 mm.
        /// Below it no aperture of any size or shape can produce a single sample, so the limit is
        /// universal and model-independent, and it is worth stating before any geometry is built.
        ///
        /// It is NOT the smallest USEFUL grid size. It is the point at which the request stops
        /// being answerable at all.
        /// </summary>
        /// <param name="tolerance_Area">Area tolerance the cells will be built to, m2.</param>
        public static double MinimumGridSize(double tolerance_Area = Core.Tolerance.MacroDistance)
        {
            return double.IsNaN(tolerance_Area) || tolerance_Area <= 0 ? 0.0 : Math.Sqrt(tolerance_Area);
        }

        /// <summary>
        /// Whether a requested grid size can produce analysis samples, and the sentence to show the
        /// engineer when it cannot.
        ///
        /// WHY THIS EXISTS. Before Stage 11 a too-fine grid size failed SILENTLY: every candidate
        /// cell fell below the area tolerance, every aperture was skipped for having no cells, the
        /// target list came back empty, and the solar context then returned null. The Grasshopper
        /// message that followed blamed the aperture — "check that it is an external sun-exposed
        /// aperture of THIS model" — which is both wrong and unactionable when the real cause is a
        /// number the user typed into a different input. A run that cannot be performed has to say
        /// so, and say why.
        ///
        /// The check is deliberately made BEFORE any cell generation: the answer is a property of
        /// the number and the tolerance alone, it costs nothing, and diagnosing it afterwards from
        /// an empty list cannot separate "this grid is impossible" from "this aperture is too
        /// small". Those are different faults with different fixes and they get different messages.
        /// </summary>
        /// <param name="gridSize">Requested aperture analysis-grid size, m.</param>
        /// <param name="message">An actionable sentence, or null when the grid size is usable.</param>
        /// <param name="tolerance_Area">Area tolerance the cells will be built to, m2.</param>
        public static GridSizeValidity GridSizeValidity(double gridSize, out string message, double tolerance_Area = Core.Tolerance.MacroDistance)
        {
            message = null;

            if (double.IsNaN(gridSize) || double.IsInfinity(gridSize))
            {
                message = "The analysis grid size is not a number. It is the SPACING between analysis sample points, in metres — a finite length greater than zero. The default is 0.5 m.";
                return SolarCalculator.GridSizeValidity.NotANumber;
            }

            if (gridSize <= 0)
            {
                message = string.Format(CultureInfo.InvariantCulture,
                    "The analysis grid size must be greater than zero (it is {0}). It is the SPACING between analysis sample points, in metres, not a count of them. The default is 0.5 m.",
                    gridSize);
                return SolarCalculator.GridSizeValidity.NotPositive;
            }

            double minimum = MinimumGridSize(tolerance_Area);
            if (gridSize < minimum)
            {
                message = string.Format(CultureInfo.InvariantCulture,
                    "An analysis grid size of {0:0.####} m is finer than this model's geometry tolerance can represent, so NO analysis samples can be produced and nothing can be calculated. A sample cell {0:0.####} m square has an area of {1:0.######} m2, which is below the {2:0.######} m2 area tolerance every cell is tested against, so every cell is discarded. Use {3:0.####} m or coarser. This limit is a property of the tolerance, not of the model — no aperture of any size or shape can be sampled below it.",
                    gridSize, gridSize * gridSize, tolerance_Area, minimum);

                return SolarCalculator.GridSizeValidity.BelowAreaTolerance;
            }

            return SolarCalculator.GridSizeValidity.Valid;
        }

        /// <summary>Convenience: true when the grid size can produce samples at all.</summary>
        public static bool IsValidGridSize(double gridSize, double tolerance_Area = Core.Tolerance.MacroDistance)
        {
            return GridSizeValidity(gridSize, out string _, tolerance_Area) == SolarCalculator.GridSizeValidity.Valid;
        }

        /// <summary>
        /// Why a SPECIFIC aperture produced no analysis samples on a grid size that is itself
        /// usable — the geometric half of the question, which can only be answered after trying.
        ///
        /// The distinction matters to the engineer. A grid size below
        /// <see cref="MinimumGridSize"/> is a bad number and is fixed by typing a different one. An
        /// aperture smaller than the area tolerance is a MODELLING problem — a 60 x 60 mm opening
        /// is 0.0036 m2 and can be sampled, a 20 x 20 mm one is 0.0004 m2 and can never be, at any
        /// grid size — and is fixed in the model, or by accepting that the opening is too small to
        /// analyse.
        /// </summary>
        /// <param name="apertureArea">The aperture's own area, m2.</param>
        /// <param name="gridSize">The grid size that produced no cells, m.</param>
        /// <param name="tolerance_Area">Area tolerance the cells were built to, m2.</param>
        public static string EmptyAnalysisCellsMessage(double apertureArea, double gridSize, double tolerance_Area = Core.Tolerance.MacroDistance)
        {
            if (!double.IsNaN(apertureArea) && apertureArea < tolerance_Area)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "This opening's area ({0:0.######} m2) is below the {1:0.######} m2 geometry tolerance, so it cannot be sampled at ANY analysis grid size and is not analysable. Check the opening in the model; if it is genuinely that small, it is too small for this analysis to say anything about.",
                    apertureArea, tolerance_Area);
            }

            return string.Format(CultureInfo.InvariantCulture,
                "This opening produced no analysis samples at a grid size of {0:0.####} m, although that grid size is usable in general. Every candidate sample cell clipped to less than the {1:0.######} m2 area tolerance, which happens when an opening is a narrow sliver or is much smaller than the grid. Try a finer grid size (at least {2:0.####} m is representable) or check the opening's geometry.",
                gridSize, tolerance_Area, MinimumGridSize(tolerance_Area));
        }
    }
}

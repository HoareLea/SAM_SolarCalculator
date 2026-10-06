// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>The buildable families Stage 9 can optimise, in a fixed reporting order.</summary>
        public static readonly string[] ShadingTypologyNames = new string[]
        {
            "Overhang",
            "HorizontalLouvres",
            "VerticalFins",
            "EggCrate",
            "RetractableAwning",
        };

        /// <summary>
        /// THE MINIMUM FEATURE-SIZE RULE: a repeated element array may not be spaced more finely
        /// than TWO analysis cells, and Stage 9 will not propose one that is.
        ///
        /// WHY A RULE IS NEEDED AT ALL. Performance is measured by asking, per analysis cell,
        /// whether that cell's single interior sample point is lit. A blade array of pitch p casts
        /// lit/shaded stripes of period p across the glass. The sample lattice has period g. The
        /// number of samples falling inside one stripe period is p / g, and THAT is the resolution
        /// with which the shaded fraction of each period can be estimated.
        ///
        /// WHY THE THRESHOLD IS TWO, and not a round number chosen for looking safe. At p = g there
        /// is exactly ONE sample per period, and — because the two lattices are then commensurate —
        /// it sits at the SAME relative position within every period. The estimator of the shaded
        /// fraction is therefore degenerate: it can return only 0 or 1 per period, and which one it
        /// returns is decided by the phase between the device and the grid rather than by the
        /// device. Two is simply the first ratio at which the estimator has any interior resolution
        /// at all. It is not an accuracy target; it is the point at which the measurement stops
        /// being a coin toss.
        ///
        /// MEASURED (MultiAzimuth fixture, fins of fixed 0.25 m pitch measured on a series of grids
        /// against a 0.04 m reference, phases 0 and half a pitch — ResolutionConvergenceTests):
        ///
        ///   pitch / GridSize   worst error in unwanted-solar-blocked
        ///   1.0                26.5 percentage points  (and 100.0 % reported where the truth is 97.7 %)
        ///   2.0                 1.9 percentage points
        ///
        /// The exactly-100 % reading at p = g is the failure mode Stage 9 named and did not fully
        /// prevent: it looks like a triumph and is an artefact. Note that the error does NOT fall
        /// monotonically with the ratio — 1.25, 1.5 and 3.0 all produce larger errors than 2.0 on
        /// this fixture — so no ratio makes element-level numbers reliable on its own, and the rule
        /// is justified by the degeneracy argument above rather than by a convergence plateau that
        /// the data does not show.
        ///
        /// WHY IT IS ALSO TWO ON THE REPORTING SIDE. Query.ShadingResolution warns below this same
        /// ratio. Before Stage 10.2 the optimiser was capped at pitch >= 1 x GridSize while the
        /// warning fired below 2 x GridSize, so on any aperture where more elements kept helping the
        /// search rode its own cap and the result ALWAYS carried a resolution warning — and refining
        /// the grid never cleared it, because the cap moved down with the grid. The two numbers are
        /// now the same number, so a result produced inside Stage 9's own declared reliable bounds
        /// comes back without a warning, and a warning again means what it says.
        /// </summary>
        public const double MinimumElementPitchInGridSizes = 2.0;

        /// <summary>
        /// Absorbs floating-point representation error when the cap is computed. Without it a span
        /// and grid that divide exactly in decimal (1.0 m at 0.1 m) can floor one element short,
        /// which would make the permitted count jitter with the arithmetic rather than with the
        /// geometry.
        /// </summary>
        private const double ResolutionCapTolerance = 1e-9;

        /// <summary>
        /// A default-parameterised typology by family name, or null for an unknown name.
        ///
        /// This is what lets an OptimisedShadingResult rebuild its winning device from a stored
        /// name and parameter list rather than carrying geometry, and what lets the multi-typology
        /// search enumerate families without a switch at every call site.
        /// </summary>
        public static IShadingTypology ShadingTypology(string typologyName)
        {
            switch (typologyName)
            {
                case "Overhang": return new Overhang();
                case "HorizontalLouvres": return new HorizontalLouvres();
                case "VerticalFins": return new VerticalFins();
                case "EggCrate": return new EggCrate();
                case "RetractableAwning": return new RetractableAwning();
                default: return null;
            }
        }

        /// <summary>
        /// The Stage 9 search variables for a family: which parameters may move, over what range,
        /// and at what granularity.
        ///
        /// Ranges are the typology's OWN declared bounds, narrowed where Stage 9 knows better.
        /// Granularity is set to what the parameter physically means: counts are integers because
        /// the typology rounds them before building geometry, depths are millimetre-scale because
        /// finer than that is not a buildable distinction, and blade angles move in 5 degree steps
        /// because that is a manufacturable increment.
        ///
        /// TiltDegrees is included for the louvre and fin families and NOT for the overhang, which
        /// has no tilt parameter. Nothing here invents a variable the geometry cannot model — there
        /// is no porosity, no transmittance and no blade thickness, because the ray engine
        /// underneath is binary and would silently produce numbers that look like a screen's
        /// without being one.
        ///
        /// ELEMENT COUNTS ARE CAPPED BY THE ANALYSIS RESOLUTION when a target and grid size are
        /// supplied, and this is not a tuning choice — see
        /// <see cref="MinimumElementPitchInGridSizes"/> for the rule and the evidence behind it.
        /// </summary>
        /// <param name="typology">The family whose bounds the ranges are intersected with.</param>
        /// <param name="maximumDepth">Optional cap on any depth-like parameter, m. NaN for none.</param>
        /// <param name="target">Aperture, for the span a blade array is spread over. Null to skip the resolution cap.</param>
        /// <param name="gridSize">Analysis cell size, m. NaN to skip the resolution cap.</param>
        public static List<ShadingParameter> ShadingParameters(this IShadingTypology typology, double maximumDepth = double.NaN, ApertureSolarTarget target = null, double gridSize = double.NaN)
        {
            if (typology == null)
            {
                return null;
            }

            double maximumAcross = double.NaN;
            double maximumUp = double.NaN;
            if (target != null && !double.IsNaN(gridSize) && gridSize > 0
                && Query.TryGetApertureLocalBounds(target, out double minX, out double maxX, out double minY, out double maxY))
            {
                // Pitch is span / (count - 1), so count <= span / minimumPitch + 1 keeps the pitch at
                // or above the minimum feature size.
                double minimumPitch = MinimumElementPitchInGridSizes * gridSize;
                maximumAcross = (maxX - minX) / minimumPitch + 1.0;
                maximumUp = (maxY - minY) / minimumPitch + 1.0;
            }

            List<ShadingParameter> result = new List<ShadingParameter>();
            foreach (string name in typology.ParameterNames)
            {
                if (!typology.TryGetBounds(name, out double minimum, out double maximum))
                {
                    continue;
                }

                // MountingOffset is a fixed project/building placement input, never a search
                // variable: it is not swept by Stage 9, so it must not appear in the variable
                // lattice (its declared upper bound is deliberately not a product restriction and
                // would otherwise be swept as an unbounded axis).
                if (name == "MountingOffset")
                {
                    continue;
                }

                double step;
                switch (name)
                {
                    case "Depth":
                    case "RiseAboveHead":
                    case "ExtensionBeyondJambs":
                        step = 0.01; // 10 mm: below this is not a buildable distinction
                        break;
                    case "Projection":
                        // The awning projection lattice: 0.5 m over the typology's [1.6, 3.6] m
                        // bounds gives exactly the Dakar nominal set 1.6, 2.1, 2.6, 3.1, 3.6 m.
                        step = 0.5;
                        break;
                    case "ValanceDepth":
                        // Either no valance or the preset standard depth — the two buildable states.
                        step = 0.21;
                        break;
                    case "TiltDegrees":
                        step = 5.0; // a manufacturable blade increment
                        break;
                    default:
                        step = 1.0; // element counts are integers before any geometry is built
                        break;
                }

                if (name == "Depth" && !double.IsNaN(maximumDepth) && maximumDepth > minimum)
                {
                    maximum = Math.Min(maximum, maximumDepth);
                }

                // Blade arrays run UP the aperture; fin arrays run ACROSS it.
                if (name == "Count")
                {
                    double span = typology.Name == "VerticalFins" ? maximumAcross : maximumUp;
                    maximum = ResolutionCap(maximum, span);
                }
                else if (name == "LouvreCount")
                {
                    maximum = ResolutionCap(maximum, maximumUp);
                }
                else if (name == "FinCount")
                {
                    maximum = ResolutionCap(maximum, maximumAcross);
                }

                result.Add(new ShadingParameter(name, minimum, maximum, step));
            }

            return result;
        }

        /// <summary>
        /// The declared maximum, narrowed to what the analysis grid can resolve. Never drops below
        /// 1 — a single element has no pitch and is always representable — and leaves the maximum
        /// alone when no resolution was supplied.
        /// </summary>
        private static double ResolutionCap(double maximum, double resolutionLimit)
        {
            if (double.IsNaN(resolutionLimit))
            {
                return maximum;
            }

            return Math.Max(1.0, Math.Min(maximum, Math.Floor(resolutionLimit + ResolutionCapTolerance)));
        }
    }
}

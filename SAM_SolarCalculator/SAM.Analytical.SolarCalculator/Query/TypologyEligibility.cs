// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Geometry.Spatial;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// Which shading families are worth optimising for this aperture, decided from where the
        /// UNWANTED solar actually comes from rather than from a rule of thumb about orientation.
        ///
        /// For every sun group carrying unwanted energy and reaching the front of the aperture, the
        /// toward-sun direction is resolved into the aperture's own frame and split into the two
        /// angles a shading device answers:
        ///
        ///   vertical shadow angle    VSA = atan(up-slope component / outward component)
        ///   horizontal shadow angle  HSA = atan(across-facade component / outward component)
        ///
        /// A horizontal device (overhang, louvres) intercepts sun that is HIGH in the aperture's
        /// frame — large VSA. A vertical device (fins) intercepts sun that is OBLIQUE — large HSA.
        /// The eligibility test is therefore what share of the unwanted energy arrives at each,
        /// weighted by that energy so a handful of weak grazing hours cannot qualify a family.
        ///
        /// THIS IS A CAPABILITY GATE, NOT A RANKING, and it is deliberately permissive: a family is
        /// admitted when at least a quarter of the unwanted energy arrives at an angle it can act
        /// on. The optimiser still has to prove any device is worth building.
        ///
        /// It is also worth being clear about what it does NOT exclude, because the textbook
        /// expectation is wrong and was measured rather than assumed. A north-facing London
        /// aperture is not excluded from the horizontal families, and it should not be. Its summer
        /// sun arrives at the ends of the day, far round the corner, and for such a direction the
        /// outward component approaches zero — so the PROFILE ANGLE is large even though the sun is
        /// low, and an overhang genuinely does intercept that beam. Measured on the London north
        /// facade in the Stage 9 tests: 89 kWh/m2 of unwanted beam against the south facade's 225,
        /// no wanted winter solar to protect at all, and an optimised overhang worth 28 kWh. "No
        /// overhangs on north facades" is a rule of thumb about a different climate and a different
        /// brief, and this gate is not in the business of enforcing rules of thumb.
        ///
        /// So the exclusion this reliably provides is the strong one — an aperture where no
        /// unwanted solar reaches the front at all makes NOTHING eligible — plus a narrowing on
        /// facades whose unwanted beam is genuinely one-sided in angle. Treat a long eligibility
        /// list as "nothing is ruled out", not as "all of these are sensible"; the objective, not
        /// this gate, decides whether a device is worth building.
        /// </summary>
        /// <param name="target">The aperture, for its local frame.</param>
        /// <param name="solarVisibilityCache">Supplies the sun groups.</param>
        /// <param name="desirability">Supplies the per-group unwanted energy.</param>
        /// <param name="highSunFraction">Share of unwanted energy above the VSA threshold.</param>
        /// <param name="obliqueSunFraction">Share of unwanted energy above the HSA threshold.</param>
        /// <returns>Family names in Create.ShadingTypologyNames order, or null on bad input.</returns>
        public static List<string> EligibleShadingTypologies(this ApertureSolarTarget target, SolarVisibilityCache solarVisibilityCache, ApertureDesirability desirability, out double highSunFraction, out double obliqueSunFraction)
        {
            highSunFraction = double.NaN;
            obliqueSunFraction = double.NaN;

            Plane plane = target?.Plane;
            List<SunBin> bins = solarVisibilityCache?.Bins;
            double[] unwanted = desirability?.UnwantedEnergyPerGroup;
            if (plane == null || bins == null || unwanted == null || unwanted.Length != bins.Count)
            {
                return null;
            }

            Vector3D axisX = plane.AxisX;
            Vector3D axisY = plane.AxisY;
            Vector3D axisZ = plane.Normal;

            // 15 degrees of profile angle is about the point at which an overhang of buildable
            // depth starts shading the head of a storey-height window; 30 degrees of horizontal
            // shadow angle is where a fin of comparable depth starts to bite.
            const double highSunAngle = 15.0 * Math.PI / 180.0;
            const double obliqueSunAngle = 30.0 * Math.PI / 180.0;
            const double admissionFraction = 0.25;

            double total = 0, high = 0, oblique = 0;

            for (int b = 0; b < bins.Count; b++)
            {
                double energy = unwanted[b];
                if (!(energy > 0))
                {
                    continue;
                }

                Vector3D towardSun = bins[b]?.RepresentativeDirection?.GetNegated();
                if (towardSun == null || !towardSun.IsValid())
                {
                    continue;
                }

                towardSun = towardSun.Unit;

                double ux = towardSun.DotProduct(axisX);
                double uy = towardSun.DotProduct(axisY);
                double uz = towardSun.DotProduct(axisZ);
                if (uz <= 0)
                {
                    continue; // behind the aperture: no device in front of it can act on this sun
                }

                total += energy;
                if (Math.Atan2(uy, uz) > highSunAngle) { high += energy; }
                if (Math.Abs(Math.Atan2(ux, uz)) > obliqueSunAngle) { oblique += energy; }
            }

            List<string> result = new List<string>();
            if (!(total > 0))
            {
                // No unwanted solar reaches the front of the aperture at all. Nothing is eligible
                // on the evidence — reported as an empty list rather than as "everything", so a
                // caller cannot mistake absence of a problem for a free choice of device.
                highSunFraction = double.NaN;
                obliqueSunFraction = double.NaN;
                return result;
            }

            highSunFraction = high / total;
            obliqueSunFraction = oblique / total;

            bool horizontal = highSunFraction >= admissionFraction;
            bool vertical = obliqueSunFraction >= admissionFraction;

            foreach (string name in Create.ShadingTypologyNames)
            {
                bool eligible;
                switch (name)
                {
                    case "Overhang":
                    case "HorizontalLouvres":
                    case "RetractableAwning":
                        eligible = horizontal;
                        break;
                    case "VerticalFins":
                        eligible = vertical;
                        break;
                    case "EggCrate":
                        eligible = horizontal && vertical;
                        break;
                    default:
                        eligible = false;
                        break;
                }

                if (eligible)
                {
                    result.Add(name);
                }
            }

            return result;
        }
    }
}

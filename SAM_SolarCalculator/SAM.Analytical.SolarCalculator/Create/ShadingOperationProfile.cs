// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// What one device actually DID over the weather year at one aperture: the deployed
        /// schedule is passed through from the control profile, the energy accounting is the
        /// EXISTING ShadingPerformance pipeline run against the deployed hours, and the
        /// per-element effective hours are a bin-to-hour lookup — no new ray tracing, no new
        /// sun positions, no new physics.
        ///
        /// THE TWO WEIGHTINGS, ONE GEOMETRY. The candidate attribution cache is built ONCE over
        /// context plus the device; two desirability weightings are then measured against it:
        ///
        ///   controlled    DeployedDesirability  (+1 on the hours the device was actually out)
        ///   uncontrolled  ExternalDesirability  (+1 on the hours shading was REQUESTED)
        ///
        /// Both go through the SAME public accounting, so the difference is exactly what the wind
        /// retraction costs — never more than the request, because the deployed set is a subset of
        /// the demand set.
        ///
        /// ONE TIMELINE, ENFORCED. The profile's aperture, year and sun-position shift must match
        /// the target and the cache; anything else is refused with null, never approximated — a
        /// schedule on one timeline and an energy accounting on another would produce a number
        /// nobody can interpret.
        /// </summary>
        /// <param name="target">The aperture.</param>
        /// <param name="solarControlProfile">The control schedule. Its ShadeOn hours ARE the deployment; they are passed through, not recomputed.</param>
        /// <param name="solarVisibilityCache">Visibility with context only; its bins carry the hour-of-year membership.</param>
        /// <param name="contextOccluders">The physical surroundings. Present for API symmetry; the shading answer takes context from solarVisibilityCache.</param>
        /// <param name="typology">The device. Its elements are built around this target.</param>
        /// <param name="weatherData">Hourly weather on the same timeline.</param>
        /// <param name="cellIndexOffset">This target's first cell index within the visibility cache's shared cell space.</param>
        public static ShadingOperationProfile ShadingOperationProfile(
            this ApertureSolarTarget target,
            SolarControlProfile solarControlProfile,
            SolarVisibilityCache solarVisibilityCache,
            List<LinkedFace3D> contextOccluders,
            IShadingTypology typology,
            WeatherData weatherData,
            int cellIndexOffset = 0)
        {
            List<ShadingElement> elements = typology?.ShadingElements(target);
            if (elements == null)
            {
                return null;
            }

            return ShadingOperationProfileCore(
                target, solarControlProfile, solarVisibilityCache, elements, typology.Name,
                typology.MaterialFraction(target), Parameters(typology), weatherData, cellIndexOffset,
                out ShadingPerformance _, out ShadingPerformance _);
        }

        /// <summary>
        /// The shared core: builds the candidate attribution once, measures the controlled and the
        /// uncontrolled weighting through the existing accounting, and computes per-element
        /// effective hours by bin-to-hour lookup. The grouped path reuses this per member with the
        /// SHARED element set; the single path calls it with the per-target elements.
        /// </summary>
        internal static ShadingOperationProfile ShadingOperationProfileCore(
            ApertureSolarTarget target,
            SolarControlProfile solarControlProfile,
            SolarVisibilityCache solarVisibilityCache,
            List<ShadingElement> shadingElements,
            string typologyName,
            double materialFraction,
            IDictionary<string, double> deviceParameters,
            WeatherData weatherData,
            int cellIndexOffset,
            out ShadingPerformance controlledPerformance,
            out ShadingPerformance uncontrolledPerformance)
        {
            controlledPerformance = null;
            uncontrolledPerformance = null;

            if (target == null || solarControlProfile == null || solarVisibilityCache == null || weatherData == null)
            {
                return null;
            }

            // The schedule and the energy accounting must describe the same window and the same
            // timeline. A mismatch is refused: it would produce a plausible number about the wrong
            // design, which is the one failure mode identity checks exist to prevent.
            if (solarControlProfile.ApertureGuid != target.ApertureGuid)
            {
                return null;
            }

            if (solarControlProfile.Year != solarVisibilityCache.Year)
            {
                return null;
            }

            if (Math.Abs(solarControlProfile.TimeShiftInMinutes - solarVisibilityCache.SunPositionShiftInMinutes) > 1e-6)
            {
                return null;
            }

            List<ShadingElement> elements = new List<ShadingElement>(shadingElements ?? new List<ShadingElement>());
            elements.RemoveAll(x => x == null);

            // ONE attribution build over context plus the device. Both weightings and the
            // effective-hours lookup read this — the geometry is identical, only the weighting
            // differs, so nothing geometric is ever done twice.
            SolarAttributionCache attributionCache = CandidateAttributionCache(solarVisibilityCache, elements, target.AnalysisCells, cellIndexOffset);
            if (attributionCache == null)
            {
                return null;
            }

            ApertureDesirability controlledDesirability = ApertureDesirability(target, solarVisibilityCache, solarControlProfile.DeployedDesirability(), weatherData);
            if (controlledDesirability == null)
            {
                return null;
            }

            ApertureDesirability uncontrolledDesirability = ApertureDesirability(target, solarVisibilityCache, solarControlProfile.ExternalDesirability(), weatherData);
            if (uncontrolledDesirability == null)
            {
                return null;
            }

            controlledPerformance = ShadingPerformance(target, solarVisibilityCache, attributionCache, controlledDesirability, elements, typologyName, materialFraction, cellIndexOffset);
            if (controlledPerformance == null)
            {
                return null;
            }

            uncontrolledPerformance = ShadingPerformance(target, solarVisibilityCache, attributionCache, uncontrolledDesirability, elements, typologyName, materialFraction, cellIndexOffset);
            if (uncontrolledPerformance == null)
            {
                return null;
            }

            Guid canopyGuid = Guid.Empty;
            Guid valanceGuid = Guid.Empty;
            foreach (ShadingElement element in elements)
            {
                if (element.Name == "RetractableAwning_Canopy")
                {
                    canopyGuid = element.Guid;
                }
                else if (element.Name == "RetractableAwning_Valance")
                {
                    valanceGuid = element.Guid;
                }
            }

            // Element-effective hours: bins x cells Guid lookups, no sun positions.
            List<int> deployedHoursOfYear = solarControlProfile.ShadeOnHoursOfYear;
            Dictionary<Guid, List<int>> effectiveHoursOfYearPerElement = new Dictionary<Guid, List<int>>();
            foreach (ShadingElement element in elements)
            {
                effectiveHoursOfYearPerElement[element.Guid] = Query.ElementEffectiveHoursOfYear(
                    attributionCache, solarVisibilityCache, element.Guid, deployedHoursOfYear, target.CellCount, cellIndexOffset);
            }

            Dictionary<Guid, double> energyPerElement = controlledPerformance.EnergyPerElement;
            double canopyAttributedEnergy = canopyGuid != Guid.Empty && energyPerElement.TryGetValue(canopyGuid, out double canopyEnergy)
                ? canopyEnergy
                : double.NaN;

            double valanceAttributedEnergy = valanceGuid != Guid.Empty && energyPerElement.TryGetValue(valanceGuid, out double valanceEnergy)
                ? valanceEnergy
                : double.NaN;

            return new ShadingOperationProfile(
                target.ApertureGuid,
                typologyName,
                deviceParameters,
                solarControlProfile.Settings,
                solarControlProfile.Year,
                solarControlProfile.TimeShiftInMinutes,
                deployedHoursOfYear,
                solarControlProfile.HighWindHoursOfYear,
                solarControlProfile.ShadeUseFraction,
                canopyGuid,
                valanceGuid,
                effectiveHoursOfYearPerElement,
                canopyAttributedEnergy,
                valanceAttributedEnergy,
                controlledPerformance.DirectSolarIntercepted,
                controlledPerformance.UnwantedSolarIntercepted,
                uncontrolledPerformance.UnwantedSolarIntercepted,
                energyPerElement);
        }

        private static Dictionary<string, double> Parameters(IShadingTypology typology)
        {
            Dictionary<string, double> result = new Dictionary<string, double>();
            if (typology == null)
            {
                return result;
            }

            foreach (string name in typology.ParameterNames ?? new List<string>())
            {
                result[name] = typology.GetParameter(name);
            }

            return result;
        }
    }
}

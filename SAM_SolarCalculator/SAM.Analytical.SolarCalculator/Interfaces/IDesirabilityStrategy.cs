// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using SAM.Core;
using SAM.Weather;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// Pluggable solar desirability strategy (Stage 5): for each weather-timeline hour, how
    /// desirable or undesirable is the admitted direct solar energy?
    ///
    /// SIGN CONVENTION (frozen, tested):
    ///   weight &gt; 0  →  blocking this hour's direct sun is BENEFICIAL (unwanted solar, e.g.
    ///                   overheating period). The hour's energy accumulates into UnwantedEnergy.
    ///   weight &lt; 0  →  blocking this hour's direct sun is HARMFUL (wanted solar, e.g. winter
    ///                   passive gain). The hour's energy accumulates into WantedEnergy.
    ///   weight == 0  →  neutral; the hour does not drive shading design either way.
    /// The magnitude scales the hour's energy contribution; a weight of +0.5 counts half the
    /// hour's aperture-plane beam energy as unwanted.
    ///
    /// This is the load-free Phase 1 approximation of the Shaderade formulation (Sargent, Niemasz
    /// &amp; Reinhart 2011), which weights each timestep by the zone's cooling-minus-heating load.
    /// SAM_SolarCalculator has no thermal load model; a load-based strategy (e.g. TAS-derived
    /// weights supplied through ExternalDesirability) plugs in here later without changing any
    /// caller. Implementations are IJSAMObject so a strategy choice persists with a model.
    /// </summary>
    public interface IDesirabilityStrategy : IJSAMObject
    {
        /// <summary>
        /// Signed desirability weight for one weather-timeline hour.
        /// </summary>
        /// <param name="dateTime">Weather-timeline timestamp (whole hour).</param>
        /// <param name="weatherHour">The weather values for that hour (may carry NaN fields).</param>
        /// <param name="target">The aperture the desirability is evaluated for.</param>
        /// <returns>Positive = blocking is beneficial; negative = blocking is harmful; 0 = neutral.</returns>
        double Weight(DateTime dateTime, WeatherHour weatherHour, ApertureSolarTarget target);
    }
}

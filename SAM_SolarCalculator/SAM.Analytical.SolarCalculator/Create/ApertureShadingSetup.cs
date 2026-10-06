// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Core.SolarCalculator;
using SAM.Weather;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// One aperture, ready for the shading stages: its target, its place in the shared cell space,
    /// the visibility calculation behind it, and the desirability weighting that says which of its
    /// solar is unwanted and which is worth keeping.
    ///
    /// Transient, like <see cref="ApertureSolarContext"/>: it is cheap to rebuild once the
    /// visibility calculation is reusable, and nothing here belongs in a saved file.
    /// </summary>
    public class ApertureShadingSetup
    {
        private ApertureSolarContext context;
        private ApertureSolarTarget target;
        private ApertureDesirability desirability;
        private int cellIndexOffset;

        internal ApertureShadingSetup(ApertureSolarContext context, ApertureSolarTarget target, ApertureDesirability desirability, int cellIndexOffset)
        {
            this.context = context;
            this.target = target;
            this.desirability = desirability;
            this.cellIndexOffset = cellIndexOffset;
        }

        public ApertureSolarContext Context
        {
            get
            {
                return context;
            }
        }

        public ApertureSolarTarget Target
        {
            get
            {
                return target;
            }
        }

        /// <summary>Per-sun-group unwanted and wanted energy for this aperture.</summary>
        public ApertureDesirability Desirability
        {
            get
            {
                return desirability;
            }
        }

        /// <summary>This aperture's first cell index within the shared cell space.</summary>
        public int CellIndexOffset
        {
            get
            {
                return cellIndexOffset;
            }
        }

        public bool ReusedPreviousCalculation
        {
            get
            {
                return context != null && context.ReusedPreviousCalculation;
            }
        }
    }

    public static partial class Create
    {
        /// <summary>
        /// The default brief when none is stated: summer solar is unwanted, winter solar is worth
        /// keeping. Hemisphere-aware through the location, so a Sydney project is not given a
        /// northern-hemisphere calendar.
        ///
        /// It is a DEFAULT, not a recommendation. Any project with a real overheating brief should
        /// state its own periods or supply its own strategy.
        /// </summary>
        public static IDesirabilityStrategy DefaultDesirabilityStrategy(int year, Core.Location location = null)
        {
            return new SeasonalDesirability(
                Core.SolarCalculator.Create.AnalysisPeriod(AnalysisPeriodPreset.Summer, year, location),
                Core.SolarCalculator.Create.AnalysisPeriod(AnalysisPeriodPreset.Winter, year, location));
        }

        /// <summary>
        /// Prepares one aperture for the shading stages: resolves the shared solar context (reusing
        /// a previous calculation whenever the identity still matches), finds the aperture's target
        /// within it, and builds the desirability weighting.
        ///
        /// PRECEDENCE. A supplied desirabilityStrategy wins outright. Otherwise the unwanted and
        /// wanted periods are used. If neither is given, the default summer-unwanted /
        /// winter-wanted brief applies.
        ///
        /// Returns null when the context cannot be built (no weather, no analysable aperture, an
        /// unresolved timezone) or when the requested aperture is not one of the analysable ones —
        /// the caller reports which, because it knows what the user actually asked for.
        /// </summary>
        /// <param name="analyticalModel">The model.</param>
        /// <param name="apertureGuid">The aperture to prepare.</param>
        /// <param name="year">Requested analysis year; resolved against the weather.</param>
        /// <param name="weatherData">Weather. Null = the weather attached to the model.</param>
        /// <param name="desirabilityStrategy">Explicit weighting. Wins over the periods when supplied.</param>
        /// <param name="unwantedPeriod">Hours whose solar should be blocked. Null with a null strategy = the default brief.</param>
        /// <param name="wantedPeriod">Hours whose solar should be preserved.</param>
        /// <param name="apertureGuids">The aperture set the calculation covers. Null = all analysable apertures, which is what keeps it shared with ApertureIrradiance.</param>
        /// <param name="gridSize">Aperture analysis-grid size, m.</param>
        /// <param name="sunAngleStep">Angular resolution used to group similar sun positions, degrees.</param>
        /// <param name="recalculate">Force a rebuild even when a previous calculation could be reused.</param>
        /// <param name="sunTimeConvention">Timestamp convention of the weather timeline.</param>
        public static ApertureShadingSetup ApertureShadingSetup(this AnalyticalModel analyticalModel, Guid apertureGuid, int year, WeatherData weatherData = null, IDesirabilityStrategy desirabilityStrategy = null, AnalysisPeriod unwantedPeriod = null, AnalysisPeriod wantedPeriod = null, IEnumerable<Guid> apertureGuids = null, double gridSize = 0.5, double sunAngleStep = 2.0, bool recalculate = false, SunTimeConvention sunTimeConvention = SunTimeConvention.IntervalStart)
        {
            return ApertureShadingSetup(analyticalModel, apertureGuid, year, out string _, weatherData, desirabilityStrategy, unwantedPeriod, wantedPeriod, apertureGuids, gridSize, sunAngleStep, recalculate, sunTimeConvention);
        }

        /// <summary>
        /// The same, reporting WHY the setup could not be built.
        ///
        /// See <see cref="ApertureSolarContextFailureReason"/> for why a bare null was not good
        /// enough: the causes range from a mistyped grid size to an unresolvable site, and they are
        /// fixed in completely different places.
        /// </summary>
        /// <param name="message">Null on success; an actionable sentence otherwise.</param>
        public static ApertureShadingSetup ApertureShadingSetup(this AnalyticalModel analyticalModel, Guid apertureGuid, int year, out string message, WeatherData weatherData = null, IDesirabilityStrategy desirabilityStrategy = null, AnalysisPeriod unwantedPeriod = null, AnalysisPeriod wantedPeriod = null, IEnumerable<Guid> apertureGuids = null, double gridSize = 0.5, double sunAngleStep = 2.0, bool recalculate = false, SunTimeConvention sunTimeConvention = SunTimeConvention.IntervalStart)
        {
            message = null;

            ApertureSolarContext context = ApertureSolarContext(analyticalModel, year, weatherData, apertureGuids, gridSize, sunAngleStep, recalculate, sunTimeConvention);
            if (context == null)
            {
                message = ApertureSolarContextFailureReason(analyticalModel, apertureGuids, gridSize, weatherData)
                    ?? "The solar calculation could not be set up for this model.";
                return null;
            }

            ApertureSolarTarget target = context.Target(apertureGuid);
            if (target == null)
            {
                message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Aperture {0} is not one of the {1} analysable apertures of this model. Only apertures on sun-exposed external panels are analysed; an aperture on a panel shared by two spaces is internal and is never a target. Check that the target came from THIS model and that the grid size ({2:0.####} m) is the one the targets were built with.",
                    apertureGuid, context.TargetCount, gridSize);

                return null;
            }

            IDesirabilityStrategy strategy = desirabilityStrategy;
            if (strategy == null)
            {
                strategy = unwantedPeriod == null && wantedPeriod == null
                    ? DefaultDesirabilityStrategy(context.Year, context.Location)
                    : new SeasonalDesirability(ReRoot(unwantedPeriod, context.Year), ReRoot(wantedPeriod, context.Year));
            }

            ApertureDesirability desirability = ApertureDesirability(target, context.SolarVisibilityCache, strategy, context.WeatherData);
            if (desirability == null)
            {
                message = "The desirability weighting could not be built for this aperture: the weather, the sun groups and the aperture's outward normal do not describe a usable calculation.";
                return null;
            }

            return new ApertureShadingSetup(context, target, desirability, context.CellIndexOffset(apertureGuid));
        }

        /// <summary>A period on the analysis year, preserving its hour-of-year structure.</summary>
        internal static AnalysisPeriod ReRoot(AnalysisPeriod analysisPeriod, int year)
        {
            if (analysisPeriod == null || analysisPeriod.Year == year)
            {
                return analysisPeriod;
            }

            List<int> explicitHours = analysisPeriod.ExplicitHoursOfYear;
            if (explicitHours != null)
            {
                return new AnalysisPeriod(year, explicitHours);
            }

            return new AnalysisPeriod(year, analysisPeriod.StartMonth, analysisPeriod.StartDay, analysisPeriod.EndMonth, analysisPeriod.EndDay, analysisPeriod.StartHour, analysisPeriod.EndHour, analysisPeriod.Timestep);
        }
    }
}

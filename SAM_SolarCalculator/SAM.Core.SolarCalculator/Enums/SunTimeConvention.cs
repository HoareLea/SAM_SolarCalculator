// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.ComponentModel;

namespace SAM.Core.SolarCalculator
{
    /// <summary>
    /// The timestamp convention of an hourly weather/simulation timeline, i.e. which instant of the
    /// hourly interval a timestamp labels. This decides where the representative sun position for
    /// the interval's radiation is sampled. It is deliberately a separate concept from any
    /// application-compatibility shift (see remarks).
    /// </summary>
    /// <remarks>
    /// SAM's EPW importer decrements the EPW hour field (1-24, values averaged over the interval
    /// ENDING at that hour), so a SAM WeatherData timestamp is the START of its interval
    /// (IntervalStart): the representative sun position for the interval-averaged radiation is the
    /// interval midpoint, timestamp + 30 min.
    ///
    /// TAS EDSL labels the same interval by its END. When SAM drives a DateTime grid meant to match
    /// a TAS hourly series hour-for-hour, the matching sun sample is at timestamp - 30 min
    /// (IntervalEnd) - the same physical midpoint, reached from the other label convention. The
    /// legacy Grasshopper "_timeShift_ = -30" default encodes exactly this TAS-compatibility case;
    /// it is not the EPW weather convention.
    /// </remarks>
    [Description("Sun Time Convention")]
    public enum SunTimeConvention
    {
        [Description("Undefined")] Undefined,
        [Description("Timestamp labels the interval instant itself (sun at the timestamp)")] OnTheHour,
        [Description("Timestamp labels the interval start (SAM/EPW weather timeline; sun at +30 min)")] IntervalStart,
        [Description("Timestamp labels the interval end (TAS EDSL compatibility; sun at -30 min)")] IntervalEnd,
    }
}

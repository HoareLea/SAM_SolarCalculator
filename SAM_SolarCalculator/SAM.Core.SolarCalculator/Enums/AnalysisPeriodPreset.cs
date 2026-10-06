// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.ComponentModel;

namespace SAM.Core.SolarCalculator
{
    /// <summary>
    /// Named analysis-period presets. Seasonal presets are hemisphere-aware: they flip when the
    /// reference Location has a negative latitude (see Create.AnalysisPeriod).
    /// </summary>
    [Description("Analysis Period Preset")]
    public enum AnalysisPeriodPreset
    {
        [Description("Undefined")] Undefined,
        [Description("Full Year")] FullYear,
        [Description("Summer")] Summer,
        [Description("Winter")] Winter,
        [Description("Equinox")] Equinox,
        [Description("Cooling Season")] CoolingSeason,
        [Description("Heating Season")] HeatingSeason,
        [Description("Peak Summer Day")] PeakSummerDay,
        [Description("Peak Winter Day")] PeakWinterDay,
        [Description("Custom")] Custom,
    }
}

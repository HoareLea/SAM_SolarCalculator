// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// Sun-position sampling offset, in minutes, implied by a timeline's SunTimeConvention:
        /// OnTheHour = 0, IntervalStart = +30 (SAM/EPW weather), IntervalEnd = -30 (TAS EDSL
        /// compatibility). Returns NaN for Undefined.
        /// </summary>
        public static double TimeShiftInMinutes(this SunTimeConvention sunTimeConvention)
        {
            switch (sunTimeConvention)
            {
                case SunTimeConvention.OnTheHour:
                    return 0;
                case SunTimeConvention.IntervalStart:
                    return 30;
                case SunTimeConvention.IntervalEnd:
                    return -30;
                default:
                    return double.NaN;
            }
        }
    }
}

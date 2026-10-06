// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;

namespace SAM.Core.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// AnalysisPeriod from a named preset. Seasonal presets are hemisphere-aware: when the
        /// reference location is in the southern hemisphere (Latitude &lt; 0) summer and winter swap
        /// halves of the year. A null location is treated as northern hemisphere.
        ///
        /// Conventions (northern hemisphere; mirrored in the south):
        ///   Summer        = 1 Jun – 31 Aug
        ///   Winter        = 1 Dec – end of Feb (year-wrapping)
        ///   CoolingSeason = 1 May – 30 Sep
        ///   HeatingSeason = 1 Oct – 31 Mar (year-wrapping)
        ///   PeakSummerDay = 21 Jun (solar-declination maximum)
        ///   PeakWinterDay = 21 Dec
        ///   Equinox       = two 14-day windows centred on the equinoxes (15–28 Mar and 17–30 Sep),
        ///                   emitted as explicit hours of the year.
        /// </summary>
        public static AnalysisPeriod AnalysisPeriod(this AnalysisPeriodPreset analysisPeriodPreset, int year, Location location = null)
        {
            bool northern = location == null || location.Latitude >= 0;

            switch (analysisPeriodPreset)
            {
                case AnalysisPeriodPreset.FullYear:
                    return new AnalysisPeriod(year);

                case AnalysisPeriodPreset.Summer:
                    return northern
                        ? new AnalysisPeriod(year, 6, 1, 8, 31)
                        : new AnalysisPeriod(year, 12, 1, 2, 29);

                case AnalysisPeriodPreset.Winter:
                    return northern
                        ? new AnalysisPeriod(year, 12, 1, 2, 29)
                        : new AnalysisPeriod(year, 6, 1, 8, 31);

                case AnalysisPeriodPreset.CoolingSeason:
                    return northern
                        ? new AnalysisPeriod(year, 5, 1, 9, 30)
                        : new AnalysisPeriod(year, 11, 1, 3, 31);

                case AnalysisPeriodPreset.HeatingSeason:
                    return northern
                        ? new AnalysisPeriod(year, 10, 1, 3, 31)
                        : new AnalysisPeriod(year, 4, 1, 9, 30);

                case AnalysisPeriodPreset.PeakSummerDay:
                    return northern
                        ? new AnalysisPeriod(year, 6, 21, 6, 21)
                        : new AnalysisPeriod(year, 12, 21, 12, 21);

                case AnalysisPeriodPreset.PeakWinterDay:
                    return northern
                        ? new AnalysisPeriod(year, 12, 21, 12, 21)
                        : new AnalysisPeriod(year, 6, 21, 6, 21);

                case AnalysisPeriodPreset.Equinox:
                    {
                        List<int> hoursOfYear = new List<int>();
                        AnalysisPeriod spring = new AnalysisPeriod(year, 3, 15, 3, 28);
                        AnalysisPeriod autumn = new AnalysisPeriod(year, 9, 17, 9, 30);
                        hoursOfYear.AddRange(spring.HoursOfYear());
                        hoursOfYear.AddRange(autumn.HoursOfYear());
                        return new AnalysisPeriod(year, hoursOfYear);
                    }

                case AnalysisPeriodPreset.Custom:
                case AnalysisPeriodPreset.Undefined:
                default:
                    return null;
            }
        }
    }
}

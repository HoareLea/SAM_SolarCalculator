// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;

namespace SAM.Core.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// The hours a calculation should run over, from the two ways a user can ask for them.
        ///
        /// PRECEDENCE, and it is never silent:
        ///   1. explicit hours of the year — the most specific request wins;
        ///   2. the supplied AnalysisPeriod;
        ///   3. the full year.
        ///
        /// When both are supplied the hours win and <paramref name="hoursOverrodeAnalysisPeriod"/>
        /// comes back true, so the caller can say so rather than quietly discarding a connected
        /// input. Out-of-range hours are dropped by AnalysisPeriod itself; a list containing none
        /// that are valid yields an empty period rather than a full year, because "these hours"
        /// with nothing usable in it is a mistake worth seeing, not a request for 8760 hours.
        /// </summary>
        /// <param name="year">Year the period is rooted in.</param>
        /// <param name="analysisPeriod">Period, or null.</param>
        /// <param name="hoursOfYear">Explicit hours of the year, or null/empty.</param>
        /// <param name="hoursOverrodeAnalysisPeriod">True when both were supplied and the hours won.</param>
        public static AnalysisPeriod ResolvedAnalysisPeriod(int year, AnalysisPeriod analysisPeriod, IEnumerable<int> hoursOfYear, out bool hoursOverrodeAnalysisPeriod)
        {
            hoursOverrodeAnalysisPeriod = false;

            List<int> hours = hoursOfYear == null ? null : new List<int>(hoursOfYear);
            if (hours != null && hours.Count != 0)
            {
                hoursOverrodeAnalysisPeriod = analysisPeriod != null;
                return new AnalysisPeriod(year, hours);
            }

            if (analysisPeriod != null)
            {
                return analysisPeriod;
            }

            return new AnalysisPeriod(year);
        }
    }
}

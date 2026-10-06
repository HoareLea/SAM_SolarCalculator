// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    public class AnalysisPeriodTests
    {
        [Fact]
        public void FullYear_HourCount_LeapAndNonLeap()
        {
            Assert.Equal(8760, new AnalysisPeriod(2018).HoursOfYear().Count);
            Assert.Equal(8784, new AnalysisPeriod(2020).HoursOfYear().Count);
        }

        [Fact]
        public void FullYear_FirstAndLastHours()
        {
            List<DateTime> dateTimes = new AnalysisPeriod(2018).DateTimes();
            Assert.Equal(new DateTime(2018, 1, 1, 0, 0, 0), dateTimes.First());
            Assert.Equal(new DateTime(2018, 12, 31, 23, 0, 0), dateTimes.Last());
        }

        [Fact]
        public void DateRange_SingleDay()
        {
            AnalysisPeriod period = new AnalysisPeriod(2018, 6, 21, 6, 21);
            List<int> hoursOfYear = period.HoursOfYear();
            Assert.Equal(24, hoursOfYear.Count);
            Assert.All(period.DateTimes(), x => Assert.Equal(6, x.Month));
            Assert.All(period.DateTimes(), x => Assert.Equal(21, x.Day));
        }

        [Fact]
        public void DateRange_WrapsYear()
        {
            // Heating-season style range: 1 Nov - 28 Feb (non-leap 2018) must yield Nov+Dec+Jan+Feb,
            // not an empty set.
            AnalysisPeriod period = new AnalysisPeriod(2018, 11, 1, 2, 28);
            Assert.True(period.WrapsYear);

            List<DateTime> dateTimes = period.DateTimes();
            Assert.Equal((30 + 31 + 31 + 28) * 24, dateTimes.Count);
            Assert.All(dateTimes, x => Assert.Contains(x.Month, new[] { 11, 12, 1, 2 }));
            Assert.Contains(new DateTime(2018, 11, 1, 0, 0, 0), dateTimes);
            Assert.Contains(new DateTime(2018, 2, 28, 23, 0, 0), dateTimes);
            Assert.DoesNotContain(new DateTime(2018, 3, 1, 0, 0, 0), dateTimes);
            Assert.DoesNotContain(new DateTime(2018, 10, 31, 23, 0, 0), dateTimes);
        }

        [Fact]
        public void DateRange_WrapsYear_LeapYear_Includes_Feb29()
        {
            AnalysisPeriod period = new AnalysisPeriod(2020, 11, 1, 2, 29);
            List<DateTime> dateTimes = period.DateTimes();
            Assert.Equal((30 + 31 + 31 + 29) * 24, dateTimes.Count);
            Assert.Contains(new DateTime(2020, 2, 29, 12, 0, 0), dateTimes);
        }

        [Fact]
        public void HourOfDay_Filter()
        {
            AnalysisPeriod period = new AnalysisPeriod(2018, 6, 1, 8, 31, 9, 17);
            List<DateTime> dateTimes = period.DateTimes();
            Assert.Equal(92 * 9, dateTimes.Count);
            Assert.All(dateTimes, x => Assert.InRange(x.Hour, 9, 17));
        }

        [Fact]
        public void HourOfDay_Overnight_Window()
        {
            AnalysisPeriod period = new AnalysisPeriod(2018, 6, 21, 6, 21, 22, 6);
            List<int> hours = period.DateTimes().ConvertAll(x => x.Hour);
            Assert.Equal(2 + 7, hours.Count);
            Assert.All(hours, x => Assert.True(x >= 22 || x <= 6));
        }

        [Fact]
        public void ExplicitHoursOfYear()
        {
            List<int> explicitHours = new List<int> { 0, 100, 5000, 8759 };
            AnalysisPeriod period = new AnalysisPeriod(2018, explicitHours);
            Assert.Equal(explicitHours, period.HoursOfYear());
            Assert.Equal(new DateTime(2018, 12, 31, 23, 0, 0), period.DateTimes().Last());

            // Out-of-range hours are dropped.
            Assert.Single(new AnalysisPeriod(2018, new[] { 8759, 8760, 9000 }).HoursOfYear());
            Assert.Single(new AnalysisPeriod(2020, new[] { 8783, 8784 }).HoursOfYear());
        }

        [Fact]
        public void Presets_NorthernHemisphere()
        {
            Location location = new Location("London", -0.1278, 51.5074, 0);

            AnalysisPeriod summer = Core.SolarCalculator.Create.AnalysisPeriod(AnalysisPeriodPreset.Summer, 2018, location);
            Assert.Equal(92 * 24, summer.HoursOfYear().Count);
            Assert.All(summer.DateTimes(), x => Assert.Contains(x.Month, new[] { 6, 7, 8 }));

            AnalysisPeriod winter = Core.SolarCalculator.Create.AnalysisPeriod(AnalysisPeriodPreset.Winter, 2018, location);
            Assert.True(winter.WrapsYear);
            Assert.All(winter.DateTimes(), x => Assert.Contains(x.Month, new[] { 12, 1, 2 }));

            AnalysisPeriod peakSummer = Core.SolarCalculator.Create.AnalysisPeriod(AnalysisPeriodPreset.PeakSummerDay, 2018, location);
            Assert.Equal(24, peakSummer.HoursOfYear().Count);
            Assert.Equal(6, peakSummer.DateTimes().First().Month);
            Assert.Equal(21, peakSummer.DateTimes().First().Day);
        }

        [Fact]
        public void Presets_SouthernHemisphere_Flip()
        {
            Location location = new Location("Sydney", 151.2093, -33.8688, 0);

            // Southern summer = Dec-Feb (year-wrapping).
            AnalysisPeriod summer = Core.SolarCalculator.Create.AnalysisPeriod(AnalysisPeriodPreset.Summer, 2018, location);
            Assert.True(summer.WrapsYear);
            Assert.All(summer.DateTimes(), x => Assert.Contains(x.Month, new[] { 12, 1, 2 }));

            // Southern winter = Jun-Aug.
            AnalysisPeriod winter = Core.SolarCalculator.Create.AnalysisPeriod(AnalysisPeriodPreset.Winter, 2018, location);
            Assert.False(winter.WrapsYear);
            Assert.All(winter.DateTimes(), x => Assert.Contains(x.Month, new[] { 6, 7, 8 }));

            AnalysisPeriod peakSummer = Core.SolarCalculator.Create.AnalysisPeriod(AnalysisPeriodPreset.PeakSummerDay, 2018, location);
            Assert.Equal(12, peakSummer.DateTimes().First().Month);
            Assert.Equal(21, peakSummer.DateTimes().First().Day);

            AnalysisPeriod peakWinter = Core.SolarCalculator.Create.AnalysisPeriod(AnalysisPeriodPreset.PeakWinterDay, 2018, location);
            Assert.Equal(6, peakWinter.DateTimes().First().Month);
        }

        [Fact]
        public void Presets_Equinox_TwoWindows()
        {
            AnalysisPeriod equinox = Core.SolarCalculator.Create.AnalysisPeriod(AnalysisPeriodPreset.Equinox, 2018, null);
            List<DateTime> dateTimes = equinox.DateTimes();
            Assert.Equal(28 * 24, dateTimes.Count);
            Assert.All(dateTimes, x => Assert.Contains(x.Month, new[] { 3, 9 }));
            Assert.Contains(new DateTime(2018, 3, 20, 12, 0, 0), dateTimes);
            Assert.Contains(new DateTime(2018, 9, 23, 12, 0, 0), dateTimes);
        }

        [Fact]
        public void NullLocation_TreatedAsNorthern()
        {
            AnalysisPeriod summer = Core.SolarCalculator.Create.AnalysisPeriod(AnalysisPeriodPreset.Summer, 2018, null);
            Assert.Equal(92 * 24, summer.HoursOfYear().Count);
        }

        [Fact]
        public void Json_RoundTrip()
        {
            AnalysisPeriod period = new AnalysisPeriod(2020, 11, 1, 2, 29, 8, 18);
            AnalysisPeriod roundTripped = new AnalysisPeriod(period.ToJsonObject());

            Assert.Equal(period.Year, roundTripped.Year);
            Assert.Equal(period.HoursOfYear(), roundTripped.HoursOfYear());
            Assert.True(roundTripped.WrapsYear);

            AnalysisPeriod explicitPeriod = new AnalysisPeriod(2018, new[] { 3, 7, 42 });
            AnalysisPeriod explicitRoundTripped = new AnalysisPeriod(explicitPeriod.ToJsonObject());
            Assert.Equal(new List<int> { 3, 7, 42 }, explicitRoundTripped.HoursOfYear());
        }

        [Fact]
        public void ComplementaryPeriods_Partition_FullYear()
        {
            // Two complementary custom periods whose union is exactly the full year — the correct
            // conservation-test construction (seasons do not partition the year; these do).
            AnalysisPeriod firstHalf = new AnalysisPeriod(2018, 1, 1, 6, 30);
            AnalysisPeriod secondHalf = new AnalysisPeriod(2018, 7, 1, 12, 31);
            AnalysisPeriod fullYear = new AnalysisPeriod(2018);

            HashSet<int> union = new HashSet<int>(firstHalf.HoursOfYear());
            union.UnionWith(secondHalf.HoursOfYear());
            Assert.Equal(fullYear.HoursOfYear().Count, union.Count);
            Assert.Equal(0, firstHalf.HoursOfYear().Intersect(secondHalf.HoursOfYear()).Count());
        }
    }
}

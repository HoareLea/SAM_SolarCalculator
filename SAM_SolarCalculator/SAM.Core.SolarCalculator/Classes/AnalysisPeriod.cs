// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace SAM.Core.SolarCalculator
{
    /// <summary>
    /// A reusable selection of hours within a single calendar year, used to scope solar analysis.
    /// Supports a full year, a (possibly year-wrapping) date range, a single day, an hour-of-day
    /// window (including overnight windows), or an explicit list of hours of the year.
    ///
    /// DateTimes produced are on the weather/simulation timeline (whole hours). Any sun-position
    /// time shift (e.g. the TAS EDSL -30 min convention) is applied by the caller, explicitly,
    /// at the point of use — this type never mixes the two timelines silently.
    /// </summary>
    public class AnalysisPeriod : IJSAMObject, ISolarObject
    {
        private int year;
        private int startMonth = 1;
        private int startDay = 1;
        private int endMonth = 12;
        private int endDay = 31;
        private int startHour;
        private int endHour = 23;
        private int timestep = 1;
        private List<int> hoursOfYear;

        public AnalysisPeriod(int year)
        {
            this.year = year;
        }

        public AnalysisPeriod(int year, int startMonth, int startDay, int endMonth, int endDay, int startHour = 0, int endHour = 23, int timestep = 1)
        {
            this.year = year;
            StartMonth = startMonth;
            StartDay = startDay;
            EndMonth = endMonth;
            EndDay = endDay;
            StartHour = startHour;
            EndHour = endHour;
            Timestep = timestep;
        }

        public AnalysisPeriod(int year, IEnumerable<int> hoursOfYear)
        {
            this.year = year;
            if (hoursOfYear != null)
            {
                int count = DateTime.IsLeapYear(year) ? 8784 : 8760;
                SortedSet<int> sorted = new SortedSet<int>();
                foreach (int hourOfYear in hoursOfYear)
                {
                    if (hourOfYear >= 0 && hourOfYear < count)
                    {
                        sorted.Add(hourOfYear);
                    }
                }

                this.hoursOfYear = new List<int>(sorted);
            }
        }

        public AnalysisPeriod(AnalysisPeriod analysisPeriod)
        {
            if (analysisPeriod != null)
            {
                year = analysisPeriod.year;
                startMonth = analysisPeriod.startMonth;
                startDay = analysisPeriod.startDay;
                endMonth = analysisPeriod.endMonth;
                endDay = analysisPeriod.endDay;
                startHour = analysisPeriod.startHour;
                endHour = analysisPeriod.endHour;
                timestep = analysisPeriod.timestep;
                hoursOfYear = analysisPeriod.hoursOfYear == null ? null : new List<int>(analysisPeriod.hoursOfYear);
            }
        }

        public AnalysisPeriod(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public int Year
        {
            get
            {
                return year;
            }
        }

        /// <summary>Start month of the date range (1–12). Inclusive.</summary>
        public int StartMonth
        {
            get
            {
                return startMonth;
            }
            private set
            {
                if (value < 1 || value > 12)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Month must be within 1–12.");
                }
                startMonth = value;
            }
        }

        /// <summary>Start day of the date range. Inclusive. Clamped to the number of days in the month for Year.</summary>
        public int StartDay
        {
            get
            {
                return startDay;
            }
            private set
            {
                startDay = ClampDay(year, startMonth, value);
            }
        }

        /// <summary>End month of the date range (1–12). Inclusive — the whole end day is part of the period.</summary>
        public int EndMonth
        {
            get
            {
                return endMonth;
            }
            private set
            {
                if (value < 1 || value > 12)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Month must be within 1–12.");
                }
                endMonth = value;
            }
        }

        /// <summary>End day of the date range. Inclusive. Clamped to the number of days in the month for Year.</summary>
        public int EndDay
        {
            get
            {
                return endDay;
            }
            private set
            {
                endDay = ClampDay(year, endMonth, value);
            }
        }

        /// <summary>First hour of the day included (0–23). Inclusive.</summary>
        public int StartHour
        {
            get
            {
                return startHour;
            }
            private set
            {
                startHour = Math.Min(Math.Max(value, 0), 23);
            }
        }

        /// <summary>Last hour of the day included (0–23). Inclusive. May be less than StartHour for overnight windows.</summary>
        public int EndHour
        {
            get
            {
                return endHour;
            }
            private set
            {
                endHour = Math.Min(Math.Max(value, 0), 23);
            }
        }

        /// <summary>
        /// Timesteps per hour. Only 1 (hourly) is currently supported; any other value fails at
        /// construction. Reserved for future sub-hourly weather support.
        /// </summary>
        public int Timestep
        {
            get
            {
                return timestep;
            }
            private set
            {
                if (value != 1)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(value), "AnalysisPeriod currently supports hourly periods (Timestep == 1) only.");
                }
                timestep = value;
            }
        }

        /// <summary>
        /// True when the date range wraps the year boundary (e.g. 1 Nov – 28 Feb): months at the end
        /// AND the start of the year are included.
        /// </summary>
        public bool WrapsYear
        {
            get
            {
                return MonthDay(startMonth, startDay) > MonthDay(endMonth, endDay);
            }
        }

        /// <summary>Explicit hours of the year, when this period was built from an explicit list; otherwise null.</summary>
        public List<int> ExplicitHoursOfYear
        {
            get
            {
                return hoursOfYear == null ? null : new List<int>(hoursOfYear);
            }
        }

        /// <summary>True when the given DateTime (weather-timeline) belongs to the period.</summary>
        public bool Contains(DateTime dateTime)
        {
            if (dateTime.Year != year)
            {
                return false;
            }

            if (hoursOfYear != null)
            {
                return hoursOfYear.Contains(HourOfYear(dateTime));
            }

            return ContainsMonthDay(dateTime.Month, dateTime.Day) && ContainsHour(dateTime.Hour);
        }

        /// <summary>True when the given hour of the year (0-based, from 1 Jan 00:00) belongs to the period.</summary>
        public bool Contains(int hourOfYear)
        {
            int count = DateTime.IsLeapYear(year) ? 8784 : 8760;
            if (hourOfYear < 0 || hourOfYear >= count)
            {
                return false;
            }

            if (hoursOfYear != null)
            {
                return hoursOfYear.Contains(hourOfYear);
            }

            DateTime dateTime = new DateTime(year, 1, 1).AddHours(hourOfYear);
            return ContainsMonthDay(dateTime.Month, dateTime.Day) && ContainsHour(dateTime.Hour);
        }

        /// <summary>All hours of the year (0-based) belonging to the period, ascending.</summary>
        public List<int> HoursOfYear()
        {
            if (hoursOfYear != null)
            {
                return new List<int>(hoursOfYear);
            }

            int count = DateTime.IsLeapYear(year) ? 8784 : 8760;
            List<int> result = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (Contains(i))
                {
                    result.Add(i);
                }
            }

            return result;
        }

        /// <summary>All DateTimes (weather-timeline, on the hour) belonging to the period, ascending.</summary>
        public List<DateTime> DateTimes()
        {
            List<int> hoursOfYear = HoursOfYear();
            DateTime start = new DateTime(year, 1, 1);
            return hoursOfYear.ConvertAll(x => start.AddHours(x));
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("Year"))
            {
                year = jObject["Year"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("StartMonth"))
            {
                StartMonth = jObject["StartMonth"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("StartDay"))
            {
                StartDay = jObject["StartDay"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("EndMonth"))
            {
                EndMonth = jObject["EndMonth"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("EndDay"))
            {
                EndDay = jObject["EndDay"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("StartHour"))
            {
                StartHour = jObject["StartHour"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("EndHour"))
            {
                EndHour = jObject["EndHour"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("Timestep"))
            {
                Timestep = jObject["Timestep"]?.GetValue<int>() ?? default;
            }

            hoursOfYear = null;
            if (jObject.ContainsKey("HoursOfYear"))
            {
                JsonArray jArray = jObject["HoursOfYear"] as JsonArray;
                if (jArray != null)
                {
                    hoursOfYear = new List<int>();
                    foreach (JsonNode jNode in jArray)
                    {
                        hoursOfYear.Add(jNode?.GetValue<int>() ?? default);
                    }
                }
            }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            jObject.Add("Year", year);
            jObject.Add("StartMonth", startMonth);
            jObject.Add("StartDay", startDay);
            jObject.Add("EndMonth", endMonth);
            jObject.Add("EndDay", endDay);
            jObject.Add("StartHour", startHour);
            jObject.Add("EndHour", endHour);
            jObject.Add("Timestep", timestep);

            if (hoursOfYear != null)
            {
                JsonArray jArray = new JsonArray();
                foreach (int hourOfYear in hoursOfYear)
                {
                    jArray.Add(hourOfYear);
                }
                jObject.Add("HoursOfYear", jArray);
            }

            return jObject;
        }

        public override string ToString()
        {
            if (hoursOfYear != null)
            {
                return string.Format("AnalysisPeriod: {0}, {1} explicit hours", year, hoursOfYear.Count);
            }

            return string.Format("AnalysisPeriod: {0}, {1:D2}/{2:D2}-{3:D2}/{4:D2}, {5:D2}:00-{6:D2}:59", year, startDay, startMonth, endDay, endMonth, startHour, endHour);
        }

        private static int HourOfYear(DateTime dateTime)
        {
            return (int)(dateTime - new DateTime(dateTime.Year, 1, 1)).TotalHours;
        }

        private static int MonthDay(int month, int day)
        {
            return month * 100 + day;
        }

        private static int ClampDay(int year, int month, int day)
        {
            if (month < 1 || month > 12)
            {
                return day;
            }

            return Math.Min(Math.Max(day, 1), DateTime.DaysInMonth(year, month));
        }

        private bool ContainsMonthDay(int month, int day)
        {
            int start = MonthDay(startMonth, startDay);
            int end = MonthDay(endMonth, endDay);
            int value = MonthDay(month, day);

            if (start <= end)
            {
                return value >= start && value <= end;
            }

            // Year-wrapping range (e.g. heating season 1 Nov – 28 Feb).
            return value >= start || value <= end;
        }

        private bool ContainsHour(int hour)
        {
            if (startHour <= endHour)
            {
                return hour >= startHour && hour <= endHour;
            }

            // Overnight window (e.g. 22:00 – 06:00).
            return hour >= startHour || hour <= endHour;
        }
    }
}

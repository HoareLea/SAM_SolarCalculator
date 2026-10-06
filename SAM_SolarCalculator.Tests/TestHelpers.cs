// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Core;
using SAM.Weather;

namespace SAM.SolarCalculator.Tests
{
    internal static class TestHelpers
    {
        public static Location London()
        {
            Location location = new Location("London", -0.1278, 51.5074, 0);
            location.SetValue(LocationParameter.TimeZone, "UTC+00:00");
            return location;
        }

        /// <summary>
        /// Synthetic hourly weather for one year with the given per-day radiation profile.
        /// Keys are SAM WeatherDataType names (the same keys the EPW import writes).
        /// Values are W/m2 on the weather-timeline hour.
        /// </summary>
        public static WeatherData SyntheticWeatherData(int year, Location location, Func<DateTime, Tuple<double, double, double>> globalDiffuseDirect_NormalNotUsed)
        {
            WeatherData weatherData = new WeatherData("Synthetic", "Synthetic test weather", location.Latitude, location.Longitude, location.Elevation);
            weatherData.SetValue(WeatherDataParameter.TimeZone, "UTC+00:00");

            DateTime start = new DateTime(year, 1, 1);
            int hours = DateTime.IsLeapYear(year) ? 8784 : 8760;
            for (int i = 0; i < hours; i++)
            {
                DateTime dateTime = start.AddHours(i);
                Tuple<double, double, double> values = globalDiffuseDirect_NormalNotUsed(dateTime);
                Dictionary<string, double> dictionary = new Dictionary<string, double>
                {
                    { WeatherDataType.GlobalSolarRadiation.ToString(), values.Item1 },
                    { WeatherDataType.DiffuseSolarRadiation.ToString(), values.Item2 },
                    { WeatherDataType.DryBulbTemperature.ToString(), 12.0 },
                };
                weatherData.Add(dateTime, dictionary);
            }

            return weatherData;
        }

        /// <summary>
        /// Synthetic hourly weather that ALSO populates the ambiguous DirectSolarRadiation field
        /// (B6 regression): global and diffuse come from the profile, direct is whatever the caller
        /// says — typically a genuine DNI, or a deliberately absurd sentinel. The new aperture
        /// workflow must produce identical results whether this field is present or absent.
        /// </summary>
        public static WeatherData SyntheticWeatherData_WithDirectField(int year, Location location, Func<DateTime, Tuple<double, double>> globalDiffuse, Func<DateTime, double> direct)
        {
            WeatherData weatherData = new WeatherData("Synthetic", "Synthetic test weather", location.Latitude, location.Longitude, location.Elevation);
            weatherData.SetValue(WeatherDataParameter.TimeZone, "UTC+00:00");

            DateTime start = new DateTime(year, 1, 1);
            int hours = DateTime.IsLeapYear(year) ? 8784 : 8760;
            for (int i = 0; i < hours; i++)
            {
                DateTime dateTime = start.AddHours(i);
                Tuple<double, double> values = globalDiffuse(dateTime);
                Dictionary<string, double> dictionary = new Dictionary<string, double>
                {
                    { WeatherDataType.GlobalSolarRadiation.ToString(), values.Item1 },
                    { WeatherDataType.DiffuseSolarRadiation.ToString(), values.Item2 },
                    { WeatherDataType.DryBulbTemperature.ToString(), 12.0 },
                };

                double directValue = direct == null ? double.NaN : direct(dateTime);
                if (!double.IsNaN(directValue))
                {
                    dictionary[WeatherDataType.DirectSolarRadiation.ToString()] = directValue;
                }

                weatherData.Add(dateTime, dictionary);
            }

            return weatherData;
        }

        /// <summary>
        /// Synthetic weather whose radiation is a pure function of the SUN POSITION at
        /// (timestamp + sunSampleShiftInMinutes). Because solar elevation is symmetric about solar
        /// noon in solar time, the resulting series is AM/PM symmetric on the same timeline the
        /// analysis samples the sun on — which is what makes the east/west symmetry test able to
        /// detect a timestamp-midpoint error rather than merely reproduce it.
        /// </summary>
        /// <param name="peakGlobal">Global horizontal irradiance at zenith sun, W/m2.</param>
        /// <param name="diffuseFraction">Diffuse share of global.</param>
        public static WeatherData SolarSymmetricWeatherData(int year, Location location, double sunSampleShiftInMinutes, double peakGlobal = 900.0, double diffuseFraction = 0.2)
        {
            return SyntheticWeatherData(year, location, dateTime =>
            {
                DateTime sunTime = sunSampleShiftInMinutes == 0 ? dateTime : dateTime.AddMinutes(sunSampleShiftInMinutes);
                if (!SAM.Geometry.SolarCalculator.Query.TryGetSunAngles(location, sunTime, out double altitude, out _))
                {
                    return Tuple.Create(0.0, 0.0, 0.0);
                }

                double sinElevation = Math.Sin(altitude * Math.PI / 180.0);
                if (sinElevation <= 0)
                {
                    return Tuple.Create(0.0, 0.0, 0.0);
                }

                double global = peakGlobal * sinElevation;
                return Tuple.Create(global, global * diffuseFraction, 0.0);
            });
        }
    }
}

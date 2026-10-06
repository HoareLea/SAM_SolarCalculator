// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Weather;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// Temperature-balance desirability: a genuine proxy for cooling vs heating demand that needs
    /// only the EPW. Weight = clamp((dryBulbTemperature - balanceTemperature) / band, -1, +1):
    /// above the balance temperature blocking is beneficial (positive), below it harmful
    /// (negative), with a linear transition of width 2 x band around the balance point.
    ///
    /// Approximation honesty: outdoor dry-bulb vs balance temperature is a crude stand-in for the
    /// zone's net cooling-minus-heating load (the true Shaderade weighting). It ignores internal
    /// gains, solar gains already captured, thermal mass and scheduling. Documented approximation,
    /// not a silent one.
    ///
    /// A missing dry-bulb value (NaN) yields exactly zero weight rather than a fabricated one.
    /// </summary>
    public class TemperatureDesirability : IDesirabilityStrategy, IJSAMObject
    {
        private double balanceTemperature = 15.5;
        private double band = 1.0;

        public TemperatureDesirability(double balanceTemperature = 15.5, double band = 1.0)
        {
            this.balanceTemperature = balanceTemperature;
            this.band = band <= 0 ? 1.0 : band;
        }

        public TemperatureDesirability(TemperatureDesirability temperatureDesirability)
        {
            if (temperatureDesirability != null)
            {
                balanceTemperature = temperatureDesirability.balanceTemperature;
                band = temperatureDesirability.band;
            }
        }

        public TemperatureDesirability(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>Balance temperature, degC. Above it sun is unwanted; below it, wanted.</summary>
        public double BalanceTemperature
        {
            get
            {
                return balanceTemperature;
            }
        }

        /// <summary>Half-width of the linear transition band around the balance temperature, degC.</summary>
        public double Band
        {
            get
            {
                return band;
            }
        }

        public double Weight(DateTime dateTime, WeatherHour weatherHour, ApertureSolarTarget target)
        {
            if (weatherHour == null)
            {
                return 0.0;
            }

            double dryBulbTemperature = weatherHour.DryBulbTemperature;
            if (double.IsNaN(dryBulbTemperature))
            {
                return 0.0;
            }

            double weight = (dryBulbTemperature - balanceTemperature) / band;
            return Math.Max(-1.0, Math.Min(1.0, weight));
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("BalanceTemperature"))
            {
                balanceTemperature = jObject["BalanceTemperature"]?.GetValue<double>() ?? 15.5;
            }

            if (jObject.ContainsKey("Band"))
            {
                band = jObject["Band"]?.GetValue<double>() ?? 1.0;
            }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            jObject.Add("BalanceTemperature", balanceTemperature);
            jObject.Add("Band", band);
            return jObject;
        }
    }
}

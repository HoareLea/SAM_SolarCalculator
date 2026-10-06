// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// The environmental and solar state of ONE aperture in ONE weather hour: what the sun is doing
    /// relative to this opening, and what the outdoor air is doing at the same time.
    ///
    /// It is the common hourly quantity every dynamic-shading control rule needs — retractable
    /// awnings, external blinds, louvres, or a plain "unwanted solar period" generator — computed
    /// once, from the aperture's own orientation, rather than re-derived per device.
    ///
    /// APERTURE-PLANE SOLAR, NOT GLOBAL HORIZONTAL. <see cref="ApertureDirectIrradiance"/> is the
    /// direct beam arriving on THIS opening's plane, DNI x max(0, cos(incidence)). A north-facing
    /// and a south-facing window in the same hour therefore see completely different values, which
    /// is the whole point: a control threshold read on global horizontal irradiance would deploy
    /// both at the same instant.
    ///
    /// TRANSIENT BY DESIGN. There are up to 8784 of these per aperture-year and every field is
    /// reproducible from the weather file, the site and the aperture. It is deliberately NOT an
    /// IJSAMObject: what persists is the sparse <see cref="SolarControlProfile"/> built from them.
    ///
    /// A missing weather field arrives here as NaN and is passed on as NaN. Nothing is invented.
    ///
    /// CONTEXT IS NOT CONSIDERED. <see cref="SunInFrontOfAperture"/> is pure geometry: it says the
    /// sun is on the outward side of the opening, NOT that anything reaches it. How much of the
    /// opening the surroundings actually leave lit is a separate question, answered by the existing
    /// SolarVisibilityCache, and it is deliberately left to a later phase.
    /// </summary>
    public class ApertureSolarHour
    {
        private int hourOfYear;
        private DateTime dateTime;
        private double solarElevation = double.NaN;
        private double solarAzimuth = double.NaN;
        private double cosIncidence = double.NaN;
        private double directNormalIrradiance = double.NaN;
        private double apertureDirectIrradiance = double.NaN;
        private double dryBulbTemperature = double.NaN;
        private double windSpeed = double.NaN;

        /// <param name="hourOfYear">Hour of the year, 0-based from 1 Jan 00:00 of the weather year.</param>
        /// <param name="dateTime">Weather-timeline timestamp (whole hour).</param>
        /// <param name="solarElevation">Solar elevation at the SAMPLED sun time, degrees.</param>
        /// <param name="solarAzimuth">Solar compass azimuth at the sampled sun time, degrees clockwise from north.</param>
        /// <param name="cosIncidence">Cosine of the angle between the aperture outward normal and the sun. Negative behind the aperture.</param>
        /// <param name="directNormalIrradiance">Direct normal irradiance, W/m2.</param>
        /// <param name="apertureDirectIrradiance">Direct beam on the aperture plane, W/m2 (zero when the sun is behind it).</param>
        /// <param name="dryBulbTemperature">Outdoor dry-bulb temperature, degC. NaN when the weather file has none.</param>
        /// <param name="windSpeed">Wind speed as recorded by the weather file, m/s. NaN when it has none.</param>
        public ApertureSolarHour(int hourOfYear, DateTime dateTime, double solarElevation, double solarAzimuth, double cosIncidence, double directNormalIrradiance, double apertureDirectIrradiance, double dryBulbTemperature, double windSpeed)
        {
            this.hourOfYear = hourOfYear;
            this.dateTime = dateTime;
            this.solarElevation = solarElevation;
            this.solarAzimuth = solarAzimuth;
            this.cosIncidence = cosIncidence;
            this.directNormalIrradiance = directNormalIrradiance;
            this.apertureDirectIrradiance = apertureDirectIrradiance;
            this.dryBulbTemperature = dryBulbTemperature;
            this.windSpeed = windSpeed;
        }

        public ApertureSolarHour(ApertureSolarHour apertureSolarHour)
        {
            if (apertureSolarHour != null)
            {
                hourOfYear = apertureSolarHour.hourOfYear;
                dateTime = apertureSolarHour.dateTime;
                solarElevation = apertureSolarHour.solarElevation;
                solarAzimuth = apertureSolarHour.solarAzimuth;
                cosIncidence = apertureSolarHour.cosIncidence;
                directNormalIrradiance = apertureSolarHour.directNormalIrradiance;
                apertureDirectIrradiance = apertureSolarHour.apertureDirectIrradiance;
                dryBulbTemperature = apertureSolarHour.dryBulbTemperature;
                windSpeed = apertureSolarHour.windSpeed;
            }
        }

        /// <summary>Hour of the year, 0-based: 0 = 1 Jan 00:00.</summary>
        public int HourOfYear
        {
            get
            {
                return hourOfYear;
            }
        }

        /// <summary>The weather-timeline timestamp. The sun was sampled at this plus the timeline's shift.</summary>
        public DateTime DateTime
        {
            get
            {
                return dateTime;
            }
        }

        /// <summary>Solar elevation above the horizon at the sampled sun time, degrees. Negative at night.</summary>
        public double SolarElevation
        {
            get
            {
                return solarElevation;
            }
        }

        /// <summary>Solar compass azimuth at the sampled sun time, degrees clockwise from north.</summary>
        public double SolarAzimuth
        {
            get
            {
                return solarAzimuth;
            }
        }

        /// <summary>True when the sun is above the horizon. Nothing a shading device does matters otherwise.</summary>
        public bool SunAboveHorizon
        {
            get
            {
                return solarElevation > 0;
            }
        }

        /// <summary>
        /// True when the sun lies on the OUTWARD side of the aperture, so its beam could strike the
        /// opening. This is pure geometry: it says nothing about whether anything is in the way.
        /// </summary>
        public bool SunInFrontOfAperture
        {
            get
            {
                return cosIncidence > 0;
            }
        }

        /// <summary>Cosine of the solar incidence angle on the aperture. Negative when the sun is behind it.</summary>
        public double CosIncidence
        {
            get
            {
                return cosIncidence;
            }
        }

        /// <summary>Direct normal irradiance, W/m2 — the beam measured perpendicular to itself.</summary>
        public double DirectNormalIrradiance
        {
            get
            {
                return directNormalIrradiance;
            }
        }

        /// <summary>
        /// Direct beam on the APERTURE PLANE, W/m2: DNI x max(0, cos(incidence)). Exactly zero when
        /// the sun is behind the opening. This is the quantity a façade shading threshold is read on.
        /// </summary>
        public double ApertureDirectIrradiance
        {
            get
            {
                return apertureDirectIrradiance;
            }
        }

        /// <summary>Outdoor dry-bulb temperature, degC. NaN when the weather file does not carry it.</summary>
        public double DryBulbTemperature
        {
            get
            {
                return dryBulbTemperature;
            }
        }

        /// <summary>
        /// Wind speed as recorded by the weather file, m/s. NaN when it does not carry one.
        ///
        /// This is the weather file's own hourly value (10 m open-country in an EPW), NOT a local
        /// instantaneous gust at the device. It is used as an operating constraint, never as a
        /// structural verification.
        /// </summary>
        public double WindSpeed
        {
            get
            {
                return windSpeed;
            }
        }

        public override string ToString()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "ApertureSolarHour [HOY {0}, {1:yyyy-MM-dd HH:mm}, sun {2:0.#}° / {3:0.#}°, aperture beam {4:0} W/m²]",
                hourOfYear, dateTime, solarElevation, solarAzimuth, apertureDirectIrradiance);
        }
    }
}

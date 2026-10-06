// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// The control rule a dynamic shading device is operated by: WHEN is solar unwanted at this
    /// window, and WHEN may the device be deployed at all.
    ///
    /// The two questions are deliberately kept apart:
    ///
    ///   SHADING WANTED (the solar and temperature criteria) — an environmental judgement about the
    ///   solar arriving at the opening. It is what feeds the shading optimisation as desirability.
    ///
    ///   DEPLOYMENT ALLOWED (the wind constraint) — an operating limit of the hardware. A gale does
    ///   not make solar welcome; it stops the awning coming out. Wind therefore never enters the
    ///   desirability weight, only the deployment schedule.
    ///
    /// UNITS, in the terms an engineer sets them in:
    ///   MinimumApertureIrradiance   W/m2 of DIRECT beam on the window plane (not global horizontal)
    ///   MinimumOutdoorTemperature   degC outdoor dry-bulb
    ///   MaximumWindSpeed            m/s, as recorded by the weather file
    ///
    /// The last two are OPTIONAL and off when NaN. A criterion that is off takes no part in the
    /// decision — it neither blocks an AND nor satisfies an OR — so leaving them empty reduces the
    /// rule to the solar threshold alone under either logic.
    ///
    /// SURROUNDING CONTEXT IS NOT A CRITERION HERE. Whether the neighbouring building actually
    /// leaves the window in sun is a separate question, answered by the existing
    /// SolarVisibilityCache, and it is deliberately left to a later phase.
    /// </summary>
    public class SolarControlSettings : IJSAMObject, ISolarObject
    {
        /// <summary>
        /// Default aperture-plane direct-beam threshold, W/m2. A mid-range setting for the façade
        /// setpoints commercial blind and awning controllers are commissioned at (roughly
        /// 150-300 W/m2 on the glass). It is a DEFAULT, not a recommendation: a project with a real
        /// glare or overheating brief should set its own.
        /// </summary>
        public const double DefaultMinimumApertureIrradiance = 200.0;

        private double minimumApertureIrradiance = DefaultMinimumApertureIrradiance;
        private double minimumOutdoorTemperature = double.NaN;
        private double maximumWindSpeed = double.NaN;
        private SolarControlLogic controlLogic = SolarControlLogic.And;

        /// <param name="minimumApertureIrradiance">Direct beam on the window plane at or above which solar is unwanted, W/m2.</param>
        /// <param name="minimumOutdoorTemperature">Outdoor dry-bulb at or above which solar is unwanted, degC. NaN = criterion not used.</param>
        /// <param name="maximumWindSpeed">Highest wind speed the device may stay deployed in, m/s. NaN = no constraint.</param>
        /// <param name="controlLogic">How the solar and temperature criteria combine. Undefined is read as And.</param>
        public SolarControlSettings(double minimumApertureIrradiance = DefaultMinimumApertureIrradiance, double minimumOutdoorTemperature = double.NaN, double maximumWindSpeed = double.NaN, SolarControlLogic controlLogic = SolarControlLogic.And)
        {
            this.minimumApertureIrradiance = minimumApertureIrradiance;
            this.minimumOutdoorTemperature = minimumOutdoorTemperature;
            this.maximumWindSpeed = maximumWindSpeed;
            this.controlLogic = controlLogic == SolarControlLogic.Undefined ? SolarControlLogic.And : controlLogic;
        }

        public SolarControlSettings(SolarControlSettings solarControlSettings)
        {
            if (solarControlSettings != null)
            {
                minimumApertureIrradiance = solarControlSettings.minimumApertureIrradiance;
                minimumOutdoorTemperature = solarControlSettings.minimumOutdoorTemperature;
                maximumWindSpeed = solarControlSettings.maximumWindSpeed;
                controlLogic = solarControlSettings.controlLogic;
            }
        }

        public SolarControlSettings(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>Direct beam on the WINDOW PLANE at or above which solar is unwanted, W/m2.</summary>
        public double MinimumApertureIrradiance
        {
            get
            {
                return minimumApertureIrradiance;
            }
        }

        /// <summary>Outdoor dry-bulb at or above which solar is unwanted, degC. NaN when not used.</summary>
        public double MinimumOutdoorTemperature
        {
            get
            {
                return minimumOutdoorTemperature;
            }
        }

        /// <summary>Highest wind speed the device may stay deployed in, m/s. NaN when unconstrained.</summary>
        public double MaximumWindSpeed
        {
            get
            {
                return maximumWindSpeed;
            }
        }

        public SolarControlLogic ControlLogic
        {
            get
            {
                return controlLogic;
            }
        }

        /// <summary>True when an outdoor-temperature criterion is in use.</summary>
        public bool TemperatureCriterionInUse
        {
            get
            {
                return !double.IsNaN(minimumOutdoorTemperature);
            }
        }

        /// <summary>True when a wind operating constraint is in use.</summary>
        public bool WindConstraintInUse
        {
            get
            {
                return !double.IsNaN(maximumWindSpeed);
            }
        }

        /// <summary>
        /// Whether this rule can be evaluated at all. Nothing is clamped: on failure
        /// <paramref name="message"/> says exactly what to change.
        /// </summary>
        /// <param name="message">Null when valid; an actionable sentence otherwise.</param>
        public bool IsValid(out string message)
        {
            message = null;

            if (double.IsNaN(minimumApertureIrradiance) || double.IsInfinity(minimumApertureIrradiance) || minimumApertureIrradiance < 0)
            {
                message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The solar threshold must be a finite number of zero or more W/m² (it is {0}). It is the DIRECT beam on the window plane at or above which solar counts as unwanted; typical commissioned setpoints are 150-300 W/m².",
                    minimumApertureIrradiance);
                return false;
            }

            if (WindConstraintInUse && (double.IsInfinity(maximumWindSpeed) || maximumWindSpeed < 0))
            {
                message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The maximum wind speed must be a finite number of zero or more m/s (it is {0}). Leave it empty for a device with no wind limit.",
                    maximumWindSpeed);
                return false;
            }

            if (TemperatureCriterionInUse && double.IsInfinity(minimumOutdoorTemperature))
            {
                message = "The outdoor-temperature threshold must be a finite number of °C. Leave it empty to control on solar alone.";
                return false;
            }

            return true;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("MinimumApertureIrradiance"))
            {
                minimumApertureIrradiance = jObject["MinimumApertureIrradiance"]?.GetValue<double>() ?? DefaultMinimumApertureIrradiance;
            }

            // Absent means "criterion not used" — the same thing NaN means in memory. Nothing is
            // defaulted into existence here: a rule that did not use temperature must not acquire
            // one by being saved and reloaded.
            minimumOutdoorTemperature = jObject.ContainsKey("MinimumOutdoorTemperature")
                ? (jObject["MinimumOutdoorTemperature"]?.GetValue<double>() ?? double.NaN)
                : double.NaN;

            maximumWindSpeed = jObject.ContainsKey("MaximumWindSpeed")
                ? (jObject["MaximumWindSpeed"]?.GetValue<double>() ?? double.NaN)
                : double.NaN;

            controlLogic = SolarControlLogic.And;
            if (jObject.ContainsKey("ControlLogic")
                && Enum.TryParse(jObject["ControlLogic"]?.GetValue<string>(), out SolarControlLogic value)
                && Enum.IsDefined(typeof(SolarControlLogic), value)
                && value != SolarControlLogic.Undefined)
            {
                controlLogic = value;
            }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            jObject.Add("MinimumApertureIrradiance", minimumApertureIrradiance);

            // NaN is not written: it is not a JSON number, and its meaning here — "this criterion is
            // not in use" — is carried perfectly well by the key being absent.
            if (TemperatureCriterionInUse)
            {
                jObject.Add("MinimumOutdoorTemperature", minimumOutdoorTemperature);
            }

            if (WindConstraintInUse)
            {
                jObject.Add("MaximumWindSpeed", maximumWindSpeed);
            }

            jObject.Add("ControlLogic", controlLogic.ToString());

            return jObject;
        }

        public override string ToString()
        {
            System.Text.StringBuilder result = new System.Text.StringBuilder();
            result.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "solar ≥ {0:0.#} W/m² on the window plane", minimumApertureIrradiance);

            if (TemperatureCriterionInUse)
            {
                result.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, " {0} outdoor ≥ {1:0.#} °C", controlLogic == SolarControlLogic.Or ? "OR" : "AND", minimumOutdoorTemperature);
            }

            result.Append(WindConstraintInUse
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "; retracts above {0:0.#} m/s", maximumWindSpeed)
                : "; no wind limit");

            return result.ToString();
        }
    }
}

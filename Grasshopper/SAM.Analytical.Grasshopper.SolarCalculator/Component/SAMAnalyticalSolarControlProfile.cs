// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SAM.Analytical.Grasshopper.SolarCalculator.Properties;
using SAM.Analytical.SolarCalculator;
using SAM.Core.Grasshopper;
using SAM.Core.SolarCalculator;
using SAM.Weather;
using System;
using System.Collections.Generic;
using SolarCreate = SAM.Analytical.SolarCalculator.Create;

namespace SAM.Analytical.Grasshopper.SolarCalculator
{
    public class SAMAnalyticalSolarControlProfile : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7f3c9a10-5b28-4e63-9a41-6c0d2e7b1108");

        /// <summary>
        /// The latest version of this component.
        /// </summary>
        public override string LatestComponentVersion => "1.0.0";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        public SAMAnalyticalSolarControlProfile()
          : base("SAMAnalytical.SolarControlProfile", "SAMAnalytical.SolarControlProfile",
              "SUMMARY\nWorks out, HOUR BY HOUR ACROSS THE WEATHER YEAR, when solar is unwanted at ONE window and when a movable shading device could actually be deployed. Use it for retractable awnings, external blinds, louvres, or simply to generate an unwanted-solar period from real weather rather than from a calendar.\n\nIt is arithmetic on the weather file and the window's orientation only — no ray tracing, no model — so it runs in about a second for a whole year.\n\nTHE THRESHOLD IS READ ON THE WINDOW PLANE, NOT ON THE SKY. The solar criterion uses the DIRECT beam arriving on this window's own plane [W/m²]. A north and a south window on the same building therefore get completely different hours from the same weather, which a global-horizontal threshold cannot do.\n\nDAYLIGHT BELONGS TO THE SITE; SUN ON THE WINDOW BELONGS TO THE WINDOW. Both are reported and they are not the same number. Every criterion is judged only on the hours the sun actually reaches this opening, so a hot afternoon can never request shading for a façade the sun is behind.\n\nINPUTS\n  _apertureSolarTarget — the window, from SAMAnalytical.ApertureSolarTargets. Its orientation is what makes the answer window-specific.\n  _weatherData — SAM WeatherData (hourly). Its location drives the sun position.\n  _solarThreshold_ — direct beam ON THE WINDOW PLANE at or above which solar counts as unwanted [W/m²]. Default 200. Commissioned blind and awning setpoints are usually 150-300 W/m².\n  _temperatureThreshold_ — OPTIONAL outdoor dry-bulb at or above which solar counts as unwanted [°C]. Leave it empty to control on solar alone.\n  _maximumWindSpeed_ — OPTIONAL highest wind speed the device may stay deployed in [m/s], as recorded by the weather file. An OPERATING LIMIT, not a reason solar is unwanted: it retracts the device, it does not change what the window wanted. Leave it empty for a device with no wind limit.\n  _logic_ — And (default) or Or, for how the solar and temperature criteria combine. With no temperature threshold set, both behave identically: an absent criterion is never treated as satisfied.\n  _year_ — weather year. Leave empty (or -1) to use the weather file's own.\n  _analysisPeriod_ — OPTIONAL restriction to part of the year. Default: the whole year.\n\nOUTPUTS\n  controlProfile — the whole hourly answer as one object, to carry downstream.\n  externalDesirability — the same answer as a desirability strategy. Wire it into RationaliseShading's _desirability_ and the search sizes the device against exactly these hours.\n  hoursOfYear / weights — the weighted hours and their weights, as two parallel lists. Weights are +1 (block me) on each requested hour.\n  daylightHOYs — hours the sun was above the horizon. A property of the SITE: identical for every window on the building.\n  apertureSunHOYs — hours the sun actually REACHES THIS WINDOW: above the horizon, in front of the opening, and putting a non-zero beam on its plane. Read this one for 'how many hours a year does this façade see sun?'.\n  solarDemandHOYs — aperture-sun hours whose window-plane beam met the solar threshold.\n  shadeDemandHOYs — hours SHADING IS REQUESTED because conditions make this window's solar unwanted.\n  shadeOnHOYs — hours the device is ACTUALLY DEPLOYED: requested AND allowed by the wind limit.\n  highWindHOYs — requested hours the wind limit REFUSED. shadeDemand = shadeOn + highWind, exactly.\n  daylightHours / apertureSunHours / solarDemandHours / shadeDemandHours / shadeOnHours / highWindHours — the same as counts.\n  shadeUse — deployed hours as a percentage of requested hours [%]. Unavailable (NaN) when nothing was requested.\n  description — the whole answer in one readable line.\n\nNOTES\nALL HOURS ARE 0-BASED HOURS OF THE YEAR: 0 = 1 Jan 00:00, 8759 = 31 Dec 23:00. They match the HOYs of SAMAnalytical.AnalysisPeriod.\nREQUESTED IS NOT THE SAME AS DEPLOYED. shadeDemandHOYs is what the weather asked for; shadeOnHOYs is what the hardware was allowed to do about it. Read them side by side — a large highWindHOYs means the device is retracted exactly when it is wanted.\nSHADING IS NEVER REQUESTED WHERE IT COULD DO NOTHING. Demand is only raised on the hours the sun actually reaches this window, so under Or logic neither a warm night nor a hot afternoon on the shaded side of the building deploys an awning.\nNOTHING IS INVENTED FROM MISSING WEATHER. An hour with no dry-bulb cannot satisfy the temperature criterion. An hour with no wind speed leaves the wind limit UNCHECKED rather than breached: the hour stays operable and is never reported as high wind, and the node warns you how many hours that was.\nTHE WIND SPEED IS THE WEATHER FILE'S OWN hourly value (10 m open country in an EPW), not a local instantaneous gust at the device. This is an annual design profile, not a live safety controller and not a structural verification.\n\nEXAMPLE\nApertureSolarTargets → SolarControlProfile (_solarThreshold_ = 200, _temperatureThreshold_ = 18, _maximumWindSpeed_ = 10.5).\nSolarControlProfile.externalDesirability → RationaliseShading._desirability_ — the search sizes the device against exactly these hours.\nSolarControlProfile.controlProfile → ShadingOperation._controlProfiles (one profile per window), alongside the device itself — e.g. RationaliseShading.shadingDevice → ShadingOperation._shadingDevice — to measure what the retractable device actually does.",
              "SAM", "Solar")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooApertureSolarTargetParam() { Name = "_apertureSolarTarget", NickName = "_apertureSolarTarget", Description = "The window, from SAMAnalytical.ApertureSolarTargets.\nIts orientation is what makes the generated hours window-specific", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_weatherData", NickName = "_weatherData", Description = "SAM WeatherData (hourly). Its location drives the sun position", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number solarThreshold = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_solarThreshold_", NickName = "_solarThreshold_", Description = "Direct beam ON THE WINDOW PLANE at or above which solar counts as unwanted [W/m²].\nNOT global horizontal irradiance: the same hour can clear it at one façade and not at another.\nDefault 200. Commissioned blind and awning setpoints are usually 150-300 W/m²", Access = GH_ParamAccess.item };
                solarThreshold.SetPersistentData(SolarControlSettings.DefaultMinimumApertureIrradiance);
                result.Add(new GH_SAMParam(solarThreshold, ParamVisibility.Binding));

                // Deliberately NO persistent default: an empty wire is how the user says "do not use
                // this criterion", and a default would quietly turn the criterion on for everyone.
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_temperatureThreshold_", NickName = "_temperatureThreshold_", Description = "OPTIONAL outdoor dry-bulb at or above which solar counts as unwanted [°C].\nLeave empty to control on solar alone — an unused criterion is never treated as satisfied", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_maximumWindSpeed_", NickName = "_maximumWindSpeed_", Description = "OPTIONAL highest wind speed the device may stay deployed in [m/s], as recorded by the weather file.\nAn OPERATING LIMIT: it retracts the device, it does not change what the window wanted.\nLeave empty for a device with no wind limit", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_logic_", NickName = "_logic_", Description = "How the solar and temperature criteria combine: And (default) or Or.\nWith no temperature threshold set both behave identically", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Integer year = new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "_year_", NickName = "_year_", Description = "Weather year. -1 (default) uses the weather file's own year", Access = GH_ParamAccess.item, Optional = true };
                year.SetPersistentData(-1);
                result.Add(new GH_SAMParam(year, ParamVisibility.Voluntary));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_analysisPeriod_", NickName = "_analysisPeriod_", Description = "OPTIONAL restriction to part of the year, from SAMAnalytical.AnalysisPeriod.\nDefault: the whole year", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));

                return result.ToArray();
            }
        }

        protected override GH_SAMParam[] Outputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "controlProfile", NickName = "controlProfile", Description = "The whole hourly answer as one object", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "externalDesirability", NickName = "externalDesirability", Description = "The same answer as a desirability strategy.\nWire it into SAMAnalytical.RationaliseShading's _desirability_ and the search sizes the device against exactly these hours", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "hoursOfYear", NickName = "hoursOfYear", Description = "The weighted hours of the year (0-based), ascending", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "weights", NickName = "weights", Description = "The weight of each hour in hoursOfYear. +1 = blocking this hour's sun is beneficial", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "daylightHOYs", NickName = "daylightHOYs", Description = "Hours the sun was above the horizon (0-based).\nA property of the SITE: identical for every window on the building", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "apertureSunHOYs", NickName = "apertureSunHOYs", Description = "Hours the sun actually REACHES THIS WINDOW (0-based): above the horizon, in front of the opening, non-zero beam on its plane.\nRead this rather than daylightHOYs for how many hours a year the façade sees sun", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "solarDemandHOYs", NickName = "solarDemandHOYs", Description = "Aperture-sun hours whose window-plane beam met the solar threshold (0-based)", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "shadeDemandHOYs", NickName = "shadeDemandHOYs", Description = "Hours SHADING IS REQUESTED because conditions make this window's solar unwanted (0-based)", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "shadeOnHOYs", NickName = "shadeOnHOYs", Description = "Hours the device is ACTUALLY DEPLOYED: requested and allowed by the wind limit (0-based)", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "highWindHOYs", NickName = "highWindHOYs", Description = "Requested hours the wind limit REFUSED (0-based).\nshadeDemandHOYs = shadeOnHOYs + highWindHOYs, exactly", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "daylightHours", NickName = "daylightHours", Description = "How many hours the sun was above the horizon. The same for every window on the site", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "apertureSunHours", NickName = "apertureSunHours", Description = "How many hours a year the sun actually reaches this window", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "solarDemandHours", NickName = "solarDemandHours", Description = "How many aperture-sun hours met the solar threshold", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "shadeDemandHours", NickName = "shadeDemandHours", Description = "How many hours shading is requested", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "shadeOnHours", NickName = "shadeOnHours", Description = "How many hours the device is actually deployed", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "highWindHours", NickName = "highWindHours", Description = "How many requested hours the wind limit refused", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "shadeUse", NickName = "shadeUse", Description = "Deployed hours as a percentage of requested hours [%]. NaN when nothing was requested", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "description", NickName = "description", Description = "The whole answer in one readable line", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "successful", NickName = "successful", Description = "Successful?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                return result.ToArray();
            }
        }

        /// <summary>
        /// An optional number input: NaN when the wire is empty, which is how the user says "do not
        /// use this criterion". A value that arrives as NaN is treated the same way.
        /// </summary>
        private double OptionalNumber(IGH_DataAccess dataAccess, string name)
        {
            int index = Params.IndexOfInputParam(name);
            if (index == -1)
            {
                return double.NaN;
            }

            double value = double.NaN;
            return dataAccess.GetData(index, ref value) ? value : double.NaN;
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            int index_Successful = Params.IndexOfOutputParam("successful");
            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, false);
            }

            int index = Params.IndexOfInputParam("_apertureSolarTarget");
            ApertureSolarTarget target = null;
            if (index == -1 || !dataAccess.GetData(index, ref target) || target == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply an aperture solar target from SAMAnalytical.ApertureSolarTargets.");
                return;
            }

            index = Params.IndexOfInputParam("_weatherData");
            GH_ObjectWrapper objectWrapper = null;
            WeatherData weatherData = null;
            if (index != -1 && dataAccess.GetData(index, ref objectWrapper))
            {
                weatherData = Query.Value<WeatherData>(objectWrapper);
            }

            if (weatherData == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply SAM WeatherData (hourly). It carries both the site and the hourly values every criterion is read from.");
                return;
            }

            double solarThreshold = OptionalNumber(dataAccess, "_solarThreshold_");
            if (double.IsNaN(solarThreshold))
            {
                solarThreshold = SolarControlSettings.DefaultMinimumApertureIrradiance;
            }

            double temperatureThreshold = OptionalNumber(dataAccess, "_temperatureThreshold_");
            double maximumWindSpeed = OptionalNumber(dataAccess, "_maximumWindSpeed_");

            SolarControlLogic logic = SolarControlLogic.And;
            index = Params.IndexOfInputParam("_logic_");
            if (index != -1)
            {
                GH_ObjectWrapper logicWrapper = null;
                if (dataAccess.GetData(index, ref logicWrapper) && logicWrapper?.Value != null)
                {
                    if (!Query.TryGetEnum(logicWrapper, out logic) || logic == SolarControlLogic.Undefined)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_logic_ was not recognised. Use And or Or.");
                        return;
                    }
                }
            }

            if (logic == SolarControlLogic.Or && double.IsNaN(temperatureThreshold))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Or logic has nothing to combine while _temperatureThreshold_ is empty, so the solar threshold decides on its own. An unused criterion is never treated as satisfied.");
            }

            int year = -1;
            index = Params.IndexOfInputParam("_year_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref year);
            }

            AnalysisPeriod analysisPeriod = null;
            index = Params.IndexOfInputParam("_analysisPeriod_");
            if (index != -1)
            {
                GH_ObjectWrapper periodWrapper = null;
                if (dataAccess.GetData(index, ref periodWrapper) && periodWrapper?.Value != null)
                {
                    analysisPeriod = Query.Value<AnalysisPeriod>(periodWrapper);
                    if (analysisPeriod == null)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_analysisPeriod_ must be an AnalysisPeriod. Use SAMAnalytical.AnalysisPeriod to build one.");
                        return;
                    }
                }
            }

            SolarControlSettings settings = new SolarControlSettings(solarThreshold, temperatureThreshold, maximumWindSpeed, logic);
            if (!settings.IsValid(out string settingsMessage))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, settingsMessage);
                return;
            }

            SolarControlProfile profile = SolarCreate.SolarControlProfile(target, weatherData, settings, year, analysisPeriod);
            if (profile == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The control profile could not be built: the weather file carries no usable year, or the window has no resolved outward normal. Check that the target came from SAMAnalytical.ApertureSolarTargets on this model.");
                return;
            }

            if (year > 0 && profile.Year != year)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, string.Format(System.Globalization.CultureInfo.InvariantCulture, "The weather file carries no data for {0}; the profile was built on its own year, {1}.", year, profile.Year));
            }

            if (profile.MissingWeatherHours > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} hours could not be evaluated (no weather values or no sun position) and take no part in the result.", profile.MissingWeatherHours));
            }

            if (settings.TemperatureCriterionInUse && profile.MissingTemperatureHours > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} of the hours the sun reaches this window carry no outdoor temperature. A value that is not there cannot meet the temperature threshold, so those hours were not claimed by it.", profile.MissingTemperatureHours));
            }

            if (settings.WindConstraintInUse && profile.MissingWindSpeedHours > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} of the hours the sun reaches this window carry no wind speed, so the wind limit could not be checked on them. They are left operable and are NOT reported as high wind — the wind side of this answer rests on an incomplete weather file to that extent.", profile.MissingWindSpeedHours));
            }

            if (profile.ShadeDemandHours == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No hour of the year meets this rule at this window. That is a real answer — read apertureSunHours first: a façade the sun barely reaches has little to shade whatever the threshold is.");
            }

            SetData(dataAccess, "controlProfile", new GooSAMObject(profile));
            SetData(dataAccess, "externalDesirability", new GooSAMObject(profile.ExternalDesirability()));
            SetDataList(dataAccess, "hoursOfYear", profile.WeightedHoursOfYear);
            SetDataList(dataAccess, "weights", profile.Weights);
            SetDataList(dataAccess, "daylightHOYs", profile.DaylightHoursOfYear);
            SetDataList(dataAccess, "apertureSunHOYs", profile.ApertureSunHoursOfYear);
            SetDataList(dataAccess, "solarDemandHOYs", profile.SolarDemandHoursOfYear);
            SetDataList(dataAccess, "shadeDemandHOYs", profile.ShadeDemandHoursOfYear);
            SetDataList(dataAccess, "shadeOnHOYs", profile.ShadeOnHoursOfYear);
            SetDataList(dataAccess, "highWindHOYs", profile.HighWindHoursOfYear);
            SetData(dataAccess, "daylightHours", profile.DaylightHours);
            SetData(dataAccess, "apertureSunHours", profile.ApertureSunHours);
            SetData(dataAccess, "solarDemandHours", profile.SolarDemandHours);
            SetData(dataAccess, "shadeDemandHours", profile.ShadeDemandHours);
            SetData(dataAccess, "shadeOnHours", profile.ShadeOnHours);
            SetData(dataAccess, "highWindHours", profile.HighWindHours);
            SetData(dataAccess, "shadeUse", Query.Percentage(profile.ShadeUseFraction));
            SetData(dataAccess, "description", profile.ToString());

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, true);
            }
        }

        private void SetData(IGH_DataAccess dataAccess, string name, object value)
        {
            int index = Params.IndexOfOutputParam(name);
            if (index != -1)
            {
                dataAccess.SetData(index, value);
            }
        }

        private void SetDataList(IGH_DataAccess dataAccess, string name, System.Collections.IEnumerable values)
        {
            int index = Params.IndexOfOutputParam(name);
            if (index != -1)
            {
                dataAccess.SetDataList(index, values);
            }
        }
    }
}

// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors
using System.ComponentModel;
using SAM.Core;
using SAM.Core.Attributes;
using SAM.Geometry.SolarCalculator;

namespace SAM.Weather.SolarCalculator
{
    [AssociatedTypes(typeof(SolarModel)), Description("SolarModel Parameter")]
    public enum SolarModelParameter
    {
        [ParameterProperties("Weather Data", "Weather Data"), SAMObjectParameterValue(typeof(WeatherData))] WeatherData,
        [ParameterProperties("Solar Visibility Cache", "Per-cell direct-beam sun-bin visibility cache"), SAMObjectParameterValue(typeof(SolarVisibilityCache))] SolarVisibilityCache,
        [ParameterProperties("Sky Visibility Cache", "Per-cell sky/horizon/ground directional visibility cache"), SAMObjectParameterValue(typeof(SkyVisibilityCache))] SkyVisibilityCache,
    }
}
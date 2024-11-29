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
    }
}
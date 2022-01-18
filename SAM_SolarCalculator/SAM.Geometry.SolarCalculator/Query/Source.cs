using System.Reflection;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        public static string Source()
        {
            return Assembly.GetExecutingAssembly().GetName()?.Name;
        }
    }
}
using Newtonsoft.Json.Linq;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Geometry.SolarCalculator
{
    public class Radiation : IJSAMObject, ISolarObject
    {
        private double diffuseHorizontal;
        private double directNormal;
        private double globalHorizontal;

        public Radiation(double directNormal, double diffuseHorizontal, double globalHorizontal)
            : base()
        {
            this.directNormal = directNormal;
            this.diffuseHorizontal = diffuseHorizontal;
            this.globalHorizontal = globalHorizontal;
        }

        public Radiation(JObject jObject)
        {
            FromJObject(jObject);
        }

        public Radiation(Radiation radiation)
        {
            if (radiation != null)
            {
                directNormal = radiation.directNormal;
                diffuseHorizontal = radiation.diffuseHorizontal;
                globalHorizontal = radiation.globalHorizontal;
            }
        }

        public double DiffuseHorizontal
        {
            get
            {
                return diffuseHorizontal;
            }
        }

        public double DirectNormal
        {
            get
            {
                return directNormal;
            }
        }
       
        public double GlobalHorizontal
        {
            get
            {
                return globalHorizontal;
            }
        }
        
        public bool FromJObject(JObject jObject)
        {
            if (jObject == null)
                return false;

            if (jObject.ContainsKey("DirectNormal"))
            {
                directNormal = jObject.Value<double>("DirectNormal");
            }

            if (jObject.ContainsKey("DiffuseHorizontal"))
            {
                diffuseHorizontal = jObject.Value<double>("DiffuseHorizontal");
            }

            if (jObject.ContainsKey("GlobalHorizontal"))
            {
                globalHorizontal = jObject.Value<double>("GlobalHorizontal");
            }

            return true;
        }

        public double GetTotal()
        {
            return directNormal + diffuseHorizontal + globalHorizontal; 
        }
        
        public JObject ToJObject()
        {
            JObject jObject = new JObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            if(!double.IsNaN(directNormal))
            {
                jObject.Add("DirectNormal", directNormal);
            }

            if (!double.IsNaN(diffuseHorizontal))
            {
                jObject.Add("DiffuseHorizontal", diffuseHorizontal);
            }

            if (!double.IsNaN(globalHorizontal))
            {
                jObject.Add("GlobalHorizontal", globalHorizontal);
            }

            return jObject;
        }
    }
}

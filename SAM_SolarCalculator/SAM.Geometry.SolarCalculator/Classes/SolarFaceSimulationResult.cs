using Newtonsoft.Json.Linq;
using SAM.Core;
using SAM.Core.SolarCalculator;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.SolarCalculator
{
    public class SolarFaceSimulationResult : Result, ISolarObject
    {
        private List<Tuple<DateTime, List<Spatial.Face3D>>> sunExposure;

        public SolarFaceSimulationResult(string name, string source, string reference, IEnumerable<Tuple<DateTime, List<Spatial.Face3D>>> sunExposure)
            : base(name, source, reference)
        {
            if(sunExposure != null)
            {
                this.sunExposure = new List<Tuple<DateTime, List<Spatial.Face3D>>>();
                foreach(Tuple<DateTime, List<Spatial.Face3D>> tuple in sunExposure)
                {
                    this.sunExposure.Add(new Tuple<DateTime, List<Spatial.Face3D>>(tuple.Item1, tuple?.Item2 == null ? null : tuple.Item2.ConvertAll(x => new Spatial.Face3D(x))));
                }
            }
        }
        
        public SolarFaceSimulationResult(JObject jObject) 
            : base(jObject)
        {
        }

        public SolarFaceSimulationResult(SolarFaceSimulationResult solarFaceSimulationResult)
            :base(solarFaceSimulationResult)
        {
            if(solarFaceSimulationResult == null)
            {
                return;
            }

            if(solarFaceSimulationResult.sunExposure != null)
            {
                sunExposure = new List<Tuple<DateTime, List<Spatial.Face3D>>>();
                foreach(Tuple<DateTime, List<Spatial.Face3D>> tuple in solarFaceSimulationResult.sunExposure)
                {
                    sunExposure.Add(new Tuple<DateTime, List<Spatial.Face3D>>(tuple.Item1, tuple?.Item2 == null ? null : tuple.Item2.ConvertAll(x => new Spatial.Face3D(x))));
                }
            }
        }

        public List<Spatial.Face3D> GetSunExposureFace3Ds(DateTime dateTime)
        {
            if(sunExposure == null)
            {
                return null;
            }

            Tuple<DateTime, List<Spatial.Face3D>> tuple = sunExposure.Find(x => x.Item1.Equals(dateTime));
            return tuple?.Item2?.ConvertAll(x => new Spatial.Face3D(x));
        }

        public double GetSunExposureArea(DateTime dateTime)
        {
            if (sunExposure == null)
            {
                return 0;
            }

            Tuple<DateTime, List<Spatial.Face3D>> tuple = sunExposure.Find(x => x.Item1.Equals(dateTime));
            if(tuple == null || tuple.Item2 == null || tuple.Item2.Count == 0)
            {
                return 0;
            }

            return tuple.Item2.ConvertAll(x => x.GetArea()).Sum();
        }

        public List<DateTime> DateTimes
        {
            get
            {
                if(sunExposure == null)
                {
                    return null;
                }

                return sunExposure.ConvertAll(x => x.Item1);
            }
        }

        public override bool FromJObject(JObject jObject)
        {
            if (!base.FromJObject(jObject))
                return false;

            if(jObject.ContainsKey("SunExposure"))
            {
                sunExposure = new List<Tuple<DateTime, List<Spatial.Face3D>>>();

                JArray jArray_SunExposure = jObject.Value<JArray>("SunExposure");
                if(jArray_SunExposure != null)
                {
                    for(int i =0; i < jArray_SunExposure.Count; i++)
                    {
                        JArray jArray = jArray_SunExposure[i] as JArray;
                        if(jArray == null || jArray.Count < 2)
                        {
                            continue;
                        }

                        DateTime dateTime = jArray[0].Value<DateTime>();
                        List<Spatial.Face3D> face3Ds = Core.Create.IJSAMObjects<Spatial.Face3D>(jArray[1] as JArray);

                        sunExposure.Add(new Tuple<DateTime, List<Spatial.Face3D>>(dateTime, face3Ds));
                    }
                }
            }

            return true;
        }

        public override JObject ToJObject()
        {
            JObject jObject = base.ToJObject();
            if (jObject == null)
                return null;

            if(sunExposure != null)
            {
                JArray jArray_SunExposure = new JArray();
                foreach(Tuple<DateTime, List<Spatial.Face3D>> tuple in sunExposure)
                {
                    if (tuple == null)
                    {
                        continue;
                    }

                    JArray jArray = new JArray();
                    jArray.Add(tuple.Item1);
                    jArray.Add(Core.Create.JArray(tuple?.Item2));

                    jArray_SunExposure.Add(jArray);
                }

                jObject.Add("SunExposure", jArray_SunExposure);
            }

            return jObject;
        }
    }
}

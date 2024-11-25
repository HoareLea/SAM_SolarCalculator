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
        private List<Tuple<DateTime, Radiation, List<Spatial.Face3D>>> sunExposure;

        public SolarFaceSimulationResult(string name, string source, string reference, IEnumerable<Tuple<DateTime, Radiation, List<Spatial.Face3D>>> sunExposure)
            : base(name, source, reference)
        {
            if(sunExposure != null)
            {
                this.sunExposure = new List<Tuple<DateTime, Radiation, List<Spatial.Face3D>>>();
                foreach(Tuple<DateTime, Radiation, List<Spatial.Face3D>> tuple in sunExposure)
                {
                    this.sunExposure.Add(new Tuple<DateTime, Radiation, List<Spatial.Face3D>>(
                        tuple.Item1, 
                        tuple.Item2?.Clone(),
                        tuple?.Item3 == null ? null : tuple.Item3.ConvertAll(x => new Spatial.Face3D(x))));
                }
            }
        }

        public SolarFaceSimulationResult(SolarFaceSimulationResult solarFaceSimulationResult, IEnumerable<Tuple<DateTime, Radiation, List<Spatial.Face3D>>> sunExposure)
        : base(solarFaceSimulationResult)
        {
            if (sunExposure != null)
            {
                this.sunExposure = new List<Tuple<DateTime, Radiation, List<Spatial.Face3D>>>();
                foreach (Tuple<DateTime, Radiation, List<Spatial.Face3D>> tuple in sunExposure)
                {
                    this.sunExposure.Add(new Tuple<DateTime, Radiation, List<Spatial.Face3D>>(tuple.Item1, tuple.Item2?.Clone(), tuple?.Item3 == null ? null : tuple.Item3.ConvertAll(x => new Spatial.Face3D(x))));
                }
            }
        }

        public SolarFaceSimulationResult(SolarFaceSimulationResult solarFaceSimulationResult, Dictionary<DateTime, Tuple<Radiation, List<Spatial.Face3D>>> sunExposure)
            : base(solarFaceSimulationResult)
        {
            if (sunExposure != null)
            {
                this.sunExposure = new List<Tuple<DateTime, Radiation, List<Spatial.Face3D>>>();
                foreach (DateTime dateTime in sunExposure.Keys)
                {
                    this.sunExposure.Add(new Tuple<DateTime, Radiation, List<Spatial.Face3D>>(dateTime, sunExposure[dateTime].Item1?.Clone(), sunExposure[dateTime].Item2?.ConvertAll(x => new Spatial.Face3D(x))));
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
                sunExposure = new List<Tuple<DateTime, Radiation, List<Spatial.Face3D>>>();
                foreach(Tuple<DateTime, Radiation, List<Spatial.Face3D>> tuple in solarFaceSimulationResult.sunExposure)
                {
                    sunExposure.Add(new Tuple<DateTime, Radiation, List<Spatial.Face3D>>(tuple.Item1, tuple.Item2?.Clone(), tuple?.Item3 == null ? null : tuple.Item3.ConvertAll(x => new Spatial.Face3D(x))));
                }
            }
        }

        public SolarFaceSimulationResult(SolarFaceSimulationResult solarFaceSimulationResult, IEnumerable<DateTime> dateTimes)
            : base(solarFaceSimulationResult)
        {
            if (solarFaceSimulationResult == null)
            {
                return;
            }

            if (solarFaceSimulationResult.sunExposure != null)
            {
                sunExposure = new List<Tuple<DateTime, Radiation, List<Spatial.Face3D>>>();
                foreach (Tuple<DateTime, Radiation, List<Spatial.Face3D>> tuple in solarFaceSimulationResult.sunExposure)
                {
                    if(dateTimes != null && !dateTimes.Contains(tuple.Item1))
                    {
                        continue;
                    }
                    
                    sunExposure.Add(new Tuple<DateTime, Radiation, List<Spatial.Face3D>>(tuple.Item1, tuple.Item2, tuple?.Item3 == null ? null : tuple.Item3.ConvertAll(x => new Spatial.Face3D(x))));
                }
            }
        }

        public List<Spatial.Face3D> GetSunExposureFace3Ds(DateTime dateTime)
        {
            if(sunExposure == null)
            {
                return null;
            }

            Tuple<DateTime, Radiation, List<Spatial.Face3D>> tuple = sunExposure.Find(x => x.Item1.Equals(dateTime));
            return tuple?.Item3?.ConvertAll(x => new Spatial.Face3D(x));
        }

        public Radiation GetRadiation(DateTime dateTime)
        {
            if (sunExposure == null)
            {
                return null;
            }

            Tuple<DateTime, Radiation, List<Spatial.Face3D>> tuple = sunExposure.Find(x => x.Item1.Equals(dateTime));
            return tuple?.Item2?.Clone();
        }

        public double GetSunExposureArea(DateTime dateTime)
        {
            if (sunExposure == null)
            {
                return 0;
            }

            Tuple<DateTime, Radiation, List<Spatial.Face3D>> tuple = sunExposure.Find(x => x.Item1.Equals(dateTime));
            if(tuple == null || tuple.Item2 == null || tuple.Item3.Count == 0)
            {
                return 0;
            }

            return tuple.Item3.ConvertAll(x => x.GetArea()).Sum();
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

        public List<double> TotalRadiations
        {
            get
            {
                if(sunExposure == null)
                {
                    return null;
                }

                List<double> result = new List<double>();
                foreach(Tuple<DateTime, Radiation, List<Spatial.Face3D>> tuple in sunExposure)
                {
                    result.Add(tuple.Item2 == null ? double.NaN : tuple.Item2.GetTotal());
                }

                return result;
            }
        }

        public override bool FromJObject(JObject jObject)
        {
            if (!base.FromJObject(jObject))
                return false;

            if(jObject.ContainsKey("SunExposure"))
            {
                sunExposure = new List<Tuple<DateTime, Radiation, List<Spatial.Face3D>>>();

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

                        Radiation radiation = jArray.Count <= 2 ? null : Core.Create.IJSAMObject<Radiation>(jArray[2] as JObject);

                        sunExposure.Add(new Tuple<DateTime, Radiation, List<Spatial.Face3D>>(dateTime, radiation, face3Ds));
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
                foreach(Tuple<DateTime, Radiation, List<Spatial.Face3D>> tuple in sunExposure)
                {
                    if (tuple == null)
                    {
                        continue;
                    }

                    JArray jArray = new JArray();
                    jArray.Add(tuple.Item1);
                    jArray.Add(Core.Create.JArray(tuple?.Item3));

                    if(tuple.Item2 != null)
                    {
                        jArray.Add(tuple.Item2.ToJObject());
                    }

                    jArray_SunExposure.Add(jArray);
                }

                jObject.Add("SunExposure", jArray_SunExposure);
            }

            return jObject;
        }
    }
}

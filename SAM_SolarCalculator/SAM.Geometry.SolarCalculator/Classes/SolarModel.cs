using Newtonsoft.Json.Linq;
using SAM.Core;
using SAM.Core.SolarCalculator;
using System.Collections.Generic;

namespace SAM.Geometry.SolarCalculator
{
    public class SolarModel : SAMModel, ISolarObject
    {
        private Location location;
        private RelationCluster relationCluster;

        public SolarModel(JObject jObject)
            : base(jObject)
        {

        }

        public SolarModel(Location location)
            : base()
        {
            if(location != null)
            {
                this.location = new Location(location);
            }
        }

        public Location Location
        {
            get
            {
                return location == null ? null : new Location(location);
            }
        }

        public bool Add(SolarFace solarFace)
        {
            if(solarFace == null)
            {
                return false;
            }

            if (relationCluster == null)
                relationCluster = new RelationCluster();

            SolarFace solarFace_Temp = new SolarFace(solarFace);

            if (!relationCluster.AddObject(solarFace_Temp))
            {
                return false;
            }

            return true;
        }

        public bool Add(SolarFaceSimulationResult solarFaceSimulationResult, System.Guid solarFaceGuid)
        {
            if (solarFaceSimulationResult == null)
            {
                return false;
            }

            if (relationCluster == null)
            {
                relationCluster = new RelationCluster();
            }

            bool result = relationCluster.AddObject(solarFaceSimulationResult);
            if (!result)
            {
                return result;
            }

            if (solarFaceGuid != System.Guid.Empty)
            {
                SolarFace solarFace = relationCluster.GetObject<SolarFace>(solarFaceGuid);
                if (solarFace != null)
                {
                    relationCluster.AddRelation(solarFaceSimulationResult, solarFace);
                }
            }

            return result;
        }
        
        public List<SolarFace> GetSolarFaces()
        {
            return relationCluster?.GetObjects<SolarFace>()?.ConvertAll(x => x == null ? null : new SolarFace(x));
        }

        public List<SolarFaceSimulationResult> GetSolarFaceSimulationResults()
        {
            return relationCluster?.GetObjects<SolarFaceSimulationResult>()?.ConvertAll(x => x == null ? null : new SolarFaceSimulationResult(x));
        }

        public override bool FromJObject(JObject jObject)
        {
            if (!base.FromJObject(jObject))
                return false;

            if (jObject.ContainsKey("Location"))
                location = new Location(jObject.Value<JObject>("Location"));

            if (jObject.ContainsKey("RelationCluster"))
                relationCluster = new RelationCluster(jObject.Value<JObject>("RelationCluster"));

            return true;
        }

        public override JObject ToJObject()
        {
            JObject jObject = base.ToJObject();
            if (jObject == null)
                return jObject;

            if (location != null)
                jObject.Add("Location", location.ToJObject());

            if (relationCluster != null)
                jObject.Add("RelationCluster", relationCluster.ToJObject());

            return jObject;
        }
    }
}

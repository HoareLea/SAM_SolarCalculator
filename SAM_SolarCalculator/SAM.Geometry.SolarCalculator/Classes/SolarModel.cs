using Newtonsoft.Json.Linq;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Spatial;
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

        public bool Add(LinkedFace3D linkedFace3D)
        {
            if(linkedFace3D == null)
            {
                return false;
            }

            if (relationCluster == null)
                relationCluster = new RelationCluster();

            LinkedFace3D solarFace_Temp = new LinkedFace3D(linkedFace3D);

            if (!relationCluster.AddObject(solarFace_Temp))
            {
                return false;
            }

            return true;
        }

        public bool Add(SolarFaceSimulationResult solarFaceSimulationResult, System.Guid linkedFace3DGuid)
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

            if (linkedFace3DGuid != System.Guid.Empty)
            {
                LinkedFace3D linkedFace3D = relationCluster.GetObject<LinkedFace3D>(linkedFace3DGuid);
                if (linkedFace3D != null)
                {
                    relationCluster.AddRelation(solarFaceSimulationResult, linkedFace3D);
                }
            }

            return result;
        }
        
        public List<LinkedFace3D> GetLinkedFace3Ds()
        {
            return relationCluster?.GetObjects<LinkedFace3D>()?.ConvertAll(x => x == null ? null : new LinkedFace3D(x));
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

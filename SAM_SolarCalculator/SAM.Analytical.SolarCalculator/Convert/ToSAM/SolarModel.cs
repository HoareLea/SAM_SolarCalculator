using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Convert
    {
        public static SolarModel ToSAM_SolarModel(this AnalyticalModel analyticalModel)
        {
            if(analyticalModel == null)
            {
                return null;
            }

            AdjacencyCluster adjacencyCluster = analyticalModel.AdjacencyCluster;
            if (adjacencyCluster == null)
            {
                return null;
            }

            SolarModel result = new SolarModel(analyticalModel.Location);

            List<Panel> panels = adjacencyCluster.GetPanels();
            if(panels != null || panels.Count != 0)
            {
                foreach(Panel panel in panels)
                {
                    List<Space> spaces = adjacencyCluster.GetSpaces(panel);
                    if (spaces != null && spaces.Count >= 2)
                    {
                        continue;
                    }

                    if(!panel.IsExposedToSun())
                    {
                        continue;
                    }

                    LinkedFace3D linkedFace3D = new LinkedFace3D(panel.Guid, panel.Face3D);
                    result.Add(linkedFace3D);
                }

            }

            return result;
        }

        public static SolarModel ToSAM_SolarModel(this BuildingModel buildingModel)
        {
            if (buildingModel == null)
            {
                return null;
            }

            SolarModel result = new SolarModel(buildingModel.Location);

            List<IPartition> partitions = buildingModel.GetPartitions();
            if (partitions != null || partitions.Count != 0)
            {
                foreach (IPartition partition in partitions)
                {
                    if(partition == null)
                    {
                        continue;
                    }
                    
                    if(!buildingModel.Shade(partition))
                    {
                        List<Space> spaces = buildingModel.GetSpaces(partition);
                        if (spaces != null && spaces.Count >= 2)
                        {
                            continue;
                        }
                    }

                    LinkedFace3D linkedFace3D = Geometry.Spatial.Create.LinkedFace3D(partition);
                    if(linkedFace3D == null)
                    {
                        continue;
                    }

                    result.Add(linkedFace3D);
                }
            }

            return result;
        }
    }
}

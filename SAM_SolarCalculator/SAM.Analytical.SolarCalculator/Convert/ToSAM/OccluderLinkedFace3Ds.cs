// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Object.Spatial;
using System.Collections.Generic;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Convert
    {
        /// <summary>
        /// Context occluders for the aperture analysis pipeline: every sun-exposed panel that is not
        /// shared by two spaces (the Convert.ToSAM_SolarModel filter), with the panel face CUT at its
        /// apertures so the window openings do not self-shadow their own analysis cells. Shade panels
        /// enter whole. Target apertures are NOT included — they are analysis targets, not occluders.
        /// </summary>
        public static List<LinkedFace3D> ToSAM_OccluderLinkedFace3Ds(this AnalyticalModel analyticalModel)
        {
            AdjacencyCluster adjacencyCluster = analyticalModel?.AdjacencyCluster;
            if (adjacencyCluster == null)
            {
                return null;
            }

            List<Panel> panels = adjacencyCluster.GetPanels();
            if (panels == null || panels.Count == 0)
            {
                return null;
            }

            List<LinkedFace3D> result = new List<LinkedFace3D>();
            foreach (Panel panel in panels)
            {
                if (panel == null)
                {
                    continue;
                }

                List<Space> spaces = adjacencyCluster.GetSpaces(panel);
                if (spaces != null && spaces.Count >= 2)
                {
                    continue;
                }

                if (!panel.IsExposedToSun())
                {
                    continue;
                }

                // cutOpenings: the host wall must not occlude its own window cells.
                Geometry.Spatial.Face3D face3D = panel.GetFace3D(true);
                if (face3D == null)
                {
                    continue;
                }

                result.Add(new LinkedFace3D(panel.Guid, face3D));
            }

            return result;
        }
    }
}

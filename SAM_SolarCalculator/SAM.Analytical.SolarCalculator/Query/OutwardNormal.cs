// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// Resolves the outward-pointing normal of a panel against the space topology: for a panel
        /// bounding exactly one space, the direction is taken from the space shell (ray-cast based,
        /// AdjacencyCluster.ExternalVector3D), so a flipped panel Face3D winding does not silently
        /// invert the analysis. For a panel with no related space (e.g. a shade) the panel's own
        /// plane normal is returned as-is.
        /// </summary>
        public static Vector3D OutwardNormal(this AdjacencyCluster adjacencyCluster, Panel panel, double silverSpacing = Core.Tolerance.MacroDistance, double tolerance = Core.Tolerance.Distance)
        {
            if (adjacencyCluster == null || panel == null)
            {
                return null;
            }

            List<Space> spaces = adjacencyCluster.GetSpaces(panel);
            if (spaces != null && spaces.Count > 0)
            {
                Vector3D vector3D = adjacencyCluster.ExternalVector3D(spaces[0], panel, silverSpacing, tolerance);
                if (vector3D != null && vector3D.IsValid())
                {
                    return vector3D.Unit;
                }
            }

            // No space topology to resolve against (e.g. shade panel): trust the panel's own normal.
            Plane plane = panel.Face3D?.GetPlane();
            Vector3D normal = plane?.Normal;
            return normal != null && normal.IsValid() ? normal.Unit : null;
        }

        /// <summary>
        /// Resolves the outward-pointing normal of an aperture: the aperture Face3D's own normal,
        /// flipped if necessary so it agrees with the host panel's outward direction. Never trust
        /// the aperture Face3D winding alone.
        /// </summary>
        public static Vector3D OutwardNormal(this AdjacencyCluster adjacencyCluster, Panel panel, Aperture aperture, double silverSpacing = Core.Tolerance.MacroDistance, double tolerance = Core.Tolerance.Distance)
        {
            if (adjacencyCluster == null || panel == null || aperture == null)
            {
                return null;
            }

            Vector3D outward = adjacencyCluster.OutwardNormal(panel, silverSpacing, tolerance);
            if (outward == null)
            {
                return null;
            }

            Vector3D normal = aperture.Face3D?.GetPlane()?.Normal;
            if (normal == null || !normal.IsValid())
            {
                return null;
            }

            normal = normal.Unit;
            return normal.DotProduct(outward) < 0 ? normal.GetNegated() : normal;
        }
    }
}

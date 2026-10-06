// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.SolarCalculator;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// Builds the aperture-centric analysis targets for a model. When apertureGuids is null or
        /// empty, every aperture on a sun-exposed external (single-space) panel is selected —
        /// mirroring the panel filter in Convert.ToSAM_SolarModel. An explicit selection returns
        /// exactly the resolvable subset, minus apertures on two-space (interior) panels, which are
        /// always rejected (an internal aperture receives no direct sun and would produce
        /// physically meaningless results). Each target's Face3D is re-oriented to the resolved
        /// OUTWARD normal, so flipped aperture winding cannot invert the analysis.
        /// </summary>
        /// <param name="analyticalModel">Model supplying panels, apertures and adjacency.</param>
        /// <param name="apertureGuids">Null/empty = all apertures on sun-exposed external panels.</param>
        /// <param name="gridSize">Aperture analysis-grid size, m (the AnalysisCell subdivision step).</param>
        /// <param name="tolerance_Area">Area tolerance.</param>
        /// <param name="tolerance_Distance">Distance tolerance.</param>
        public static List<ApertureSolarTarget> ApertureSolarTargets(this AnalyticalModel analyticalModel, IEnumerable<Guid> apertureGuids = null, double gridSize = 0.5, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Distance = Core.Tolerance.Distance)
        {
            return ApertureSolarTargets(analyticalModel, apertureGuids, gridSize, out string _, tolerance_Area, tolerance_Distance);
        }

        /// <summary>
        /// The same, reporting WHY the result is null or empty.
        ///
        /// Stage 11 added this because the failure had no voice. A grid size finer than
        /// sqrt(tolerance_Area) makes every candidate sample cell fall below the area tolerance, so
        /// every aperture is skipped for having no cells and the list comes back empty — after
        /// which the solar context returns null and the Grasshopper message blames the aperture.
        /// The three ways to get nothing (an unusable grid size, no aperture matching the
        /// selection, apertures that individually produced no samples) are different faults with
        /// different fixes, and each now says which one it is.
        /// </summary>
        /// <param name="message">Null when targets were produced; an actionable sentence otherwise.</param>
        public static List<ApertureSolarTarget> ApertureSolarTargets(this AnalyticalModel analyticalModel, IEnumerable<Guid> apertureGuids, double gridSize, out string message, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Distance = Core.Tolerance.Distance)
        {
            message = null;

            // The grid size is checked BEFORE any geometry is touched: the answer is a property of
            // the number and the tolerance alone, and once the cells are gone the cause cannot be
            // recovered from the empty list.
            if (Query.GridSizeValidity(gridSize, out string gridSizeMessage, tolerance_Area) != SolarCalculator.GridSizeValidity.Valid)
            {
                message = gridSizeMessage;
                return null;
            }

            AdjacencyCluster adjacencyCluster = analyticalModel?.AdjacencyCluster;
            if (adjacencyCluster == null)
            {
                message = "No model was supplied, or it has no adjacency information to find apertures in.";
                return null;
            }

            HashSet<Guid> selection = null;
            if (apertureGuids != null)
            {
                selection = new HashSet<Guid>(apertureGuids);
                if (selection.Count == 0)
                {
                    selection = null;
                }
            }

            List<Panel> panels = adjacencyCluster.GetPanels();
            if (panels == null || panels.Count == 0)
            {
                message = "The model has no panels, so it has no apertures to analyse.";
                return null;
            }

            // Apertures that were eligible but yielded no samples, so an empty result can say which
            // of the two geometric reasons applies rather than leaving the caller to guess.
            int consideredApertures = 0;
            double largestEmptyApertureArea = double.NaN;

            List<ApertureSolarTarget> result = new List<ApertureSolarTarget>();
            foreach (Panel panel in panels)
            {
                if (panel == null || !panel.HasApertures)
                {
                    continue;
                }

                bool explicitSelection = selection != null;

                // Two-space (interior) panels never produce targets: their apertures receive no
                // direct sun and would yield physically meaningless results. Applies to explicit
                // selections too — an explicitly requested internal aperture is rejected, not
                // silently analysed.
                List<Space> spaces = adjacencyCluster.GetSpaces(panel);
                if (spaces != null && spaces.Count >= 2)
                {
                    continue;
                }

                if (!explicitSelection)
                {
                    // Default selection: mirror Convert.ToSAM_SolarModel — sun-exposed panels that
                    // are not shared by two spaces.
                    if (!panel.IsExposedToSun())
                    {
                        continue;
                    }
                }

                List<Aperture> apertures = panel.Apertures;
                foreach (Aperture aperture in apertures)
                {
                    if (aperture == null)
                    {
                        continue;
                    }

                    if (explicitSelection && !selection.Contains(aperture.Guid))
                    {
                        continue;
                    }

                    consideredApertures++;

                    IClosedPlanar3D externalEdge3D = aperture.GetExternalEdge3D();
                    if (externalEdge3D == null)
                    {
                        continue;
                    }

                    Vector3D outward = adjacencyCluster.OutwardNormal(panel, aperture, tolerance_Area, tolerance_Distance);
                    if (outward == null)
                    {
                        continue;
                    }

                    Face3D face3D = new Face3D(externalEdge3D);
                    Plane plane = face3D?.GetPlane();
                    if (plane?.Normal == null)
                    {
                        continue;
                    }

                    if (plane.Normal.DotProduct(outward) < 0)
                    {
                        face3D.FlipNormal(true);
                    }

                    List<AnalysisCell> analysisCells = Geometry.SolarCalculator.Query.AnalysisCells(face3D, gridSize, tolerance_Area, tolerance_Distance);
                    if (analysisCells == null || analysisCells.Count == 0)
                    {
                        // Remember the biggest one that failed: if nothing at all comes back, that
                        // is the aperture whose area best explains why.
                        double area = face3D.GetArea();
                        if (double.IsNaN(largestEmptyApertureArea) || area > largestEmptyApertureArea)
                        {
                            largestEmptyApertureArea = area;
                        }

                        continue;
                    }

                    result.Add(new ApertureSolarTarget(aperture.Guid, panel.Guid, face3D, analysisCells));
                }
            }

            if (result.Count == 0)
            {
                message = consideredApertures == 0
                    ? "No aperture in this model matched the selection. With no selection, only apertures on sun-exposed external panels are analysed — apertures on panels shared by two spaces are internal, receive no direct sun, and are never targets."
                    : Query.EmptyAnalysisCellsMessage(largestEmptyApertureArea, gridSize, tolerance_Area);
            }

            return result;
        }
    }
}

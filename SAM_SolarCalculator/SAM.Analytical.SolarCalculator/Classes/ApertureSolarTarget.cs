// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// An aperture as a first-class solar analysis target: the opening Face3D with a resolved
    /// OUTWARD normal (never trusted to the aperture's own winding — see Query.OutwardNormal),
    /// a local coordinate frame (origin at the centroid, Z = outward normal, X horizontal,
    /// Y up-slope), azimuth/tilt of the outward normal, gross area, and the analysis-cell
    /// subdivision used by the irradiance pipeline.
    /// </summary>
    public class ApertureSolarTarget : IJSAMObject, ISolarObject
    {
        private Guid apertureGuid;
        private Guid panelGuid;
        private Face3D face3D;
        private Plane plane;
        private double azimuth = double.NaN;
        private double tilt = double.NaN;
        private double grossArea = double.NaN;
        private List<AnalysisCell> analysisCells;

        public ApertureSolarTarget(Guid apertureGuid, Guid panelGuid, Face3D face3D, IEnumerable<AnalysisCell> analysisCells)
        {
            this.apertureGuid = apertureGuid;
            this.panelGuid = panelGuid;
            this.face3D = face3D == null ? null : Core.Query.Clone(face3D);

            Plane plane_Face = face3D?.GetPlane();
            if (plane_Face != null)
            {
                Vector3D outward = plane_Face.Normal;
                Point3D origin = this.face3D.GetCentroid();

                // Local frame: X horizontal (perpendicular to world Z), Y up-slope, Z = outward normal.
                Vector3D axisX = Vector3D.WorldZ.CrossProduct(outward);
                if (axisX == null || axisX.Length < Core.Tolerance.Distance)
                {
                    axisX = Vector3D.WorldX;
                }
                else
                {
                    axisX = axisX.Unit;
                }

                Vector3D axisY = outward.CrossProduct(axisX)?.Unit;
                if (origin != null && axisY != null)
                {
                    plane = new Plane(origin, axisX, axisY);
                }

                azimuth = Geometry.Spatial.Query.Azimuth(plane_Face, Vector3D.WorldY);
                tilt = Geometry.Spatial.Query.Tilt(plane_Face);
                grossArea = this.face3D.GetArea();
            }

            this.analysisCells = analysisCells == null ? null : new List<AnalysisCell>(analysisCells);
        }

        public ApertureSolarTarget(ApertureSolarTarget apertureSolarTarget)
        {
            if (apertureSolarTarget != null)
            {
                apertureGuid = apertureSolarTarget.apertureGuid;
                panelGuid = apertureSolarTarget.panelGuid;
                face3D = apertureSolarTarget.face3D == null ? null : Core.Query.Clone(apertureSolarTarget.face3D);
                plane = apertureSolarTarget.plane == null ? null : new Plane(apertureSolarTarget.plane);
                azimuth = apertureSolarTarget.azimuth;
                tilt = apertureSolarTarget.tilt;
                grossArea = apertureSolarTarget.grossArea;
                analysisCells = apertureSolarTarget.analysisCells?.ConvertAll(x => x == null ? null : new AnalysisCell(x));
            }
        }

        public ApertureSolarTarget(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public Guid ApertureGuid
        {
            get
            {
                return apertureGuid;
            }
        }

        public Guid PanelGuid
        {
            get
            {
                return panelGuid;
            }
        }

        /// <summary>The aperture opening face, oriented so its plane normal points OUTWARD.</summary>
        public Face3D Face3D
        {
            get
            {
                return face3D == null ? null : Core.Query.Clone(face3D);
            }
        }

        /// <summary>Local frame: origin at the centroid, Z = outward normal, X horizontal, Y up-slope.</summary>
        public Plane Plane
        {
            get
            {
                return plane == null ? null : new Plane(plane);
            }
        }

        public Vector3D OutwardNormal
        {
            get
            {
                return plane?.Normal == null ? null : new Vector3D(plane.Normal);
            }
        }

        /// <summary>Compass azimuth of the outward normal in degrees (0 = north, 90 = east, 180 = south, 270 = west).</summary>
        public double Azimuth
        {
            get
            {
                return azimuth;
            }
        }

        /// <summary>Angle between the outward normal and world +Z in degrees (0 = up-facing, 90 = vertical).</summary>
        public double Tilt
        {
            get
            {
                return tilt;
            }
        }

        public double GrossArea
        {
            get
            {
                return grossArea;
            }
        }

        public List<AnalysisCell> AnalysisCells
        {
            get
            {
                return analysisCells?.ConvertAll(x => x == null ? null : new AnalysisCell(x));
            }
        }

        public int CellCount
        {
            get
            {
                return analysisCells?.Count ?? 0;
            }
        }

        /// <summary>
        /// The area the analysis samples actually cover, m2 — the sum of the cell areas.
        ///
        /// Normally this equals <see cref="GrossArea"/>: the cells are a clipped partition of the
        /// opening. It can be slightly LESS when the grid does not divide the opening and the
        /// leftover edge strip is too thin to clear the geometry area tolerance, in which case that
        /// strip is discarded like any other degenerate sliver. See
        /// <see cref="SampledAreaFraction"/> for why that is worth being able to see.
        /// </summary>
        public double SampledArea
        {
            get
            {
                if (analysisCells == null)
                {
                    return double.NaN;
                }

                double result = 0;
                foreach (AnalysisCell analysisCell in analysisCells)
                {
                    double area = analysisCell?.Area ?? 0;
                    if (!double.IsNaN(area))
                    {
                        result += area;
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// SampledArea / GrossArea — 1.0 when the samples cover the whole opening.
        ///
        /// WHY THIS IS EXPOSED. Every ABSOLUTE energy this library reports is a sum of
        /// (cell area x energy density), so it scales with the sampled area. When an edge strip is
        /// dropped, absolute energies are under-reported in exactly this proportion — measured at up
        /// to 4 % on a 1 m opening at grid sizes near the tolerance limit. That is counter-intuitive
        /// (a FINER grid can under-report more, because the leftover strip gets thinner relative to
        /// the tolerance) and it was invisible before Stage 11.
        ///
        /// PERCENTAGES ARE NOT AFFECTED. Unwanted-solar-blocked, wanted-solar-retained and shading
        /// efficiency are all ratios over the same cell set, so the sampled area cancels. It is only
        /// absolute kWh that moves.
        ///
        /// The cure is free: choose a grid size that divides the opening, which every recommended
        /// setting does on ordinary geometry. This value is here so the exception is visible rather
        /// than discovered on a project.
        /// </summary>
        public double SampledAreaFraction
        {
            get
            {
                double gross = grossArea;
                return double.IsNaN(gross) || gross <= 0 ? double.NaN : SampledArea / gross;
            }
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("ApertureGuid"))
            {
                Guid.TryParse(jObject["ApertureGuid"]?.GetValue<string>(), out apertureGuid);
            }

            if (jObject.ContainsKey("PanelGuid"))
            {
                Guid.TryParse(jObject["PanelGuid"]?.GetValue<string>(), out panelGuid);
            }

            if (jObject.ContainsKey("Face3D"))
            {
                face3D = new Face3D(jObject["Face3D"] as JsonObject);
            }

            if (jObject.ContainsKey("Plane"))
            {
                plane = new Plane(jObject["Plane"] as JsonObject);
            }

            if (jObject.ContainsKey("Azimuth"))
            {
                azimuth = jObject["Azimuth"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("Tilt"))
            {
                tilt = jObject["Tilt"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("GrossArea"))
            {
                grossArea = jObject["GrossArea"]?.GetValue<double>() ?? double.NaN;
            }

            analysisCells = null;
            if (jObject.ContainsKey("AnalysisCells"))
            {
                JsonArray jArray = jObject["AnalysisCells"] as JsonArray;
                if (jArray != null)
                {
                    analysisCells = new List<AnalysisCell>();
                    foreach (JsonNode jNode in jArray)
                    {
                        analysisCells.Add(Core.Create.IJSAMObject<AnalysisCell>(jNode as JsonObject));
                    }
                }
            }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            jObject.Add("ApertureGuid", apertureGuid.ToString());
            jObject.Add("PanelGuid", panelGuid.ToString());

            if (face3D != null)
            {
                jObject.Add("Face3D", face3D.ToJsonObject());
            }

            if (plane != null)
            {
                jObject.Add("Plane", plane.ToJsonObject());
            }

            if (!double.IsNaN(azimuth))
            {
                jObject.Add("Azimuth", azimuth);
            }

            if (!double.IsNaN(tilt))
            {
                jObject.Add("Tilt", tilt);
            }

            if (!double.IsNaN(grossArea))
            {
                jObject.Add("GrossArea", grossArea);
            }

            if (analysisCells != null)
            {
                JsonArray jArray = new JsonArray();
                foreach (AnalysisCell analysisCell in analysisCells)
                {
                    jArray.Add(analysisCell?.ToJsonObject());
                }
                jObject.Add("AnalysisCells", jArray);
            }

            return jObject;
        }
    }
}

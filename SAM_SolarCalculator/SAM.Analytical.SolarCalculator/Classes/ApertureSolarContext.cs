// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Core;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// Everything the aperture-level solar and shading workflows need in front of them, resolved
    /// once: the analysis targets, the context occluders, the two visibility calculations, the
    /// weather and the analysis year — plus each target's offset into the shared cell space.
    ///
    /// It exists because Stages 4, 6, 8 and 9 all need the SAME setup and must agree on its
    /// identity. Building it in one place means the expensive geometric work is reused across
    /// irradiance, shading potential, rationalisation and verification instead of each of them
    /// deciding for itself when a previous calculation still applies.
    ///
    /// This is a TRANSIENT container, deliberately not an IJSAMObject: the durable part (the
    /// visibility calculations) already lives on the model's SolarModel and the results already
    /// carry their own provenance. Nothing here is stored in a file.
    /// </summary>
    public class ApertureSolarContext
    {
        private List<ApertureSolarTarget> targets;
        private List<AnalysisCell> cells;
        private List<Vector3D> cellNormals;
        private List<LinkedFace3D> contextOccluders;
        private SolarVisibilityCache solarVisibilityCache;
        private SkyVisibilityCache skyVisibilityCache;
        private WeatherData weatherData;
        private Location location;
        private int year;
        private double gridSize = double.NaN;
        private double sunAngleStep = double.NaN;
        private double timeShiftInMinutes;
        private string contextGeometryHash;
        private string targetGeometryHash;
        private bool reusedPreviousCalculation;
        private Dictionary<Guid, int> cellIndexOffsets = new Dictionary<Guid, int>();

        internal ApertureSolarContext(
            List<ApertureSolarTarget> targets,
            List<AnalysisCell> cells,
            List<Vector3D> cellNormals,
            List<LinkedFace3D> contextOccluders,
            SolarVisibilityCache solarVisibilityCache,
            SkyVisibilityCache skyVisibilityCache,
            WeatherData weatherData,
            Location location,
            int year,
            double gridSize,
            double sunAngleStep,
            double timeShiftInMinutes,
            string contextGeometryHash,
            string targetGeometryHash,
            bool reusedPreviousCalculation)
        {
            this.targets = targets;
            this.cells = cells;
            this.cellNormals = cellNormals;
            this.contextOccluders = contextOccluders;
            this.solarVisibilityCache = solarVisibilityCache;
            this.skyVisibilityCache = skyVisibilityCache;
            this.weatherData = weatherData;
            this.location = location;
            this.year = year;
            this.gridSize = gridSize;
            this.sunAngleStep = sunAngleStep;
            this.timeShiftInMinutes = timeShiftInMinutes;
            this.contextGeometryHash = contextGeometryHash;
            this.targetGeometryHash = targetGeometryHash;
            this.reusedPreviousCalculation = reusedPreviousCalculation;

            int offset = 0;
            foreach (ApertureSolarTarget target in targets ?? new List<ApertureSolarTarget>())
            {
                if (target == null)
                {
                    continue;
                }

                cellIndexOffsets[target.ApertureGuid] = offset;
                offset += target.CellCount;
            }
        }

        /// <summary>The analysis targets, in the order their cells occupy the shared cell space.</summary>
        public List<ApertureSolarTarget> Targets
        {
            get
            {
                return targets == null ? null : new List<ApertureSolarTarget>(targets);
            }
        }

        /// <summary>Every target's analysis cells, concatenated in target order.</summary>
        public List<AnalysisCell> Cells
        {
            get
            {
                return cells == null ? null : new List<AnalysisCell>(cells);
            }
        }

        /// <summary>Outward normals, one per cell, in cell order.</summary>
        public List<Vector3D> CellNormals
        {
            get
            {
                return cellNormals == null ? null : new List<Vector3D>(cellNormals);
            }
        }

        /// <summary>The existing building and site geometry the sun is traced against.</summary>
        public List<LinkedFace3D> ContextOccluders
        {
            get
            {
                return contextOccluders == null ? null : new List<LinkedFace3D>(contextOccluders);
            }
        }

        /// <summary>Direct-beam visibility: sun groups against cells.</summary>
        public SolarVisibilityCache SolarVisibilityCache
        {
            get
            {
                return solarVisibilityCache;
            }
        }

        /// <summary>Sky, horizon and ground view factors per cell.</summary>
        public SkyVisibilityCache SkyVisibilityCache
        {
            get
            {
                return skyVisibilityCache;
            }
        }

        /// <summary>The weather actually used (supplied weather first, then the model's own).</summary>
        public WeatherData WeatherData
        {
            get
            {
                return weatherData;
            }
        }

        public Location Location
        {
            get
            {
                return location;
            }
        }

        /// <summary>The analysis year, resolved against the weather data.</summary>
        public int Year
        {
            get
            {
                return year;
            }
        }

        /// <summary>Aperture analysis-grid size, m.</summary>
        public double GridSize
        {
            get
            {
                return gridSize;
            }
        }

        /// <summary>Angular resolution used to group similar sun positions, degrees.</summary>
        public double SunAngleStep
        {
            get
            {
                return sunAngleStep;
            }
        }

        /// <summary>Sun-position sampling offset applied to each weather timestamp, minutes.</summary>
        public double TimeShiftInMinutes
        {
            get
            {
                return timeShiftInMinutes;
            }
        }

        public string ContextGeometryHash
        {
            get
            {
                return contextGeometryHash;
            }
        }

        public string TargetGeometryHash
        {
            get
            {
                return targetGeometryHash;
            }
        }

        /// <summary>
        /// True when both visibility calculations were reused, i.e. no geometric pass ran. False
        /// means the model, the grid, the sun-angle step, the site or the timeline changed — or a
        /// recalculation was forced.
        /// </summary>
        public bool ReusedPreviousCalculation
        {
            get
            {
                return reusedPreviousCalculation;
            }
        }

        public int TargetCount
        {
            get
            {
                return targets == null ? 0 : targets.Count;
            }
        }

        /// <summary>The target for an aperture, or null when the aperture is not part of this context.</summary>
        public ApertureSolarTarget Target(Guid apertureGuid)
        {
            return targets?.Find(x => x != null && x.ApertureGuid == apertureGuid);
        }

        /// <summary>
        /// The first cell index belonging to an aperture within the shared cell space. -1 when the
        /// aperture is not part of this context. Stages 6, 8 and 9 need it to read the right rows.
        /// </summary>
        public int CellIndexOffset(Guid apertureGuid)
        {
            return cellIndexOffsets.TryGetValue(apertureGuid, out int offset) ? offset : -1;
        }
    }
}

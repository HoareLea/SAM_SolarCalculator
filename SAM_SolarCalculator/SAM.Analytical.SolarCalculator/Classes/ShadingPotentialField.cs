// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// Stage 6 result: the shading potential field — a SCALAR voxel field over the ShadingVolume in
    /// front of one aperture, answering "if shading material were placed at voxel P, how useful
    /// would it be?". It is the source of truth for Stage 7 geometry extraction and Stage 8
    /// rationalisation; any mesh/shell produced from it is a derived, disposable representation.
    ///
    /// Two RAW physical accumulations are kept per voxel, in kWh (aperture-plane beam energy that
    /// would pass through the voxel, area-weighted over the lit analysis cells):
    ///   UnwantedEnergyPerVoxel[v] — energy that SHOULD be blocked, intercepted at v
    ///   WantedEnergyPerVoxel[v]   — energy that should be PRESERVED, intercepted at v
    /// The scalar score combines them on demand:
    ///   Score(v, wantedSolarPenalty) = Unwanted[v] - wantedSolarPenalty x Wanted[v]
    /// so the wanted-solar penalty can be swept without rebuilding the field. Positive score =
    /// worth filling with material; negative = must stay open. Existing-context obstruction is
    /// already removed: a voxel never receives energy from a sun group whose cells are not lit.
    ///
    /// Normalisation for visualisation/comparison: NormalizedScore maps positive scores onto
    /// (0, 1] against the maximum positive score and negative scores onto [-1, 0) against the
    /// minimum — the RAW kWh values are never discarded.
    /// </summary>
    public class ShadingPotentialField : IJSAMObject, ISolarObject
    {
        public const int CurrentSchemaVersion = 1;

        private int schemaVersion = CurrentSchemaVersion;
        private Guid apertureGuid;
        private string desirabilityStrategyName;
        private double gridSize = double.NaN;
        private double sunAngleStep = double.NaN;
        private double timeShiftInMinutes;
        private ShadingVolume volume;
        private double[] unwantedEnergy;
        private double[] wantedEnergy;

        public ShadingPotentialField(Guid apertureGuid, string desirabilityStrategyName, double gridSize, double sunAngleStep, double timeShiftInMinutes, ShadingVolume volume, double[] unwantedEnergy, double[] wantedEnergy)
        {
            this.apertureGuid = apertureGuid;
            this.desirabilityStrategyName = desirabilityStrategyName;
            this.gridSize = gridSize;
            this.sunAngleStep = sunAngleStep;
            this.timeShiftInMinutes = timeShiftInMinutes;
            this.volume = volume == null ? null : new ShadingVolume(volume);
            this.unwantedEnergy = unwantedEnergy == null ? null : (double[])unwantedEnergy.Clone();
            this.wantedEnergy = wantedEnergy == null ? null : (double[])wantedEnergy.Clone();
        }

        public ShadingPotentialField(ShadingPotentialField shadingPotentialField)
        {
            if (shadingPotentialField != null)
            {
                schemaVersion = shadingPotentialField.schemaVersion;
                apertureGuid = shadingPotentialField.apertureGuid;
                desirabilityStrategyName = shadingPotentialField.desirabilityStrategyName;
                gridSize = shadingPotentialField.gridSize;
                sunAngleStep = shadingPotentialField.sunAngleStep;
                timeShiftInMinutes = shadingPotentialField.timeShiftInMinutes;
                volume = shadingPotentialField.volume == null ? null : new ShadingVolume(shadingPotentialField.volume);
                unwantedEnergy = shadingPotentialField.unwantedEnergy == null ? null : (double[])shadingPotentialField.unwantedEnergy.Clone();
                wantedEnergy = shadingPotentialField.wantedEnergy == null ? null : (double[])shadingPotentialField.wantedEnergy.Clone();
            }
        }

        public ShadingPotentialField(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public int SchemaVersion
        {
            get
            {
                return schemaVersion;
            }
        }

        public Guid ApertureGuid
        {
            get
            {
                return apertureGuid;
            }
        }

        /// <summary>Full type name of the desirability strategy the field was built with (provenance).</summary>
        public string DesirabilityStrategyName
        {
            get
            {
                return desirabilityStrategyName;
            }
        }

        /// <summary>Aperture analysis-grid size the field was built with, m.</summary>
        public double GridSize
        {
            get
            {
                return gridSize;
            }
        }

        /// <summary>Angular resolution of the sun groups the field was built with, degrees.</summary>
        public double SunAngleStep
        {
            get
            {
                return sunAngleStep;
            }
        }

        /// <summary>Sun-position sampling offset of the timeline, minutes.</summary>
        public double TimeShiftInMinutes
        {
            get
            {
                return timeShiftInMinutes;
            }
        }

        public ShadingVolume Volume
        {
            get
            {
                return volume == null ? null : new ShadingVolume(volume);
            }
        }

        /// <summary>Raw unwanted (block-me) intercepted energy per voxel, kWh.</summary>
        public double[] UnwantedEnergyPerVoxel
        {
            get
            {
                return unwantedEnergy == null ? null : (double[])unwantedEnergy.Clone();
            }
        }

        /// <summary>Raw wanted (preserve-me) intercepted energy per voxel, kWh.</summary>
        public double[] WantedEnergyPerVoxel
        {
            get
            {
                return wantedEnergy == null ? null : (double[])wantedEnergy.Clone();
            }
        }

        /// <summary>The scalar field score: Unwanted[v] - wantedSolarPenalty x Wanted[v], kWh.</summary>
        public double Score(int voxelIndex, double wantedSolarPenalty = 1.0)
        {
            if (unwantedEnergy == null || voxelIndex < 0 || voxelIndex >= unwantedEnergy.Length)
            {
                return double.NaN;
            }

            return unwantedEnergy[voxelIndex] - wantedSolarPenalty * wantedEnergy[voxelIndex];
        }

        /// <summary>Total unwanted energy intercepted across all voxels, kWh.</summary>
        public double TotalUnwantedEnergy
        {
            get
            {
                return Sum(unwantedEnergy);
            }
        }

        /// <summary>Total wanted energy intercepted across all voxels, kWh.</summary>
        public double TotalWantedEnergy
        {
            get
            {
                return Sum(wantedEnergy);
            }
        }

        /// <summary>Maximum voxel score, kWh (NaN when empty).</summary>
        public double MaxScore(double wantedSolarPenalty = 1.0)
        {
            if (unwantedEnergy == null || unwantedEnergy.Length == 0)
            {
                return double.NaN;
            }

            double result = double.NegativeInfinity;
            for (int i = 0; i < unwantedEnergy.Length; i++)
            {
                double score = unwantedEnergy[i] - wantedSolarPenalty * wantedEnergy[i];
                if (score > result)
                {
                    result = score;
                }
            }

            return result;
        }

        /// <summary>Minimum voxel score, kWh (NaN when empty).</summary>
        public double MinScore(double wantedSolarPenalty = 1.0)
        {
            if (unwantedEnergy == null || unwantedEnergy.Length == 0)
            {
                return double.NaN;
            }

            double result = double.PositiveInfinity;
            for (int i = 0; i < unwantedEnergy.Length; i++)
            {
                double score = unwantedEnergy[i] - wantedSolarPenalty * wantedEnergy[i];
                if (score < result)
                {
                    result = score;
                }
            }

            return result;
        }

        /// <summary>
        /// Sum of the POSITIVE voxel scores — the total positive shading potential of the field.
        ///
        /// THIS IS NOT AN ENERGY SAVING, and reading it as one is the single most damaging
        /// misunderstanding this class can cause. Each voxel independently accumulates the beam that
        /// would pass through it, and a single solar ray passes through MANY voxels along its path,
        /// contributing to every one of them. Summing the field therefore counts the same kWh over
        /// and over, once per voxel the ray crosses. A real device occupies a thin surface, not the
        /// whole volume, and intercepts each ray exactly once: its saving is bounded by the
        /// aperture's admitted unwanted beam and is typically a small fraction of this total.
        ///
        /// The unit is inherited from the per-voxel accumulations (kWh of aperture-plane beam) but
        /// the SUM is a path-length-weighted cumulative potential, not a quantity of energy anything
        /// could save. It is useful for exactly what Stage 7 uses it for — RANKING locations and
        /// choosing a threshold — and for nothing that looks like a saving. Only
        /// <see cref="ShadingPerformance"/>, which builds real geometry and traces it, states what a
        /// device saves.
        /// </summary>
        public double PositiveTotal(double wantedSolarPenalty = 1.0)
        {
            if (unwantedEnergy == null)
            {
                return double.NaN;
            }

            double result = 0;
            for (int i = 0; i < unwantedEnergy.Length; i++)
            {
                double score = unwantedEnergy[i] - wantedSolarPenalty * wantedEnergy[i];
                if (score > 0)
                {
                    result += score;
                }
            }

            return result;
        }

        /// <summary>
        /// Sum of the NEGATIVE voxel scores (&lt;= 0) — the total negative shading potential, i.e.
        /// the wanted-solar risk carried by the locations that should stay open.
        ///
        /// The same caution as <see cref="PositiveTotal"/> applies in full: this is a cumulative
        /// spatial potential summed over voxels a ray passes through, NOT the wanted solar any
        /// device would destroy. Only a traced device reports that.
        /// </summary>
        public double NegativeTotal(double wantedSolarPenalty = 1.0)
        {
            if (unwantedEnergy == null)
            {
                return double.NaN;
            }

            double result = 0;
            for (int i = 0; i < unwantedEnergy.Length; i++)
            {
                double score = unwantedEnergy[i] - wantedSolarPenalty * wantedEnergy[i];
                if (score < 0)
                {
                    result += score;
                }
            }

            return result;
        }

        /// <summary>
        /// Visualisation normalisation onto [-1, 1]: positive scores against the maximum positive,
        /// negative against the absolute minimum. +1 = the single most valuable shading location;
        /// -1 = the most damaging. 0 when the field has no positive (resp. negative) content.
        /// </summary>
        public double NormalizedScore(int voxelIndex, double wantedSolarPenalty = 1.0)
        {
            double score = Score(voxelIndex, wantedSolarPenalty);
            if (double.IsNaN(score))
            {
                return double.NaN;
            }

            if (score > 0)
            {
                double max = MaxScore(wantedSolarPenalty);
                return max > 0 ? score / max : 0.0;
            }

            if (score < 0)
            {
                double min = MinScore(wantedSolarPenalty);
                return min < 0 ? score / Math.Abs(min) : 0.0;
            }

            return 0.0;
        }

        /// <summary>
        /// Threshold selection by retained benefit: the engineer-readable "keep the top X % of the
        /// benefit" rule. NaN when there is no positive benefit.
        ///
        /// The returned level is meant for the STRICT selector Query.IdealShadingVoxels uses
        /// (Score &gt; threshold), so it is not the score of the voxel that tips the total over the
        /// target but the next distinct score BELOW it. Returning the tipping score itself would
        /// exclude that voxel — and every voxel tied with it — and quietly deliver slightly less
        /// benefit than was asked for. With this level the selection is exactly
        /// { v : Score(v) &gt;= tipping score }, so asking for 90 % returns at least 90 %.
        /// </summary>
        public double ThresholdForCumulativeCapture(double fraction, double wantedSolarPenalty = 1.0)
        {
            double positiveTotal = PositiveTotal(wantedSolarPenalty);
            if (double.IsNaN(positiveTotal) || positiveTotal <= 0)
            {
                return double.NaN;
            }

            fraction = Math.Max(0.0, Math.Min(1.0, fraction));

            List<double> positives = new List<double>();
            for (int i = 0; i < unwantedEnergy.Length; i++)
            {
                double score = unwantedEnergy[i] - wantedSolarPenalty * wantedEnergy[i];
                if (score > 0)
                {
                    positives.Add(score);
                }
            }

            positives.Sort();
            positives.Reverse();

            double cumulative = 0;
            double target = fraction * positiveTotal;
            double tipping = positives[positives.Count - 1];
            foreach (double score in positives)
            {
                cumulative += score;
                if (cumulative >= target)
                {
                    tipping = score;
                    break;
                }
            }

            // Drop to the next distinct score below the tipping one so a strict > selector keeps
            // every voxel at or above it. Zero when the tipping score is already the smallest
            // positive: > 0 then selects exactly the positive voxels.
            foreach (double score in positives)
            {
                if (score < tipping)
                {
                    return score;
                }
            }

            return 0.0;
        }

        /// <summary>Threshold selection as a fraction of the maximum voxel score.</summary>
        public double ThresholdForMaxFraction(double fraction, double wantedSolarPenalty = 1.0)
        {
            double max = MaxScore(wantedSolarPenalty);
            if (double.IsNaN(max) || max <= 0)
            {
                return double.NaN;
            }

            return fraction * max;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("SchemaVersion"))
            {
                schemaVersion = jObject["SchemaVersion"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("ApertureGuid"))
            {
                Guid.TryParse(jObject["ApertureGuid"]?.GetValue<string>(), out apertureGuid);
            }

            if (jObject.ContainsKey("DesirabilityStrategyName"))
            {
                desirabilityStrategyName = jObject["DesirabilityStrategyName"]?.GetValue<string>();
            }

            if (jObject.ContainsKey("GridSize"))
            {
                gridSize = jObject["GridSize"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("SunAngleStep"))
            {
                sunAngleStep = jObject["SunAngleStep"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("TimeShiftInMinutes"))
            {
                timeShiftInMinutes = jObject["TimeShiftInMinutes"]?.GetValue<double>() ?? default;
            }

            if (jObject.ContainsKey("Volume"))
            {
                volume = new ShadingVolume(jObject["Volume"] as JsonObject);
            }

            unwantedEnergy = FromBase64(jObject, "UnwantedEnergy");
            wantedEnergy = FromBase64(jObject, "WantedEnergy");

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            jObject.Add("SchemaVersion", schemaVersion);
            jObject.Add("ApertureGuid", apertureGuid.ToString());

            if (desirabilityStrategyName != null)
            {
                jObject.Add("DesirabilityStrategyName", desirabilityStrategyName);
            }

            jObject.Add("GridSize", gridSize);
            jObject.Add("SunAngleStep", sunAngleStep);
            jObject.Add("TimeShiftInMinutes", timeShiftInMinutes);

            if (volume != null)
            {
                jObject.Add("Volume", volume.ToJsonObject());
            }

            Add(jObject, "UnwantedEnergy", unwantedEnergy);
            Add(jObject, "WantedEnergy", wantedEnergy);

            return jObject;
        }

        private static double Sum(double[] values)
        {
            if (values == null)
            {
                return double.NaN;
            }

            double result = 0;
            foreach (double value in values)
            {
                result += value;
            }

            return result;
        }

        private static void Add(JsonObject jObject, string name, double[] values)
        {
            if (values == null)
            {
                return;
            }

            byte[] bytes = new byte[values.Length * 8];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            jObject.Add(name, System.Convert.ToBase64String(bytes));
        }

        private static double[] FromBase64(JsonObject jObject, string name)
        {
            if (!jObject.ContainsKey(name))
            {
                return null;
            }

            string base64 = jObject[name]?.GetValue<string>();
            if (base64 == null)
            {
                return null;
            }

            byte[] bytes = System.Convert.FromBase64String(base64);
            double[] result = new double[bytes.Length / 8];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }
    }
}

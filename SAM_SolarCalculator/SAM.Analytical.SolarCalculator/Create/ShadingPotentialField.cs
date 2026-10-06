// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// Stage 6: the shading potential field. Marches the direct sun of every sun group from
        /// every lit analysis cell through the ShadingVolume's voxel grid, accumulating the
        /// desirability-weighted beam energy each voxel would intercept:
        ///
        ///   for each sun group g:
        ///       uw = UnwantedEnergy[g], wn = WantedEnergy[g]   (Stage 5, kWh/m2, energy-weighted)
        ///       if |uw| + |wn| ~ 0: skip
        ///       for each analysis cell a lit at g (the Stage 2 lit bits — a cell already shaded
        ///       by existing context contributes NOTHING, so a voxel never gets credit for
        ///       blocking sun that context already blocks):
        ///           march the ray from the cell's interior point toward the sun through the grid
        ///           (Amanatides-Woo DDA, NOT per-voxel intersection tests)
        ///           for each voxel v entered:
        ///               Unwanted[v] += area(a) x uw
        ///               Wanted[v]   += area(a) x wn
        ///
        /// Raw voxel units are kWh of aperture-plane beam energy over the desirability-weighted
        /// year. Each entered voxel receives the FULL cell contribution: the score answers "if
        /// material were placed at v, how much (un)wanted energy would it intercept", which is
        /// independent per voxel. Positive Score = worth filling; negative = must stay open.
        ///
        /// Determinism: sun groups are partitioned into contiguous, ordered ranges; each range
        /// accumulates into its own sparse accumulator; the accumulators are merged in range
        /// order — the field is bit-identical across runs and thread counts (no per-voxel locking,
        /// no Interlocked). Voxel candidate ordering is the ShadingVolume's fixed linear index.
        ///
        /// The field inherits the analysis resolution: gridSize in the aperture plane (one ray per
        /// analysis cell, from its guaranteed-interior point, exactly as the visibility cache
        /// tests it) and sunAngleStep in direction space (group-centre directions).
        /// </summary>
        /// <param name="target">The aperture (cells + local frame come from it).</param>
        /// <param name="solarVisibilityCache">Sun groups + per-cell lit bits.</param>
        /// <param name="desirability">Stage 5 per-group unwanted/wanted energies for this aperture.</param>
        /// <param name="volume">The candidate voxel grid.</param>
        /// <param name="cellIndexOffset">First cache cell index belonging to this target (0 when the cache was built for this target alone; the offset within a whole-model cache otherwise).</param>
        public static ShadingPotentialField ShadingPotentialField(this ApertureSolarTarget target, SolarVisibilityCache solarVisibilityCache, ApertureDesirability desirability, ShadingVolume volume, int cellIndexOffset = 0)
        {
            List<AnalysisCell> cells = target?.AnalysisCells;
            if (cells == null || cells.Count == 0 || solarVisibilityCache == null || desirability == null || volume == null || volume.VoxelCount <= 0)
            {
                return null;
            }

            List<SunBin> bins = solarVisibilityCache.Bins;
            double[] unwantedGroups = desirability.UnwantedEnergyPerGroup;
            double[] wantedGroups = desirability.WantedEnergyPerGroup;
            if (bins == null || unwantedGroups == null || wantedGroups == null || unwantedGroups.Length != bins.Count || wantedGroups.Length != bins.Count)
            {
                return null;
            }

            int voxelCount = volume.VoxelCount;
            int cellCount = cells.Count;
            if (cellIndexOffset < 0 || cellIndexOffset + cellCount > solarVisibilityCache.CellCount)
            {
                return null;
            }

            Point3D[] cellPoints = new Point3D[cellCount];
            double[] cellAreas = new double[cellCount];
            for (int c = 0; c < cellCount; c++)
            {
                cellPoints[c] = cells[c]?.InternalPoint3D;
                cellAreas[c] = cells[c]?.Area ?? 0;
            }

            // Ordered, contiguous bin ranges: parallelism changes nothing about the result.
            int rangeCount = Math.Min(bins.Count, Math.Max(1, Environment.ProcessorCount * 2));
            List<Tuple<int, int>> ranges = new List<Tuple<int, int>>(rangeCount);
            int perRange = (int)Math.Ceiling((double)bins.Count / rangeCount);
            for (int start = 0; start < bins.Count; start += perRange)
            {
                ranges.Add(new Tuple<int, int>(start, Math.Min(bins.Count, start + perRange)));
            }

            Dictionary<int, double>[] rangeUnwanted = new Dictionary<int, double>[ranges.Count];
            Dictionary<int, double>[] rangeWanted = new Dictionary<int, double>[ranges.Count];

            ShadingVolume volume_Local = volume;
            SolarVisibilityCache cache_Local = solarVisibilityCache;

            Parallel.For(0, ranges.Count, r =>
            {
                Dictionary<int, double> localUnwanted = new Dictionary<int, double>();
                Dictionary<int, double> localWanted = new Dictionary<int, double>();

                for (int b = ranges[r].Item1; b < ranges[r].Item2; b++)
                {
                    double uw = unwantedGroups[b];
                    double wn = wantedGroups[b];
                    if (Math.Abs(uw) + Math.Abs(wn) < 1e-12)
                    {
                        continue;
                    }

                    Vector3D towardSun = bins[b]?.RepresentativeDirection?.GetNegated();
                    if (towardSun == null || !towardSun.IsValid())
                    {
                        continue;
                    }

                    towardSun = towardSun.Unit;

                    // March direction in the volume-local frame (orthonormal axes: dot products).
                    double dx = towardSun.X * volume_Local.AxisX.X + towardSun.Y * volume_Local.AxisX.Y + towardSun.Z * volume_Local.AxisX.Z;
                    double dy = towardSun.X * volume_Local.AxisY.X + towardSun.Y * volume_Local.AxisY.Y + towardSun.Z * volume_Local.AxisY.Z;
                    double dz = towardSun.X * volume_Local.AxisZ.X + towardSun.Y * volume_Local.AxisZ.Y + towardSun.Z * volume_Local.AxisZ.Z;

                    for (int c = 0; c < cellCount; c++)
                    {
                        if (cellPoints[c] == null || !cache_Local.IsLit(b, cellIndexOffset + c))
                        {
                            continue;
                        }

                        if (!volume_Local.TryToLocal(cellPoints[c], out double sx, out double sy, out double sz))
                        {
                            continue;
                        }

                        double area = cellAreas[c];
                        double contributionUnwanted = area * uw;
                        double contributionWanted = area * wn;

                        Query.March(volume_Local, sx, sy, sz, dx, dy, dz, voxelIndex =>
                        {
                            localUnwanted.TryGetValue(voxelIndex, out double u);
                            localUnwanted[voxelIndex] = u + contributionUnwanted;
                            localWanted.TryGetValue(voxelIndex, out double w);
                            localWanted[voxelIndex] = w + contributionWanted;
                        });
                    }
                }

                rangeUnwanted[r] = localUnwanted;
                rangeWanted[r] = localWanted;
            });

            // Ordered reduction: bit-identical across runs and thread counts.
            double[] unwanted = new double[voxelCount];
            double[] wanted = new double[voxelCount];
            for (int r = 0; r < ranges.Count; r++)
            {
                foreach (KeyValuePair<int, double> pair in rangeUnwanted[r])
                {
                    unwanted[pair.Key] += pair.Value;
                }

                foreach (KeyValuePair<int, double> pair in rangeWanted[r])
                {
                    wanted[pair.Key] += pair.Value;
                }
            }

            return new ShadingPotentialField(target.ApertureGuid, desirability.DesirabilityStrategyName, solarVisibilityCache.CellSize, solarVisibilityCache.BinSizeDegrees, solarVisibilityCache.SunPositionShiftInMinutes, volume, unwanted, wanted);
        }

    }
}

// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// The voxel set above threshold: { v : Score(v, wantedSolarPenalty) &gt; threshold },
        /// ascending linear-index order (deterministic). threshold is a RAW field score in kWh —
        /// derive it with ShadingPotentialField.ThresholdForCumulativeCapture /
        /// ThresholdForMaxFraction, or pass an explicit absolute energy score.
        /// </summary>
        public static List<int> IdealShadingVoxels(this ShadingPotentialField field, double threshold, double wantedSolarPenalty = 1.0)
        {
            if (field == null || double.IsNaN(threshold))
            {
                return null;
            }

            double[] unwanted = field.UnwantedEnergyPerVoxel;
            double[] wanted = field.WantedEnergyPerVoxel;
            if (unwanted == null || wanted == null)
            {
                return null;
            }

            List<int> result = new List<int>();
            for (int i = 0; i < unwanted.Length; i++)
            {
                if (unwanted[i] - wantedSolarPenalty * wanted[i] > threshold)
                {
                    result.Add(i);
                }
            }

            return result;
        }

        /// <summary>
        /// Connected regions of a voxel set under 6-connectivity (face adjacency), returned as
        /// voxel-index lists ordered by descending size (ties: ascending first index — stable and
        /// deterministic). Used to support disconnected high-value regions explicitly and to drop
        /// floating specks via the largest-component filter.
        /// </summary>
        public static List<List<int>> ShadingVoxelRegions(this ShadingPotentialField field, IEnumerable<int> voxelIndices)
        {
            ShadingVolume volume = field?.Volume;
            if (volume == null || voxelIndices == null)
            {
                return null;
            }

            HashSet<int> remaining = new HashSet<int>(voxelIndices);
            List<List<int>> regions = new List<List<int>>();

            int countX = volume.CountX;
            int countY = volume.CountY;
            int countZ = volume.CountZ;

            while (remaining.Count > 0)
            {
                int seed = -1;
                foreach (int index in remaining)
                {
                    seed = index;
                    break;
                }

                List<int> region = new List<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(seed);
                remaining.Remove(seed);

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    region.Add(current);

                    int i = current % countX;
                    int j = (current / countX) % countY;
                    int k = current / (countX * countY);

                    TryNeighbour(i - 1, j, k);
                    TryNeighbour(i + 1, j, k);
                    TryNeighbour(i, j - 1, k);
                    TryNeighbour(i, j + 1, k);
                    TryNeighbour(i, j, k - 1);
                    TryNeighbour(i, j, k + 1);

                    void TryNeighbour(int ni, int nj, int nk)
                    {
                        if (ni < 0 || ni >= countX || nj < 0 || nj >= countY || nk < 0 || nk >= countZ)
                        {
                            return;
                        }

                        int neighbour = volume.VoxelIndex(ni, nj, nk);
                        if (remaining.Remove(neighbour))
                        {
                            queue.Enqueue(neighbour);
                        }
                    }
                }

                region.Sort();
                regions.Add(region);
            }

            regions.Sort((x, y) => x.Count != y.Count ? y.Count.CompareTo(x.Count) : x[0].CompareTo(y[0]));
            return regions;
        }

        /// <summary>
        /// The ideal-shading voxel set with buildability filters: above threshold, optionally
        /// restricted to regions that TOUCH the aperture plane (a region is buildable only if it
        /// connects back to the facade), optionally reduced to the single largest region.
        /// Deterministic ascending order.
        /// </summary>
        public static List<int> IdealShadingVoxels(this ShadingPotentialField field, double threshold, double wantedSolarPenalty, bool requireFacadeContact, bool keepLargestRegionOnly)
        {
            List<int> voxels = IdealShadingVoxels(field, threshold, wantedSolarPenalty);
            if (voxels == null || (!requireFacadeContact && !keepLargestRegionOnly))
            {
                return voxels;
            }

            ShadingVolume volume = field.Volume;
            List<List<int>> regions = ShadingVoxelRegions(field, voxels);
            if (regions == null)
            {
                return null;
            }

            List<int> result = new List<int>();
            foreach (List<int> region in regions)
            {
                if (requireFacadeContact && !TouchesAperturePlane(volume, region))
                {
                    continue;
                }

                result.AddRange(region);
                if (keepLargestRegionOnly)
                {
                    break; // regions are size-ordered
                }
            }

            result.Sort();
            return result;
        }

        /// <summary>A region touches the aperture plane when any of its voxels sits in the k = 0 layer.</summary>
        private static bool TouchesAperturePlane(ShadingVolume volume, List<int> region)
        {
            int layerSize = volume.CountX * volume.CountY;
            foreach (int index in region)
            {
                if (index / layerSize == 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Voxel-set metrics for an ideal-shading region selection: projected area onto the
        /// aperture plane (distinct (i, j) columns x voxel area), enclosed voxel volume, maximum
        /// outward projection depth, and the benefit captured (sum of positive scores over the set
        /// as a fraction of the field's PositiveTotal).
        /// </summary>
        public static bool TryGetIdealShadingMetrics(this ShadingPotentialField field, IEnumerable<int> voxelIndices, double wantedSolarPenalty, out double projectedArea, out double volume, out double maxProjectionDepth, out double capturedPotentialFraction)
        {
            projectedArea = double.NaN;
            volume = double.NaN;
            maxProjectionDepth = double.NaN;
            capturedPotentialFraction = double.NaN;

            ShadingVolume shadingVolume = field?.Volume;
            if (shadingVolume == null || voxelIndices == null)
            {
                return false;
            }

            double voxelSize = shadingVolume.VoxelSize;
            int countX = shadingVolume.CountX;
            int countY = shadingVolume.CountY;

            HashSet<int> columns = new HashSet<int>();
            int maxK = -1;
            int count = 0;
            double captured = 0;
            foreach (int index in voxelIndices)
            {
                int i = index % countX;
                int j = (index / countX) % countY;
                int k = index / (countX * countY);
                columns.Add(i + countX * j);
                if (k > maxK)
                {
                    maxK = k;
                }

                count++;

                double score = field.Score(index, wantedSolarPenalty);
                if (score > 0)
                {
                    captured += score;
                }
            }

            projectedArea = columns.Count * voxelSize * voxelSize;
            volume = count * voxelSize * voxelSize * voxelSize;
            maxProjectionDepth = (maxK + 1) * voxelSize;

            double positiveTotal = field.PositiveTotal(wantedSolarPenalty);
            capturedPotentialFraction = positiveTotal > 0 ? captured / positiveTotal : double.NaN;
            return true;
        }
    }
}

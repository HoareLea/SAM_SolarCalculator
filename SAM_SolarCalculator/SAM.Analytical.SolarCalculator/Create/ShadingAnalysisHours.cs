// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Core.SolarCalculator;
using SAM.Geometry.SolarCalculator;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// The analysis-hour accounting for a resolved context: how many hours of the year the study
        /// looked at, how much of the facade's sun the surroundings remove, and how the remaining
        /// beam-admitting hours split by the brief.
        ///
        /// NO NEW PHYSICS, NO SCHEMA CHANGE. Every figure is read from the context, the desirability
        /// arrays and the strategy already in hand; there is no ray casting and no second visibility
        /// calculation. The per-hour unwanted/wanted split MUST come from per-hour weights rather
        /// than from sun bins — a bin groups similar sun positions and the sun is at the same
        /// altitude and azimuth in June and December, so one bin routinely mixes unwanted and wanted
        /// hours and a bin-derived split double-counts every mixed bin.
        ///
        /// The split is evaluated against the FIRST member target (scope order); the default brief
        /// is target-independent and the counting is per hour, not per aperture.
        /// </summary>
        /// <param name="context">The resolved solar context.</param>
        /// <param name="desirabilities">Per-target desirability, in context target order.</param>
        /// <param name="strategy">The brief that produced the desirabilities.</param>
        public static ShadingAnalysisHours ShadingAnalysisHours(this ApertureSolarContext context, List<ApertureDesirability> desirabilities, IDesirabilityStrategy strategy)
        {
            if (context == null || context.SolarVisibilityCache == null)
            {
                return null;
            }

            List<ApertureSolarTarget> targets = context.Targets;
            if (targets == null || targets.Count == 0)
            {
                return null;
            }

            SolarVisibilityCache cache = context.SolarVisibilityCache;
            List<SunBin> bins = cache.Bins;
            if (bins == null)
            {
                return null;
            }

            int year = context.Year;
            int timelineHours = DateTime.IsLeapYear(year) ? 8784 : 8760;

            ApertureDesirability first = desirabilities == null || desirabilities.Count == 0 ? null : desirabilities[0];
            // The desirability's MissingWeatherHours counts bin hours skipped for missing/invalid
            // weather values — the same for every aperture in one context (same weather, same bins),
            // and the tests assert that equality. Its EvaluatedHours is a PER-APERTURE contributing-
            // hour count (front-facing sun above the gate), so the SCOPE-level evaluated count is
            // derived instead: every timeline hour with usable weather, day and night — which is
            // what makes Timeline = Evaluated + Missing hold.
            int missingWeatherHours = first?.MissingWeatherHours ?? 0;
            int evaluatedHours = timelineHours - missingWeatherHours;

            // Per-aperture slice bookkeeping: outward normal, cell window, desirability arrays.
            int apertureCount = targets.Count;
            List<Guid> apertureGuids = new List<Guid>();
            List<Geometry.Spatial.Vector3D> outwardNormals = new List<Geometry.Spatial.Vector3D>();
            List<int> offsets = new List<int>();
            List<int> cellCounts = new List<int>();
            List<double[]> directEnergies = new List<double[]>();
            List<double> cellAreas = new List<double>(); // per cell, across all apertures, in shared cell order

            foreach (ApertureSolarTarget target in targets)
            {
                apertureGuids.Add(target.ApertureGuid);
                outwardNormals.Add(target.OutwardNormal);
                offsets.Add(context.CellIndexOffset(target.ApertureGuid));
                cellCounts.Add(target.CellCount);

                List<AnalysisCell> cells = target.AnalysisCells;
                foreach (AnalysisCell cell in cells ?? new List<AnalysisCell>())
                {
                    cellAreas.Add(cell?.Area ?? 0);
                }

                ApertureDesirability desirability = desirabilities == null ? null : desirabilities.Find(x => x != null && x.ApertureGuid == target.ApertureGuid);
                directEnergies.Add(desirability?.DirectEnergyPerGroup);
            }

            // Sun-up hours: the hours that landed in a bin at all.
            int sunUpHours = 0;
            foreach (SunBin bin in bins)
            {
                sunUpHours += bin.HoursOfYear?.Count ?? 0;
            }

            // Per bin: which apertures are front-facing, which are lit; the hour counts follow from
            // the bin hour lists, the energy accumulation from the same nested bin x cell loop the
            // accounting uses, with the lit test swapped for the front-facing test.
            int binCount = bins.Count;
            bool[] anyFront = new bool[binCount];
            bool[] anyLit = new bool[binCount];
            bool[,] apertureLit = new bool[binCount, apertureCount];
            double frontFacingEnergy = 0;
            double admittedEnergy = 0;

            for (int b = 0; b < binCount; b++)
            {
                SunBin bin = bins[b];
                Geometry.Spatial.Vector3D direction = bin.RepresentativeDirection;

                bool front = false;
                bool lit = false;
                for (int a = 0; a < apertureCount; a++)
                {
                    Geometry.Spatial.Vector3D normal = outwardNormals[a];
                    // THE FRONT-FACING TEST. RepresentativeDirection is stored in the sun -> surface
                    // convention (Z < 0 when the sun is up), so the sun is geometrically IN FRONT of
                    // an aperture exactly when the dot product against the outward normal is
                    // NEGATIVE — the raycast itself uses the negated direction (see
                    // Create.SolarVisibilityCache). This is the same test the cache's lit gate
                    // includes, applied with the surroundings ignored.
                    if (direction != null && normal != null && direction.DotProduct(normal) < 0)
                    {
                        front = true;

                        int offset = offsets[a];
                        int count = cellCounts[a];
                        double[] direct = directEnergies[a];
                        if (offset >= 0 && direct != null && direct.Length == binCount)
                        {
                            for (int c = 0; c < count; c++)
                            {
                                double area = cellAreas[offset + c];
                                frontFacingEnergy += area * direct[b];
                            }
                        }
                    }

                    int offsetLit = offsets[a];
                    int countLit = cellCounts[a];
                    if (offsetLit >= 0)
                    {
                        for (int c = 0; c < countLit; c++)
                        {
                            if (cache.IsLit(b, offsetLit + c))
                            {
                                lit = true;
                                apertureLit[b, a] = true;

                                double area = cellAreas[offsetLit + c];
                                double[] direct = directEnergies[a];
                                if (direct != null && direct.Length == binCount)
                                {
                                    admittedEnergy += area * direct[b];
                                }
                            }
                        }
                    }
                }

                anyFront[b] = front;
                anyLit[b] = lit;
            }

            int facadeIncidentHours = 0;
            int beamAdmittingHours = 0;
            Dictionary<Guid, int> perApertureHours = new Dictionary<Guid, int>();
            for (int a = 0; a < apertureCount; a++)
            {
                perApertureHours[apertureGuids[a]] = 0;
            }

            for (int b = 0; b < binCount; b++)
            {
                int binHours = bins[b].HoursOfYear?.Count ?? 0;
                if (anyFront[b])
                {
                    facadeIncidentHours += binHours;
                }

                if (anyLit[b])
                {
                    beamAdmittingHours += binHours;
                }

                for (int a = 0; a < apertureCount; a++)
                {
                    if (apertureLit[b, a])
                    {
                        perApertureHours[apertureGuids[a]] += binHours;
                    }
                }
            }

            // The unwanted/wanted/neutral split of the beam-admitting hours, from per-hour weights.
            // A beam-admitting hour is one where ANY scope aperture is lit.
            HashSet<int> beamHours = new HashSet<int>();
            for (int b = 0; b < binCount; b++)
            {
                if (!anyLit[b])
                {
                    continue;
                }

                foreach (int hour in bins[b].HoursOfYear ?? new List<int>())
                {
                    beamHours.Add(hour);
                }
            }

            int unwantedHours = 0;
            int wantedHours = 0;
            int neutralHours = 0;
            if (strategy != null)
            {
                ApertureSolarTarget weightTarget = targets[0];
                WeatherData weatherData = context.WeatherData;
                DateTime yearStart = new DateTime(year, 1, 1);
                foreach (int hour in beamHours)
                {
                    DateTime dateTime = yearStart.AddHours(hour);
                    WeatherHour weatherHour = weatherData?.GetWeatherHour(dateTime);
                    double weight = strategy.Weight(dateTime, weatherHour, weightTarget);

                    if (double.IsNaN(weight))
                    {
                        continue;
                    }

                    if (weight > 0)
                    {
                        unwantedHours++;
                    }
                    else if (weight < 0)
                    {
                        wantedHours++;
                    }
                    else
                    {
                        neutralHours++;
                    }
                }
            }

            return new ShadingAnalysisHours(
                timelineHours,
                evaluatedHours,
                missingWeatherHours,
                sunUpHours,
                facadeIncidentHours,
                beamAdmittingHours,
                frontFacingEnergy - admittedEnergy,
                unwantedHours,
                wantedHours,
                neutralHours,
                perApertureHours);
        }
    }
}

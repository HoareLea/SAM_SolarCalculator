// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// The hours of the year in which a given shading element actually intercepts sun that
        /// would otherwise reach the aperture: a pure LOOKUP over the existing caches, no ray
        /// tracing and no sun-position evaluation.
        ///
        /// MECHANISM. Every hour of the year belongs to exactly one sun bin, and
        /// <see cref="SunBin.HoursOfYear"/> already lists them. An element is EFFECTIVE in a bin
        /// when at least one analysis cell's traced ray first hits that element — read from the
        /// attribution cache, addressed at LOCAL cell index c and paired with the base visibility
        /// cache at the shared-space index cellIndexOffset + c, exactly as the ShadingPerformance
        /// accounting pairs them:
        ///
        ///   effective(element) = union over bins b of
        ///       { b.HoursOfYear intersect hoursOfYear }
        ///   where there exists a cell c with
        ///       solarVisibilityCache.IsLit(b, cellIndexOffset + c)
        ///       AND attributionCache.FirstHitGuid(b, c) == element
        ///
        /// The lit gate matters and is the accounting's own: a sample the base context already
        /// shades is never the candidate's to claim, so its first hit is not consulted by the
        /// energy accounting and must not qualify the element here either.
        ///
        /// THE HOUR-LEVEL TRUTH, NOT THE RAY-LEVEL ONE. Within one hour different cells may
        /// first-hit different elements, so an hour can be effective for the canopy AND for the
        /// valance at once. First-hit exclusivity holds per (bin, cell) ray — never assert it per
        /// hour. What per hour always holds is that an element's effective hours are a subset of
        /// the hours supplied (the deployed schedule).
        ///
        /// Cost is bins x cells Guid lookups — thousands of comparisons, nothing geometric.
        /// </summary>
        /// <param name="attributionCache">First hit over context PLUS the device, this target's cells only. Addressed at local cell index c.</param>
        /// <param name="solarVisibilityCache">Visibility with context only. Its bins supply the hour-of-year membership and its lit bits the accounting gate.</param>
        /// <param name="elementGuid">The element to find effective hours for. Guid.Empty is refused.</param>
        /// <param name="hoursOfYear">The schedule to intersect with, typically the deployed hours. 0-based.</param>
        /// <param name="cellCount">This target's analysis-cell count.</param>
        /// <param name="cellIndexOffset">This target's first cell index within the visibility cache's shared cell space.</param>
        /// <returns>The effective hours, ascending. Null when the inputs cannot describe one schedule.</returns>
        public static List<int> ElementEffectiveHoursOfYear(this SolarAttributionCache attributionCache, SolarVisibilityCache solarVisibilityCache, Guid elementGuid, IEnumerable<int> hoursOfYear, int cellCount, int cellIndexOffset = 0)
        {
            if (attributionCache == null || solarVisibilityCache == null || elementGuid == Guid.Empty || hoursOfYear == null)
            {
                return null;
            }

            List<SunBin> bins = solarVisibilityCache.Bins;
            if (bins == null || bins.Count != attributionCache.BinCount)
            {
                return null;
            }

            if (cellCount <= 0 || cellCount > attributionCache.CellCount
                || cellIndexOffset < 0 || cellIndexOffset + cellCount > solarVisibilityCache.CellCount)
            {
                return null;
            }

            HashSet<int> schedule = new HashSet<int>(hoursOfYear);
            if (schedule.Count == 0)
            {
                return new List<int>();
            }

            HashSet<int> result = new HashSet<int>();
            for (int b = 0; b < bins.Count; b++)
            {
                bool effective = false;
                for (int c = 0; c < cellCount; c++)
                {
                    if (!solarVisibilityCache.IsLit(b, cellIndexOffset + c))
                    {
                        continue;
                    }

                    if (attributionCache.FirstHitGuid(b, c) == elementGuid)
                    {
                        effective = true;
                        break;
                    }
                }

                if (!effective)
                {
                    continue;
                }

                List<int> binHours = bins[b]?.HoursOfYear;
                if (binHours == null)
                {
                    continue;
                }

                foreach (int hourOfYear in binHours)
                {
                    if (schedule.Contains(hourOfYear))
                    {
                        result.Add(hourOfYear);
                    }
                }
            }

            List<int> result_List = new List<int>(result);
            result_List.Sort();
            return result_List;
        }
    }
}

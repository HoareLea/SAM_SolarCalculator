// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.Weather.SolarCalculator
{
    /// <summary>
    /// One sun-position bin: all hours whose sun direction falls inside the same
    /// (altitude, azimuth) quantisation cell. The representative direction is the angular
    /// BIN CENTRE — a deterministic, purely geometric choice. It deliberately does NOT use
    /// any irradiance (DNI) weighting: binning is a property of solar geometry only, so the
    /// same cache stays valid for any weather file and any AnalysisPeriod.
    /// </summary>
    public class SunBin : IJSAMObject, ISolarObject
    {
        private int index;
        private int altitudeBin;
        private int azimuthBin;
        private Vector3D representativeDirection;
        private List<int> hoursOfYear;

        public SunBin(int index, int altitudeBin, int azimuthBin, Vector3D representativeDirection, IEnumerable<int> hoursOfYear)
        {
            this.index = index;
            this.altitudeBin = altitudeBin;
            this.azimuthBin = azimuthBin;
            this.representativeDirection = representativeDirection == null ? null : new Vector3D(representativeDirection);
            this.hoursOfYear = hoursOfYear == null ? null : new List<int>(hoursOfYear);
        }

        public SunBin(SunBin sunBin)
        {
            if (sunBin != null)
            {
                index = sunBin.index;
                altitudeBin = sunBin.altitudeBin;
                azimuthBin = sunBin.azimuthBin;
                representativeDirection = sunBin.representativeDirection == null ? null : new Vector3D(sunBin.representativeDirection);
                hoursOfYear = sunBin.hoursOfYear == null ? null : new List<int>(sunBin.hoursOfYear);
            }
        }

        public SunBin(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public int Index
        {
            get
            {
                return index;
            }
        }

        public int AltitudeBin
        {
            get
            {
                return altitudeBin;
            }
        }

        public int AzimuthBin
        {
            get
            {
                return azimuthBin;
            }
        }

        /// <summary>Unit vector at the angular bin centre, sun-to-surface convention (Z &lt; 0 when the sun is up).</summary>
        public Vector3D RepresentativeDirection
        {
            get
            {
                return representativeDirection == null ? null : new Vector3D(representativeDirection);
            }
        }

        /// <summary>Hours of the year (0-based) whose sun direction falls in this bin, ascending.</summary>
        public List<int> HoursOfYear
        {
            get
            {
                return hoursOfYear == null ? null : new List<int>(hoursOfYear);
            }
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("Index"))
            {
                index = jObject["Index"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("AltitudeBin"))
            {
                altitudeBin = jObject["AltitudeBin"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("AzimuthBin"))
            {
                azimuthBin = jObject["AzimuthBin"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("RepresentativeDirection"))
            {
                representativeDirection = new Vector3D(jObject["RepresentativeDirection"] as JsonObject);
            }

            hoursOfYear = null;
            if (jObject.ContainsKey("HoursOfYear"))
            {
                JsonArray jArray = jObject["HoursOfYear"] as JsonArray;
                if (jArray != null)
                {
                    hoursOfYear = new List<int>();
                    foreach (JsonNode jNode in jArray)
                    {
                        hoursOfYear.Add(jNode?.GetValue<int>() ?? default);
                    }
                }
            }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            jObject.Add("Index", index);
            jObject.Add("AltitudeBin", altitudeBin);
            jObject.Add("AzimuthBin", azimuthBin);

            if (representativeDirection != null)
            {
                jObject.Add("RepresentativeDirection", representativeDirection.ToJsonObject());
            }

            if (hoursOfYear != null)
            {
                JsonArray jArray = new JsonArray();
                foreach (int hourOfYear in hoursOfYear)
                {
                    jArray.Add(hourOfYear);
                }
                jObject.Add("HoursOfYear", jArray);
            }

            return jObject;
        }
    }
}

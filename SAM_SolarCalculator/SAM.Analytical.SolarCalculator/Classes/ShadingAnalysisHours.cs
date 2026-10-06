// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Weather;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// How much of the year the study actually looked at. Without this a figure like
    /// "52.3 kWh of unwanted solar intercepted" has no denominator in time.
    ///
    /// WHY IT SEPARATES TWO IDENTICAL-LOOKING CASES. A window with little solar because it faces
    /// away from the sun, and a window with little solar because a neighbouring building blocks it,
    /// produce the same energies. The first is a design fact; the second is a site fact a shading
    /// study must surface, because a device on a window already 60 % obstructed has far less to work
    /// with than its raw energies suggest. <see cref="FacadeIncidentHours"/> (buildings ignored) and
    /// <see cref="BeamAdmittingHours"/> (buildings in place) separate them.
    ///
    /// "CONTEXT IGNORED" MEANS BUILDING AND SITE OCCLUDERS ONLY. <see cref="FacadeIncidentHours"/>
    /// keeps the horizon cut applied when the visibility cache was built and does not restore hours
    /// the sun spent below the horizon; it is never the theoretical maximum.
    /// </summary>
    public class ShadingAnalysisHours : IJSAMObject, ISolarObject
    {
        private int timelineHours;
        private int evaluatedHours;
        private int missingWeatherHours;
        private int sunUpHours;
        private int facadeIncidentHours;
        private int beamAdmittingHours;
        private double contextObstructedEnergy = double.NaN;
        private int unwantedHours;
        private int wantedHours;
        private int neutralHours;
        private Dictionary<Guid, int> beamAdmittingHoursPerAperture = new Dictionary<Guid, int>();

        public ShadingAnalysisHours(
            int timelineHours,
            int evaluatedHours,
            int missingWeatherHours,
            int sunUpHours,
            int facadeIncidentHours,
            int beamAdmittingHours,
            double contextObstructedEnergy,
            int unwantedHours,
            int wantedHours,
            int neutralHours,
            Dictionary<Guid, int> beamAdmittingHoursPerAperture)
        {
            this.timelineHours = timelineHours;
            this.evaluatedHours = evaluatedHours;
            this.missingWeatherHours = missingWeatherHours;
            this.sunUpHours = sunUpHours;
            this.facadeIncidentHours = facadeIncidentHours;
            this.beamAdmittingHours = beamAdmittingHours;
            this.contextObstructedEnergy = contextObstructedEnergy;
            this.unwantedHours = unwantedHours;
            this.wantedHours = wantedHours;
            this.neutralHours = neutralHours;
            this.beamAdmittingHoursPerAperture = beamAdmittingHoursPerAperture == null ? new Dictionary<Guid, int>() : new Dictionary<Guid, int>(beamAdmittingHoursPerAperture);
        }

        public ShadingAnalysisHours(ShadingAnalysisHours shadingAnalysisHours)
        {
            if (shadingAnalysisHours != null)
            {
                timelineHours = shadingAnalysisHours.timelineHours;
                evaluatedHours = shadingAnalysisHours.evaluatedHours;
                missingWeatherHours = shadingAnalysisHours.missingWeatherHours;
                sunUpHours = shadingAnalysisHours.sunUpHours;
                facadeIncidentHours = shadingAnalysisHours.facadeIncidentHours;
                beamAdmittingHours = shadingAnalysisHours.beamAdmittingHours;
                contextObstructedEnergy = shadingAnalysisHours.contextObstructedEnergy;
                unwantedHours = shadingAnalysisHours.unwantedHours;
                wantedHours = shadingAnalysisHours.wantedHours;
                neutralHours = shadingAnalysisHours.neutralHours;
                beamAdmittingHoursPerAperture = new Dictionary<Guid, int>(shadingAnalysisHours.beamAdmittingHoursPerAperture);
            }
        }

        public ShadingAnalysisHours(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>Hours on the weather timeline: 8760, or 8784 in a leap year.</summary>
        public int TimelineHours { get { return timelineHours; } }

        /// <summary>Hours with usable weather (ApertureDesirability.EvaluatedHours).</summary>
        public int EvaluatedHours { get { return evaluatedHours; } }

        /// <summary>Hours skipped for missing weather values.</summary>
        public int MissingWeatherHours { get { return missingWeatherHours; } }

        /// <summary>Evaluated hours whose sun position landed in a sun bin (above the horizon gate).</summary>
        public int SunUpHours { get { return sunUpHours; } }

        /// <summary>Sun-up hours the sun was geometrically in FRONT of at least one scope aperture — context ignored.</summary>
        public int FacadeIncidentHours { get { return facadeIncidentHours; } }

        /// <summary>Of those, the hours beam actually arrives with the surrounding buildings in place and no device. The denominator of every energy in the report.</summary>
        public int BeamAdmittingHours { get { return beamAdmittingHours; } }

        /// <summary>FacadeIncidentHours - BeamAdmittingHours: the hours the surroundings remove.</summary>
        public int ContextObstructedHours { get { return facadeIncidentHours - beamAdmittingHours; } }

        /// <summary>kWh the surroundings remove before any device is considered.</summary>
        public double ContextObstructedEnergy { get { return contextObstructedEnergy; } }

        /// <summary>Of the beam-admitting hours, those the brief weights positive (block-me).</summary>
        public int UnwantedHours { get { return unwantedHours; } }

        /// <summary>Of the beam-admitting hours, those the brief weights negative (keep-me).</summary>
        public int WantedHours { get { return wantedHours; } }

        /// <summary>Of the beam-admitting hours, those the brief weights zero.</summary>
        public int NeutralHours { get { return neutralHours; } }

        /// <summary>Beam-admitting hours per scope aperture.</summary>
        public Dictionary<Guid, int> BeamAdmittingHoursPerAperture { get { return new Dictionary<Guid, int>(beamAdmittingHoursPerAperture); } }

        public int BeamAdmittingHoursOf(Guid apertureGuid)
        {
            return beamAdmittingHoursPerAperture.TryGetValue(apertureGuid, out int hours) ? hours : 0;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("TimelineHours")) { timelineHours = jObject["TimelineHours"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("EvaluatedHours")) { evaluatedHours = jObject["EvaluatedHours"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("MissingWeatherHours")) { missingWeatherHours = jObject["MissingWeatherHours"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("SunUpHours")) { sunUpHours = jObject["SunUpHours"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("FacadeIncidentHours")) { facadeIncidentHours = jObject["FacadeIncidentHours"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("BeamAdmittingHours")) { beamAdmittingHours = jObject["BeamAdmittingHours"]?.GetValue<int>() ?? default; }
            contextObstructedEnergy = Read(jObject, "ContextObstructedEnergy");
            if (jObject.ContainsKey("UnwantedHours")) { unwantedHours = jObject["UnwantedHours"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("WantedHours")) { wantedHours = jObject["WantedHours"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("NeutralHours")) { neutralHours = jObject["NeutralHours"]?.GetValue<int>() ?? default; }

            beamAdmittingHoursPerAperture = new Dictionary<Guid, int>();
            if (jObject.ContainsKey("BeamAdmittingHoursPerAperture") && jObject["BeamAdmittingHoursPerAperture"] is JsonArray perApertureArray)
            {
                foreach (JsonNode node in perApertureArray)
                {
                    JsonObject entry = node as JsonObject;
                    if (entry == null || !Guid.TryParse(entry["Guid"]?.GetValue<string>(), out Guid guid))
                    {
                        continue;
                    }

                    beamAdmittingHoursPerAperture[guid] = entry["Hours"]?.GetValue<int>() ?? 0;
                }
            }

            return true;
        }

        private static double Read(JsonObject jObject, string name)
        {
            return jObject.ContainsKey(name) ? (jObject[name]?.GetValue<double>() ?? double.NaN) : double.NaN;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            jObject.Add("TimelineHours", timelineHours);
            jObject.Add("EvaluatedHours", evaluatedHours);
            jObject.Add("MissingWeatherHours", missingWeatherHours);
            jObject.Add("SunUpHours", sunUpHours);
            jObject.Add("FacadeIncidentHours", facadeIncidentHours);
            jObject.Add("BeamAdmittingHours", beamAdmittingHours);
            AddFinite(jObject, "ContextObstructedEnergy", contextObstructedEnergy);
            jObject.Add("UnwantedHours", unwantedHours);
            jObject.Add("WantedHours", wantedHours);
            jObject.Add("NeutralHours", neutralHours);

            JsonArray perApertureArray = new JsonArray();
            foreach (KeyValuePair<Guid, int> pair in beamAdmittingHoursPerAperture)
            {
                JsonObject entry = new JsonObject();
                entry.Add("Guid", pair.Key.ToString());
                entry.Add("Hours", pair.Value);
                perApertureArray.Add(entry);
            }
            jObject.Add("BeamAdmittingHoursPerAperture", perApertureArray);

            return jObject;
        }

        /// <summary>Non-finite doubles are OMITTED from the JSON (absent = NaN on read), so the serialised form is always writable and byte-stable.</summary>
        private static void AddFinite(JsonObject jObject, string name, double value)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value))
            {
                jObject.Add(name, value);
            }
        }
    }
}

// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>One evaluated candidate of the grouped awning search, kept so the winner can be checked rather than taken on trust.</summary>
    public class AwningSearchCandidate : IJSAMObject, ISolarObject
    {
        private string typologyName;
        private double projection = double.NaN;
        private double tiltDegrees = double.NaN;
        private double valanceDepth = double.NaN;
        private double score = double.NaN;
        private double materialFraction = double.NaN;
        private bool measurable;

        public AwningSearchCandidate(string typologyName, double projection, double tiltDegrees, double valanceDepth, double score, double materialFraction, bool measurable)
        {
            this.typologyName = typologyName;
            this.projection = projection;
            this.tiltDegrees = tiltDegrees;
            this.valanceDepth = valanceDepth;
            this.score = score;
            this.materialFraction = materialFraction;
            this.measurable = measurable;
        }

        public AwningSearchCandidate(JsonObject jObject)
        {
            if (jObject == null)
            {
                return;
            }

            if (jObject.ContainsKey("TypologyName")) { typologyName = jObject["TypologyName"]?.GetValue<string>(); }
            projection = Read(jObject, "Projection");
            tiltDegrees = Read(jObject, "TiltDegrees");
            valanceDepth = Read(jObject, "ValanceDepth");
            score = Read(jObject, "Score");
            materialFraction = Read(jObject, "MaterialFraction");
            if (jObject.ContainsKey("Measurable")) { measurable = jObject["Measurable"]?.GetValue<bool>() ?? false; }
        }

        public string TypologyName { get { return typologyName; } }

        public double Projection { get { return projection; } }

        public double TiltDegrees { get { return tiltDegrees; } }

        public double ValanceDepth { get { return valanceDepth; } }

        public double Score { get { return score; } }

        public double MaterialFraction { get { return materialFraction; } }

        public bool Measurable { get { return measurable; } }

        private static double Read(JsonObject jObject, string name)
        {
            return jObject.ContainsKey(name) ? (jObject[name]?.GetValue<double>() ?? double.NaN) : double.NaN;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            return jObject != null;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            if (typologyName != null) { jObject.Add("TypologyName", typologyName); }
            jObject.Add("Projection", projection);
            jObject.Add("TiltDegrees", tiltDegrees);
            jObject.Add("ValanceDepth", valanceDepth);
            jObject.Add("Score", score);
            jObject.Add("MaterialFraction", materialFraction);
            jObject.Add("Measurable", measurable);
            return jObject;
        }
    }

    /// <summary>
    /// The grouped awning answer for ONE aperture group: the recommended device (or the null device
    /// when nothing beats building nothing), its measured group performance, and the diagnostics
    /// that let the recommendation be reviewed rather than taken on trust.
    ///
    /// STATUS. NotEvaluated outranks everything — numbers that do not exist cannot be reported as
    /// "no shading needed". NoShading is a successful measured answer. A device recommendation
    /// becomes Warning when the host-bound fit could not be verified, exactly as the single-aperture
    /// component warns about grid-resolution caps.
    /// </summary>
    public class GroupedAwningResult : IJSAMObject, ISolarObject
    {
        private ApertureShadingGroup group;
        private GroupedShadingDevice device;
        private GroupedShadingDevice bestCandidateDevice;
        private GroupedShadingPerformance performance;
        private ShadingDesignStatus status = ShadingDesignStatus.Undefined;
        private string designSummary;
        private List<string> warnings = new List<string>();
        private List<AwningSearchCandidate> evaluatedCandidates = new List<AwningSearchCandidate>();
        private int evaluations;

        public GroupedAwningResult(ApertureShadingGroup group, GroupedShadingDevice device, GroupedShadingDevice bestCandidateDevice, GroupedShadingPerformance performance, ShadingDesignStatus status, string designSummary, IEnumerable<string> warnings, IEnumerable<AwningSearchCandidate> evaluatedCandidates, int evaluations)
        {
            this.group = group == null ? null : new ApertureShadingGroup(group);
            this.device = device == null ? null : new GroupedShadingDevice(device);
            this.bestCandidateDevice = bestCandidateDevice == null ? null : new GroupedShadingDevice(bestCandidateDevice);
            this.performance = performance;
            this.status = status;
            this.designSummary = designSummary;
            this.warnings = new List<string>(warnings ?? new List<string>());
            this.evaluatedCandidates = new List<AwningSearchCandidate>(evaluatedCandidates ?? new List<AwningSearchCandidate>());
            this.evaluations = evaluations;
        }

        public GroupedAwningResult(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public ApertureShadingGroup Group { get { return group == null ? null : new ApertureShadingGroup(group); } }

        /// <summary>The recommended device. NoShading = the measured NO SHADE answer, never a failure.</summary>
        public GroupedShadingDevice Device { get { return device == null ? null : new GroupedShadingDevice(device); } }

        /// <summary>The least-bad candidate, ALWAYS supplied for a NoShading answer so the recommendation can be checked.</summary>
        public GroupedShadingDevice BestCandidateDevice { get { return bestCandidateDevice == null ? null : new GroupedShadingDevice(bestCandidateDevice); } }

        public GroupedShadingPerformance Performance { get { return performance; } }

        public ShadingDesignStatus Status { get { return status; } }

        public string StatusText { get { return Query.StatusText(status); } }

        public string DesignSummary { get { return designSummary; } }

        public List<string> Warnings { get { return new List<string>(warnings); } }

        /// <summary>Every evaluated candidate, best first, so the winner can be checked rather than taken on trust.</summary>
        public List<AwningSearchCandidate> EvaluatedCandidates { get { return new List<AwningSearchCandidate>(evaluatedCandidates); } }

        /// <summary>Distinct candidate geometries actually measured for this group.</summary>
        public int Evaluations { get { return evaluations; } }

        public double Width { get { return device?.Width(group) ?? double.NaN; } }

        public double Projection { get { return Parameter("Projection"); } }

        public double TiltDegrees { get { return Parameter("TiltDegrees"); } }

        public double ValanceDepth { get { return Parameter("ValanceDepth"); } }

        /// <summary>The fixed horizontal outward distance from the aperture plane to the awning mounting line [m].</summary>
        public double MountingOffset { get { return Parameter("MountingOffset"); } }

        /// <summary>Wall bracket count for the recommended awning width. 0 when there is no awning.</summary>
        public int RequiredWallBrackets
        {
            get
            {
                double width = Width;
                AwningSpecification specification = device?.Specification;
                return specification == null || double.IsNaN(width) || device.IsNoShading ? 0 : specification.RequiredWallBracketCount(width);
            }
        }

        private double Parameter(string name)
        {
            double value = device?.Typology?.GetParameter(name) ?? double.NaN;
            return value;
        }

        /// <summary>
        /// Appends a host-bound finding to the answer. A recommended device with an unresolved
        /// warning is promoted from OK to WARNING, following the single-aperture convention that a
        /// "check before building" result must not read as an unqualified success. NO SHADE and
        /// NOT EVALUATED outrank the warning and are left alone.
        /// </summary>
        internal void AddHostBoundWarning(string warning)
        {
            if (string.IsNullOrWhiteSpace(warning))
            {
                return;
            }

            warnings.Add(warning);
            if (status == ShadingDesignStatus.Ok)
            {
                status = ShadingDesignStatus.Warning;
            }
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            group = jObject.ContainsKey("Group") ? new ApertureShadingGroup(jObject["Group"] as JsonObject) : null;
            device = jObject.ContainsKey("Device") ? new GroupedShadingDevice(jObject["Device"] as JsonObject) : null;
            bestCandidateDevice = jObject.ContainsKey("BestCandidateDevice") ? new GroupedShadingDevice(jObject["BestCandidateDevice"] as JsonObject) : null;
            performance = jObject.ContainsKey("Performance") ? new GroupedShadingPerformance(jObject["Performance"] as JsonObject) : null;
            if (jObject.ContainsKey("Status")) { Enum.TryParse(jObject["Status"]?.GetValue<string>(), out status); }
            if (jObject.ContainsKey("DesignSummary")) { designSummary = jObject["DesignSummary"]?.GetValue<string>(); }

            warnings = new List<string>();
            if (jObject.ContainsKey("Warnings") && jObject["Warnings"] is JsonArray warningsArray)
            {
                foreach (JsonNode node in warningsArray)
                {
                    warnings.Add(node?.GetValue<string>());
                }
            }

            evaluatedCandidates = new List<AwningSearchCandidate>();
            if (jObject.ContainsKey("EvaluatedCandidates") && jObject["EvaluatedCandidates"] is JsonArray candidatesArray)
            {
                foreach (JsonNode node in candidatesArray)
                {
                    if (node is JsonObject candidateObject)
                    {
                        evaluatedCandidates.Add(new AwningSearchCandidate(candidateObject));
                    }
                }
            }

            if (jObject.ContainsKey("Evaluations")) { evaluations = jObject["Evaluations"]?.GetValue<int>() ?? default; }
            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            if (group != null) { jObject.Add("Group", group.ToJsonObject()); }
            if (device != null) { jObject.Add("Device", device.ToJsonObject()); }
            if (bestCandidateDevice != null) { jObject.Add("BestCandidateDevice", bestCandidateDevice.ToJsonObject()); }
            if (performance != null) { jObject.Add("Performance", performance.ToJsonObject()); }
            jObject.Add("Status", status.ToString());
            if (designSummary != null) { jObject.Add("DesignSummary", designSummary); }

            JsonArray warningsArray = new JsonArray();
            foreach (string warning in warnings)
            {
                warningsArray.Add(warning);
            }
            jObject.Add("Warnings", warningsArray);

            JsonArray candidatesArray = new JsonArray();
            foreach (AwningSearchCandidate candidate in evaluatedCandidates)
            {
                candidatesArray.Add(candidate?.ToJsonObject());
            }
            jObject.Add("EvaluatedCandidates", candidatesArray);

            jObject.Add("Evaluations", evaluations);
            return jObject;
        }
    }
}

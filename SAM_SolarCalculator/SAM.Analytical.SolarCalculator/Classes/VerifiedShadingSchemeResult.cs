// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// One shading scheme, verified: the scheme, its independently measured performance against
    /// EVERY aperture of its scope at once, and the analysis-basis signature that makes the
    /// measurement comparable with another scheme's.
    ///
    /// THE MEASUREMENT IS OF THE COMPLETE SCHEME. Every member aperture is measured against the
    /// whole scheme's element set, so an overhang over window A that overhangs window B is credited
    /// on B. <see cref="DesignTimeScore"/> — the sum of the per-window searches — is carried
    /// separately and never ranks; the gap between the two is the cross-shading blind spot this
    /// verification exists to surface.
    /// </summary>
    public class VerifiedShadingSchemeResult : IJSAMObject, ISolarObject
    {
        private ShadingScheme scheme;
        private ShadingSchemePerformance performance;
        private ShadingAnalysisSignature signature;
        private ShadingDesignStatus status = ShadingDesignStatus.Undefined;
        private List<string> warnings = new List<string>();
        private string verificationSummary;
        private string schemeGeometryHash;
        private ShadingObjective designObjective;
        private double designTimeScore = double.NaN;
        private double verifiedScore = double.NaN;
        private List<string> designDiagnostics = new List<string>();
        private bool reusedPreviousCalculation;

        public VerifiedShadingSchemeResult(
            ShadingScheme scheme,
            ShadingSchemePerformance performance,
            ShadingAnalysisSignature signature,
            ShadingDesignStatus status,
            IEnumerable<string> warnings,
            string verificationSummary,
            string schemeGeometryHash,
            double verifiedScore,
            bool reusedPreviousCalculation)
        {
            this.scheme = scheme == null ? null : new ShadingScheme(scheme);
            this.performance = performance;
            this.signature = signature == null ? null : new ShadingAnalysisSignature(signature);
            this.status = status;
            this.warnings = new List<string>(warnings ?? new List<string>());
            this.verificationSummary = verificationSummary;
            this.schemeGeometryHash = schemeGeometryHash;
            this.designObjective = scheme?.DesignObjective;
            this.designTimeScore = scheme?.DesignTimeScore ?? double.NaN;
            this.verifiedScore = verifiedScore;
            this.designDiagnostics = scheme?.DesignDiagnostics ?? new List<string>();
            this.reusedPreviousCalculation = reusedPreviousCalculation;
        }

        public VerifiedShadingSchemeResult(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public ShadingScheme Scheme { get { return scheme == null ? null : new ShadingScheme(scheme); } }

        public ShadingSchemePerformance Performance { get { return performance; } }

        public ShadingAnalysisSignature Signature { get { return signature == null ? null : new ShadingAnalysisSignature(signature); } }

        public ShadingDesignStatus Status { get { return status; } }

        public List<string> Warnings { get { return new List<string>(warnings); } }

        /// <summary>One line, Panel-readable: the scheme answer and its two headline percentages.</summary>
        public string VerificationSummary { get { return verificationSummary; } }

        /// <summary>Hash over the built scheme elements, so a changed geometry cannot hide behind an unchanged name.</summary>
        public string SchemeGeometryHash { get { return schemeGeometryHash; } }

        /// <summary>The objective the geometry was optimised under; null when none was recorded.</summary>
        public ShadingObjective DesignObjective { get { return designObjective == null ? null : new ShadingObjective(designObjective); } }

        /// <summary>
        /// The objective value recorded while the geometry was being searched. NEVER used for
        /// ranking. NaN when not recorded. Where it differs from <see cref="VerifiedScore"/> by more
        /// than 1e-6 kWh the difference appears in the report diagnostics — the gap is the signal
        /// that per-window optimisation ignored cross-shading.
        /// </summary>
        public double DesignTimeScore { get { return designTimeScore; } }

        /// <summary>The score computed from the verified whole-scheme performance under <see cref="DesignObjective"/>. NaN when the objective is null. The comparison re-scores every row under its own objective; this value is provenance, not a rank input.</summary>
        public double VerifiedScore { get { return verifiedScore; } }

        public List<string> DesignDiagnostics { get { return new List<string>(designDiagnostics); } }

        /// <summary>True when the verification reused the context's stored visibility calculation (no ray casting).</summary>
        public bool ReusedPreviousCalculation { get { return reusedPreviousCalculation; } }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            scheme = jObject.ContainsKey("Scheme") ? new ShadingScheme(jObject["Scheme"] as JsonObject) : null;
            performance = jObject.ContainsKey("Performance") ? new ShadingSchemePerformance(jObject["Performance"] as JsonObject) : null;
            signature = jObject.ContainsKey("Signature") ? new ShadingAnalysisSignature(jObject["Signature"] as JsonObject) : null;
            if (jObject.ContainsKey("Status")) { Enum.TryParse(jObject["Status"]?.GetValue<string>(), out status); }

            warnings = new List<string>();
            if (jObject.ContainsKey("Warnings") && jObject["Warnings"] is JsonArray warningsArray)
            {
                foreach (JsonNode node in warningsArray)
                {
                    warnings.Add(node?.GetValue<string>());
                }
            }

            if (jObject.ContainsKey("VerificationSummary")) { verificationSummary = jObject["VerificationSummary"]?.GetValue<string>(); }
            if (jObject.ContainsKey("SchemeGeometryHash")) { schemeGeometryHash = jObject["SchemeGeometryHash"]?.GetValue<string>(); }

            designObjective = jObject.ContainsKey("DesignObjective") ? new ShadingObjective(jObject["DesignObjective"] as JsonObject) : null;
            if (jObject.ContainsKey("DesignTimeScore")) { designTimeScore = jObject["DesignTimeScore"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("VerifiedScore")) { verifiedScore = jObject["VerifiedScore"]?.GetValue<double>() ?? double.NaN; }

            designDiagnostics = new List<string>();
            if (jObject.ContainsKey("DesignDiagnostics") && jObject["DesignDiagnostics"] is JsonArray diagnosticsArray)
            {
                foreach (JsonNode node in diagnosticsArray)
                {
                    designDiagnostics.Add(node?.GetValue<string>());
                }
            }

            if (jObject.ContainsKey("ReusedPreviousCalculation")) { reusedPreviousCalculation = jObject["ReusedPreviousCalculation"]?.GetValue<bool>() ?? false; }
            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            if (scheme != null) { jObject.Add("Scheme", scheme.ToJsonObject()); }
            if (performance != null) { jObject.Add("Performance", performance.ToJsonObject()); }
            if (signature != null) { jObject.Add("Signature", signature.ToJsonObject()); }
            jObject.Add("Status", status.ToString());

            JsonArray warningsArray = new JsonArray();
            foreach (string warning in warnings)
            {
                warningsArray.Add(warning);
            }
            jObject.Add("Warnings", warningsArray);

            if (verificationSummary != null) { jObject.Add("VerificationSummary", verificationSummary); }
            if (schemeGeometryHash != null) { jObject.Add("SchemeGeometryHash", schemeGeometryHash); }
            if (designObjective != null) { jObject.Add("DesignObjective", designObjective.ToJsonObject()); }
            AddFinite(jObject, "DesignTimeScore", designTimeScore);
            AddFinite(jObject, "VerifiedScore", verifiedScore);

            JsonArray diagnosticsArray = new JsonArray();
            foreach (string diagnostic in designDiagnostics)
            {
                diagnosticsArray.Add(diagnostic);
            }
            jObject.Add("DesignDiagnostics", diagnosticsArray);

            jObject.Add("ReusedPreviousCalculation", reusedPreviousCalculation);
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

        internal static string Summary(ShadingScheme scheme, ShadingSchemePerformance performance)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(scheme?.Name ?? "Scheme");

            if (performance == null)
            {
                return stringBuilder.ToString();
            }

            stringBuilder.AppendFormat(CultureInfo.InvariantCulture, " | {0:0.###} kWh baseline | {1:0.###} kWh intercepted",
                performance.AdmittedDirectEnergy, performance.DirectSolarIntercepted);

            AppendPercentage(stringBuilder, performance.UnwantedSolarBlocked, "unwanted blocked");
            AppendPercentage(stringBuilder, performance.WantedSolarRetained, "wanted retained");
            return stringBuilder.ToString();
        }

        private static void AppendPercentage(StringBuilder stringBuilder, double ratio, string label)
        {
            stringBuilder.Append(" | ");
            if (double.IsNaN(ratio))
            {
                stringBuilder.Append("n/a ").Append(label);
                return;
            }

            stringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#}% {1}", 100.0 * ratio, label);
        }
    }
}

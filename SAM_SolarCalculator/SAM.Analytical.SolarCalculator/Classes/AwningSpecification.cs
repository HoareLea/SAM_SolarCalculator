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
    /// <summary>
    /// A manufacturer's product limits for a retractable folding-arm awning, kept as DATA so the
    /// solar geometry and the product policy stay apart.
    ///
    /// The solar side (RetractableAwning) builds planes from parameters and knows nothing about what
    /// a Dakar unit is allowed to be; this side knows the product and nothing about ray tracing.
    /// Validation REFUSES rather than clamps: a width, projection or tilt outside the product must
    /// be reported as such, never silently reshaped into the nearest saleable size.
    ///
    /// The Dakar instance encodes the limits of the reference product (Dakar Awning) and is the
    /// only preset shipped in this PR. Nothing here claims these are universal laws for every
    /// manufacturer — a different product is a different instance of this type.
    /// </summary>
    public class AwningSpecification : IJSAMObject, ISolarObject
    {
        /// <summary>Relative tolerance for matching a nominal projection against the allowed set.</summary>
        public const double ProjectionTolerance = 1e-9;

        /// <summary>Absolute tolerance for matching the standard valance depth.</summary>
        public const double ValanceTolerance = 1e-6;

        private readonly string name;
        private readonly List<double> allowedProjections = new List<double>();
        private readonly double minimumTiltDegrees;
        private readonly double maximumTiltDegrees;
        private readonly double maximumWidth;
        private readonly double minimumWidthAllowance;
        private readonly double standardValanceDepth;
        private readonly double bracketThresholdWidth;
        private readonly int bracketCountAtOrBelowThreshold;
        private readonly int bracketCountAboveThreshold;

        /// <summary>
        /// The reference product: the Dakar Awning.
        ///
        ///   allowed nominal projections   1.6, 2.1, 2.6, 3.1, 3.6 m
        ///   tilt from horizontal          5° to 40°
        ///   minimum unit width            projection + 0.4 m
        ///   maximum unit width            6.0 m
        ///   standard valance depth        0.21 m
        ///   wall brackets                 2 up to and including 4.1 m width, 3 above 4.1 m up to 6.0 m
        ///
        /// Mounting hardware (wall, ceiling, roof-rafter) is metadata only in this solar-analysis PR.
        /// The product is NOT modularly connectable: two units are two independent awnings.
        /// </summary>
        public static readonly AwningSpecification Dakar = new AwningSpecification(
            "Dakar",
            new double[] { 1.6, 2.1, 2.6, 3.1, 3.6 },
            5.0, 40.0,
            6.0,
            0.4,
            0.21,
            4.1, 2, 3);

        public AwningSpecification(string name, IEnumerable<double> allowedProjections, double minimumTiltDegrees, double maximumTiltDegrees, double maximumWidth, double minimumWidthAllowance, double standardValanceDepth, double bracketThresholdWidth, int bracketCountAtOrBelowThreshold, int bracketCountAboveThreshold)
        {
            this.name = name;
            if (allowedProjections != null)
            {
                this.allowedProjections.AddRange(allowedProjections);
                this.allowedProjections.Sort();
            }

            this.minimumTiltDegrees = minimumTiltDegrees;
            this.maximumTiltDegrees = maximumTiltDegrees;
            this.maximumWidth = maximumWidth;
            this.minimumWidthAllowance = minimumWidthAllowance;
            this.standardValanceDepth = standardValanceDepth;
            this.bracketThresholdWidth = bracketThresholdWidth;
            this.bracketCountAtOrBelowThreshold = bracketCountAtOrBelowThreshold;
            this.bracketCountAboveThreshold = bracketCountAboveThreshold;
        }

        public AwningSpecification(JsonObject jObject)
        {
            if (jObject == null)
            {
                return;
            }

            if (jObject.ContainsKey("Name")) { name = jObject["Name"]?.GetValue<string>(); }

            allowedProjections = new List<double>();
            if (jObject.ContainsKey("AllowedProjections") && jObject["AllowedProjections"] is JsonArray projectionsArray)
            {
                foreach (JsonNode node in projectionsArray)
                {
                    double value = node?.GetValue<double>() ?? double.NaN;
                    if (!double.IsNaN(value))
                    {
                        allowedProjections.Add(value);
                    }
                }

                allowedProjections.Sort();
            }

            minimumTiltDegrees = Read(jObject, "MinimumTiltDegrees");
            maximumTiltDegrees = Read(jObject, "MaximumTiltDegrees");
            maximumWidth = Read(jObject, "MaximumWidth");
            minimumWidthAllowance = Read(jObject, "MinimumWidthAllowance");
            standardValanceDepth = Read(jObject, "StandardValanceDepth");
            bracketThresholdWidth = Read(jObject, "BracketThresholdWidth");
            if (jObject.ContainsKey("BracketCountAtOrBelowThreshold")) { bracketCountAtOrBelowThreshold = jObject["BracketCountAtOrBelowThreshold"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("BracketCountAboveThreshold")) { bracketCountAboveThreshold = jObject["BracketCountAboveThreshold"]?.GetValue<int>() ?? default; }
        }

        public string Name { get { return name; } }

        /// <summary>Allowed nominal projections [m], ascending.</summary>
        public List<double> AllowedProjections { get { return new List<double>(allowedProjections); } }

        public double MinimumTiltDegrees { get { return minimumTiltDegrees; } }

        public double MaximumTiltDegrees { get { return maximumTiltDegrees; } }

        /// <summary>The largest unit width the product can supply [m].</summary>
        public double MaximumWidth { get { return maximumWidth; } }

        /// <summary>Added to the projection to get the smallest unit width the product needs [m].</summary>
        public double MinimumWidthAllowance { get { return minimumWidthAllowance; } }

        public double StandardValanceDepth { get { return standardValanceDepth; } }

        /// <summary>Width boundary between the two wall-bracket counts [m]; at or below it the lower count applies.</summary>
        public double BracketThresholdWidth { get { return bracketThresholdWidth; } }

        public int BracketCountAtOrBelowThreshold { get { return bracketCountAtOrBelowThreshold; } }

        public int BracketCountAboveThreshold { get { return bracketCountAboveThreshold; } }

        /// <summary>True when the projection is one of the allowed nominal values, within a relative epsilon.</summary>
        public bool IsProjectionAllowed(double projection)
        {
            if (double.IsNaN(projection))
            {
                return false;
            }

            foreach (double allowed in allowedProjections)
            {
                if (Math.Abs(projection - allowed) <= ProjectionTolerance * Math.Max(1.0, Math.Abs(allowed)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The smallest unit width the product needs for this projection [m]. NaN for a projection outside the allowed set.</summary>
        public double MinimumWidth(double projection)
        {
            return IsProjectionAllowed(projection) ? projection + minimumWidthAllowance : double.NaN;
        }

        /// <summary>True when the valance depth is none or the standard depth (0 or the preset standard value).</summary>
        public bool IsValidValanceDepth(double valanceDepth)
        {
            return Math.Abs(valanceDepth) <= ValanceTolerance
                || Math.Abs(valanceDepth - standardValanceDepth) <= ValanceTolerance;
        }

        /// <summary>
        /// Wall bracket count for a unit width: 2 up to and including the threshold, 3 above it.
        /// Widths above the product maximum are the caller's to refuse; this query still answers.
        /// </summary>
        public int RequiredWallBracketCount(double width)
        {
            return width > bracketThresholdWidth ? bracketCountAboveThreshold : bracketCountAtOrBelowThreshold;
        }

        /// <summary>
        /// Whether a width / projection / tilt combination is buildable under the product limits.
        /// Nothing is clamped: on failure <paramref name="message"/> says exactly what to change.
        /// </summary>
        /// <param name="width">Unit width [m], including side extensions.</param>
        /// <param name="projection">Nominal projection [m].</param>
        /// <param name="tiltDegrees">Deployed tilt from horizontal [°].</param>
        /// <param name="message">Null when valid; an actionable sentence otherwise.</param>
        public bool IsValid(double width, double projection, double tiltDegrees, out string message)
        {
            message = null;

            if (!IsProjectionAllowed(projection))
            {
                message = string.Format(CultureInfo.InvariantCulture,
                    "Projection {0:0.###} m is not an allowed {1} projection. Allowed nominal projections are {2} m.",
                    projection, name ?? "awning", FormatProjections());
                return false;
            }

            if (double.IsNaN(width) || width > maximumWidth)
            {
                message = string.Format(CultureInfo.InvariantCulture,
                    "Width {0:0.###} m exceeds the maximum {1} width of {2:0.#} m. Split the apertures into separate independent units.",
                    width, name ?? "awning", maximumWidth);
                return false;
            }

            double minimum = MinimumWidth(projection);
            if (!double.IsNaN(minimum) && width < minimum)
            {
                message = string.Format(CultureInfo.InvariantCulture,
                    "Width {0:0.###} m is below the minimum width of {1:0.###} m required for a {2:0.###} m projection. Widen the awning or choose a shorter projection.",
                    width, minimum, projection);
                return false;
            }

            if (double.IsNaN(tiltDegrees) || tiltDegrees < minimumTiltDegrees || tiltDegrees > maximumTiltDegrees)
            {
                message = string.Format(CultureInfo.InvariantCulture,
                    "Tilt {0:0.#}° is outside the {1} tilt range of {2:0.#}° to {3:0.#}° from horizontal.",
                    tiltDegrees, name ?? "awning", minimumTiltDegrees, maximumTiltDegrees);
                return false;
            }

            return true;
        }

        private string FormatProjections()
        {
            List<string> values = new List<string>();
            foreach (double projection in allowedProjections)
            {
                values.Add(projection.ToString("0.#", CultureInfo.InvariantCulture));
            }

            return string.Join(", ", values);
        }

        private static double Read(JsonObject jObject, string name)
        {
            return jObject.ContainsKey(name) ? (jObject[name]?.GetValue<double>() ?? double.NaN) : double.NaN;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            // Fields are set in the constructor; the type is immutable by convention.
            return jObject != null;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            if (name != null) { jObject.Add("Name", name); }

            JsonArray projections = new JsonArray();
            foreach (double projection in allowedProjections)
            {
                projections.Add(projection);
            }
            jObject.Add("AllowedProjections", projections);

            jObject.Add("MinimumTiltDegrees", minimumTiltDegrees);
            jObject.Add("MaximumTiltDegrees", maximumTiltDegrees);
            jObject.Add("MaximumWidth", maximumWidth);
            jObject.Add("MinimumWidthAllowance", minimumWidthAllowance);
            jObject.Add("StandardValanceDepth", standardValanceDepth);
            jObject.Add("BracketThresholdWidth", bracketThresholdWidth);
            jObject.Add("BracketCountAtOrBelowThreshold", bracketCountAtOrBelowThreshold);
            jObject.Add("BracketCountAboveThreshold", bracketCountAboveThreshold);
            return jObject;
        }
    }
}

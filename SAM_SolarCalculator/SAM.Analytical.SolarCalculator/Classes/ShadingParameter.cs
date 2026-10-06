// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// One search variable: a typology parameter, the range Stage 9 may move it over, and the
    /// granularity it is meaningful at.
    ///
    /// The granularity is not decoration. Some typology parameters are integers in disguise —
    /// HorizontalLouvres.Count is rounded to an int before any geometry is built — so the objective
    /// over them is a staircase, and a search that keeps halving its step below 1 would spend its
    /// evaluations re-measuring identical devices. Snapping every proposal to Step makes the search
    /// space finite and the result exactly reproducible, because two runs cannot land on
    /// almost-equal parameter vectors that round to different geometry.
    ///
    /// Bounds are always intersected with the typology's OWN declared bounds when the parameter set
    /// is built, so Stage 9 can never propose a value the typology would silently clamp — a clamp
    /// would make the reported parameters differ from the geometry actually evaluated.
    /// </summary>
    public class ShadingParameter : IJSAMObject, ISolarObject
    {
        private string name;
        private double minimum = double.NaN;
        private double maximum = double.NaN;
        private double step = double.NaN;

        public ShadingParameter(string name, double minimum, double maximum, double step)
        {
            this.name = name;
            this.minimum = minimum;
            this.maximum = maximum;
            this.step = step;
        }

        public ShadingParameter(ShadingParameter shadingParameter)
        {
            if (shadingParameter != null)
            {
                name = shadingParameter.name;
                minimum = shadingParameter.minimum;
                maximum = shadingParameter.maximum;
                step = shadingParameter.step;
            }
        }

        public ShadingParameter(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public string Name { get { return name; } }

        public double Minimum { get { return minimum; } }

        public double Maximum { get { return maximum; } }

        /// <summary>Smallest meaningful change. Every proposal is snapped to a multiple of it.</summary>
        public double Step { get { return step; } }

        public double Range { get { return maximum - minimum; } }

        /// <summary>
        /// The smallest change to this parameter that <see cref="Snap"/> can actually express: one
        /// place on its own lattice.
        ///
        /// A search that proposes less than this gets the value it started from back, so the probe
        /// rebuilds identical geometry and reports no improvement — indistinguishable from a genuine
        /// local optimum. That is not hypothetical: a count narrowed to [1, 3] by the analysis
        /// resolution has a range of 2, and a step derived as a fraction of that range falls below
        /// the whole element the lattice is made of. Any step schedule that shrinks must therefore
        /// stop HERE rather than at an arbitrary epsilon, or the axis is silently unreachable.
        ///
        /// Where no granularity is declared the lattice is continuous and has no physical floor, so
        /// a small fraction of the range stands in.
        /// </summary>
        public double MinimumIncrement
        {
            get
            {
                if (double.IsNaN(step) || step <= 0)
                {
                    return 1e-4 * Range;
                }

                return step;
            }
        }

        /// <summary>True when the range is a single point — nothing for the search to do.</summary>
        public bool IsFixed { get { return !(Range > 0); } }

        /// <summary>Clamps into range and snaps onto the step lattice measured from the minimum.</summary>
        public double Snap(double value)
        {
            if (double.IsNaN(value))
            {
                return minimum;
            }

            double clamped = Math.Min(maximum, Math.Max(minimum, value));
            if (double.IsNaN(step) || step <= 0)
            {
                return clamped;
            }

            double snapped = minimum + Math.Round((clamped - minimum) / step) * step;
            return Math.Min(maximum, Math.Max(minimum, snapped));
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("Name")) { name = jObject["Name"]?.GetValue<string>(); }
            if (jObject.ContainsKey("Minimum")) { minimum = jObject["Minimum"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("Maximum")) { maximum = jObject["Maximum"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("Step")) { step = jObject["Step"]?.GetValue<double>() ?? double.NaN; }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            if (name != null) { jObject.Add("Name", name); }
            jObject.Add("Minimum", minimum);
            jObject.Add("Maximum", maximum);
            jObject.Add("Step", step);
            return jObject;
        }
    }
}

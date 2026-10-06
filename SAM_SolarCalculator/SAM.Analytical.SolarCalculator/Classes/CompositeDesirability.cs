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
    /// Composite desirability: the weighted sum of component strategies,
    /// Weight = Σ blend_i x component_i.Weight(...). A single component at blend weight 1.0
    /// reproduces that component exactly. The sum is NOT clamped — component magnitudes carry
    /// through, so a dominant component can outvote another proportionally.
    /// </summary>
    public class CompositeDesirability : IDesirabilityStrategy, IJSAMObject
    {
        private List<IDesirabilityStrategy> strategies;
        private List<double> blendWeights;

        public CompositeDesirability(IEnumerable<IDesirabilityStrategy> strategies, IEnumerable<double> blendWeights)
        {
            this.strategies = strategies == null ? new List<IDesirabilityStrategy>() : new List<IDesirabilityStrategy>(strategies);
            this.blendWeights = blendWeights == null ? new List<double>() : new List<double>(blendWeights);
            while (this.blendWeights.Count < this.strategies.Count)
            {
                this.blendWeights.Add(1.0);
            }
        }

        public CompositeDesirability(CompositeDesirability compositeDesirability)
        {
            if (compositeDesirability != null)
            {
                strategies = compositeDesirability.strategies == null ? null : new List<IDesirabilityStrategy>(compositeDesirability.strategies);
                blendWeights = compositeDesirability.blendWeights == null ? null : new List<double>(compositeDesirability.blendWeights);
            }
        }

        public CompositeDesirability(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public List<IDesirabilityStrategy> Strategies
        {
            get
            {
                return strategies == null ? null : new List<IDesirabilityStrategy>(strategies);
            }
        }

        public List<double> BlendWeights
        {
            get
            {
                return blendWeights == null ? null : new List<double>(blendWeights);
            }
        }

        public double Weight(DateTime dateTime, WeatherHour weatherHour, ApertureSolarTarget target)
        {
            if (strategies == null)
            {
                return 0.0;
            }

            double result = 0.0;
            for (int i = 0; i < strategies.Count; i++)
            {
                if (strategies[i] == null)
                {
                    continue;
                }

                double blend = i < blendWeights.Count ? blendWeights[i] : 1.0;
                result += blend * strategies[i].Weight(dateTime, weatherHour, target);
            }

            return result;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            strategies = null;
            if (jObject.ContainsKey("Strategies"))
            {
                JsonArray jArray = jObject["Strategies"] as JsonArray;
                if (jArray != null)
                {
                    strategies = new List<IDesirabilityStrategy>();
                    foreach (JsonNode jNode in jArray)
                    {
                        strategies.Add(Core.Create.IJSAMObject<IDesirabilityStrategy>(jNode as JsonObject));
                    }
                }
            }

            blendWeights = null;
            if (jObject.ContainsKey("BlendWeights"))
            {
                JsonArray jArray = jObject["BlendWeights"] as JsonArray;
                if (jArray != null)
                {
                    blendWeights = new List<double>();
                    foreach (JsonNode jNode in jArray)
                    {
                        blendWeights.Add(jNode?.GetValue<double>() ?? 1.0);
                    }
                }
            }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            if (strategies != null)
            {
                JsonArray jArray = new JsonArray();
                foreach (IDesirabilityStrategy strategy in strategies)
                {
                    jArray.Add((strategy as IJSAMObject)?.ToJsonObject());
                }
                jObject.Add("Strategies", jArray);
            }

            if (blendWeights != null)
            {
                JsonArray jArray = new JsonArray();
                foreach (double blendWeight in blendWeights)
                {
                    jArray.Add(blendWeight);
                }
                jObject.Add("BlendWeights", jArray);
            }

            return jObject;
        }
    }
}

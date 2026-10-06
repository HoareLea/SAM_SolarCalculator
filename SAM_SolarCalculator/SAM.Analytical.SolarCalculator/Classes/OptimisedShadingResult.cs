// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>Why the search stopped. Part of the result, so a run can be judged, not just read.</summary>
    public enum ShadingOptimisationTermination
    {
        Undefined,

        /// <summary>The pattern step fell below every parameter's granularity: a local optimum on the search lattice.</summary>
        StepBelowGranularity,

        /// <summary>The evaluation budget ran out before the step converged. The result is the best seen, not a converged one.</summary>
        EvaluationBudgetExhausted,

        /// <summary>Nothing to search: every parameter is fixed, or the typology produced no geometry.</summary>
        NothingToSearch,

        /// <summary>No candidate beat the null device. The recommendation is to build nothing.</summary>
        NoBeneficialCandidate,

        /// <summary>
        /// No candidate could be MEASURED — the aperture, the caches and the candidate geometry did
        /// not describe the same samples, so every score came back NaN. Distinct from
        /// <see cref="NoBeneficialCandidate"/> on purpose: "nothing is worth building here" is an
        /// answer, "nothing could be evaluated" is a fault, and the two must never read alike.
        /// </summary>
        EvaluationFailed,
    }

    /// <summary>
    /// Stage 9 result: the optimised buildable device for one aperture, with enough of the run
    /// retained to review it, reproduce it, and understand WHY it won rather than merely what won.
    ///
    /// The raw objective components are kept alongside the scalar score, because the score alone
    /// cannot distinguish a device that blocks a lot of unwanted sun from one that simply uses no
    /// material. Every energy is kWh; every fraction is a ratio, NaN when its denominator is zero.
    ///
    /// What is deliberately NOT stored: the potential field, the visibility cache, the attribution
    /// cache and the geometry. Those are large, they already exist, and duplicating them here would
    /// make a result object heavier than the analysis it describes. What IS stored is their
    /// IDENTITY — the geometry and attribution-table hashes, the grid size, the sun-angle step, the
    /// year and the time shift — which is what a reader needs to check that a result belongs to the
    /// model in front of them.
    /// </summary>
    public class OptimisedShadingResult : IJSAMObject, ISolarObject
    {
        public const int CurrentSchemaVersion = 1;

        private int schemaVersion = CurrentSchemaVersion;
        private Guid apertureGuid;
        private string typologyName;
        private List<string> parameterNames = new List<string>();
        private Dictionary<string, double> parameters = new Dictionary<string, double>();
        private Dictionary<string, double> seedParameters = new Dictionary<string, double>();
        private List<ShadingParameter> bounds = new List<ShadingParameter>();
        private ShadingObjective objective;

        private double objectiveScore = double.NaN;
        private double benefit = double.NaN;
        private double harm = double.NaN;
        private double cost = double.NaN;
        private double seedObjectiveScore = double.NaN;

        private double admittedDirectEnergy = double.NaN;
        private double admittedUnwantedEnergy = double.NaN;
        private double admittedWantedEnergy = double.NaN;
        private double directSolarIntercepted = double.NaN;
        private double unwantedSolarIntercepted = double.NaN;
        private double wantedSolarBlocked = double.NaN;
        private double unattributedInterceptedEnergy = double.NaN;
        private double materialFraction = double.NaN;

        private List<Guid> elementGuids = new List<Guid>();
        private int evaluations;
        private int iterations;
        private int coarseStartsAvailable;
        private int coarseStartsRefined;
        private double elapsedMilliseconds = double.NaN;
        private double geometryMilliseconds = double.NaN;
        private double evaluationMilliseconds = double.NaN;
        private ShadingOptimisationTermination termination = ShadingOptimisationTermination.Undefined;
        private bool recommendsNoShading;

        private string desirabilityStrategyName;
        private double gridSize = double.NaN;
        private double sunAngleStep = double.NaN;
        private double timeShiftInMinutes;
        private int year;
        private string contextGeometryHash;
        private string targetGeometryHash;
        private string attributionTableHash;

        public OptimisedShadingResult()
        {
        }

        public OptimisedShadingResult(OptimisedShadingResult optimisedShadingResult)
        {
            if (optimisedShadingResult == null)
            {
                return;
            }

            schemaVersion = optimisedShadingResult.schemaVersion;
            apertureGuid = optimisedShadingResult.apertureGuid;
            typologyName = optimisedShadingResult.typologyName;
            parameterNames = new List<string>(optimisedShadingResult.parameterNames);
            parameters = new Dictionary<string, double>(optimisedShadingResult.parameters);
            seedParameters = new Dictionary<string, double>(optimisedShadingResult.seedParameters);
            bounds = optimisedShadingResult.bounds.ConvertAll(x => new ShadingParameter(x));
            objective = optimisedShadingResult.objective == null ? null : new ShadingObjective(optimisedShadingResult.objective);

            objectiveScore = optimisedShadingResult.objectiveScore;
            benefit = optimisedShadingResult.benefit;
            harm = optimisedShadingResult.harm;
            cost = optimisedShadingResult.cost;
            seedObjectiveScore = optimisedShadingResult.seedObjectiveScore;

            admittedDirectEnergy = optimisedShadingResult.admittedDirectEnergy;
            admittedUnwantedEnergy = optimisedShadingResult.admittedUnwantedEnergy;
            admittedWantedEnergy = optimisedShadingResult.admittedWantedEnergy;
            directSolarIntercepted = optimisedShadingResult.directSolarIntercepted;
            unwantedSolarIntercepted = optimisedShadingResult.unwantedSolarIntercepted;
            wantedSolarBlocked = optimisedShadingResult.wantedSolarBlocked;
            unattributedInterceptedEnergy = optimisedShadingResult.unattributedInterceptedEnergy;
            materialFraction = optimisedShadingResult.materialFraction;

            elementGuids = new List<Guid>(optimisedShadingResult.elementGuids);
            evaluations = optimisedShadingResult.evaluations;
            iterations = optimisedShadingResult.iterations;
            coarseStartsAvailable = optimisedShadingResult.coarseStartsAvailable;
            coarseStartsRefined = optimisedShadingResult.coarseStartsRefined;
            elapsedMilliseconds = optimisedShadingResult.elapsedMilliseconds;
            geometryMilliseconds = optimisedShadingResult.geometryMilliseconds;
            evaluationMilliseconds = optimisedShadingResult.evaluationMilliseconds;
            termination = optimisedShadingResult.termination;
            recommendsNoShading = optimisedShadingResult.recommendsNoShading;

            desirabilityStrategyName = optimisedShadingResult.desirabilityStrategyName;
            gridSize = optimisedShadingResult.gridSize;
            sunAngleStep = optimisedShadingResult.sunAngleStep;
            timeShiftInMinutes = optimisedShadingResult.timeShiftInMinutes;
            year = optimisedShadingResult.year;
            contextGeometryHash = optimisedShadingResult.contextGeometryHash;
            targetGeometryHash = optimisedShadingResult.targetGeometryHash;
            attributionTableHash = optimisedShadingResult.attributionTableHash;
        }

        public OptimisedShadingResult(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        // ---------------------------------------------------------------------- what won ----

        public int SchemaVersion { get { return schemaVersion; } }

        public Guid ApertureGuid { get { return apertureGuid; } internal set { apertureGuid = value; } }

        public string TypologyName { get { return typologyName; } internal set { typologyName = value; } }

        /// <summary>Winning parameter names in the typology's own fixed order.</summary>
        public List<string> ParameterNames { get { return new List<string>(parameterNames); } }

        public double GetParameter(string name) { return parameters.TryGetValue(name, out double value) ? value : double.NaN; }

        /// <summary>The seed the search started from, for judging how far it actually moved.</summary>
        public double GetSeedParameter(string name) { return seedParameters.TryGetValue(name, out double value) ? value : double.NaN; }

        /// <summary>The bounds and granularity the search was allowed, in parameter order.</summary>
        public List<ShadingParameter> Bounds { get { return bounds.ConvertAll(x => new ShadingParameter(x)); } }

        /// <summary>The objective the score was computed with. Without it the score is uninterpretable.</summary>
        public ShadingObjective Objective { get { return objective == null ? null : new ShadingObjective(objective); } internal set { objective = value == null ? null : new ShadingObjective(value); } }

        // ------------------------------------------------------------------ why it won ----

        /// <summary>Benefit - lambda x Harm - mu x Cost, kWh. Zero is exactly "build nothing".</summary>
        public double ObjectiveScore { get { return objectiveScore; } }

        /// <summary>Benefit component: unwanted solar intercepted, kWh.</summary>
        public double Benefit { get { return benefit; } }

        /// <summary>Harm component: wanted solar blocked, kWh (a positive loss).</summary>
        public double Harm { get { return harm; } }

        /// <summary>Cost component: MaterialFraction x reference energy, kWh.</summary>
        public double Cost { get { return cost; } }

        /// <summary>Score of the starting seed, so the improvement the search bought is visible.</summary>
        public double SeedObjectiveScore { get { return seedObjectiveScore; } }

        /// <summary>Improvement over the seed, kWh.</summary>
        public double ObjectiveImprovement { get { return objectiveScore - seedObjectiveScore; } }

        // ------------------------------------------------------------- physical metrics ----

        public double AdmittedDirectEnergy { get { return admittedDirectEnergy; } }

        public double AdmittedUnwantedEnergy { get { return admittedUnwantedEnergy; } }

        public double AdmittedWantedEnergy { get { return admittedWantedEnergy; } }

        /// <summary>Direct Solar Intercepted [kWh].</summary>
        public double DirectSolarIntercepted { get { return directSolarIntercepted; } }

        public double UnwantedSolarIntercepted { get { return unwantedSolarIntercepted; } }

        public double WantedSolarBlocked { get { return wantedSolarBlocked; } }

        /// <summary>
        /// Admitted direct beam the brief claimed neither way, kWh. See
        /// <see cref="ShadingPerformance.AdmittedNeutralEnergy"/> for the accounting identity and
        /// its general treatment.
        /// </summary>
        public double AdmittedNeutralEnergy { get { return admittedDirectEnergy - admittedUnwantedEnergy - admittedWantedEnergy; } }

        /// <summary>
        /// Neutral part of what the winning device stopped, kWh. See
        /// <see cref="ShadingPerformance.NeutralSolarIntercepted"/>.
        /// </summary>
        public double NeutralSolarIntercepted { get { return directSolarIntercepted - unwantedSolarIntercepted - wantedSolarBlocked; } }

        /// <summary>First-hit residual. Non-zero means attribution and the baseline disagree.</summary>
        public double UnattributedInterceptedEnergy { get { return unattributedInterceptedEnergy; } }

        public double MaterialFraction { get { return materialFraction; } }

        /// <summary>Direct Shading Efficiency [%] as a ratio. NaN when nothing is admitted.</summary>
        public double DirectShadingEfficiency { get { return Ratio(directSolarIntercepted, admittedDirectEnergy); } }

        /// <summary>Unwanted Solar Blocked [%] as a ratio. NaN when there is no unwanted solar.</summary>
        public double UnwantedSolarBlocked { get { return Ratio(unwantedSolarIntercepted, admittedUnwantedEnergy); } }

        /// <summary>Wanted Solar Retained [%] as a ratio. NaN when there is no wanted solar.</summary>
        public double WantedSolarRetained { get { return Ratio(admittedWantedEnergy - wantedSolarBlocked, admittedWantedEnergy); } }

        private static double Ratio(double numerator, double denominator)
        {
            return double.IsNaN(denominator) || denominator <= 0 ? double.NaN : numerator / denominator;
        }

        // ------------------------------------------------------------------- the run ----

        /// <summary>Guids of the winning device's elements, in build order.</summary>
        public List<Guid> ElementGuids { get { return new List<Guid>(elementGuids); } }

        /// <summary>Distinct candidate geometries actually evaluated (cache hits excluded).</summary>
        public int Evaluations { get { return evaluations; } }

        /// <summary>Pattern-search iterations completed.</summary>
        public int Iterations { get { return iterations; } }

        /// <summary>
        /// Distinct coarse points the ranking offered as refinement starts. The default search takes
        /// as many as the evaluation budget allows rather than a fixed number, so this and
        /// <see cref="CoarseStartsRefined"/> together say whether it ran out of basins or out of budget.
        /// </summary>
        public int CoarseStartsAvailable { get { return coarseStartsAvailable; } }

        /// <summary>Distinct coarse points actually refined, best-first.</summary>
        public int CoarseStartsRefined { get { return coarseStartsRefined; } }

        public double ElapsedMilliseconds { get { return elapsedMilliseconds; } }

        /// <summary>Time spent building candidate geometry, ms.</summary>
        public double GeometryMilliseconds { get { return geometryMilliseconds; } }

        /// <summary>Time spent on ray casting, attribution and energy accounting, ms.</summary>
        public double EvaluationMilliseconds { get { return evaluationMilliseconds; } }

        /// <summary>Everything that was neither geometry nor evaluation: the search itself, ms.</summary>
        public double OptimiserOverheadMilliseconds { get { return elapsedMilliseconds - geometryMilliseconds - evaluationMilliseconds; } }

        public ShadingOptimisationTermination Termination { get { return termination; } }

        /// <summary>
        /// True when no candidate beat the null device, i.e. the honest answer is that shading this
        /// aperture is not worth building. The parameters still describe the least-bad candidate so
        /// the recommendation can be inspected rather than taken on trust.
        /// </summary>
        public bool RecommendsNoShading { get { return recommendsNoShading; } }

        // --------------------------------------------------------------- provenance ----

        public string DesirabilityStrategyName { get { return desirabilityStrategyName; } }

        public double GridSize { get { return gridSize; } }

        public double SunAngleStep { get { return sunAngleStep; } }

        public double TimeShiftInMinutes { get { return timeShiftInMinutes; } }

        public int Year { get { return year; } }

        public string ContextGeometryHash { get { return contextGeometryHash; } }

        public string TargetGeometryHash { get { return targetGeometryHash; } }

        /// <summary>Attribution table hash of the WINNING candidate: pins the geometry the numbers came from.</summary>
        public string AttributionTableHash { get { return attributionTableHash; } }

        // ------------------------------------------------------------------ build ----

        internal void SetParameters(List<string> names, Dictionary<string, double> values, Dictionary<string, double> seeds, List<ShadingParameter> parameterBounds)
        {
            parameterNames = names == null ? new List<string>() : new List<string>(names);
            parameters = values == null ? new Dictionary<string, double>() : new Dictionary<string, double>(values);
            seedParameters = seeds == null ? new Dictionary<string, double>() : new Dictionary<string, double>(seeds);
            bounds = parameterBounds == null ? new List<ShadingParameter>() : parameterBounds.ConvertAll(x => new ShadingParameter(x));
        }

        internal void SetPerformance(ShadingPerformance performance, ShadingObjective shadingObjective)
        {
            if (performance == null || shadingObjective == null)
            {
                return;
            }

            admittedDirectEnergy = performance.AdmittedDirectEnergy;
            admittedUnwantedEnergy = performance.AdmittedUnwantedEnergy;
            admittedWantedEnergy = performance.AdmittedWantedEnergy;
            directSolarIntercepted = performance.DirectSolarIntercepted;
            unwantedSolarIntercepted = performance.UnwantedSolarIntercepted;
            wantedSolarBlocked = performance.WantedSolarBlocked;
            unattributedInterceptedEnergy = performance.UnattributedInterceptedEnergy;
            materialFraction = performance.MaterialFraction;

            benefit = shadingObjective.Benefit(performance);
            harm = shadingObjective.Harm(performance);
            cost = shadingObjective.Cost(performance);
            objectiveScore = shadingObjective.Score(performance);
        }

        internal void SetRun(int evaluationCount, int iterationCount, double milliseconds, ShadingOptimisationTermination terminationReason, bool noShading, double seedScore, int startsAvailable, int startsRefined)
        {
            evaluations = evaluationCount;
            iterations = iterationCount;
            coarseStartsAvailable = startsAvailable;
            coarseStartsRefined = startsRefined;
            elapsedMilliseconds = milliseconds;
            termination = terminationReason;
            recommendsNoShading = noShading;
            seedObjectiveScore = seedScore;
        }

        internal void SetTiming(double geometry, double evaluation)
        {
            geometryMilliseconds = geometry;
            evaluationMilliseconds = evaluation;
        }

        internal void SetProvenance(Guid aperture, string strategyName, double grid, double angleStep, double shiftInMinutes, int analysisYear, string contextHash, string targetHash, string tableHash, IEnumerable<Guid> guids)
        {
            apertureGuid = aperture;
            desirabilityStrategyName = strategyName;
            gridSize = grid;
            sunAngleStep = angleStep;
            timeShiftInMinutes = shiftInMinutes;
            year = analysisYear;
            contextGeometryHash = contextHash;
            targetGeometryHash = targetHash;
            attributionTableHash = tableHash;
            elementGuids = guids == null ? new List<Guid>() : new List<Guid>(guids);
        }

        /// <summary>Rebuilds the winning device. Geometry is regenerated, never stored.</summary>
        public IShadingTypology Typology()
        {
            IShadingTypology result = Create.ShadingTypology(typologyName);
            if (result == null)
            {
                return null;
            }

            foreach (string name in parameterNames)
            {
                result.SetParameter(name, GetParameter(name));
            }

            return result;
        }

        // ------------------------------------------------------------------- JSON ----

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("SchemaVersion")) { schemaVersion = jObject["SchemaVersion"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("ApertureGuid")) { Guid.TryParse(jObject["ApertureGuid"]?.GetValue<string>(), out apertureGuid); }
            if (jObject.ContainsKey("TypologyName")) { typologyName = jObject["TypologyName"]?.GetValue<string>(); }

            parameterNames = new List<string>();
            parameters = new Dictionary<string, double>();
            seedParameters = new Dictionary<string, double>();
            if (jObject.ContainsKey("Parameters") && jObject["Parameters"] is JsonArray parameterArray)
            {
                foreach (JsonNode node in parameterArray)
                {
                    JsonObject entry = node as JsonObject;
                    string name = entry?["Name"]?.GetValue<string>();
                    if (name == null)
                    {
                        continue;
                    }

                    parameterNames.Add(name);
                    parameters[name] = entry["Value"]?.GetValue<double>() ?? double.NaN;
                    if (entry.ContainsKey("Seed")) { seedParameters[name] = entry["Seed"]?.GetValue<double>() ?? double.NaN; }
                }
            }

            bounds = new List<ShadingParameter>();
            if (jObject.ContainsKey("Bounds") && jObject["Bounds"] is JsonArray boundsArray)
            {
                foreach (JsonNode node in boundsArray)
                {
                    if (node is JsonObject entry)
                    {
                        bounds.Add(new ShadingParameter(entry));
                    }
                }
            }

            objective = jObject.ContainsKey("Objective") ? new ShadingObjective(jObject["Objective"] as JsonObject) : null;

            objectiveScore = Read(jObject, "ObjectiveScore");
            benefit = Read(jObject, "Benefit");
            harm = Read(jObject, "Harm");
            cost = Read(jObject, "Cost");
            seedObjectiveScore = Read(jObject, "SeedObjectiveScore");

            admittedDirectEnergy = Read(jObject, "AdmittedDirectEnergy");
            admittedUnwantedEnergy = Read(jObject, "AdmittedUnwantedEnergy");
            admittedWantedEnergy = Read(jObject, "AdmittedWantedEnergy");
            directSolarIntercepted = Read(jObject, "DirectSolarIntercepted");
            unwantedSolarIntercepted = Read(jObject, "UnwantedSolarIntercepted");
            wantedSolarBlocked = Read(jObject, "WantedSolarBlocked");
            unattributedInterceptedEnergy = Read(jObject, "UnattributedInterceptedEnergy");
            materialFraction = Read(jObject, "MaterialFraction");

            elementGuids = new List<Guid>();
            if (jObject.ContainsKey("ElementGuids") && jObject["ElementGuids"] is JsonArray guidArray)
            {
                foreach (JsonNode node in guidArray)
                {
                    if (Guid.TryParse(node?.GetValue<string>(), out Guid guid))
                    {
                        elementGuids.Add(guid);
                    }
                }
            }

            if (jObject.ContainsKey("Evaluations")) { evaluations = jObject["Evaluations"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("Iterations")) { iterations = jObject["Iterations"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("CoarseStartsAvailable")) { coarseStartsAvailable = jObject["CoarseStartsAvailable"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("CoarseStartsRefined")) { coarseStartsRefined = jObject["CoarseStartsRefined"]?.GetValue<int>() ?? default; }
            elapsedMilliseconds = Read(jObject, "ElapsedMilliseconds");
            geometryMilliseconds = Read(jObject, "GeometryMilliseconds");
            evaluationMilliseconds = Read(jObject, "EvaluationMilliseconds");
            if (jObject.ContainsKey("Termination")) { Enum.TryParse(jObject["Termination"]?.GetValue<string>(), out termination); }
            if (jObject.ContainsKey("RecommendsNoShading")) { recommendsNoShading = jObject["RecommendsNoShading"]?.GetValue<bool>() ?? false; }

            if (jObject.ContainsKey("DesirabilityStrategyName")) { desirabilityStrategyName = jObject["DesirabilityStrategyName"]?.GetValue<string>(); }
            gridSize = Read(jObject, "GridSize");
            sunAngleStep = Read(jObject, "SunAngleStep");
            timeShiftInMinutes = Read(jObject, "TimeShiftInMinutes");
            if (jObject.ContainsKey("Year")) { year = jObject["Year"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("ContextGeometryHash")) { contextGeometryHash = jObject["ContextGeometryHash"]?.GetValue<string>(); }
            if (jObject.ContainsKey("TargetGeometryHash")) { targetGeometryHash = jObject["TargetGeometryHash"]?.GetValue<string>(); }
            if (jObject.ContainsKey("AttributionTableHash")) { attributionTableHash = jObject["AttributionTableHash"]?.GetValue<string>(); }

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
            jObject.Add("SchemaVersion", schemaVersion);
            jObject.Add("ApertureGuid", apertureGuid.ToString());
            if (typologyName != null) { jObject.Add("TypologyName", typologyName); }

            // Parameter order is the typology's own, never a dictionary's enumeration order, so the
            // serialised form is byte-identical between runs.
            JsonArray parameterArray = new JsonArray();
            foreach (string name in parameterNames)
            {
                JsonObject entry = new JsonObject();
                entry.Add("Name", name);
                entry.Add("Value", GetParameter(name));
                if (seedParameters.ContainsKey(name)) { entry.Add("Seed", seedParameters[name]); }
                parameterArray.Add(entry);
            }
            jObject.Add("Parameters", parameterArray);

            JsonArray boundsArray = new JsonArray();
            foreach (ShadingParameter parameter in bounds)
            {
                boundsArray.Add(parameter.ToJsonObject());
            }
            jObject.Add("Bounds", boundsArray);

            if (objective != null) { jObject.Add("Objective", objective.ToJsonObject()); }

            jObject.Add("ObjectiveScore", objectiveScore);
            jObject.Add("Benefit", benefit);
            jObject.Add("Harm", harm);
            jObject.Add("Cost", cost);
            jObject.Add("SeedObjectiveScore", seedObjectiveScore);

            jObject.Add("AdmittedDirectEnergy", admittedDirectEnergy);
            jObject.Add("AdmittedUnwantedEnergy", admittedUnwantedEnergy);
            jObject.Add("AdmittedWantedEnergy", admittedWantedEnergy);
            jObject.Add("DirectSolarIntercepted", directSolarIntercepted);
            jObject.Add("UnwantedSolarIntercepted", unwantedSolarIntercepted);
            jObject.Add("WantedSolarBlocked", wantedSolarBlocked);
            jObject.Add("UnattributedInterceptedEnergy", unattributedInterceptedEnergy);
            jObject.Add("MaterialFraction", materialFraction);

            JsonArray guidArray = new JsonArray();
            foreach (Guid guid in elementGuids)
            {
                guidArray.Add(guid.ToString());
            }
            jObject.Add("ElementGuids", guidArray);

            jObject.Add("Evaluations", evaluations);
            jObject.Add("Iterations", iterations);
            jObject.Add("CoarseStartsAvailable", coarseStartsAvailable);
            jObject.Add("CoarseStartsRefined", coarseStartsRefined);
            jObject.Add("ElapsedMilliseconds", elapsedMilliseconds);
            jObject.Add("GeometryMilliseconds", geometryMilliseconds);
            jObject.Add("EvaluationMilliseconds", evaluationMilliseconds);
            jObject.Add("Termination", termination.ToString());
            jObject.Add("RecommendsNoShading", recommendsNoShading);

            if (desirabilityStrategyName != null) { jObject.Add("DesirabilityStrategyName", desirabilityStrategyName); }
            jObject.Add("GridSize", gridSize);
            jObject.Add("SunAngleStep", sunAngleStep);
            jObject.Add("TimeShiftInMinutes", timeShiftInMinutes);
            jObject.Add("Year", year);
            if (contextGeometryHash != null) { jObject.Add("ContextGeometryHash", contextGeometryHash); }
            if (targetGeometryHash != null) { jObject.Add("TargetGeometryHash", targetGeometryHash); }
            if (attributionTableHash != null) { jObject.Add("AttributionTableHash", attributionTableHash); }

            return jObject;
        }
    }
}

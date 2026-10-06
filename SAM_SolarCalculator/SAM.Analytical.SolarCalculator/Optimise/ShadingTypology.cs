// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using SAM.Geometry.Object.Spatial;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// Stage 9: search a buildable shading family's parameter space against the Stage 9 objective,
    /// using the Stage 5 desirability and the Stage 8 first-hit energy accounting as the measure.
    ///
    /// This is optimisation against PHYSICS, not against shape. No candidate is scored on how much
    /// it resembles the Stage 7 ideal mesh; every candidate is built, ray-traced through the same
    /// first-hit engine as any other device, and judged on kWh. A design that looks like the ideal
    /// and performs worse loses.
    /// </summary>
    public static partial class Optimise
    {
        /// <summary>
        /// Scores that differ by less than this RELATIVE amount are treated as equal, so the
        /// tie-break below decides rather than the last bit of a floating-point sum. Comparisons
        /// stay deterministic because the tolerance is a pure function of the two values.
        /// </summary>
        private const double ScoreTolerance = 1e-12;

        /// <summary>
        /// The default start count: refine from AS MANY distinct coarse points as the evaluation
        /// budget allows, best-first, rather than from a fixed number of them.
        ///
        /// Gate 7 measured single-start refinement at a mean 16.29 % and worst 41.28 % below an
        /// enumeration on a lattice five times coarser than the search's own, while spending only
        /// 38-75 of its 400 permitted evaluations. The failure was basin-lock, not arithmetic:
        /// families whose response is near-unimodal BEAT the coarse enumeration, while those with
        /// more interacting parameters, and every family at a high wanted-solar penalty, did not.
        ///
        /// Refining several basins spends the idle budget on the actual weakness. Memoisation makes
        /// later starts much cheaper than the first, since they re-walk points already evaluated.
        ///
        /// A FIXED count of five was the first form of that fix, and it left most of the budget
        /// unspent — 67 to 161 evaluations of 400. Worse, the cut-off is arbitrary in a way the
        /// measurement exposed: on the Stage 11 fixture the best EggCrate coarse point carrying the
        /// count the enumeration wanted ranked SIXTH, one place outside the set. Raising five to six
        /// would fit the constant to that fixture. The honest bound is the one the caller already
        /// states — the evaluation budget — so the search keeps taking the next ranked start until
        /// the budget is gone or the ranking is exhausted, and no count needs choosing at all.
        /// </summary>
        private const int BudgetBoundedRefinementStarts = 0;

        /// <summary>
        /// Optimises one family against one aperture.
        ///
        /// SEARCH. A deterministic two-phase derivative-free search, chosen over a population
        /// method because it is inspectable, needs no random seed, and the objective here is cheap
        /// enough per evaluation but not free (a full attribution rebuild per candidate — see
        /// AttributionCacheScaleTests) that a few dozen evaluations is the right budget:
        ///
        ///   1. COARSE LATTICE. Every free parameter is sampled at a fixed number of levels across
        ///      its bounds, in ascending parameter order. This is what stops the search from
        ///      committing to the basin the seed happens to sit in — the depth response of a real
        ///      device is not unimodal once counts and tilts are in play.
        ///   2. COMPASS REFINEMENT. From the best coarse point, each parameter is probed at plus
        ///      and minus the current step in fixed order; when no probe improves, every step is
        ///      halved — but never below that parameter's own granularity, which is a real physical
        ///      limit (a 10 mm depth change, one whole louvre) rather than an arbitrary epsilon. It
        ///      terminates when every free axis is already probing one granularity either way and
        ///      none of them improves, so the answer is the best point on its own lattice along
        ///      every axis. The floor is not a refinement of the stopping rule but a correctness
        ///      condition: a step finer than the lattice snaps back onto the incumbent, so without
        ///      it an axis whose bounds are narrow relative to its granularity is never probed at
        ///      all. See ShadingParameter.MinimumIncrement.
        ///
        /// The seed INFORMS but does not decide: it is one lattice point among many, and it wins
        /// only if it scores best. Stage 7 can supply it (a depth read off the field's own zero
        /// crossing), which usually saves iterations and never changes the answer's basis.
        ///
        /// DETERMINISM. Parameters are visited in the typology's fixed order; every proposal is
        /// snapped onto its parameter's step lattice; evaluated points are memoised by an exact
        /// string key so a revisit cannot produce a different answer; ties are broken by a total
        /// order (score, then lower material, then lexicographically smaller parameters). There is
        /// no randomness and no parallel reduction at this level — the parallelism lives inside the
        /// attribution build, where each sun group writes its own row.
        ///
        /// THE NULL DEVICE. The score of building nothing is exactly zero. If no candidate beats
        /// it, the result says so through RecommendsNoShading rather than returning the least-bad
        /// geometry as though it were a recommendation.
        /// </summary>
        /// <param name="target">The aperture.</param>
        /// <param name="baseVisibilityCache">Visibility with CONTEXT ONLY. Defines what a candidate may be credited for, and is reused for every candidate.</param>
        /// <param name="desirability">Stage 5 per-group energies for this aperture.</param>
        /// <param name="contextOccluders">Existing context. Present in every candidate's occluder set.</param>
        /// <param name="typologyName">Family to optimise.</param>
        /// <param name="objective">The objective. Null for the Stage 9 default.</param>
        /// <param name="parameters">Search variables. Null for the family defaults.</param>
        /// <param name="seed">Starting device. Null for the family default.</param>
        /// <param name="maximumEvaluations">Hard budget on distinct candidate evaluations.</param>
        /// <param name="coarseLevels">Samples per free parameter in the coarse phase.</param>
        /// <param name="cellIndexOffset">This target's first cell index within baseVisibilityCache when the cache spans the whole model.</param>
        public static OptimisedShadingResult ShadingTypology(
            this ApertureSolarTarget target,
            SolarVisibilityCache baseVisibilityCache,
            ApertureDesirability desirability,
            List<LinkedFace3D> contextOccluders,
            string typologyName,
            ShadingObjective objective = null,
            List<ShadingParameter> parameters = null,
            IShadingTypology seed = null,
            int maximumEvaluations = 400,
            int coarseLevels = 3,
            int cellIndexOffset = 0)
        {
            return ShadingTypology(target, baseVisibilityCache, desirability, contextOccluders, typologyName,
                objective, parameters, seed, maximumEvaluations, coarseLevels, cellIndexOffset, BudgetBoundedRefinementStarts);
        }

        /// <summary>
        /// As the overload above, with explicit control over how many distinct coarse points the
        /// refinement starts from. The parameterless-tail overload is preserved unchanged so already
        /// compiled callers keep resolving — appending an optional parameter in place would be a
        /// binary break.
        /// </summary>
        /// <param name="refinementStarts">Distinct coarse points to refine from, best-first. 1 restores single-start behaviour. Zero or less takes as many as <paramref name="maximumEvaluations"/> allows.</param>
        public static OptimisedShadingResult ShadingTypology(
            this ApertureSolarTarget target,
            SolarVisibilityCache baseVisibilityCache,
            ApertureDesirability desirability,
            List<LinkedFace3D> contextOccluders,
            string typologyName,
            ShadingObjective objective,
            List<ShadingParameter> parameters,
            IShadingTypology seed,
            int maximumEvaluations,
            int coarseLevels,
            int cellIndexOffset,
            int refinementStarts)
        {
            return ShadingTypology(target, baseVisibilityCache, desirability, contextOccluders, typologyName,
                objective, parameters, seed, maximumEvaluations, coarseLevels, cellIndexOffset, refinementStarts, true);
        }

        /// <summary>
        /// As the overload above, with explicit control over ACTIVE-BIN PRUNING of the candidate
        /// attribution (see Create.ActiveDesirabilityBins). The default and only production path is
        /// <c>true</c>: candidate attribution is traced only at bins whose Benefit/Harm contribution
        /// can be non-zero, which cannot change any candidate score, comparison, or the selected
        /// design — every reported number of the winning device comes from one complete, unpruned
        /// attribution pass run after the winner is known.
        ///
        /// INTERNAL ON PURPOSE. The parameter exists only so the test assembly can compare the
        /// pruned search against the unpruned one; it is not part of the public SAM API and no
        /// consumer should ever need to disable the fast path, which is answer-preserving anyway.
        /// Passing <c>false</c> restores the unpruned per-candidate attribution for that
        /// equivalence test. The assembly grants its test project friend access for this one seam
        /// (see AssemblyInfo).
        /// </summary>
        /// <param name="pruneActiveBins">True (default via the other overloads) traces candidate attribution at active desirability bins only; false traces the complete timeline for every candidate.</param>
        internal static OptimisedShadingResult ShadingTypology(
            this ApertureSolarTarget target,
            SolarVisibilityCache baseVisibilityCache,
            ApertureDesirability desirability,
            List<LinkedFace3D> contextOccluders,
            string typologyName,
            ShadingObjective objective,
            List<ShadingParameter> parameters,
            IShadingTypology seed,
            int maximumEvaluations,
            int coarseLevels,
            int cellIndexOffset,
            int refinementStarts,
            bool pruneActiveBins)
        {
            if (target == null || baseVisibilityCache == null || desirability == null)
            {
                return null;
            }

            IShadingTypology prototype = Create.ShadingTypology(typologyName);
            if (prototype == null)
            {
                return null;
            }

            ShadingObjective objective_Local = objective ?? new ShadingObjective();

            // The default variable set is capped by the analysis resolution the visibility cache
            // was built at, so a candidate can never be credited for shading finer than the
            // analysis can see. See Create.ShadingParameters for why that is a correctness
            // constraint rather than a convenience.
            List<ShadingParameter> parameters_Local = parameters
                ?? Create.ShadingParameters(prototype, double.NaN, target, baseVisibilityCache.CellSize);
            if (parameters_Local == null || parameters_Local.Count == 0)
            {
                return null;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();

            // THE ACTIVE-BIN MASK: built ONCE for this aperture/desirability pair and reused for
            // every candidate. Null restores the unpruned full-timeline attribution (equivalence
            // testing); null is also the safe fallback when the desirability cannot produce a mask.
            bool[] activeBins = pruneActiveBins ? Create.ActiveDesirabilityBins(desirability, baseVisibilityCache.BinCount) : null;

            Evaluator evaluator = new Evaluator(target, baseVisibilityCache, desirability, contextOccluders, typologyName, objective_Local, parameters_Local, cellIndexOffset, activeBins);

            // --- the starting vector: the caller's seed where given, the family default otherwise.
            double[] seedVector = new double[parameters_Local.Count];
            for (int i = 0; i < parameters_Local.Count; i++)
            {
                double value = seed == null ? prototype.GetParameter(parameters_Local[i].Name) : seed.GetParameter(parameters_Local[i].Name);
                if (double.IsNaN(value))
                {
                    value = prototype.GetParameter(parameters_Local[i].Name);
                }

                seedVector[i] = parameters_Local[i].Snap(value);
            }

            Candidate best = evaluator.Evaluate(seedVector);
            Candidate seedCandidate = best;

            List<int> free = new List<int>();
            for (int i = 0; i < parameters_Local.Count; i++)
            {
                if (!parameters_Local[i].IsFixed)
                {
                    free.Add(i);
                }
            }

            int iterations = 0;

            // How far down the coarse ranking the budget actually got. Reported rather than inferred,
            // because with a budget-bounded start count the two numbers are the whole story of
            // whether the search ran out of basins or ran out of money.
            int startsAvailable = 0;
            int startsRefined = 0;

            ShadingOptimisationTermination termination = ShadingOptimisationTermination.NothingToSearch;

            if (best != null && free.Count > 0)
            {
                // --- phase 1: coarse lattice. EVERY evaluated point is kept, not just the running
                //     incumbent, so phase 2 can start from several basins rather than only the one
                //     that happened to win. The incumbent is still tracked here so that a lattice
                //     that exhausts the budget outright still yields the best point it reached.
                List<Candidate> coarse = new List<Candidate>();
                foreach (double[] point in Lattice(parameters_Local, free, seedVector, Math.Max(2, coarseLevels)))
                {
                    if (evaluator.Evaluations >= maximumEvaluations)
                    {
                        break;
                    }

                    Candidate candidate = evaluator.Evaluate(point);
                    if (candidate != null && !double.IsNaN(candidate.Score))
                    {
                        coarse.Add(candidate);
                    }

                    if (IsBetter(candidate, best))
                    {
                        best = candidate;
                    }
                }

                // The seed is a legitimate basin too — Stage 7 picks it off the field's own zero
                // crossing — so it competes for a starting slot rather than being discarded once the
                // lattice has run.
                if (seedCandidate != null && !double.IsNaN(seedCandidate.Score))
                {
                    coarse.Add(seedCandidate);
                }

                // Rank by an EXACT total order, deliberately not by IsBetter. IsBetter compares
                // scores within a relative tolerance, which is right for picking a winner but is not
                // guaranteed transitive, and List.Sort throws on an inconsistent comparer. Ranking
                // only decides which points are worth refining, so exact comparison is both safe and
                // sufficient; IsBetter still decides the final winner.
                coarse.Sort(CompareForRanking);

                // Distinct starting points, best first, using the evaluator's own identity so two
                // points it would treat as one are never counted as two basins.
                //
                // A caller may still ask for a fixed number. The DEFAULT does not: it collects the
                // whole ranking and lets the evaluation budget decide how far down it gets, because
                // any fixed cut-off is a number chosen without reference to the problem — and the
                // ranking is a total order, so "as far as the budget allows" is just as deterministic
                // as "the best five".
                List<double[]> starts = new List<double[]>();
                HashSet<string> seenStarts = new HashSet<string>();
                foreach (Candidate candidate in coarse)
                {
                    if (refinementStarts > 0 && starts.Count >= refinementStarts)
                    {
                        break;
                    }

                    if (seenStarts.Add(Evaluator.Key(candidate.Parameters)))
                    {
                        starts.Add(candidate.Parameters);
                    }
                }

                startsAvailable = starts.Count;

                // The scale ladder each axis is polled over: its normal initial compass step — half
                // the coarse spacing, fine enough to resolve between lattice points and coarse
                // enough not to start at the floor — halved down to the smallest change the
                // parameter's own lattice can express.
                //
                // The floor is a correctness condition, not a stopping preference: a step below
                // MinimumIncrement snaps straight back onto the incumbent, so the probe rebuilds
                // identical geometry and reports no improvement. Create.ShadingParameters narrows
                // element counts to what the analysis grid can resolve, and a count capped to [1, 3]
                // has a range of 2 — so a fraction of its range is smaller than the single whole
                // element it is measured in, and without the floor that axis is never probed at all.
                //
                // The ladder depends only on the parameter, so it is built once and reused by every
                // start and every poll.
                List<double>[] ladders = new List<double>[parameters_Local.Count];
                foreach (int i in free)
                {
                    List<double> ladder = new List<double>();
                    double scale = Math.Max(
                        parameters_Local[i].MinimumIncrement,
                        0.5 * parameters_Local[i].Range / Math.Max(1, coarseLevels - 1));

                    while (true)
                    {
                        ladder.Add(scale);

                        double next = Math.Max(parameters_Local[i].MinimumIncrement, 0.5 * scale);
                        if (!(next < scale))
                        {
                            break;
                        }

                        scale = next;
                    }

                    ladders[i] = ladder;
                }

                // --- phase 2: compass refinement, run independently from each starting point.
                //     Each start keeps its OWN incumbent so a descent cannot be dragged into the
                //     basin of a better start; the global winner is taken at the end under the same
                //     total order as before. Memoisation means overlapping descents cost nothing
                //     twice, which is what keeps several starts inside one evaluation budget.
                termination = ShadingOptimisationTermination.StepBelowGranularity;

                foreach (double[] startPoint in starts)
                {
                    if (evaluator.Evaluations >= maximumEvaluations)
                    {
                        termination = ShadingOptimisationTermination.EvaluationBudgetExhausted;
                        break;
                    }

                    startsRefined++;

                    Candidate local = evaluator.Evaluate(startPoint);
                    if (local == null || double.IsNaN(local.Score))
                    {
                        continue;
                    }

                    while (true)
                    {
                        if (evaluator.Evaluations >= maximumEvaluations)
                        {
                            termination = ShadingOptimisationTermination.EvaluationBudgetExhausted;
                            break;
                        }

                        iterations++;

                        // --- BALANCED MULTISCALE POLL.
                        //
                        //     The incumbent is held FIXED while every axis is examined. Each axis is
                        //     probed both ways at every distinct scale on its own ladder, from its
                        //     normal initial compass step down to its MinimumIncrement, and the best
                        //     improving candidate that axis can offer is recorded. Only when all of
                        //     them have reported does the incumbent move, once, to the best offer
                        //     under the same IsBetter total order that decides everything else.
                        //
                        //     WHY, measured. Probing an axis at one scale and moving immediately
                        //     makes the answer depend on where the parameter sits in the typology's
                        //     declaration order: whichever axis is visited first while it still has a
                        //     productive coarse move gets to commit the descent, and the axis that
                        //     needed a finer scale is stranded. Resolving each axis fully before
                        //     moving on only changes WHICH axis is stranded — it repaired an egg
                        //     crate and broke a fin array on the same fixture. Polling every axis
                        //     over its whole ladder from a common incumbent removes the ordering
                        //     commitment entirely: declaration order then decides nothing except how
                        //     an exact tie is broken.
                        Candidate pollBest = null;

                        // Vectors already measured in THIS poll. Different scales on one axis often
                        // snap to the same lattice point, and the incumbent itself is never a move.
                        HashSet<string> polled = new HashSet<string>();
                        polled.Add(Evaluator.Key(local.Parameters));

                        foreach (int i in free)
                        {
                            Candidate axisBest = null;

                            foreach (double scale in ladders[i])
                            {
                                foreach (int sign in new int[] { 1, -1 })
                                {
                                    if (evaluator.Evaluations >= maximumEvaluations)
                                    {
                                        break;
                                    }

                                    double[] probe = (double[])local.Parameters.Clone();
                                    probe[i] = parameters_Local[i].Snap(probe[i] + sign * scale);
                                    if (probe[i] == local.Parameters[i])
                                    {
                                        continue; // snapped back onto the incumbent: not a move
                                    }

                                    if (!polled.Add(Evaluator.Key(probe)))
                                    {
                                        continue; // a coarser scale on this axis already landed here
                                    }

                                    Candidate candidate = evaluator.Evaluate(probe);
                                    if (IsBetter(candidate, local) && IsBetter(candidate, axisBest))
                                    {
                                        axisBest = candidate;
                                    }
                                }

                                // THE FIRST PRODUCTIVE SCALE DECIDES THIS AXIS. Descending PAST
                                // failing scales is the part that matters — it is what lets an axis
                                // whose useful move is far finer than its first step be seen at all,
                                // and it is what stopped a misleading coarse move on another axis
                                // from committing the descent. Continuing past a scale that already
                                // worked buys a marginally better move for a multiple of the cost:
                                // every extra scale is another pair of distinct evaluations, on
                                // every axis, on every poll.
                                //
                                // That cost is not academic. Polling whole ladders made a 30-point
                                // one-dimensional problem take 30 evaluations — the search stopped
                                // earning its place against brute force — and left a 60-evaluation
                                // budget with almost nothing after the coarse lattice. Stopping here
                                // keeps the ordering independence, which comes from judging every
                                // axis from the SAME incumbent and taking the global best, not from
                                // exhausting each ladder.
                                if (axisBest != null)
                                {
                                    break;
                                }
                            }

                            if (axisBest != null && IsBetter(axisBest, pollBest))
                            {
                                pollBest = axisBest;
                            }
                        }

                        if (pollBest == null)
                        {
                            // No axis can improve on this point anywhere on its ladder, so the
                            // descent has converged. Nothing about the order of the poll can change
                            // that verdict, which is the point of taking it this way.
                            if (evaluator.Evaluations >= maximumEvaluations)
                            {
                                termination = ShadingOptimisationTermination.EvaluationBudgetExhausted;
                            }

                            break;
                        }

                        // One move per poll. Every ladder restarts from the new incumbent, because
                        // the scale a parameter needs depends on where the descent now stands.
                        local = pollBest;
                    }

                    if (IsBetter(local, best))
                    {
                        best = local;
                    }
                }
            }

            stopwatch.Stop();

            if (best == null)
            {
                return null;
            }

            // The winner's reported performance comes from ONE complete, unpruned attribution pass
            // over the full timeline, run only now that the winner is known. A no-op on the
            // unpruned path and for unmeasured winners. Scores are unchanged by construction, so the
            // selection, the null-device verdict and every statistic below are already final.
            evaluator.VerifyFull(best);

            // --- the null device scores exactly zero: no benefit, no harm, no material.
            //
            // A candidate that could NOT BE MEASURED at all has a NaN score, and NaN > 0 is false —
            // so testing the score alone reports an unmeasurable aperture as "no shading is worth
            // building here", which is a confident engineering answer the run never earned. The two
            // are separated: a measured candidate that fails to beat zero recommends no shading; an
            // unmeasured one terminates as a failure and recommends nothing at all.
            bool measured = best.Performance != null;
            bool recommendsNoShading = measured && !(best.Score > 0);
            if (!measured)
            {
                termination = ShadingOptimisationTermination.EvaluationFailed;
            }
            else if (recommendsNoShading)
            {
                termination = ShadingOptimisationTermination.NoBeneficialCandidate;
            }

            return evaluator.Result(best, seedCandidate, iterations, stopwatch.Elapsed.TotalMilliseconds, termination, recommendsNoShading, startsAvailable, startsRefined);
        }

        /// <summary>
        /// An EXACT total order over evaluated candidates, used only to rank coarse points for
        /// refinement. Highest score first, then least material, then the lexicographically smaller
        /// parameter vector — the same priorities as <see cref="IsBetter"/> but without its relative
        /// score tolerance, which is not guaranteed transitive and would make List.Sort throw.
        /// Unmeasurable candidates (NaN score) sort last.
        /// </summary>
        private static int CompareForRanking(Candidate x, Candidate y)
        {
            if (ReferenceEquals(x, y)) { return 0; }
            if (x == null) { return y == null ? 0 : 1; }
            if (y == null) { return -1; }

            bool xNaN = double.IsNaN(x.Score);
            bool yNaN = double.IsNaN(y.Score);
            if (xNaN || yNaN)
            {
                return xNaN == yNaN ? 0 : (xNaN ? 1 : -1);
            }

            if (x.Score > y.Score) { return -1; }
            if (x.Score < y.Score) { return 1; }

            double xCost = double.IsNaN(x.MaterialFraction) ? double.PositiveInfinity : x.MaterialFraction;
            double yCost = double.IsNaN(y.MaterialFraction) ? double.PositiveInfinity : y.MaterialFraction;
            if (xCost < yCost) { return -1; }
            if (xCost > yCost) { return 1; }

            int length = Math.Min(x.Parameters.Length, y.Parameters.Length);
            for (int i = 0; i < length; i++)
            {
                if (x.Parameters[i] < y.Parameters[i]) { return -1; }
                if (x.Parameters[i] > y.Parameters[i]) { return 1; }
            }

            return x.Parameters.Length.CompareTo(y.Parameters.Length);
        }

        /// <summary>
        /// The coarse lattice: every free parameter at evenly spaced levels across its bounds,
        /// enumerated in a fixed odometer order so two runs visit the same points in the same
        /// sequence. Fixed parameters hold their seed value.
        /// </summary>
        private static IEnumerable<double[]> Lattice(List<ShadingParameter> parameters, List<int> free, double[] seedVector, int levels)
        {
            int[] counter = new int[free.Count];
            while (true)
            {
                double[] point = (double[])seedVector.Clone();
                for (int f = 0; f < free.Count; f++)
                {
                    int i = free[f];
                    double fraction = levels <= 1 ? 0.5 : (double)counter[f] / (levels - 1);
                    point[i] = parameters[i].Snap(parameters[i].Minimum + fraction * parameters[i].Range);
                }

                yield return point;

                int digit = free.Count - 1;
                while (digit >= 0)
                {
                    counter[digit]++;
                    if (counter[digit] < levels)
                    {
                        break;
                    }

                    counter[digit] = 0;
                    digit--;
                }

                if (digit < 0)
                {
                    yield break;
                }
            }
        }

        /// <summary>
        /// The total order that makes the winner unique. Strictly better score wins; on a tie
        /// within the score tolerance, less material wins; on a tie there too, the
        /// lexicographically smaller parameter vector wins. Without the last two rules two runs
        /// could return different devices with identical scores, which is exactly what a
        /// reproducibility requirement forbids.
        /// </summary>
        private static bool IsBetter(Candidate candidate, Candidate incumbent)
        {
            if (candidate == null || double.IsNaN(candidate.Score))
            {
                return false;
            }

            if (incumbent == null || double.IsNaN(incumbent.Score))
            {
                return true;
            }

            double tolerance = ScoreTolerance * Math.Max(1.0, Math.Max(Math.Abs(candidate.Score), Math.Abs(incumbent.Score)));
            if (candidate.Score > incumbent.Score + tolerance) { return true; }
            if (candidate.Score < incumbent.Score - tolerance) { return false; }

            double candidateCost = double.IsNaN(candidate.MaterialFraction) ? double.PositiveInfinity : candidate.MaterialFraction;
            double incumbentCost = double.IsNaN(incumbent.MaterialFraction) ? double.PositiveInfinity : incumbent.MaterialFraction;
            if (candidateCost < incumbentCost) { return true; }
            if (candidateCost > incumbentCost) { return false; }

            for (int i = 0; i < candidate.Parameters.Length && i < incumbent.Parameters.Length; i++)
            {
                if (candidate.Parameters[i] < incumbent.Parameters[i]) { return true; }
                if (candidate.Parameters[i] > incumbent.Parameters[i]) { return false; }
            }

            return false;
        }

        /// <summary>One evaluated point: its parameters, its score and everything needed to report it.</summary>
        private class Candidate
        {
            public double[] Parameters;
            public double Score = double.NaN;
            public double MaterialFraction = double.NaN;
            public ShadingPerformance Performance;
            public List<Guid> ElementGuids;
            public string AttributionTableHash;
        }

        /// <summary>
        /// Builds and measures candidates, memoising by snapped parameter vector so a revisited
        /// point costs nothing and — more importantly — can never return a different answer.
        /// </summary>
        private class Evaluator
        {
            private readonly ApertureSolarTarget target;
            private readonly SolarVisibilityCache baseVisibilityCache;
            private readonly ApertureDesirability desirability;
            private readonly List<LinkedFace3D> contextOccluders;
            private readonly string typologyName;
            private readonly ShadingObjective objective;
            private readonly List<ShadingParameter> parameters;
            private readonly int cellIndexOffset;
            private readonly bool[] activeBins;
            private readonly Dictionary<string, Candidate> cache = new Dictionary<string, Candidate>();

            private double geometryMilliseconds;
            private double evaluationMilliseconds;

            public Evaluator(ApertureSolarTarget target, SolarVisibilityCache baseVisibilityCache, ApertureDesirability desirability, List<LinkedFace3D> contextOccluders, string typologyName, ShadingObjective objective, List<ShadingParameter> parameters, int cellIndexOffset, bool[] activeBins)
            {
                this.cellIndexOffset = cellIndexOffset;
                this.target = target;
                this.baseVisibilityCache = baseVisibilityCache;
                this.desirability = desirability;
                this.contextOccluders = contextOccluders ?? new List<LinkedFace3D>();
                this.typologyName = typologyName;
                this.objective = objective;
                this.parameters = parameters;
                this.activeBins = activeBins;
            }

            /// <summary>Distinct geometries actually built and ray-traced (cache hits excluded).</summary>
            public int Evaluations { get { return cache.Count; } }

            public double GeometryMilliseconds { get { return geometryMilliseconds; } }

            public double EvaluationMilliseconds { get { return evaluationMilliseconds; } }

            public Candidate Evaluate(double[] values)
            {
                string key = Key(values);
                if (cache.TryGetValue(key, out Candidate cached))
                {
                    return cached;
                }

                Stopwatch stopwatch = Stopwatch.StartNew();

                IShadingTypology typology = Create.ShadingTypology(typologyName);
                for (int i = 0; i < parameters.Count; i++)
                {
                    typology.SetParameter(parameters[i].Name, values[i]);
                }

                List<ShadingElement> elements = typology.ShadingElements(target);
                List<Guid> guids = new List<Guid>();
                if (elements != null)
                {
                    foreach (ShadingElement element in elements)
                    {
                        if (element?.LinkedFace3D != null)
                        {
                            guids.Add(element.Guid);
                        }
                    }
                }

                stopwatch.Stop();
                geometryMilliseconds += stopwatch.Elapsed.TotalMilliseconds;

                Candidate result = new Candidate { Parameters = (double[])values.Clone(), ElementGuids = guids };

                if (elements == null || elements.Count == 0)
                {
                    cache[key] = result;
                    return result;
                }

                stopwatch = Stopwatch.StartNew();

                // Context is already in baseVisibilityCache and is not traced again per candidate —
                // see Create.ShadingPerformance for why that is exact, and what it costs not to.
                // The active-bin mask prunes ONLY the attribution at bins whose Benefit/Harm
                // contribution is provably zero; admitted accounting is never pruned.
                SolarAttributionCache attributionCache = Create.CandidateAttributionCache(baseVisibilityCache, elements, target.AnalysisCells, cellIndexOffset, activeBins);
                if (attributionCache != null)
                {
                    ShadingPerformance performance = Create.ShadingPerformance(
                        target, baseVisibilityCache, attributionCache, desirability, elements,
                        typologyName, typology.MaterialFraction(target), cellIndexOffset, activeBins);

                    if (performance != null)
                    {
                        result.Performance = performance;
                        result.MaterialFraction = performance.MaterialFraction;
                        result.Score = objective.Score(performance);
                        result.AttributionTableHash = attributionCache.AttributionTableHash;
                    }
                }

                stopwatch.Stop();
                evaluationMilliseconds += stopwatch.Elapsed.TotalMilliseconds;

                cache[key] = result;
                return result;
            }

            /// <summary>
            /// THE FINAL FULL VERIFICATION OF THE WINNER. When candidates were scored through a
            /// pruned attribution, the winning device is rebuilt from its winning parameters and
            /// measured ONCE through the complete, unpruned attribution path, and its provisional
            /// performance is replaced by the full result. Every reported quantity — intercepted
            /// totals, per-element credits, unattributed energy, the attribution-table hash — then
            /// comes from the complete pass, exactly as it did before pruning existed.
            ///
            /// The score cannot move: the pruned and full paths agree bit-for-bit on Benefit, Harm
            /// and the admitted sums (see the pruning notes in Create.ShadingPerformance), and the
            /// score is recomputed here from the same objective over the same numbers.
            ///
            /// THE MEMO IS NOT TOUCHED. The candidate object being replaced is already the instance
            /// stored under its parameter key, so no new entry is created and Evaluations is
            /// unchanged by the verification.
            /// </summary>
            public void VerifyFull(Candidate candidate)
            {
                if (candidate == null || candidate.Performance == null || activeBins == null)
                {
                    return; // unmeasured winner, or the unpruned path: nothing to verify
                }

                Stopwatch stopwatch = Stopwatch.StartNew();

                IShadingTypology typology = Create.ShadingTypology(typologyName);
                for (int i = 0; i < parameters.Count; i++)
                {
                    typology.SetParameter(parameters[i].Name, candidate.Parameters[i]);
                }

                List<ShadingElement> elements = typology.ShadingElements(target);

                stopwatch.Stop();
                geometryMilliseconds += stopwatch.Elapsed.TotalMilliseconds;

                if (elements == null || elements.Count == 0)
                {
                    return;
                }

                stopwatch = Stopwatch.StartNew();

                SolarAttributionCache attributionCache = Create.CandidateAttributionCache(baseVisibilityCache, elements, target.AnalysisCells, cellIndexOffset);
                if (attributionCache != null)
                {
                    ShadingPerformance performance = Create.ShadingPerformance(
                        target, baseVisibilityCache, attributionCache, desirability, elements,
                        typologyName, typology.MaterialFraction(target), cellIndexOffset);

                    if (performance != null)
                    {
                        candidate.Performance = performance;
                        candidate.MaterialFraction = performance.MaterialFraction;
                        candidate.Score = objective.Score(performance);
                        candidate.AttributionTableHash = attributionCache.AttributionTableHash;
                    }
                }

                stopwatch.Stop();
                evaluationMilliseconds += stopwatch.Elapsed.TotalMilliseconds;
            }

            /// <summary>
            /// Round-trip exact key. "R" formatting means two parameter vectors that are equal as
            /// doubles produce the same key and one that differs in the last bit does not, so the
            /// memo can never conflate two distinct geometries.
            /// </summary>
            /// <summary>
            /// The memo identity of a parameter vector. Static and shared so that de-duplication of
            /// refinement starting points uses EXACTLY the identity the cache uses — two starts that
            /// the evaluator would treat as the same point must not be counted as different basins.
            /// </summary>
            public static string Key(double[] values)
            {
                StringBuilder stringBuilder = new StringBuilder();
                for (int i = 0; i < values.Length; i++)
                {
                    if (i > 0) { stringBuilder.Append('|'); }
                    stringBuilder.Append(values[i].ToString("R", CultureInfo.InvariantCulture));
                }

                return stringBuilder.ToString();
            }

            public OptimisedShadingResult Result(Candidate best, Candidate seed, int iterations, double elapsedMilliseconds, ShadingOptimisationTermination termination, bool recommendsNoShading, int startsAvailable, int startsRefined)
            {
                OptimisedShadingResult result = new OptimisedShadingResult();
                result.TypologyName = typologyName;
                result.Objective = objective;

                List<string> names = new List<string>();
                Dictionary<string, double> values = new Dictionary<string, double>();
                Dictionary<string, double> seeds = new Dictionary<string, double>();
                for (int i = 0; i < parameters.Count; i++)
                {
                    names.Add(parameters[i].Name);
                    values[parameters[i].Name] = best.Parameters[i];
                    if (seed != null) { seeds[parameters[i].Name] = seed.Parameters[i]; }
                }

                result.SetParameters(names, values, seeds, parameters);
                result.SetPerformance(best.Performance, objective);
                result.SetRun(cache.Count, iterations, elapsedMilliseconds, termination, recommendsNoShading, seed == null ? double.NaN : seed.Score, startsAvailable, startsRefined);
                result.SetTiming(geometryMilliseconds, evaluationMilliseconds);
                result.SetProvenance(
                    target.ApertureGuid,
                    desirability.DesirabilityStrategyName,
                    baseVisibilityCache.CellSize,
                    baseVisibilityCache.BinSizeDegrees,
                    baseVisibilityCache.SunPositionShiftInMinutes,
                    baseVisibilityCache.Year,
                    baseVisibilityCache.ContextGeometryHash,
                    baseVisibilityCache.TargetGeometryHash,
                    best.AttributionTableHash,
                    best.ElementGuids);

                return result;
            }
        }
    }
}

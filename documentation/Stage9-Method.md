# Stage 9 implementation — optimisation of buildable shading geometry

Companion to `Stages0-4-Method.md` and `Stages5-8-Method.md`. Everything here is measured on the
committed tests; figures are quoted with the case that produces them.

The progression this stage completes:

```
Stage 6 scalar shading field  →  Stage 7 ideal geometry  →  Stage 8 rationalised typology
                                                         →  Stage 9 OPTIMISED buildable device
```

Stage 9 is not another shape generator. It searches a buildable family's parameter space against an
explicit energy objective, using the Stage 8 first-hit accounting as the measure. **No candidate is
scored on resemblance to the Stage 7 mesh.** A design that looks like the ideal and performs worse
loses.

---

## 1. Gate 0 — what the review of Stages 0–8 changed

Stage 9 was gated on an independent re-examination of five concerns. Two produced production fixes,
one produced a documented divergence, two were sound. Details are in `Stages5-8-Method.md` §2.5.1,
§4.7 and §4.8; the summary:

| review | verdict | evidence | change |
|---|---|---|---|
| A — ideal geometry representation | **architectural problem in the comparison** | plates understate interception by 28.2 % and overstate wanted retention by 21.5 points against the voxel solid | voxel-solid and mesh representations added; Stage 8 comparison now uses the solid |
| B — `LatticeTolerance` / DDA | **two small defects** | staircase voxels at exact edge/corner crossings; fixed 1e-9 stops firing beyond ~1e6 m world offset (error 4.8e-9 at 1e7 m) | simultaneous-axis stepping; scale-aware tolerance floored at 1e-9 |
| C — material penalty | **sound form, wrong reference energy** | scaling by an energy is scale-invariant (0 % drift over a 300× radiation change vs 300× for a bare fraction); `AdmittedUnwantedEnergy` collapses to exactly 0 with no unwanted solar | Stage 8 unchanged; Stage 9 defaults to `AdmittedDirectEnergy` |
| D — EggCrate Guid identity | **sound as implemented** | 582 elements over 117 candidates, 0 collisions; rebuild reproduces identity; parameter change moves the attribution table hash; crossing blades reconcile exactly with 0 unattributed | none |
| E — attribution cache scale | **sound, and the cost model for Stage 9** | 675 groups × 576 cells = 388 800 samples, 47.5 kB vs 1518.9 kB, ratio 32× exactly; 46–66 ms per candidate; no reuse possible across candidates | none |

---

## 2. The objective

`ShadingObjective` is a first-class object, separate from the search, so the two can be reasoned
about independently and the objective can be stored with the result.

```
Benefit = UnwantedSolarIntercepted                    [kWh]
Harm    = WantedSolarBlocked                          [kWh]
Cost    = MaterialFraction × ReferenceEnergy          [kWh]

Score   = Benefit − λ × Harm − μ × Cost               [kWh]
```

| symbol | name | default | units |
|---|---|---|---|
| λ | `WantedSolarPenalty` | 1.0 | dimensionless |
| μ | `MaterialPenalty` | 0.1 | dimensionless |
| — | `ReferenceEnergy` | `AdmittedDirectEnergy` | kWh |

### 2.1 Signs

**All three components are positive quantities.** Benefit is **added**; Harm and Cost are
**subtracted**. Names state what a quantity *is*, not which way it points — `WantedSolarBlocked` is
a loss, so a score that adds it would reward a device for destroying the winter sun. This is the
easiest error in the whole pipeline to make and it is guarded by measurement, not by inspection:
Case 2 sweeps the depth and the score **rises then falls**.

| depth | benefit | harm | cost | score |
|---|---|---|---|---|
| 0.1 m | 103.10 | 0.00 | 270.41 | 76.06 |
| 0.2 m | 152.80 | 0.00 | 540.83 | 98.72 |
| **0.3 m** | 221.06 | 40.32 | 811.24 | **99.62** |
| 0.4 m | 284.08 | 95.81 | 1081.65 | 80.10 |
| 0.8 m | 381.48 | 237.95 | 2163.31 | −72.80 |
| 3.0 m | 383.82 | 592.36 | 8112.40 | **−1019.78** |

Benefit **saturates** at 383.82 kWh past 1.2 m while harm keeps climbing. If the harm term had the
wrong sign the score would be monotonic in depth.

### 2.2 Units and normalisation

Every term is kWh, so the score is kWh and both penalties are dimensionless **exchange rates**. λ is
how many kWh of unwanted solar blocked is worth one kWh of wanted solar lost. μ is the share of the
aperture's admitted beam a designer will forgo per unit of `MaterialFraction`.

The cost is scaled by an admitted **energy** rather than left as a bare fraction. That makes the
whole objective scale linearly with the site's radiation, so the cost-to-benefit weighting is a
property of the *brief* rather than of how sunny the site is. Measured over a 300× radiation change
on identical geometry (`MaterialPenaltyTests`):

| cost basis | cost/benefit, bright | cost/benefit, dim | drift |
|---|---|---|---|
| **× admitted energy** | 0.40271 | 0.40271 | **0 %** |
| bare fraction | 0.00014893 | 0.04467755 | **300×** |

The whole objective rescales by exactly the radiation ratio, so the winning depth is the same
physical answer in both.

### 2.3 Why the reference energy differs from Stage 8

Stage 8's `ShadingFitScore` scales material by `AdmittedUnwantedEnergy`. That is dimensionally fine
and behaves well whenever there **is** unwanted solar — which is always true where Stage 8 uses it.
It goes to **exactly zero** when there is none, taking the entire cost term with it:

| reference energy | cost of a 0.05 m overhang | cost of a spread 0.05 m overhang |
|---|---|---|
| `AdmittedUnwantedEnergy` | **0 kWh** | **0 kWh** |
| `AdmittedDirectEnergy` | 13.52 kWh | 27.04 kWh |

An aperture with no unwanted solar is exactly the case where Stage 9 must be able to answer "build
nothing", and it needs a cost term to do it. Stage 9 therefore defaults to `AdmittedDirectEnergy`.
**Stage 8's own behaviour is unchanged**; the divergence is deliberate and is why `ShadingObjective`
carries a `MaterialCostReference` rather than hard-coding one.

The remaining degenerate case — an aperture receiving no direct beam at all — makes the reference
zero too, but there the benefit and harm are also zero because there is no beam to intercept or
preserve. A flat objective is the correct answer, not a gap.

### 2.4 Zero denominators

The score is an **absolute energy** and needs no denominator, so it stays defined when there is no
unwanted or no wanted solar. The reported percentages keep the Stage 8 rule: `DirectShadingEfficiency`,
`UnwantedSolarBlocked` and `WantedSolarRetained` are **`NaN`** when their denominator is zero, never
a flattering 0 % or 100 %. Nothing in Stage 9 converts a `NaN` percentage into a number.

### 2.5 The null device

Building nothing has no benefit, no harm and no material, so it scores **exactly 0**. Any candidate
scoring below zero is worse than leaving the aperture alone. This is what lets Stage 9 report
`RecommendsNoShading` with termination `NoBeneficialCandidate` instead of returning the least-bad
geometry as though it were a recommendation.

---

## 3. Search

`Optimise.ShadingTypology` — deterministic, derivative-free, two phases. **No external optimiser
dependency**; `netstandard2.0` and C# 7.3 throughout.

**1. Coarse lattice.** Every free parameter sampled at `coarseLevels` (default 3) evenly spaced
values across its bounds, enumerated in a fixed odometer order.

**2. Compass refinement.** From the coarse winner, each parameter is probed at ±step in the
typology's fixed order; the first strict improvement is accepted and the search moves on. When no
probe improves, every step halves. It stops when the step falls below each parameter's own
**granularity** — a 10 mm depth change, one whole louvre, a 5° blade increment — which is a physical
limit rather than an arbitrary epsilon.

### 3.1 Why a coarse phase, not just refinement from the seed

Because the objective is **not unimodal**. The exhaustively enumerated 1-D landscape from Case 7:

```
0.20 →  98.72     0.25 → 107.84  (global)     0.30 →  99.62
0.35 → 100.62  (local)            0.40 →  80.10
```

A pure descent from a seed at 0.35 m would settle in the wrong basin. The coarse lattice is what
stops the search committing to whichever basin the seed happens to sit in.

### 3.2 Why not a population method

Each evaluation costs a full attribution rebuild — 46–66 ms at 144 cells, and the cache is provably
not reusable between candidates (Gate 0 Review E). A GA or PSO would need hundreds of evaluations to
match what a compass search finds in tens, and would introduce a random seed that has to be stored
and honoured for reproducibility. The pattern search is inspectable, needs no tuning, and its
termination reason is meaningful.

### 3.3 Determinism

Reproducibility is structural, not incidental:

- parameters visited in the typology's fixed order, never a dictionary's enumeration order;
- every proposal **snapped** onto its parameter's step lattice, so two runs cannot land on
  almost-equal vectors that round to different geometry;
- evaluated points **memoised** by an exact round-trip (`"R"`) key, so a revisit cannot return a
  different answer;
- ties broken by a **total order**: score (within a relative 1e-12), then lower material, then
  lexicographically smaller parameters;
- **no randomness**, and no parallel reduction inside the search. The parallelism stays inside the
  attribution build, where each sun group owns its own row and has nothing to disagree about.

Case 8 asserts identical parameters, **bit-identical** score, identical evaluation and iteration
counts, identical element Guids and attribution hash, and identical JSON once wall-clock timings are
removed.

### 3.4 Termination

| reason | meaning |
|---|---|
| `StepBelowGranularity` | converged on the search lattice — the normal outcome |
| `EvaluationBudgetExhausted` | budget hit first; the result is the best seen, **not** converged |
| `NothingToSearch` | every parameter fixed, or the typology produced no geometry |
| `NoBeneficialCandidate` | nothing beat the null device; `RecommendsNoShading` is set |

---

## 4. Variables and bounds

Ranges are the typology's **own** declared bounds, narrowed where Stage 9 knows better, so the
optimiser can never propose a value the typology would silently clamp.

| family | variables | bounds | granularity |
|---|---|---|---|
| `Overhang` | Depth | 0.05 – 3.0 m | 0.01 m |
| | RiseAboveHead | 0.0 – 1.0 m | 0.01 m |
| | ExtensionBeyondJambs | 0.0 – 1.0 m | 0.01 m |
| `HorizontalLouvres` | Depth | 0.05 – 2.0 m | 0.01 m |
| | Count | 1 – *capped* | 1 |
| | TiltDegrees | −60 – +60° | 5° |
| `VerticalFins` | Depth | 0.05 – 2.0 m | 0.01 m |
| | Count | 1 – *capped* | 1 |
| | TiltDegrees | −60 – +60° | 5° |
| `EggCrate` | Depth | 0.05 – 2.0 m | 0.01 m |
| | LouvreCount | 1 – *capped* | 1 |
| | FinCount | 1 – *capped* | 1 |

No variable is introduced that the geometry or ray engine cannot model consistently. There is **no
porosity, no transmittance and no blade thickness**: the direct ray engine is binary, and a
perforated screen faked as an opaque face plus a porosity scalar would produce numbers that look
like a screen's without being one. Perforated screens remain **unsupported** (`Stages5-8-Method.md`
§4.2).

### 4.1 Element counts are capped by the analysis resolution

**This is a correctness constraint, and it was found because the failure looks like a triumph.**

On a 1 m tall aperture sampled at 0.5 m there are two rows of analysis cells. An eleven-blade louvre
array at minimum depth can sit so that *both rows fall just under a blade* — every sample is shaded
against high summer sun and none against low winter sun. The optimiser duly reported:

```
EggCrate Depth=0.05 LouvreCount=11 FinCount=1
   unwanted blocked 100 %, wanted retained 100 %, harm 0
```

Nothing in the energy accounting is wrong. The geometry is simply **finer than the analysis
measuring it**, so the number describes where the samples happened to fall.

Blade pitch is therefore tied to the grid: `count ≤ span / gridSize + 1`. On the 2 m × 1 m test
window at `gridSize` 0.25 that caps louvres at **5** and fins at **9**, against a declared bound of
24. A candidate can only be credited for shading the analysis can resolve. **Refine the grid to
justify a finer device.**

The cap is opt-in — `Create.ShadingParameters` applies it only when given a target and a grid size —
so a caller's explicit parameter set is never silently narrowed.

---

## 5. Typology eligibility

`Query.EligibleShadingTypologies` decides which families are worth *asking about*, from where the
unwanted solar actually arrives rather than from a compass bearing. Each sun group carrying unwanted
energy is resolved into the aperture frame and split into:

```
vertical shadow angle    VSA = atan(up-slope component / outward component)
horizontal shadow angle  HSA = atan(across-facade component / outward component)
```

A family is admitted when **≥ 25 %** of the unwanted energy arrives above its threshold — VSA > 15°
for the horizontal families, |HSA| > 30° for fins, both for the egg crate.

| aperture | high-sun share | oblique share | eligible |
|---|---|---|---|
| south (az 180°) | 100 % | 69.1 % | all four |
| east (az 90°) | 71.5 % | 26.1 % | all four |
| north (az 0°) | 58.7 % | 100 % | all four |
| no unwanted solar | — | — | **none** |

### 5.1 What this gate does not do

It is a **capability** gate, not a ranking, and it is honest about being permissive.

The expected result before measuring was that a north facade would exclude the horizontal families.
It does not, and it should not. A north facade's summer sun arrives at the ends of the day far round
the corner, and for such a direction the **outward component approaches zero — so the profile angle
is large even though the sun is low**, and an overhang genuinely does intercept that beam. Measured
on the London north facade: **89 kWh/m²** of unwanted beam against the south facade's 225, **no
wanted winter solar at all** to protect, and an optimised overhang worth **28.15 kWh** (36.1 % of
unwanted blocked). "No overhangs on north facades" is a rule of thumb about a different climate and
a different brief.

So the exclusion the gate reliably provides is the strong one — an aperture where no unwanted solar
reaches the front at all makes **nothing** eligible — plus a narrowing where the unwanted beam is
genuinely one-sided in angle. **Read a long eligibility list as "nothing is ruled out", not as "all
of these are sensible".** The objective decides whether a device is worth building.

---

## 6. Result object

`OptimisedShadingResult` retains the raw objective components **alongside** the scalar, because a
score alone cannot distinguish a device that blocks a lot of unwanted sun from one that simply uses
no material.

Stored: aperture Guid, typology, final parameters (in typology order), seed parameters, search
bounds and granularity, the objective itself, score, Benefit / Harm / Cost, seed score and
improvement, the five physical metrics plus admitted energies and the first-hit residual, material
fraction, element Guids, evaluations, iterations, elapsed / geometry / evaluation / optimiser-overhead
timings, termination reason, `RecommendsNoShading`, and provenance (strategy name, grid size,
sun-angle step, time shift, year, context and target geometry hashes, winning attribution table hash).

**Not stored:** the field, the caches or the geometry. Those are large, they already exist, and
duplicating them would make the result heavier than the analysis it describes. Identity is enough,
and `Typology()` rebuilds the winning device from its name and parameters — verified to reproduce the
same element Guids.

JSON round-trips **byte-identically**. Parameters are emitted in the typology's order rather than a
dictionary's enumeration order, which is what makes that true.

---

## 7. Validation

`gridSize` 0.25, `SunAngleStep` **2° (unchanged)**, London, 2 m × 1 m windows, synthetic
sun-symmetric weather.

| case | result |
|---|---|
| 1 — summer south | overhang 0.40 m, score **+138.6 kWh**, 57.9 % unwanted blocked, 98.7 % wanted retained |
| 2 — wanted winter sun | score rises then falls; 3.0 m scores **−1019.8 kWh** against the interior optimum |
| 3 — east aperture | overhang **84.32** vs fins **84.18 kWh** — within 0.2 %, fins using half the material |
| 4 — context obstruction | soffit leaves 1.1 % of the unwanted beam; optimiser drops to the **minimum 0.05 m** device, earns 0.01 kWh |
| 5 — no unwanted solar | `RecommendsNoShading`, `NoBeneficialCandidate`, fallback is the minimum device |
| 6 — no wanted solar | 1.54 m device; a 20× material penalty shrinks it to the minimum |
| 7 — known 1-D optimum | 30 points enumerated; optimiser finds the same **0.25 m / 107.8409 kWh in 10 evaluations** |
| 8 — determinism | identical parameters, bit-identical score, identical counts, Guids, hash and JSON |
| 9 — ideal vs Stage 8 vs Stage 9 | see below |
| 10 — attribution conservation | optimised egg crate reconciles to **1e-9**, zero unattributed |

Case 3 is worth reading rather than skimming: the two families come out within 0.2 % of each other
and the fin array gets there with roughly half the material. A brief weighting material more heavily
would flip the winner. That is exactly why the objective's components are reported alongside its
scalar, and why the test asserts only what must be true either way rather than a preferred outcome.

### 7.1 Case 9 — ideal vs rationalised vs optimised

One aperture, one objective, the same first-hit engine throughout:

| stage | device | benefit | harm | cost | **score** | unwanted blocked | wanted retained |
|---|---|---|---|---|---|---|---|
| Stage 7 ideal (voxel solid) | 2500 boundary faces | 450.9 | 249.1 | 0.0 | **201.8** | 100 % | 77.2 % |
| Stage 8 rationalised | Overhang 0.338 m, ext 0.2 | 268.2 | 63.3 | 1095.2 | **95.4** | 59.5 % | 94.2 % |
| **Stage 9 optimised** | Overhang 0.570 m, ext 0.05 | 308.3 | 6.1 | 1618.4 | **140.4** | **68.4 %** | **99.4 %** |

Stage 9 improves on the Stage 8 device by **+45.0 kWh**, and does so on **both** physical metrics at
once rather than trading one for the other. The search itself contributes **+87.9 kWh** over its own
seed (52.5 → 140.4 kWh), which is the measure of what the optimisation buys beyond the Stage 6
analytical seed.

Stage 9 does **not** beat the physically unconstrained ideal, and is not required to. The ideal
over-shades: it blocks everything unwanted and pays **22.8 points of winter sun** for it. The Stage 6
field is a **per-voxel marginal value** — "how useful would material at *this* voxel be", answered
independently — so filling every above-threshold voxel is not a jointly optimised solid. This is the
concrete argument for Stage 9 optimising against energy rather than fitting geometry to the mesh.

---

## 8. Performance

Measured in Release on the validation cases.

| aperture | typology | vars | evaluations | iterations | elapsed | geometry | rays | optimiser | final score |
|---|---|---|---|---|---|---|---|---|---|
| south | Overhang | 3 | 69 | 12 | 772 ms | 1 ms | 770 ms | <1 ms | 138.64 kWh |
| south | HorizontalLouvres | 3 | 55 | 10 | 978 ms | 2 ms | 976 ms | <1 ms | 140.55 kWh |
| south | EggCrate | 3 | 42 | 9 | 1646 ms | 7 ms | 1639 ms | 2 ms | 74.01 kWh |
| south | Overhang, 1-D | 1 | 10 | 6 | 139 ms | <1 ms | 137 ms | 2 ms | 107.84 kWh |
| east | Overhang | 3 | 53 | 9 | 452 ms | 1 ms | 451 ms | <1 ms | 84.32 kWh |
| east | VerticalFins | 3 | 45 | 10 | 771 ms | 2 ms | 768 ms | <1 ms | 84.18 kWh |

The only run seeded from a Stage 6 field is Case 9 (south, Overhang): **seed 52.52 kWh → final
140.45 kWh**, so the search contributes **+87.93 kWh** beyond the analytical seed. The other runs
start from the family default and their seed scores are not separately reported here.

**Ray casting and attribution are >99 % of the time.** Geometry construction is 0.1–0.4 %; the
optimiser itself is unmeasurable at this scale. The search is not the bottleneck and optimising it
further would buy nothing.

Cache reuse: the **base visibility cache is built once** and shared by every candidate of every
typology — it is the expensive one. The **attribution cache is rebuilt per candidate and cannot be
otherwise**, because the candidate's own faces are in the occluder set, so the first hit changes with
every parameter (Gate 0 Review E: 5 distinct table hashes over 5 depths). The winning candidate's
attribution table hash is stored on the result, so a cache is never silently reused after the
geometry identity changes.

The 4-family comparison on one aperture takes **~9 s**. Evaluation count is bounded by
`maximumEvaluations` (default 400); every run above terminated on `StepBelowGranularity`, well inside
it.

---

## 9. Limitations

| item | status |
|---|---|
| Inter-reflection | **None.** Intercepted solar must never be described as reflected solar |
| Perforated / translucent screens | **Unsupported** — the ray engine is binary |
| Device families | Four. No light shelves, no external roller blinds, no operable/seasonal devices |
| Multi-objective | **Single scalar only.** No Pareto front; λ and μ must be chosen up front |
| Optimality | **Local on the search lattice.** The coarse phase mitigates but does not eliminate multimodality |
| Device fineness | Capped by the analysis grid (§4.1). A finer device needs a finer grid |
| Eligibility gate | Permissive; excludes reliably only the no-unwanted-solar case (§5.1) |
| Diffuse solar | **Not in the objective.** Shading desirability is direct-beam only, as in Stages 5–8 |
| Aperture interaction | Each aperture optimised **independently**; a device does not shade its neighbour |
| Structural / cost realism | `MaterialFraction` is area only — no thickness, weight, fixing or money |
| Time resolution | **Hourly**, inherited |

---

## 10. Divergences from `ShadingOptimisation-Plan.md`

| planned | implemented | why |
|---|---|---|
| `PatternSearch` + `NSGAII` + `ParetoFront` | coarse lattice + compass refinement, single objective | NSGA-II needs hundreds of evaluations at 46 ms each and a stored random seed. A Pareto front is genuinely useful and is **deferred**, not rejected — `ShadingObjective` already exposes Benefit / Harm / Cost separately, which is the input a front needs |
| `ShadingOptimisationProblem` class | `ShadingObjective` + `List<ShadingParameter>` | the "problem" was three separable things: what to optimise, over what, and to what end. Splitting them makes the objective storable with the result |
| constraints by rejection/resampling | constraints as **bounds and granularity** | there are no non-box constraints in these four families; a box constraint is enforced by snapping, which is cheaper and exactly reproducible |
| objectives incl. "minimise material area" | material as a **priced cost term** | keeps the objective a single comparable energy. The raw material fraction is still reported |

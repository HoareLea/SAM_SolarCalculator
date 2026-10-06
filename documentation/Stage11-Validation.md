# Stage 11 — Validation status and assumptions register

**Scope:** the Phase-1 aperture-level shading and direct-solar workflow, Stages 0–10.1.
Stage 8.1 (internal solar penetration) is deferred and not covered here.

---

## 0. How to read this document

Two audiences, two sections.

- **§2 Validation status** — what has been measured, with the numbers. Read this to know whether the
  tool works.
- **§3 Assumptions register** — every approximation, its magnitude and its *direction*. Read this to
  know whether the tool is fit for **your** job. A number from this workflow does not belong in a
  report unless the assumptions below are acceptable for that report.

**Provenance of the figures.** Every number in §2 is quoted from the stage method documents, as
recorded on the build each was committed with. They were **not** re-measured while writing this
register. If a change moves a number, update it at source in the stage document *and* here. Do not
keep a stale baseline for comparison.

> ### How CI validates the figures in §2
>
> CI runs the test suite in two halves on complementary filters (split at `ce08d2b`):
>
> - **`build.yml`** — the required PR check — runs the FAST half:
>   `dotnet test … --filter "Category!=LongRunning"`. 248 tests. GitHub-verified green on
>   `ce08d2b4ff23f9a0eff7c818deebc9c8ab528975`.
> - **`long-running-tests.yml`** runs the LONG half — `Category=LongRunning`, 23 tests: the
>   exhaustive Gate-7 enumeration, the convergence sweeps and the expensive reference comparisons —
>   on push to `master`/`sow/**`, `workflow_dispatch` and the nightly schedule. It does **not** run
>   on pull requests. `workflow_dispatch` and the schedule fire from the repository default branch,
>   so they become available only once this workflow exists there; the first GitHub LONG run for
>   this Phase-1 integration will come from the `sow/2026-Q3` push trigger after merge. For this
>   Phase-1 readiness review the LONG half was verified green **locally** (23 / 23); no GitHub
>   LONG run is claimed.
>
> Together, FAST and LONG cover all 271 tests: 248 FAST and 23 LONG, with no test omitted or run
> in both groups. Untagged tests are FAST by default, so a new test gates the PR unless it is
> deliberately tagged out.
> The one skipped test — `ReferenceExport.Write_Reference_Inputs`, the reference-regeneration
> tool — is expected `NotExecuted` behaviour and does not count against either half.

---

## 1. What was built

```
AnalyticalModel (+ WeatherData)
  Stage 0   Create.ApertureSolarTargets        outward-normal-resolved aperture targets
            Convert.ToSAM_OccluderLinkedFace3Ds  context (panels cut at apertures + shades)
  Stage 1   AnalysisPeriod                     hour selection on the weather timeline
  Stage 2   Create.SunBins / SolarVisibilityCache   geometry × sun-group lit bitsets
  Stage 3   Create.SkyVisibilityCache          per-cell SVF / horizon / ground visibility, Perez sky
  Stage 4   Query.CachedIrradiance             arithmetic re-weighting → kWh/m² per cell
            Modify.SimulateApertures           orchestration + AddResult<Aperture>
  Stage 5   desirability weighting             signed wanted/unwanted direct solar
  Stage 6   ShadingPotentialField              voxel benefit field (DDA traversal)
  Stage 7   ideal shading geometry             marching tetrahedra
  Stage 8   RationaliseShading / VerifyShading first-hit attribution, per-element performance
            SolarAttributionCache              built only for candidate devices
  Stage 9   optimisation engine                typology fitting, scalar objective
  Stage 10  Grasshopper components             + Stage 10.1 multi-aperture handling
```

The two-cache split (§2.5 of the plan, as amended) is the load-bearing design decision:
`SolarVisibilityCache` is a compact shared bitset reused across every aperture and candidate;
`SolarAttributionCache` carries first-hit element identity and is built only where a candidate device
needs it.

---

## 2. Validation status

### 2.1 The central architectural claim — **validated**

The plan's core bet (§2.1) was that grouping sun positions makes period selection a pure re-weighting,
so changing season/day/period costs no geometry. Measured on ModelB (13 apertures, 135 cells @ 0.5 m,
36 occluders):

| operation | measured |
|---|---:|
| first call — build both caches + evaluate a full year | 2607 ms |
| second call — different `AnalysisPeriod`, caches reused | **298 ms** |

**~8.7× on period change, with no geometric recomputation.** Reuse behaves as specified:
`AnalysisPeriod` change → reused; `WeatherData` swap → reused; `gridSize` change → rebuild;
`recalculate: true` → rebuild; `SunTimeConvention` change → rebuild.

### 2.2 Sun-group quantisation bias — **within gate**

Synthetic south window + overhang, 4186 daylight hours, per-hour against the exact sampled
`Simulate_Coverage` baseline:

| `sunAngleStep` | groups | build (ms) | MAE | DNI-weighted MAE |
|---:|---:|---:|---:|---:|
| 1° | 1336 | 1557 | 0.0125 | 0.0116 |
| **2° (default)** | **666** | **1587** | **0.0154** | **0.0145** |
| 5° | 240 | 1340 | 0.0293 | 0.0298 |

The 2° default sits at **1.54 % MAE**, inside the plan's 2 % gate. Two honest observations:

- **Build time does not fall linearly with group count** on a model this small — it is dominated by
  the per-group geometric pass, so 5° buys accuracy loss for little speed. The default is the right
  place to sit; going coarser is not obviously worth it.
- The 1° row shows the residual is not purely quantisation — halving the step does not halve the
  error, so some of the 1.25 % floor is baseline discretisation, not grouping.

### 2.3 Regression gate — **holds**

`WithShade_SAM_matches_TAS_within_tolerance`: 36 surfaces matched 1:1, 5371 overlapping hours,
**mean absolute delta 0.00880** against a gate of 0.02. The existing SAM-vs-TAS coverage benchmark is
unmoved by the new radiation path — the workflow was added alongside the validated engine, not
through it.

### 2.4 Component-level checks recorded

- Tregenza-145 quadrature reproduces analytic view factors to **~0.5 %**; can slightly exceed 1.0 for
  an unobstructed up-facing cell.
- Evaluated-hour conservation across sun-position shifts (T1); east/west symmetry with demonstrated
  detection power (T2); corrected standalone radiation vs cached irradiation (T3); cache identity
  (T4).
- Genuine DNI confirmed in `DirectSolarRadiation` (B6).
- ModelB annual per-aperture averages recorded for 13 apertures at 0.5 m grid.

### 2.5 Gates 1–2 — independent and analytical validation

Gate 1 (`IndependentReferenceTests`) compares SAM against **Ladybug Tools**' `ladybug.sunpath.Sunpath`,
run **once offline** by `Fixtures/Reference/generate_reference.py` into a committed
`reference-results.json`. Nothing here runs Python; Ladybug is not a build, test or runtime dependency.
This is the "borrow the validation without inheriting the dependency" approach the plan called for.

It is careful about what it actually proves, which matters more than the headline:

| | |
|---|---|
| **Independent** | Solar position — Ladybug's own declination, equation of time and hour angle. |
| **Independent** | Transposition geometry and annual accumulation — `cos(incidence)` recomputed from Ladybug's sun vectors and summed separately. |
| **Shared by design** | The GHI/DHI series, exported from the fixture so both sides read byte-identical radiation. Two different weather files would measure the files. |
| **Shared by design** | The DNI decomposition rule — SAM's documented modelling choice. Its low-sun clamp is *measured* separately rather than validated against itself. |
| **Not covered** | Diffuse and ground-reflected transposition. Ladybug's Python API exposes decomposition (DISC/DIRINT), not Perez transposition onto a tilted surface. |

Asserted thresholds: sun altitude and azimuth MAE
< 0.25°, max absolute < 1.0°, over all 8760 hours. The gates assert these bounds in code and are
now executed by CI (§0); the achieved values are not pasted here — see §5.

Gate 2 (`AnalyticalValidationTests`) adds closed-form checks: NOAA sun position at solstices and
equinoxes; solar-noon altitude against the declination identity; fractional time-zone offset; the
cosine law on an unobstructed surface; an aperture facing away admits nothing; **an overhang shading
exactly to the profile-angle construction**; an unobstructed vertical surface seeing half the sky;
first-element-reached attribution; and context obstruction never being credited to a device.

`ConvergenceStudyTests` and `ResolutionConvergenceTests` add the grid- and voxel-convergence work,
including the minimum-feature-size rule and the field's spatial stability under refinement.

### 2.6 Gate 7 — the optimality gap is **materially larger than the gate allows** ⚠

The first execution of this gate, once an invalid ceiling assertion was removed (it compared the
search against a five-times-coarser enumeration and required the search never to win), measured the
bounded search against exhaustive enumeration on 12 family/objective combinations:

| family | λ | optimiser | enumerated best | gap % | evaluated / enumerated |
|---|---:|---:|---:|---:|---:|
| Overhang | 0.5 | 147.495 | 133.974 | **−10.09** | 75 / 1500 |
| Overhang | 1 | 138.639 | 122.680 | **−13.01** | 69 / 1500 |
| Overhang | 2 | 114.797 | 121.617 | 5.61 | 66 / 1500 |
| HorizontalLouvres | 0.5 | 154.043 | 148.675 | **−3.61** | 53 / 1080 |
| HorizontalLouvres | 1 | 148.444 | 146.381 | **−1.41** | 53 / 1080 |
| HorizontalLouvres | 2 | 89.905 | 146.381 | **38.58** | 49 / 1080 |
| VerticalFins | 0.5 | 4.090 | 5.071 | 19.35 | 42 / 1800 |
| VerticalFins | 1 | 3.634 | 5.071 | 28.34 | 41 / 1800 |
| VerticalFins | 2 | 3.634 | 5.071 | 28.34 | 41 / 1800 |
| EggCrate | 0.5 | 82.952 | 115.743 | 28.33 | 40 / 600 |
| EggCrate | 1 | 74.013 | 111.762 | 33.78 | 40 / 600 |
| EggCrate | 2 | 60.950 | 103.798 | **41.28** | 38 / 600 |

**Mean 16.29 %, worst 41.28 %**, matched or beat the coarse lattice in 4 of 12. Negative = the search
found a better point than the coarser lattice contains. The gate's own thresholds are worst < 10 %
and mean < 3 %, so **it fails, and the threshold must not be relaxed to make it pass.**

**Mechanism, from the numbers.** `Optimise.ShadingTypology` runs a coarse lattice at
`coarseLevels = 3` samples per free parameter, then compass refinement from the single best coarse
point, under `maximumEvaluations = 400`. Only **38–75 evaluations are actually used** — between 10 %
and 19 % of the permitted budget. Two patterns identify the failure:

- Where the response is close to unimodal (Overhang and HorizontalLouvres at λ = 0.5 and 1) the
  search **beats** the coarse enumeration. The refinement works.
- The gap grows with **λ**, the wanted-solar penalty, and with the number of interacting parameters
  (EggCrate, VerticalFins). At λ = 2 every family is at its worst.

That is basin-lock. The method's own documentation says the coarse phase exists to prevent it — *"the
depth response of a real device is not unimodal once counts and tilts are in play"* — and three levels
per parameter is too sparse to locate the right basin once the objective stiffens.

**The coarse phase is a full Cartesian lattice** (`Lattice` is an odometer over the free parameters),
so sparseness, not structure, is the problem: `fraction = counter / (levels - 1)` means three levels
samples each parameter only at its **minimum, midpoint and maximum**. A depth bounded 0–1.5 m is tried
at 0, 0.75 and 1.5 m and nowhere between; compass refinement then descends from whichever of those
corners scored best.

**Two candidate fixes, neither yet attempted** — both change product behaviour and must be *measured*
on this gate rather than assumed:

1. **Multi-start refinement.** Refine from the best *k* coarse points instead of only the best one.
   This attacks basin-lock directly and fits the unspent budget, without growing the coarse grid.
2. **Raise `coarseLevels`.** Note this does not scale freely: at 5 levels a three-parameter family
   costs 125 coarse points and a four-parameter one 625, which exceeds the 400 budget outright. So it
   helps the narrow families and stalls the wide ones — which are precisely EggCrate and VerticalFins,
   the worst performers. Option 1 is the better first experiment.

**Consequence for reporting now.** Until this is closed, the wording rule in A17 is not a stylistic
preference but a requirement: the search returns *best found within a bounded deterministic search*,
and on EggCrate and VerticalFins that has been measured up to 41 % below the best point in a lattice
coarser than its own.

### 2.7 Multi-start refinement — measured. Large improvement, **gate still fails**

Multi-start refinement (`64b1370`): phase 1 keeps every coarse point, the best 5 distinct are ranked
and each gets its own compass descent, global winner under the existing total order. Coarse lattice,
budget, `IsBetter`, NO SHADE semantics and the gate thresholds all unchanged.

| family | λ | gap % before | gap % after | evals before | evals after |
|---|---:|---:|---:|---:|---:|
| Overhang | 0.5 | −10.09 | −12.36 | 75 | 140 |
| Overhang | 1 | −13.01 | −13.82 | 69 | 158 |
| Overhang | 2 | +5.61 | **−5.46** | 66 | 125 |
| HorizontalLouvres | 0.5 | −3.61 | −3.61 | 53 | 81 |
| HorizontalLouvres | 1 | −1.41 | −1.41 | 53 | 99 |
| HorizontalLouvres | 2 | **+38.58** | **−4.27** | 49 | 91 |
| VerticalFins | 0.5 | +19.35 | **0** | 42 | 67 |
| VerticalFins | 1 | +28.34 | **0** | 41 | 74 |
| VerticalFins | 2 | +28.34 | **0** | 41 | 74 |
| EggCrate | 0.5 | +28.33 | +28.33 | 40 | 63 |
| EggCrate | 1 | +33.78 | +33.78 | 40 | 60 |
| EggCrate | 2 | +41.28 | +41.28 | 38 | 56 |

| | before | after | gate |
|---|---:|---:|---|
| mean gap | 16.291 % | **5.204 %** | < 3 % — still fails |
| worst gap | 41.28 % | **41.28 %** | < 10 % — still fails |
| matched or beat | 4 / 12 | **9 / 12** | — |
| evaluations | 38–75 | 56–158 | ≤ 400 budget |

**The change engaged everywhere** — evaluations rose on all twelve — and basin-lock was real: mean gap
fell by 68 %, VerticalFins now lands *exactly* on the enumerated best at every λ, and the two worst
non-EggCrate cells (HorizontalLouvres λ=2 at +38.58 %, Overhang λ=2 at +5.61 %) both crossed to
*beating* the coarse lattice. **This is not a claim that the problem is fixed: the gate fails.**

**EggCrate did not move at all** — 82.952 / 74.013 / 60.95, bit-identical to single-start, while its
evaluation count rose from 38–40 to 56–63. Five distinct starts were refined and none beat the
original winner. It alone now sets the worst case, and it is the only reason the worst-case threshold
still fails.

**Leading hypothesis, not yet tested and not acted on.** EggCrate is the only family whose parameters
are strongly coupled: `Depth` [0.05, 2.0] is shared by *both* the louvre array and the fin array,
alongside `LouvreCount` and `FinCount` [1, 16]. Compass search probes **one axis at a time** and
accepts the first improvement; where progress requires changing depth *and* a count together, every
single-axis probe fails, the step halves, and the descent terminates at a point no individual move can
leave. Multi-start cannot rescue that — it changes where descents *begin*, not the moves available to
them. The evidence fits: the two-parameter families (Louvres, Fins) were fully repaired, the
three-parameter coupled one was untouched.

If that is right, the next experiment is a **move set**, not more starts or a finer lattice: allow
compound probes (e.g. pattern-search moves over parameter pairs) so a coordinated step is reachable.
Recorded as a hypothesis; no further change made pending direction.

### 2.8 Compound pairwise moves — measured, and **the hypothesis in §2.7 is rejected**

The §2.7 move-set hypothesis was implemented and measured, then **removed**. Compass refinement was
extended with deterministic compound probes: whenever no single-axis probe improved, every pair of
free parameters was tried at all four sign combinations at the current step scale, before the step was
halved. Coarse lattice, `maximumEvaluations = 400`, `coarseLevels = 3`, multi-start, `IsBetter`,
NO SHADE semantics and the gate thresholds were all left unchanged.

| family | λ | optimiser | enumerated best | gap % | evals | optimiser parameters | enumerated parameters |
|---|---:|---:|---:|---:|---:|---|---|
| Overhang | 0.5 | 154.960 | 133.974 | **−15.66** | 218 | Depth 0.54, Rise 0.11, Ext 0.02 | Depth 0.35, Rise 0, Ext 0 |
| Overhang | 1 | 139.636 | 122.680 | **−13.82** | 197 | Depth 0.42, Rise 0.1, Ext 0 | Depth 0.65, Rise 0.25, Ext 0 |
| Overhang | 2 | 138.368 | 121.617 | **−13.77** | 128 | Depth 0.43, Rise 0.12, Ext 0.01 | Depth 0.65, Rise 0.25, Ext 0 |
| HorizontalLouvres | 0.5 | 155.044 | 148.675 | **−4.28** | 108 | Depth 0.54, Count 1, Tilt −10 | Depth 0.6, Count 1, Tilt −15 |
| HorizontalLouvres | 1 | 148.444 | 146.381 | **−1.41** | 102 | Depth 0.24, Count 3, Tilt −5 | Depth 0.1, Count 3, Tilt 45 |
| HorizontalLouvres | 2 | 146.381 | 146.381 | 0 | 128 | Depth 0.1, Count 3, Tilt 45 | Depth 0.1, Count 3, Tilt 45 |
| VerticalFins | 0.5 | 5.071 | 5.071 | 0 | 80 | Depth 0.05, Count 5, Tilt 15 | Depth 0.05, Count 5, Tilt 15 |
| VerticalFins | 1 | 5.071 | 5.071 | 0 | 97 | Depth 0.05, Count 5, Tilt 15 | Depth 0.05, Count 5, Tilt 15 |
| VerticalFins | 2 | 5.071 | 5.071 | 0 | 97 | Depth 0.05, Count 5, Tilt 15 | Depth 0.05, Count 5, Tilt 15 |
| EggCrate | 0.5 | 82.952 | 115.743 | 28.33 | 65 | Depth 0.21, **LouvreCount 1**, FinCount 1 | Depth 0.1, **LouvreCount 3**, FinCount 1 |
| EggCrate | 1 | 74.013 | 111.762 | 33.78 | 62 | Depth 0.11, **LouvreCount 1**, FinCount 1 | Depth 0.1, **LouvreCount 3**, FinCount 1 |
| EggCrate | 2 | 60.950 | 103.798 | **41.28** | 58 | Depth 0.11, **LouvreCount 1**, FinCount 1 | Depth 0.1, **LouvreCount 3**, FinCount 1 |

| | multi-start (§2.7) | with compound moves | gate |
|---|---:|---:|---|
| mean gap | 5.204 % | 4.536 % | < 3 % — **still fails** |
| worst gap | 41.28 % | **41.28 %** | < 10 % — **still fails** |
| matched or beat | 9 / 12 | 9 / 12 | — |
| evaluations | 56–158 | 58–218 | ≤ 400 budget |

**EggCrate did not move — bit-identical scores *and* bit-identical parameter vectors**, at every λ, on
82.952 / 74.013 / 60.95. Evaluations rose by exactly two per case, so the compound probes did run and
none was accepted. **No compound move escaped the former incumbent.** The mean improved only because
of families that already beat the enumeration, and one cell moved the wrong way
(HorizontalLouvres λ=2, from −4.27 % to 0 %). **The hypothesis that EggCrate is trapped by parameter
coupling is therefore not supported, and the compound-move code was removed rather than kept for a
mean it did not exist to improve.**

**Root cause, measured directly — the count axis is never probed at all.** The parameter vectors, not
the objective values, identify it: at every λ the only dimension EggCrate loses on is `LouvreCount`,
1 against the reference's 3. `Depth` is already within 10 mm of the reference at λ = 1 and 2, and
`FinCount` matches exactly. That is not a diagonal the search cannot reach; it is **a single axis the
search cannot move along**.

The effective bounds are *not* the typology's declared [1, 16] quoted in §2.7. `Create.ShadingParameters`
narrows every count to what the analysis grid can resolve, giving, on this fixture:

| family | parameter | effective bounds | granularity | range | first compass step | probe from minimum |
|---|---|---|---:|---:|---:|---|
| Overhang | Depth | [0.05, 3] | 0.01 | 2.95 | 0.7375 | 0.79 — moves |
| HorizontalLouvres | Count | **[1, 3]** | 1 | 2 | **0.5** | **1 — snaps back** |
| VerticalFins | Count | [1, 5] | 1 | 4 | 1 | 2 — moves |
| EggCrate | Depth | [0.05, 2] | 0.01 | 1.95 | 0.4875 | 0.54 — moves |
| EggCrate | **LouvreCount** | **[1, 3]** | 1 | 2 | **0.5** | **1 — snaps back** |
| EggCrate | FinCount | [1, 5] | 1 | 4 | 1 | 2 — moves |

The refinement's first step is `0.5 * Range / (coarseLevels - 1)`, so a count whose effective range is
2 starts at a step of **0.5** — half its own granularity. `ShadingParameter.Snap` rounds that back onto
the incumbent, the probe is skipped as a no-op, and because **steps only ever halve, they never
recover**. `LouvreCount` is therefore unreachable from the moment refinement begins, at every scale,
from every start. Compound probes inherit the same `step[]` array, which is why the two pairs
containing `LouvreCount` degenerated and only `Depth`+`FinCount` produced the two extra evaluations.

This also explains §2.7's tidiest result without invoking coupling: `VerticalFins.Count` has range 4,
step 1, and moves — and VerticalFins was fully repaired by multi-start. `HorizontalLouvres.Count` has
the same defect as `LouvreCount` and is only masked because its coarse lattice happens to land on the
right count.

**Status: stopped for review before any further optimiser change.** The indicated fix is to floor the
refinement step at each parameter's own granularity rather than at a fraction of its range, so a
discrete axis is always probed at ±1. That is a third change to the optimiser and is **not made here**.
No threshold was relaxed. The scratch harness that produced the reachability table above was removed
rather than committed; a focused regression test asserting that every free axis is reachable at the
first refinement step belongs with that fix, since committing it now would add a second red test for a
defect whose correction is not yet authorised.

### 2.9 The unreachable-axis defect — **corrected**, and Gate 7 **still fails** for a different reason

The §2.8 root cause was fixed. It was a real correctness defect, and it is now covered by two
permanent tests in `ShadingOptimisationTests`. **It was not, however, what was holding EggCrate back**,
and Gate 7 still fails.

**The correction.** `ShadingParameter` gained `MinimumIncrement` — the smallest change `Snap` can
express, which is the declared `Step` where there is one and a small fraction of the range where the
lattice is continuous. The refinement's step schedule is floored at it, both when the step is first
set and at every halving, and the descent now terminates when *every free axis is already probing one
increment either way and none improves* rather than when the steps have shrunk below the granularity.
That last part matters: the old test stopped **below** the increment, so the increment itself was
never tried. The now-redundant `Converged` helper was deleted; nothing else in the search changed.
No parameter is named anywhere in the optimiser.

**Two tests, written before the fix.**

- `Narrowing_A_Count_To_The_Analysis_Resolution_Must_Not_Put_It_Out_Of_The_Search_s_Reach` walks every
  free axis of every family and asserts one refinement step actually moves it. It records that **2 of
  12 axes** — `EggCrate.LouvreCount` and `HorizontalLouvres.Count`, both narrowed to [1, 3] — have a
  range-derived step of 0.5 against a granularity of 1, and that the remaining 10 keep *exactly* the
  schedule they had before, so the floor is inert for continuous parameters.
- `The_Search_Result_Is_Locally_Best_Along_Every_Free_Axis` rebuilds and re-measures both lattice
  neighbours of the returned point on every free axis and requires none to score better. **This one
  failed before the fix** — and on a case that was not predicted: `HorizontalLouvres` returned
  TiltDegrees 30 when 35 scored 153.331 against 152.637. Tilt is not a degenerate axis; it was missed
  because the step schedule is global and monotone, so a value first reached late in a descent can
  never be probed at a coarse scale again. The floor repairs that too.

| family | λ | optimiser | enumerated best | gap % | evals | optimiser parameters | enumerated parameters |
|---|---:|---:|---:|---:|---:|---|---|
| Overhang | 0.5 | 150.533 | 133.974 | **−12.36** | 140 | Depth 0.58, Rise 0.13, Ext 0.02 | Depth 0.35, Rise 0, Ext 0 |
| Overhang | 1 | 139.636 | 122.680 | **−13.82** | 161 | Depth 0.42, Rise 0.1, Ext 0 | Depth 0.65, Rise 0.25, Ext 0 |
| Overhang | 2 | 128.257 | 121.617 | **−5.46** | 125 | Depth 0.61, Rise 0.24, Ext 0.04 | Depth 0.65, Rise 0.25, Ext 0 |
| HorizontalLouvres | 0.5 | 154.253 | 148.675 | **−3.75** | 102 | Depth 0.2, Count 3, Tilt 5 | Depth 0.6, Count 1, Tilt −15 |
| HorizontalLouvres | 1 | 148.444 | 146.381 | **−1.41** | 121 | Depth 0.24, Count 3, Tilt −5 | Depth 0.1, Count 3, Tilt 45 |
| HorizontalLouvres | 2 | 153.962 | 146.381 | **−5.18** | 124 | Depth 0.09, Count 3, Tilt 45 | Depth 0.1, Count 3, Tilt 45 |
| VerticalFins | 0.5 | 5.071 | 5.071 | 0 | 77 | Depth 0.05, Count 5, Tilt 15 | Depth 0.05, Count 5, Tilt 15 |
| VerticalFins | 1 | 5.071 | 5.071 | 0 | 79 | Depth 0.05, Count 5, Tilt 15 | Depth 0.05, Count 5, Tilt 15 |
| VerticalFins | 2 | 5.071 | 5.071 | 0 | 79 | Depth 0.05, Count 5, Tilt 15 | Depth 0.05, Count 5, Tilt 15 |
| EggCrate | 0.5 | 84.133 | 115.743 | 27.31 | 69 | Depth 0.22, **LouvreCount 1**, FinCount 1 | Depth 0.1, **LouvreCount 3**, FinCount 1 |
| EggCrate | 1 | 74.013 | 111.762 | 33.78 | 72 | Depth 0.11, **LouvreCount 1**, FinCount 1 | Depth 0.1, **LouvreCount 3**, FinCount 1 |
| EggCrate | 2 | 63.807 | 103.798 | **38.53** | 67 | Depth 0.1, **LouvreCount 1**, FinCount 1 | Depth 0.1, **LouvreCount 3**, FinCount 1 |

| | multi-start (§2.7) | compound (§2.8) | step floor | gate |
|---|---:|---:|---:|---|
| mean gap | 5.204 % | 4.536 % | **4.803 %** | < 3 % — **still fails** |
| worst gap | 41.28 % | 41.28 % | **38.53 %** | < 10 % — **still fails** |
| matched or beat | 9 / 12 | 9 / 12 | 9 / 12 | — |
| evaluations | 56–158 | 58–218 | 67–161 | ≤ 400 budget |

`HorizontalLouvres` improved at two of three weights (λ=2 from −4.27 % to −5.18 %, λ=0.5 to −3.75 %),
which is the tilt defect above being repaired. **EggCrate still returns `LouvreCount = 1` at every λ.**

**Why — measured, not inferred.** The axis is now genuinely probed; it simply loses. The objective
along `LouvreCount`, holding the returned depth and fin count, is a **valley at 2**:

| λ | depth | LouvreCount 1 | LouvreCount 2 | LouvreCount 3 |
|---|---:|---:|---:|---:|
| 0.5 | 0.22 (returned) | 84.133 | **24.642** | 103.389 |
| 0.5 | 0.1 (reference) | 75.752 | **48.710** | 115.743 |
| 1 | 0.11 (returned) | 74.013 | **44.267** | 114.404 |
| 1 | 0.1 (reference) | 71.770 | **44.729** | 111.762 |
| 2 | 0.1 (returned = reference) | 63.807 | **36.766** | 103.798 |

Three louvres are worth 20–40 points more than one, but **two are worth 27–50 points less than either**,
at every weight and at both depths. A strict-descent compass search probing ±1 must step onto 2 to get
to 3, and 2 is rejected. This is a genuine non-convexity in the physics — a two-blade array on this
aperture puts both blades where they block winter beam without covering the summer profile — not a
search defect. **The fix made the axis reachable; it cannot make a descent cross a valley.**

Multi-start does not supply the basin either. At λ=2 the whole coarse lattice scores *negative* —
worse than building nothing — because depth is sampled only at 0.05, 1.02 and 2.0 while the useful
region is near 0.1. Ranked, the nine `LouvreCount = 3` points come 6th, 8th, 9th and below; refinement
takes the best 5, all of which have `LouvreCount` 1 or 2. The best `LouvreCount = 3` point (−20.308)
misses the refinement set **by one place**.

**Status: stopped, per the standing decision rule.** The near-miss at rank 6 makes a sixth start look
tempting and that is exactly why it was not done — tuning `DefaultRefinementStarts` until this fixture
passes is fitting the constant to the test, not fixing the search. No threshold was relaxed, no budget
or coarse level raised, no heuristic added, and the rejected compound-move code remains removed. The
step floor is retained on its own merits: it is a correctness condition with a regression test that
fails without it, and it removes code rather than adding a heuristic.

**What the evidence now points at**, for direction rather than action: the coarse lattice samples depth
at three points spanning 0.05–2.0 m when everything of interest happens below 0.3 m, so no start in the
right region exists for *any* family — Overhang and Louvres survive that only because their response is
smooth enough for a depth descent to walk there. That is a statement about how the lattice spans a
bounded range, not about the move set, and it should be measured before anything is changed.

### 2.10 Budget-aware multi-start — measured. **Best mean yet, gate still fails, and the cause is now certain**

The fixed best-5 start limit was replaced by a budget-bounded one: the coarse ranking is collected
whole and refined in order until either the ranking is exhausted or `maximumEvaluations` is reached.
No constant was bumped from 5 to 6 — the count is gone, and the bound is the budget the caller already
states. Everything else is untouched: coarse lattice at `coarseLevels = 3`, the `MinimumIncrement`
floor from §2.9, `Snap`, `IsBetter`, NO SHADE, bounds, determinism. `OptimisedShadingResult` gained
`CoarseStartsAvailable` / `CoarseStartsRefined` so the stop reason is reported rather than inferred.

| family | λ | optimiser | enumerated best | gap % | evals | starts | stopped because | optimiser parameters | enumerated parameters |
|---|---:|---:|---:|---:|---:|---:|---|---|---|
| Overhang | 0.5 | 154.960 | 133.974 | **−15.66** | 289 | 28 / 28 | all starts refined | Depth 0.54, Rise 0.11, Ext 0.02 | Depth 0.35, Rise 0, Ext 0 |
| Overhang | 1 | 140.449 | 122.680 | **−14.48** | 306 | 28 / 28 | all starts refined | Depth 0.57, Rise 0.18, Ext 0.05 | Depth 0.65, Rise 0.25, Ext 0 |
| Overhang | 2 | 132.689 | 121.617 | **−9.10** | 259 | 28 / 28 | all starts refined | Depth 0.6, Rise 0.22, Ext 0.08 | Depth 0.65, Rise 0.25, Ext 0 |
| HorizontalLouvres | 0.5 | 154.253 | 148.675 | **−3.75** | 224 | 28 / 28 | all starts refined | Depth 0.2, Count 3, Tilt 5 | Depth 0.6, Count 1, Tilt −15 |
| HorizontalLouvres | 1 | 153.962 | 146.381 | **−5.18** | 217 | 28 / 28 | all starts refined | Depth 0.09, Count 3, Tilt 45 | Depth 0.1, Count 3, Tilt 45 |
| HorizontalLouvres | 2 | 153.962 | 146.381 | **−5.18** | 203 | 28 / 28 | all starts refined | Depth 0.09, Count 3, Tilt 45 | Depth 0.1, Count 3, Tilt 45 |
| VerticalFins | 0.5 | 5.071 | 5.071 | 0 | 154 | 28 / 28 | all starts refined | Depth 0.05, Count 5, Tilt 15 | Depth 0.05, Count 5, Tilt 15 |
| VerticalFins | 1 | 5.071 | 5.071 | 0 | 154 | 28 / 28 | all starts refined | Depth 0.05, Count 5, Tilt 15 | Depth 0.05, Count 5, Tilt 15 |
| VerticalFins | 2 | 5.071 | 5.071 | 0 | 154 | 28 / 28 | all starts refined | Depth 0.05, Count 5, Tilt 15 | Depth 0.05, Count 5, Tilt 15 |
| EggCrate | 0.5 | 84.133 | 115.743 | 27.31 | 112 | 28 / 28 | all starts refined | Depth 0.22, **LouvreCount 1**, FinCount 1 | Depth 0.1, **LouvreCount 3**, FinCount 1 |
| EggCrate | 1 | 74.013 | 111.762 | 33.78 | 116 | 28 / 28 | all starts refined | Depth 0.11, **LouvreCount 1**, FinCount 1 | Depth 0.1, **LouvreCount 3**, FinCount 1 |
| EggCrate | 2 | 63.807 | 103.798 | **38.53** | 112 | 28 / 28 | all starts refined | Depth 0.1, **LouvreCount 1**, FinCount 1 | Depth 0.1, **LouvreCount 3**, FinCount 1 |

| | multi-start 5 | step floor (§2.9) | budget-aware | gate |
|---|---:|---:|---:|---|
| mean gap | 5.204 % | 4.803 % | **3.854 %** | < 3 % — **still fails** |
| worst gap | 41.28 % | 38.53 % | **38.53 %** | < 10 % — **still fails** |
| matched or beat | 9 / 12 | 9 / 12 | 9 / 12 | — |
| evaluations | 56–158 | 67–161 | 112–306 | ≤ 400 budget |

**Every case refined all 28 available starts and stopped because the ranking was exhausted, not
because of the budget** — the worst case spent 306 of 400. So the search now refines *every distinct
coarse point there is*, and the fixed count of five was indeed leaving value on the table: the mean
fell again, and Overhang λ=2 crossed from −5.46 % to −9.10 %.

**The rank-6 near-miss explanation from §2.9 is now dead.** All nine `LouvreCount = 3` coarse points
were refined at every λ, including the rank-6 best. EggCrate still returns `LouvreCount = 1`:
Depth 0.22 / 0.11 / 0.10 at λ = 0.5 / 1 / 2, `FinCount 1` throughout.

**Why the LouvreCount 3 start does not arrive — measured.** Two facts settle it.

First, the depth response at `LouvreCount 3, FinCount 1` is a **narrow spike**:

| depth | λ=0.5 | λ=1 | λ=2 | |
|---|---:|---:|---:|---|
| 0.05 | −19.569 | −19.815 | −20.308 | the coarse start |
| 0.09 | 99.953 | 97.198 | 91.688 | |
| **0.10** | **115.743** | **111.762** | **103.798** | enumerated reference |
| 0.11 | 120.935 | 114.404 | 101.341 | |
| 0.15 | 92.270 | 77.470 | 47.870 | |
| 0.30 | 38.011 | −41.547 | −200.662 | |
| **0.54** | **−342.608** | **−593.350** | **−1094.833** | first compass probe from 0.05 (+0.4875) |
| 2.00 | −1900.209 | −2326.741 | −3179.804 | coarse maximum |

Everything worth building lives between roughly 0.08 and 0.30 m. The coarse lattice samples depth at
**0.05, 1.02 and 2.00** — one point just below the useful band and two points deep in a region where
the objective is worse than building nothing by three orders of magnitude. The refinement's first
depth step from 0.05 is 0.4875, landing on 0.54, which is −1094.833. It is rejected, correctly.

Second, at depth 0.05 the ordering along `LouvreCount` is **inverted**: 1 scores −1.753, 2 scores
−15.274, 3 scores −20.308. So a descent starting at `(0.05, 3, 1)` finds that *reducing* the count
improves matters, walks 3 → 2 → 1 on its first pass, and refines depth from there. It abandons the
basin before depth is ever resolved into the spike where three louvres win. The valley at
`LouvreCount 2` recorded in §2.9 exists at depth 0.10; at depth 0.05 it is a ridge pointing the wrong
way.

**Confirmation that the basin itself is fine.** Pinning `LouvreCount` to 3 by its bounds and running
the unmodified search:

| λ | found depth | score | reference | evaluations |
|---|---:|---:|---:|---:|
| 0.5 | 0.21 | 104.134 | 115.743 | 46 |
| 1 | 0.11 | **114.404** | 111.762 | 56 |
| 2 | 0.10 | **103.798** | 103.798 | 55 |

Held in that basin the existing depth refinement lands **exactly on the reference at λ=2 and beats it
at λ=1**, in under 60 evaluations. Nothing is wrong with the move set, the step floor, or the number
of starts. **The search cannot get into the basin because no coarse point is in it.**

**Status: stopped, per the standing decision rule — the coarse lattice was NOT modified.** No threshold
relaxed, no budget raised, no coarse level raised, no compound moves, no randomness, no
EggCrate-specific logic. The measurement now points at one thing and it is the lattice: three levels
spanning 0.05–2.00 m sample a range whose useful part is the bottom 13 %. Overhang, Louvres and Fins
survive that only because their response is smooth enough for a depth descent to walk in from outside;
EggCrate's is not. That is the next thing to measure, and it is a change to how the lattice **spans a
bounded range**, not to the move set or the start count.

### 2.11 Axis-local scale refinement — **EggCrate solved, VerticalFins broken, gate still fails**

The §2.10 conclusion said the coarse lattice was to blame. One mechanism was tested before touching it:
the refinement's **shared** step schedule. Diagnostic first, no product change — from the coarse start
`(Depth 0.05, LouvreCount 3, FinCount 1)`, holding the counts, every depth the halving schedule can
reach was scored:

| scale | snapped depth | λ=0.5 | λ=1 | λ=2 | beats Depth 0.05? |
|---:|---:|---:|---:|---:|---|
| 0.4875 | 0.54 | −342.608 | −593.350 | −1094.833 | no |
| 0.24375 | 0.29 | 50.403 | −17.016 | −151.856 | **yes / yes** / no |
| 0.121875 | 0.17 | 88.624 | 69.433 | 31.049 | **yes** |
| 0.060938 | 0.11 | 120.935 | 114.404 | 101.341 | **yes** |
| 0.030469 | 0.08 | 86.643 | 85.141 | 82.138 | **yes** |
| 0.01 | 0.06 | 1.099 | 0.722 | −0.032 | **yes** |

The downward probes all clamp to the bound and snap back. So a beneficial depth move **does** exist
from that start — at the *second* scale for λ = 0.5 and 1, the *third* for λ = 2. The first probe fails
and that was enough: under a shared schedule no axis is reduced until *every* axis has failed, so the
count axis moved first and the descent left the basin before depth was ever probed that finely.

**The change.** When an axis fails at its current ± step, reduce **that axis** and probe it again, down
to its own `MinimumIncrement`, before moving on to the next parameter. Deterministic parameter order,
first-improvement within an axis, no compound moves, no randomness, no family-specific logic; bounds,
`Snap`, `IsBetter`, NO SHADE, `coarseLevels = 3`, `maximumEvaluations = 400` and the thresholds all
unchanged.

| family | λ | optimiser | enumerated best | gap % | evals | starts | stopped because | optimiser parameters | enumerated parameters |
|---|---:|---:|---:|---:|---:|---:|---|---|---|
| Overhang | 0.5 | 154.960 | 133.974 | −15.66 | 400 | 10 / 28 | **budget** | Depth 0.54, Rise 0.11, Ext 0.02 | Depth 0.35, Rise 0, Ext 0 |
| Overhang | 1 | 135.072 | 122.680 | −10.10 | 400 | 9 / 28 | **budget** | Depth 0.61, Rise 0.18, Ext 0.05 | Depth 0.65, Rise 0.25, Ext 0 |
| Overhang | 2 | 131.766 | 121.617 | −8.35 | 400 | 9 / 28 | **budget** | Depth 0.58, Rise 0.19, Ext 0.05 | Depth 0.65, Rise 0.25, Ext 0 |
| HorizontalLouvres | 0.5 | 155.044 | 148.675 | −4.28 | 332 | 28 / 28 | all starts | Depth 0.54, Count 1, Tilt −10 | Depth 0.6, Count 1, Tilt −15 |
| HorizontalLouvres | 1 | 153.962 | 146.381 | −5.18 | 326 | 28 / 28 | all starts | Depth 0.09, Count 3, Tilt 45 | Depth 0.1, Count 3, Tilt 45 |
| HorizontalLouvres | 2 | 153.962 | 146.381 | −5.18 | 288 | 28 / 28 | all starts | Depth 0.09, Count 3, Tilt 45 | Depth 0.1, Count 3, Tilt 45 |
| VerticalFins | 0.5 | 8.711 | 5.071 | −71.78 | 387 | 28 / 28 | all starts | Depth 0.08, Count 5, Tilt 5 | Depth 0.05, Count 5, Tilt 15 |
| VerticalFins | 1 | 4.733 | 5.071 | **+6.66** | 286 | 28 / 28 | all starts | Depth 0.08, **Count 1**, Tilt 5 | Depth 0.05, **Count 5**, Tilt 15 |
| VerticalFins | 2 | 3.634 | 5.071 | **+28.34** | 290 | 28 / 28 | all starts | Depth 0.05, **Count 1**, Tilt 15 | Depth 0.05, **Count 5**, Tilt 15 |
| EggCrate | 0.5 | 104.134 | 115.743 | 10.03 | 154 | 28 / 28 | all starts | Depth 0.21, **LouvreCount 3**, FinCount 1 | Depth 0.1, LouvreCount 3, FinCount 1 |
| EggCrate | 1 | 114.404 | 111.762 | **−2.36** | 159 | 28 / 28 | all starts | Depth 0.11, **LouvreCount 3**, FinCount 1 | Depth 0.1, LouvreCount 3, FinCount 1 |
| EggCrate | 2 | 103.798 | 103.798 | **0** | 157 | 28 / 28 | all starts | Depth 0.1, **LouvreCount 3**, FinCount 1 | Depth 0.1, LouvreCount 3, FinCount 1 |

| | step floor (§2.9) | budget-aware (§2.10) | axis-local | gate |
|---|---:|---:|---:|---|
| mean gap | 4.803 % | 3.854 % | **−6.489 %** | < 3 % — passes, but see below |
| worst gap | 38.53 % | 38.53 % | **28.34 %** | < 10 % — **still fails** |
| matched or beat | 9 / 12 | 9 / 12 | 9 / 12 | — |
| evaluations | 67–161 | 112–306 | 154–400 | ≤ 400 budget |

**EggCrate is solved, and the hypothesis was exactly right.** The `(0.05, 3, 1)` start now holds
`LouvreCount = 3` long enough for depth to reach the useful band at every weight: the returned vectors
are `Depth 0.21 / 0.11 / 0.10` with `LouvreCount 3`, all inside 0.08–0.30. λ=2 lands **exactly** on the
enumerated reference, λ=1 **beats** it, and the worst EggCrate cell fell from 38.53 % to 10.03 %. The
41 % failure that has driven this whole sequence was never the coarse lattice and never the move set —
it was axes sharing one scale.

**But the gate still fails, and now for a new reason.** `VerticalFins` regressed from landing exactly
on the enumerated best at every λ to **+6.66 % and +28.34 %**, returning `Count 1` where the reference
wants `Count 5`. The same mechanism causes it: depth is now refined to its finest scale before the
count axis is visited at all, so the descent commits to a depth that suits a single fin and the count
never recovers. Axis-local scale trades *which* axis gets abandoned; it does not stop abandonment.

Overhang also regressed slightly (λ=1 from −14.48 % to −10.10 %) and for a second reason: the finer
per-axis probing **exhausts the 400-evaluation budget** on all three Overhang cells, which now refine
only **9–10 of 28** starts where §2.10 refined all 28. That is the budget-aware start behaviour working
as designed — but it means Overhang is now paying for EggCrate's fix.

**The mean of −6.489 % must not be read as a pass.** It is dominated by `VerticalFins λ=0.5` at
−71.78 %, where the enumerated best is only 5.071, so a 3.6-point absolute difference becomes a
71.8 % relative one. On a fixture where one family's objective is two orders of magnitude smaller than
another's, the mean of relative gaps is not a meaningful summary. **The worst-case threshold is the one
that matters and it fails at 28.34 %.**

**Status: stopped and reported, per the standing decision rule** — EggCrate materially improved, so the
change was not reverted, but no further optimiser change was made. Nothing was relaxed, no budget or
coarse level raised, no compound moves, no randomness. The open question is no longer "why can't the
search reach EggCrate's basin" but **"how should a compass search order axis refinement so that
resolving one axis finely does not strand another"** — and, separately, whether 400 evaluations is
still the right budget now that a single descent can consume far more of it.

### 2.12 Balanced multiscale poll — **Gate 7 passes**, with two caveats that must be settled first

§2.11 left the search repairing one family at another's expense, because probing an axis and moving
immediately makes the answer depend on the typology's declaration order. This removes the ordering
commitment: **the incumbent is held fixed while every axis is examined.** Each axis is probed both ways
at every distinct scale on its own ladder — its normal initial compass step halved down to
`MinimumIncrement` — and reports the best improving candidate it can offer. Only when all axes have
reported does the incumbent move, once, to the globally best offer under the existing `IsBetter` order.
Every ladder then restarts from the new incumbent. If no axis can offer anything, the start has
converged.

Unchanged: `coarseLevels = 3`, `maximumEvaluations = 400`, the deterministic coarse ranking, the
budget-aware multi-start from §2.10, `MinimumIncrement`, `Snap`, `IsBetter`, NO SHADE, bounds and
grid constraints. No randomness, no compound moves, no coarse-sampling change, no family-specific
logic.

**The poll chooses on merit, not order** — checked before running the gate, from a common incumbent:

| family | incumbent (λ=2) | axis 1 | axis 2 | axis 3 | winner |
|---|---|---|---|---|---|
| EggCrate | 0.05, 3, 1 → −20.308 | Depth 0.11, **+121.649** | LouvreCount 2, +5.034 | none | **axis 1** on a 24× margin |
| VerticalFins | 0.05, 1, −60 → −2.060 | none | none | Tilt −30, +2.500 | **axis 3 — not the first declared** |
| VerticalFins | 1.02, 3, 0 → −1304.693 | Depth 0.53, **+638.665** | Count 2, +548.970 | Tilt 10, +45.056 | **axis 1** on merit |

The second row is the proof that declaration order decides nothing: the last-declared axis wins. The
first row is the mechanism that fixes EggCrate — the misleading `LouvreCount 3 → 2` move is still
found, still an improvement, and now simply **loses** to a depth move worth 24 times more.

| family | λ | optimiser | enumerated best | gap % | evals | starts | stopped because | optimiser parameters | enumerated parameters |
|---|---:|---:|---:|---:|---:|---:|---|---|---|
| Overhang | 0.5 | 150.767 | 133.974 | −12.53 | 400 | 3 / 28 | budget | Depth 0.51, Rise 0.06, Ext 0.03 | Depth 0.35, Rise 0, Ext 0 |
| Overhang | 1 | 141.499 | 122.680 | −15.34 | 400 | 3 / 28 | budget | Depth 0.55, Rise 0.14, Ext 0.04 | Depth 0.65, Rise 0.25, Ext 0 |
| Overhang | 2 | 128.506 | 121.617 | −5.66 | 400 | 3 / 28 | budget | Depth 0.61, Rise 0.23, Ext 0.06 | Depth 0.65, Rise 0.25, Ext 0 |
| HorizontalLouvres | 0.5 | 156.096 | 148.675 | −4.99 | 400 | 7 / 28 | budget | Depth 0.22, Count 3, Tilt 0 | Depth 0.6, Count 1, Tilt −15 |
| HorizontalLouvres | 1 | 153.734 | 146.381 | −5.02 | 400 | 7 / 28 | budget | Depth 0.21, Count 3, Tilt 0 | Depth 0.1, Count 3, Tilt 45 |
| HorizontalLouvres | 2 | 148.444 | 146.381 | −1.41 | 400 | 5 / 28 | budget | Depth 0.24, Count 3, Tilt −5 | Depth 0.1, Count 3, Tilt 45 |
| VerticalFins | 0.5 | 8.711 | 5.071 | −71.78 | 400 | 11 / 28 | budget | Depth 0.08, **Count 5**, Tilt 5 | Depth 0.05, Count 5, Tilt 15 |
| VerticalFins | 1 | 5.071 | 5.071 | **0** | 400 | 14 / 28 | budget | Depth 0.05, **Count 5**, Tilt 15 | Depth 0.05, Count 5, Tilt 15 |
| VerticalFins | 2 | 5.071 | 5.071 | **0** | 400 | 15 / 28 | budget | Depth 0.05, **Count 5**, Tilt 15 | Depth 0.05, Count 5, Tilt 15 |
| EggCrate | 0.5 | 120.935 | 115.743 | −4.49 | 400 | 15 / 28 | budget | Depth 0.11, **LouvreCount 3**, FinCount 1 | Depth 0.1, LouvreCount 3, FinCount 1 |
| EggCrate | 1 | 114.404 | 111.762 | −2.36 | 400 | 16 / 28 | budget | Depth 0.11, **LouvreCount 3**, FinCount 1 | Depth 0.1, LouvreCount 3, FinCount 1 |
| EggCrate | 2 | 103.798 | 103.798 | **0** | 400 | 18 / 28 | budget | Depth 0.1, **LouvreCount 3**, FinCount 1 | Depth 0.1, LouvreCount 3, FinCount 1 |

| | §2.9 | §2.10 | §2.11 | balanced poll | gate |
|---|---:|---:|---:|---:|---|
| worst gap | 38.53 % | 38.53 % | 28.34 % | **0 %** | < 10 % — **passes** |
| signed mean | 4.803 % | 3.854 % | −6.489 % | **−10.30 %** | < 3 % — passes |
| **mean positive shortfall** | — | — | — | **0.000 %** | diagnostic only |
| matched or beat | 9 / 12 | 9 / 12 | 9 / 12 | **12 / 12** | — |
| evaluations | 67–161 | 112–306 | 154–400 | **400 (all)** | ≤ 400 budget |

**Both families are repaired at once, which is what §2.11 could not do.** EggCrate holds
`LouvreCount = 3` while depth reaches the useful band at every λ (0.11 / 0.11 / 0.10), and
`VerticalFins` reaches `Count = 5` at every λ rather than being stranded at 1. **No case falls short of
the enumeration anywhere** — the mean positive shortfall is exactly zero, which is the honest summary
the signed mean cannot give: −10.30 % is still dominated by `VerticalFins λ=0.5`, where the enumerated
best is only 5.071.

**Caveat 1 — the budget is now the binding constraint in every single case.** All twelve exhaust 400
evaluations; none converges. A poll costs one probe per distinct snapped offset on every axis:

| family | Depth | Count(s) | Tilt | probes per poll | polls before 400 is gone |
|---|---:|---:|---:|---:|---:|
| Overhang | 14 | — | — (Rise 12, Ext 12) | **38** | 10.5 |
| HorizontalLouvres | 14 | 2 | 8 | 24 | 16.7 |
| VerticalFins | 14 | 2 | 8 | 24 | 16.7 |
| EggCrate | 14 | 2 + 2 | — | 18 | 22.2 |

That is where the budget goes, and it explains the start counts exactly: Overhang's 38-probe poll
leaves room for about ten polls in total, so only **3 of 28** starts are refined, while EggCrate's
18-probe poll affords 22 and reaches **18 of 28**. The depth ladder alone is 14 probes because a range
of ~2 m over a 10 mm lattice needs 7 halvings. **The budget was not raised** — but it is no longer
slack, and the previous rounds' finding that the search left most of it unspent is now reversed.

**Caveat 2 — a regression test's contract was wrong, and has been corrected (test-only).**
`The_Search_Result_Is_Locally_Best_Along_Every_Free_Axis` began failing here: Overhang returned
`Depth 0.61` when `0.60` scores 129.205 against 128.506.

The cause was confirmed by diagnostic before anything was touched. Re-running that exact case on the
unmodified search with only the caller's evaluation cap raised to 2000:

| | production cap 400 | diagnostic cap 2000 |
|---|---|---|
| result | Depth 0.61, Rise 0.23, Ext 0.06 | Depth 0.43, Rise 0.12, Ext 0.01 |
| score | 128.506 | **138.368** |
| starts refined | 3 / 28 | 20 / 28 |
| every ±increment neighbour worse? | **no** — Depth 0.60 scores 129.205 | **yes, on all three axes** |

So the failure was **budget truncation, not a search defect**: the same code, given room, returns a
point with no improving neighbour on any axis.

**The test asserted something the algorithm never promised.** A bounded search that stops on
`EvaluationBudgetExhausted` returns *the best point found so far* by definition — the incumbent of a
descent still in progress, whose neighbours are unexamined. The old test ran the product configuration
and asserted local optimality of whatever came back, which tests the budget rather than the algorithm.

Merely conditioning on the reported termination would have been too weak in the other direction: the
budget bounds the **multi-start loop**, so the flag can read `EvaluationBudgetExhausted` while the
descent that produced the winner finished cleanly — and at the product budget every family reports it,
so the assertion would never run at all.

The invariant is therefore tested where it is genuinely guaranteed: **one compass descent, given enough
budget to finish, with completion asserted first** (`refinementStarts = 1`, cap 5000, and
`Termination != EvaluationBudgetExhausted` asserted before any neighbour is examined). Both are ordinary
caller arguments; **no production behaviour changed and the defaults remain 400 and budget-bounded**.
All four families converge in 78–146 evaluations, terminating `StepBelowGranularity`.

This is a correction, not a relaxation, and it was verified as such rather than argued: with the
`MinimumIncrement` floor temporarily removed from the ladder — the pre-`e027ada` defect, reinjected —
**the revised test still fails**, catching EggCrate returning `Depth 0.11` where `0.10` scores 63.807
against 60.95, on a descent that reported itself converged. The test is also *stricter* than before in
one respect: it can no longer pass silently on a truncated result, because completion is now asserted.

**Status: Gate 7 passes on its unchanged thresholds, and optimiser development is stopped as directed.**
Nothing else was altered — no threshold, no budget, no coarse sampling, no optimiser change, no CI.
Caveat 1 stands open: every production case still exhausts 400 evaluations, and the diagnostic above
shows that costs measured quality (Overhang λ=2 would go from −5.66 % to −13.77 % against the
enumerated best with more budget). **The budget was not raised.**

### 2.13 Poll cost — two regressions found by the **full suite**, not by Gate 7

Gate 7 passed at `a305787`, but the first complete-suite run afterwards failed two tests that Gate 7's
protocol never exercises. Both are real, and both have the same cause.

| test | symptom |
|---|---|
| `Case7_On_A_One_Dimensional_Problem_The_Optimiser_Finds_The_Enumerated_Optimum` | `30 evaluations against 30 lattice points` |
| `PenaltyDomainTests.A_Stronger_Wanted_Penalty_Buys_A_Design_That_Keeps_More_Wanted_Solar` | λ=2 destroyed **more** wanted solar than λ=0.5 |

**Cause: the poll's per-iteration cost.** §2.12 measured it — 18 to 38 distinct evaluations per poll,
because every axis was probed at *every* scale on its ladder. Gate 7 runs one regime: four families,
three free axes each, a 400-evaluation budget. The two failures live in regimes it never visits.

- Case 7 pins everything except `Depth` over a 30-point lattice. With whole-ladder polling across
  several starts the search touched all 30 points — it stopped earning its place against brute force,
  which is exactly what that test exists to guarantee.
- The penalty test calls the optimiser with `maximumEvaluations: 60`. The coarse lattice alone is 27
  points, so barely one poll remained and the result was little more than the coarse winner, which
  carries no obligation to respect the λ ordering.

**The correction, one line of control flow: the first productive scale decides an axis.** Descending
*past failing* scales is the part that mattered — it is what let an axis whose useful move is far finer
than its first step be seen at all, and what stopped a misleading coarse move on another axis from
committing the descent. Continuing *past a scale that already worked* bought a marginally better move
for a multiple of the cost. **Ordering independence is untouched**, because it comes from judging every
axis from the same incumbent and taking the global best, not from exhausting each ladder.

Nothing else changed: no threshold, no budget, no coarse sampling, no `MinimumIncrement`, no `Snap`,
no `IsBetter`, no NO SHADE, no randomness, no compound moves, no family-specific logic.

| family | λ | optimiser | enumerated best | gap % | evals | starts | stopped because |
|---|---:|---:|---:|---:|---:|---:|---|
| Overhang | 0.5 | 154.960 | 133.974 | −15.66 | 400 | 3 / 28 | budget |
| Overhang | 1 | 141.499 | 122.680 | −15.34 | 400 | 3 / 28 | budget |
| Overhang | 2 | 132.689 | 121.617 | **−9.10** | 400 | 4 / 28 | budget |
| HorizontalLouvres | 0.5 | 156.096 | 148.675 | −4.99 | 400 | 20 / 28 | budget |
| HorizontalLouvres | 1 | 153.962 | 146.381 | −5.18 | 400 | 10 / 28 | budget |
| HorizontalLouvres | 2 | 153.962 | 146.381 | −5.18 | 400 | 11 / 28 | budget |
| VerticalFins | 0.5 | 8.711 | 5.071 | −71.78 | **355** | **28 / 28** | **all starts** |
| VerticalFins | 1 | 5.071 | 5.071 | 0 | **335** | **28 / 28** | **all starts** |
| VerticalFins | 2 | 5.071 | 5.071 | 0 | **325** | **28 / 28** | **all starts** |
| EggCrate | 0.5 | 120.935 | 115.743 | −4.49 | **254** | **28 / 28** | **all starts** |
| EggCrate | 1 | 114.404 | 111.762 | −2.36 | **243** | **28 / 28** | **all starts** |
| EggCrate | 2 | 103.798 | 103.798 | 0 | **227** | **28 / 28** | **all starts** |

| | whole-ladder poll (§2.12) | first-productive-scale | gate |
|---|---:|---:|---|
| worst gap | 0 % | **0 %** | < 10 % — passes |
| mean positive shortfall | 0.000 % | **0.000 %** | diagnostic |
| signed mean | −10.30 % | **−11.174 %** | < 3 % — passes |
| matched or beat | 12 / 12 | **12 / 12** | — |
| cases exhausting the budget | 12 of 12 | **6 of 12** | ≤ 400 |

**Gate 7 did not merely survive the fix — it improved**, and the parameter vectors that mattered are
unchanged: EggCrate holds `LouvreCount 3` at every λ, VerticalFins holds `Count 5` at every λ.
Caveat 1 from §2.12 is now **half closed**: VerticalFins and EggCrate genuinely converge on all 28
starts instead of being cut off, and Overhang λ=2 improved from −5.66 % to −9.10 %. Overhang and
HorizontalLouvres still exhaust the budget, so the caveat stands for them and the budget was still
**not raised**.

**The lesson is about the validation protocol, not the algorithm.** Four optimiser experiments were
each measured against Gate 7 alone, and Gate 7 alone was not sufficient: it exercises one budget, one
axis count and one fixture. The two regimes that broke — a one-dimensional problem and a 60-evaluation
budget — were both already covered by the suite, and only a full run surfaced them. **A gate is not a
substitute for the suite.**

### 2.14 Real-project regression fixture — the Kołobrzeg office

The Kołobrzeg office model that produced the PR 2 resolution evidence is now a committed
**validation/regression fixture** (`SAM_SolarCalculator.Tests/Fixtures/KolobrzegOffice.sam`), run
through the same production entry points Grasshopper uses (`ApertureSolarTargets`,
`ApertureShadingSetup`, `Optimise.ShadingDevice`). It is a **real exported project** — three
WSW-facing apertures (0.90 × 2.25 m, 1.20 × 1.39 m, 0.60 × 1.39 m) with embedded Kołobrzeg weather
— not a synthetic scenario.

`KolobrzegRegressionTests` preserves the engineering conclusions, not incidental optimiser
decimals:

- **Fixture integrity** — the fixture loads, carries the three studied aperture GUIDs with their
  expected geometry, and reproduces the historical 10 / 9 / 6-cell pattern at the 0.5 m default.
- **The coarse-grid sampling artefact** — at 0.5 m the smallest aperture recommends a thin single
  vertical fin claiming ~88 % blocked; the same physical device re-measured at 0.25 m and 0.2 m
  blocks ~0 %, and at 0.1 m still only ~18 %.
- **Family/decision stability** — across 0.5 / 0.25 / 0.2 m the small aperture's winner family
  differs at the coarse grid and stabilises at HorizontalLouvres from the design-grade 0.25 m; the
  mid and tall apertures stay HorizontalLouvres throughout. Exact tall-aperture geometry is
  deliberately not asserted: the study showed it keeps moving with resolution.
- **PR 2 guidance and cap warning, natural cases** — the 0.5 m analysis is flagged as coarser than
  the 0.25 m set recommendation (guidance only: nothing is changed), and the coarse winner rides
  the grid-narrowed count maximum, triggering the resolution-cap warning; fine-grid winners carry
  none.

**This fixture must not be used to tune algorithms specifically to Kołobrzeg.** It exists to
validate behaviour and to keep the resolution findings enforced; it is one project, and any
optimiser, grid-rule or guidance change must stand on its own evidence, not on making these tests
pass.

### 2.15 What remains **not** validated

Stated plainly, because it bounds what may be claimed:

- **Diffuse and ground-reflected transposition have no independent reference** (Gate 1, "not
  covered"). The Perez implementation is checked for internal consistency and against analytic view
  factors, not against another tool's tilted-surface diffuse.
- **No full-Radiance annual run** for any typology.
- **The desirability weighting is unvalidated against thermal outcome** — necessarily, since there is
  no load model (§3, A1). Optimised devices are *best found within a bounded deterministic search*
  against the stated proxy, not against energy.
- **Debug configuration is never built.** CI builds `Release` only, so Debug-only failures
  (assertions, `#if DEBUG` paths, different overflow behaviour) would not be caught.

Everything the suite asserts — the Stage-11 gates, the SAM-vs-TAS regression, the convergence
studies, the optimiser regressions — is now executed: the FAST half on every PR, the LONG half on
the integration triggers and locally (§0 and §7).

---

## 3. Assumptions register

The register an engineer needs to decide fitness for purpose. **Direction** states which way the
result is biased: *under* = the tool reports less than reality, *over* = more.

### 3.1 Physics not modelled

| # | Assumption | Magnitude | Direction | Where it bites |
|---|---|---|---|---|
| A1 | **No thermal load model.** "Unwanted sun" is a season/temperature/irradiance proxy, not cooling-minus-heating load as in Shaderade. | Unquantified — depends entirely on the proxy chosen | Either | The generated shape. This is the largest single modelling assumption in the workflow. |
| A2 | **No inter-reflection.** Context blocks, never bounces. No shade material reflectance, no secondary rays. | Unquantified; largest in dense urban canyons and near high-albedo surfaces | **Under** (missing bounced gain) | Absolute irradiance. Intercepted solar must **never** be called "reflected". |
| A3 | **No glazing transmittance or SHGC.** Direct solar is evaluated at the aperture *plane*. | A typical double-glazed unit transmits ~0.5–0.7 of incident at normal incidence, less off-normal | **Over**, substantially, if read as transmitted gain | Any figure quoted as solar *entering the space*. These are **incident** figures. |
| A4 | **Diffuse is not in the shading objective.** Desirability is direct-beam only. | Unquantified | **Under**-values deep devices, which also cut diffuse | Optimiser output — it may select a shallower device than a total-irradiance objective would. |
| A5 | **Isotropic ground reflection**, cosine-weighted patch sum. | Small on vertical façades | Either | Ground-reflected component only. |

### 3.2 Discretisation

| # | Assumption | Magnitude | Direction | Where it bites |
|---|---|---|---|---|
| A6 | **Sun-group quantisation** at `sunAngleStep` 2°. | **1.54 % MAE** measured (§2.2) | Roughly unbiased — the representative direction is the irradiance-weighted mean of member hours | All results. Reduce to 1° for final runs (0.0125 MAE). |
| A7 | **Analysis grid** at `gridSize`, default 0.5 m. | Device fineness is capped by it | **Under**-attributes elements narrower than `gridSize` | Per-element contribution split. Aggregate totals stay correct. |
| A8 | **Voxel size** in the potential field, 0.05–0.25 m in tests. | Caller-set | Either | Ideal-shape resolution. |
| A9 | **Hourly time resolution only.** `Timestep != 1` throws rather than being silently ignored. | Up to ±30 min mis-timing of a shading cutoff | Either | Cutoff-time-sensitive results, e.g. a device sized to a specific hour. |
| A10 | **`minHorizonAngle` 2° gate** skips near-horizon hours whole, including their diffuse and ground-reflected energy. | **0.19 % of annual GHI** measured on ModelB | **Under** | Annual totals. Negligible. |
| A11 | **Tregenza-145 quadrature** for view factors. | ~0.5 % vs analytic; can slightly exceed 1.0 for an unobstructed up-facing cell | Either | SVF/GVF, hence diffuse. |

### 3.3 Scope and geometry

| # | Assumption | Magnitude | Direction | Where it bites |
|---|---|---|---|---|
| A12 | **Each aperture optimised independently** — a device does not shade its neighbour. | Grows with device depth and aperture density | **Under**-estimates shading in dense arrays | Façades of closely spaced windows with deep devices. |
| A13 | **Interior partitions excluded from context.** A neighbouring building must be modelled as shade panels. Apertures on two-space panels are rejected even when explicitly selected. | Total, where it applies | **Over**-estimates irradiance if context was expected to be picked up automatically | Models relying on adjacent-building geometry being inferred. |
| A14 | **Four device families.** No light shelves, external roller blinds, or operable/seasonal devices. | — | — | Applicability. |
| A15 | **Perforated / translucent screens unsupported** — the ray engine is binary. | — | — | Applicability. |
| A16 | **Single scalar objective.** No Pareto front; λ and μ chosen up front. | — | — | Optimisation. Trade-offs are fixed before the search, not explored after. |
| A17 | **Local optimality on the search lattice.** The balanced multiscale poll (§2.12) closes the measured gap: **Gate 7 passes on its unchanged thresholds**, worst 0 %, mean positive shortfall 0 %, 12 of 12 matched or beat. Poll cost was then corrected in §2.13 after the full suite caught two regressions Gate 7 could not see; the gate improved again. | **Measured: no case falls short of the enumerated best** (§2.13; was 41.3 % single- and multi-start, 38.5 % with the step floor, 28.3 % with axis-local scale) | **Under, but no longer measurable on this fixture** | The residual risk is now *unquantified rather than large*: the enumeration is a coarser lattice, so beating it everywhere is a lower bound being cleared, **not a proof of global optimality**. One open item: Overhang and HorizontalLouvres still exhaust the 400-evaluation budget (3–20 of 28 starts refined) and §2.12's diagnostic shows that costs measured quality; VerticalFins and EggCrate now converge on all 28 starts (§2.13). The local-optimality regression was re-pinned at the level where the invariant actually holds — a **completed** descent — after measurement confirmed the failure was budget truncation, not a search defect. **"Optimal" still means best found within a bounded deterministic search, never mathematically proven-global**, and that wording must not soften because the gate went green. |
| A18 | **`MaterialFraction` is area only** — no thickness, weight, fixing or cost. | — | **Under**-states real buildability cost | Any material/cost trade-off. |
| A19 | **A valid, resolvable TimeZone is required.** | — | — | Fails loudly, by design. |
| A20 | **The ideal shape's mesh is display geometry**, not performance truth. | — | — | Anyone measuring off the mesh instead of running `VerifyShading`. |

---

## 4. Fitness for purpose

**Reasonable to use for:**

- Comparing shading options against each other on the same model — relative ranking is far more robust
  than absolute magnitude, and most assumptions above bias all options in the same direction.
- Sizing external shading geometry, where the profile-angle physics dominates.
- Identifying which façades and apertures need shading attention.
- Per-element contribution reporting, as **direct contribution** — with A7 in mind.

**Not currently defensible for:**

- Quoting absolute solar gain entering a space (A3 — these are incident, not transmitted).
- Energy or carbon figures (A1 — no load model).
- Overheating compliance (TM52/TM59) or any assessment needing thermal outcome.
- Daylight, glare (DGP) or visual comfort.
- Dense arrays of deep devices without checking A12 by hand.

**The two that most often get misread**, worth stating to anyone receiving output: incident is not
transmitted (A3), and direct contribution is not removal-loss (per §2.5 of the plan — each blocked ray
has exactly one first hit, so contributions sum to the system total by construction, but removing an
overhang may simply expose the fin behind it).

---

## 5. Recommended next validation work

The former item 1 — add a test step to CI — is **done**: `build.yml` runs the FAST half on every PR
and `long-running-tests.yml` runs the LONG half on the integration triggers (§0). What remains, in
priority order, with the reasoning:

1. **An independent reference for diffuse transposition** — the one gap Gate 1 explicitly does not
   cover. Ladybug's Python API does not expose Perez tilted-surface transposition, so this needs a
   different reference (a Radiance `gendaymtx` run, or published Perez validation data).
2. **Record observed results, not just thresholds.** The gates assert bounds (e.g. sun-position MAE
   < 0.25°). Emitting the achieved values to test output, and pasting them into §2, turns a pass/fail
   into a trend that can be watched for drift.
3. **Build Debug as well as Release** in CI, so Debug-only failures are caught.
4. **Quantify A12** — run two adjacent apertures with deep devices independently and together, and
   record the difference. It is the one scope limitation likely to bite on a real façade.
5. **Quantify A2** on an urban-canyon fixture, even roughly, so the largest unquantified physics
   assumption has an order of magnitude.
6. **A3 worked example** — the same aperture reported as incident and as transmitted through a stated
   construction, so the factor is concrete for anyone reading a result.
7. **Replace the wall-clock assertion in `AttributionPruningTests.Pruning_Actually_Saves_Work_Where_There_Is_Work_To_Save`.**
   It asserts `pruned < complete` on elapsed time and flaked on shared CI (complete 65.9 ms vs pruned
   70.9 ms — a few milliseconds of noise). Replace or remove the timing comparison; retain a
   deterministic structural work-reduction assertion instead (e.g. cells/sun-groups actually traced
   on each path), so the saving is proven by what is counted rather than by what a stopwatch says.

---

## 6. Deferred capabilities

Recorded so they are not mistaken for oversights:

- **Stage 8.1 — internal solar penetration** (direct solar reaching floors/walls, and the reduction
  due to shading). Deferred, out of Phase-1 scope. The Stage 8 attribution structures are designed so
  this is **additive** — a second attribution cache over the interior geometry set — rather than a
  redesign. When built, geometric penetration should stay the fundamental quantity, with transmitted
  solar added as a separate construction-aware figure rather than silently replacing it.
- **Glazing angular transmittance / SHGC** (A3).
- **Marginal contribution** — per-element performance recomputed with one element removed, the honest
  measure where elements overlap.
- **Multi-objective optimisation** with a Pareto front (A16).
- **Inter-reflection** (A2), glare, daylight autonomy, thermal comfort, energy demand, BIPV yield.

---

## 7. Running the suite

The suite is split on the `Category` trait, with complementary filters so the two halves between
them run every test (271 total; see §0 for the partition). Both commands assume the Release
binaries already exist from the solution build (`--no-build`); drop that flag to have `dotnet test`
rebuild first.

FAST — the required PR half (248 tests), the one `build.yml` runs on every PR:

```bash
dotnet test SAM_SolarCalculator.Tests/SAM.SolarCalculator.Tests.csproj --no-build -c Release --filter "Category!=LongRunning"
```

LONG — the expensive half (23 tests: the exhaustive Gate-7 enumeration, convergence sweeps,
expensive reference comparisons), run separately via `long-running-tests.yml`. Its configured
triggers are push to `master`/`sow/**`, `workflow_dispatch` and the nightly schedule — but
`workflow_dispatch` and the schedule fire from the repository **default** branch (`master`), so
they become available only once the workflow also exists there. Until then, only push triggers on
branches that carry the workflow (such as `sow/2026-Q3` after this PR merges) can run it:

```bash
dotnet test SAM_SolarCalculator.Tests/SAM.SolarCalculator.Tests.csproj --no-build -c Release --filter "Category=LongRunning"
```

For this Phase-1 readiness review the LONG half was verified green **locally** (23 / 23). It has
not run on GitHub for this branch, and no GitHub LONG evidence is claimed — the first GitHub LONG
run for this Phase-1 integration will come from the `sow/2026-Q3` push trigger after merge. The
full suite can also be run in one local pass:

```bash
dotnet test SAM_SolarCalculator.Tests/SAM.SolarCalculator.Tests.csproj --no-build -c Release
```

Green means the validated behaviour still holds. A green FAST check on a PR means the FAST half
passed on GitHub; a green local run of both halves means the whole suite holds locally.

**A trap when reproducing the SPDX check locally:** it greps for the copyright line with the character
class `[-–]`, and the repo's headers use an en-dash. Under an unset or `C` locale that class is
byte-oriented and cannot match the three-byte UTF-8 en-dash, so **every correctly-headered file
appears to fail**. Run it under a UTF-8 locale, or the output is noise.

# Stage 10 implementation — the Grasshopper workflow

Companion to `Stages0-4-Method.md`, `Stages5-8-Method.md` and `Stage9-Method.md`. Those describe the
physics. This describes what an engineer sees on the canvas, what each input does, and which
behaviours are guaranteed.

Stage 10 adds **no physics**. Every number it shows is produced by the Stage 0–9 engine, unchanged.

> **Stage 10.1** (this revision) is the result of the first real manual test. It fixes a defect that
> made the whole shading chain unusable on any model with more than one window, separates "no shading
> is worth building" from "nothing could be measured", gives every device the identity of the window
> it was designed for, and rewrites the parts of the interface that were misread. §15 records what
> the manual test found and what changed.

---

## 1. The chain

The main line is five nodes. Read it left to right; it is the whole workflow.

```
AnalyticalModel
      │
      ▼
SAMAnalytical.ApertureSolarTargets        which windows, which way they face
      │
      ▼
SAMAnalytical.ShadingPotentialField       WHERE shading would help, and where it would harm
      │
      ▼
SAMAnalytical.RationaliseShading          WHAT to build
      │
      ▼
SAMAnalytical.VerifyShading               what it ACTUALLY achieves
```

Two branches hang off it. Neither belongs in the main line, and neither is a step you are skipping.

```
SAMAnalytical.ApertureSolarTargets.apertureGuids
      └──► SAMAnalytical.ApertureIrradiance._apertures_   "how much solar reaches this window as it is?"

SAMAnalytical.ShadingPotentialField
      └──► SAMAnalytical.IdealShadingShape       "what does the physics ideally want?"
```

* **ApertureIrradiance** is a diagnostic about the EXISTING window. It answers *how much solar
  arrives*. It is not an input to shading design and is never required by it. It reads apertures as
  SAM Apertures or Guids: wire the `apertureGuids` output of `ApertureSolarTargets` into
  `_apertures_` — never the `apertureSolarTargets` themselves. A target plugged into `_apertures_`
  is ignored with a warning, and the run then falls back to **every** external sun-exposed aperture.
* **IdealShadingShape** is the free-form shape the field would fill if buildability were no object.
  It answers *what the physics wants*. **RationaliseShading answers what can actually be built**, and
  it works from the field directly — it does not take the ideal shape as an input, and wiring one
  into the other would only make the canvas look linear at the cost of implying a dependency that
  does not exist.

`SAMAnalytical.AnalysisPeriod` feeds the hours into any of them.

Category **SAM**, sub-category **Solar**. All seven derive from
`GH_SAMVariableOutputParameterComponent` with **fixed** `ComponentGuid`s.

| component | Guid | version |
|---|---|---|
| `SAMAnalytical.AnalysisPeriod` | `7f3c9a10-…-1101` | 1.0.0 |
| `SAMAnalytical.ApertureSolarTargets` | `7f3c9a10-…-1102` | **1.0.1** |
| `SAMAnalytical.ApertureIrradiance` | `7f3c9a10-…-1103` | 1.0.0 |
| `SAMAnalytical.ShadingPotentialField` | `7f3c9a10-…-1104` | **1.0.1** |
| `SAMAnalytical.IdealShadingShape` | `7f3c9a10-…-1105` | 1.0.0 |
| `SAMAnalytical.RationaliseShading` | `7f3c9a10-…-1106` | **1.0.1** |
| `SAMAnalytical.VerifyShading` | `7f3c9a10-…-1107` | **1.0.1** |

A component placed before a version bump keeps the parameters stored in the file and shows an
advisory. Right-click → the version menu item replaces it with the current one. Guids never change,
so a script never loses its wires to an unknown component.

---

## 2. Several windows at once

This is the ordinary case, and it needs no expertise.

`ApertureSolarTargets` emits a **list** of targets. Every shading-design node downstream
(`ShadingPotentialField`, `IdealShadingShape`, `RationaliseShading`, `VerifyShading`) takes
**one** target.
Grasshopper therefore runs each node once per target and puts each aperture's results in its **own
branch**. You do not need to graft anything, and nothing cross-products: the model is a single item
and is replicated against the list.

What makes that safe rather than merely conventional:

* **a device carries the aperture it was designed for.** `RationaliseShading` emits a `ShadingDevice`,
  not a bare typology. `VerifyShading` compares that aperture against the target on its own wire and
  **refuses to measure** on a mismatch, naming both apertures. Verifying a south device on a north
  window would otherwise produce a number that is arithmetically correct and about the wrong design —
  the worst failure an engineering tool can have, because nothing looks wrong;
* **every result says which window it is about.** `apertureGuid` and `azimuth` are outputs of both
  `RationaliseShading` and `VerifyShading`;
* **ordering is deterministic.** Targets come out in model order and stay in it;
* **nothing is flattened silently.** A device with no identity at all — one built by hand rather than
  by `RationaliseShading` — is accepted, but says in a Remark that it is being taken on trust.

Reading ten windows at once: panel `status` and `designSummary` side by side.

```
270°  OK        Overhang | Depth 2.08 m | 40.4% unwanted blocked | 93.3% wanted retained | 130.3 kWh benefit
180°  OK        Overhang | Depth 1.16 m | 55.0% unwanted blocked | 90.5% wanted retained | 267.6 kWh benefit
 90°  OK        Overhang | Depth 2.27 m | 44.6% unwanted blocked | 91.8% wanted retained | 191.1 kWh benefit
  0°  OK        Overhang | Depth 0.24 m |  2.4% unwanted blocked | n/a wanted retained   |   1.1 kWh benefit
```

`status` is one of **OK**, **NO SHADE**, **WARNING**, **NOT EVALUATED**. Runtime warnings and errors
still appear on the component — the structured status is *additional*, so a batch stays readable
without losing the balloon that makes a problem impossible to miss.

---

## 3. Reading the potential field — red and blue

The single most misread thing in the workflow. The colour is **not** "how much sun is here". It is

> **the value of putting shading material at that location.**

| | meaning |
|---|---|
| | meaning |
|---|---|
| **RED** | **SHADE HERE.** Material here would block solar you asked to block. Stronger red, bigger the positive shading potential. |
| **BLUE** | **KEEP OPEN.** Material here would block solar you asked to KEEP. Stronger blue, bigger the negative shading potential. |
| grey | Neither. Almost no beam passes through, so material here does nothing either way. Near-zero locations are omitted entirely rather than drawn grey. |

So the map is a set of **instructions**, not a heat map: *fill the red, stay out of the blue.*

### The totals are NOT an energy saving *(corrected in Stage 10.2)*

The object string used to read

```
ShadingPotentialField [5400 points, benefit 5504 kWh, jeopardy -22.2 kWh]
```

and it was read during manual testing as "a shade here would save 5504 kWh". **It would not, and nothing
could.** Each voxel independently records the beam that would pass *through* it, and one solar ray passes
through many voxels along its path and is counted at every one. The sum therefore counts the same kWh
over and over. A real device is a thin surface, not a filled volume, and intercepts each ray exactly
once — so what it saves is bounded by the unwanted solar the window admits at all. Measured on the
Stage 9 south fixture: summed positive potential **4586** against an absolute ceiling of **450.9 kWh**
of admitted unwanted solar, a factor of **10.2**.

The word "jeopardy" is gone, and so is the `kWh` label on the totals — the sum is a path-length-weighted
cumulative potential, not an amount of energy anything could save, and labelling it kWh was the specific
thing that made it read as one. A **per-voxel** value genuinely is kWh (the beam a piece of material at
that spot would intercept over the year), so `maxScore` and `minScore` keep their unit. It is only the
sums that lost theirs.

```
ShadingPotentialField [5400 voxels | positive potential 5504 | negative potential 22.2]
```

The raw arrays are unchanged and still available numerically: `UnwantedEnergyPerVoxel`,
`WantedEnergyPerVoxel`, `Score(v, λ)`, `PositiveTotal`, `NegativeTotal`, `MaxScore`, `MinScore`.
Nothing was rounded, rescaled or hidden — only renamed.

Outputs `positiveTotal` / `negativeTotal` are now **`positiveShadingPotential` /
`negativeShadingPotential`**, and the negative one is reported as a positive magnitude so it reads as
a size rather than as something to add to the other. The `legend` output says all of this next to the
geometry:

```
RED   SHADE HERE — material here would block solar you asked to block
BLUE  KEEP OPEN  — material here would block solar you asked to keep
grey  Neither — almost no beam passes through, so material here does nothing

Positive shading potential 412.6   Negative shading potential 38.1   (5400 voxels)

THESE TOTALS ARE NOT ENERGY SAVINGS. Each voxel independently records the beam that
would pass through it, and one ray passes through many voxels, so the totals count the
same solar over and over. They rank locations and compare thresholds; they do not say
what a device would save. For that, build one and read VerifyShading, whose kWh are
measured on real geometry.
```

`_previewMode_` chooses how it is drawn — **Points** (default, cheap), **Voxels** (shaded cells,
easier to read as a volume), **None**. Display only: the numbers are identical whichever you pick.
The voxel mesh is built once and cached, and above 20 000 drawn cells it falls back to points rather
than making the viewport unusable. A preview failure has never been allowed to take the numerical
result down with it, and still is not.

---

## 4. The four numbers a design is judged on

`RationaliseShading` and `VerifyShading` both report many things. These four are the engineering
answer; everything else is supporting evidence.

| output | plain language |
|---|---|
| **Unwanted Solar Blocked [%]** | Of the summer (or whatever you called unwanted) beam this window would have let in, how much the device stops. Higher is better. |
| **Wanted Solar Retained [%]** | Of the winter (or whatever you called wanted) beam, how much still gets through. Higher is better. These two pull against each other; the design is the trade between them. |
| **unwantedSolarIntercepted [kWh]** | The unwanted solar the device stops, as energy. What the device earns. *(called `benefit` before Stage 10.2)* |
| **wantedSolarBlocked [kWh]** | The wanted solar the device destroys, as energy — a positive loss. What the device costs in daylight and free winter heat. *(called `harm` before Stage 10.2)* |

The two energies are now named for the **physical quantity**, not for the role they play in the
objective. "46 kWh benefit" was read during manual testing as "this saves 46 kWh"; it is the unwanted
beam this device intercepts, which is a narrower and checkable claim. `Benefit` and `Harm` remain the
internal names of the objective's terms, where they are correct.

`objectiveScore` is **not** the headline. It folds benefit, harm and material together through
weightings the reader may not have chosen, and two designs a fraction of a percent apart in score can
be physically very different (Stage 9 Case 3: two families within 0.2 % on score with roughly half
the material between them). It remains available as a comparison aid.

`designSummary` puts the story on one line:

```
180° | Overhang | Depth 1.16 m, RiseAboveHead 0.12 m, ExtensionBeyondJambs 0.25 m
    | 55% unwanted blocked | 90.5% wanted retained
    | 267.6 kWh unwanted solar intercepted | 32.4 kWh wanted solar blocked
```

`verificationSummary` does the same for the measured result:

```
180° | Overhang | 1821.2 kWh baseline direct solar | 616.6 kWh intercepted
    | 59.8% unwanted blocked | 91.1% wanted retained | 33.9% direct shading efficiency
```

**Unavailable is not zero.** Where a percentage has no denominator — no unwanted solar in the brief,
no wanted solar, nothing admitted at all — it is `NaN` in the outputs and reads `n/a` in the
summaries. A London north facade genuinely has no wanted winter beam, and reporting "100 % wanted
solar retained" for it would be a fabrication.

### Where the rest of the energy went — the neutral share *(new in Stage 10.2)*

Manual testing on a north window produced

```
61.3 kWh baseline direct solar    61.3 kWh intercepted
46 kWh benefit                     0 kWh harm
```

and 15.3 kWh looked as though it had gone missing. It had not. **Under the default brief — summer
unwanted, winter wanted — the spring and autumn beam is in neither period.** It was always being
measured; nothing reported it. `VerifyShading` now closes both balances explicitly:

```
Admitted without the device: 61.3 kWh = 46 unwanted + 0 wanted + 15.3 neither
Intercepted by the device:   61.3 kWh = 46 unwanted + 0 wanted + 15.3 neither
```

on the `accountingSummary` output, with `admittedNeutralSolar` and `neutralSolarIntercepted` on their
own wires. `RationaliseShading` gains `neutralSolarIntercepted` for the same reason.

**The general rule, which is not the obvious one.** `AdmittedUnwantedEnergy` and
`AdmittedWantedEnergy` are *desirability-weighted* sums — `|w| × energy` over the hours of each sign —
not slices of a partition. So the neutral share is defined as the **residual**:

```
AdmittedNeutralEnergy    = AdmittedDirectEnergy     − AdmittedUnwantedEnergy   − AdmittedWantedEnergy
NeutralSolarIntercepted  = DirectSolarIntercepted   − UnwantedSolarIntercepted − WantedSolarBlocked
```

Written that way the identity closes for **every** strategy, including continuous and custom ones.
What varies is whether the residual is a physical energy: it is, and is non-negative, exactly when
every applied weight lay within `[-1, +1]`. `ApertureDesirability.MaximumWeightMagnitude` records the
largest `|w|` actually applied — **measured, not declared by the strategy** — and
`WeightsWithinUnitMagnitude` reports `true` / `false` / `null` (unknown, for a file saved before this
was recorded). A strategy weighting beyond unit magnitude claims more beam than physically arrives and
the residual goes negative; that is a true statement about the brief and is surfaced rather than
clamped to zero.

For the simple period-based briefs almost everyone uses, weights are `+1`, `−1` and `0`, so the three
parts partition the beam exactly and the numbers reconcile by eye.

---

## 5. wantedSolarPenalty and materialPenalty

Both are **dimensionless**, and neither changes a measured energy. They change which design the
search picks.

### `_wantedSolarPenalty_` — how much you care about keeping wanted solar

> Importance of preserving wanted solar relative to blocking unwanted solar.
> 1.0 = equal importance; >1 protects wanted solar more; <1 prioritises blocking unwanted solar.

It is an **exchange rate in kWh**:

| value | reading | effect |
|---|---|---|
| **1.0** *(default)* | Equal energy weighting. Losing 1 kWh of wanted solar costs exactly what gaining 1 kWh of blocked unwanted solar earns. | The neutral position. |
| **2.0** | Strongly protect wanted / winter solar. Losing 1 kWh of wanted solar now needs about **2 kWh** of unwanted solar blocked to justify it. | The search buys **shallower** devices. Use where winter gain or daylight matters. |
| **0.5** | Prioritise blocking unwanted solar. Losing 1 kWh of wanted solar costs only **0.5 kWh** in the objective. | The search buys **deeper** devices. Use where overheating dominates. |

On the potential field it moves where the map turns from red to blue.

### `_materialPenalty_` — how reluctant you are to buy more device

> How much device is too much: how reluctant the search is to buy extra shading area for a small
> further gain.

| value | effect |
|---|---|
| **0** | Size on energy alone. Tends to return the largest device that still helps at all. |
| **0.1** *(default)* | A mild preference for the leaner of two near-equal designs. |
| higher | Favours smaller devices. Use where buildability or cost matters more than the last few kWh. |

### What am I allowed to use? *(settled in Stage 10.2)*

Both penalties are **dimensionless weights**, so there is no measurement that hands down a maximum
and inventing one would be a UI convenience dressed up as physics. What *can* be stated exactly is
the floor, which follows from the objective's algebra, and the far field, which was measured.

| | `_wantedSolarPenalty_` (λ) | `_materialPenalty_` (μ) |
|---|---|---|
| **valid domain** | finite, **≥ 0** | finite, **≥ 0** |
| **invalid** | negative, `NaN`, `±∞` — **refused with an error** | negative, `NaN`, `±∞` — **refused with an error** |
| **recommended range** | 0.5 – 2.0 | 0 – 1.0 |
| **default** | **1.0** — an even trade, the only value that assumes nothing about the brief | **0.1** — a mild tie-break toward the leaner design, small enough not to override the energy answer |
| **extreme (valid, remarked on)** | above **10** | above **10** |
| **raise it and…** | wanted solar is protected harder → **shallower** devices, less unwanted solar blocked | the device **shrinks**, and past a point the answer becomes **NO SHADE** |

**Why negative is refused rather than clamped.** Both weights multiply a *positive* quantity that the
score *subtracts*:

```
Score = UnwantedSolarIntercepted − λ × WantedSolarBlocked − μ × Cost
```

A negative λ turns the middle term into an *addition*, so the search is paid to destroy the winter sun
the brief asked it to preserve, and deeper is always better. A negative μ pays for material, and since
cost grows without limit while benefit saturates, the search runs to the largest device its bounds
allow. Neither is a strange-but-defensible weighting; each **inverts the meaning of its own term**.
Silently reading `−2` as `0` would answer a question nobody asked, so the components error instead.

If a project genuinely wants to **maximise** solar gain, that is a different brief and the model
already expresses it: swap `_unwantedPeriod_` and `_wantedPeriod_`, or supply a desirability strategy
whose weights carry the sign. The penalties stay weights.

`NaN` and infinity are refused for the ordinary reason: every candidate would score `NaN` or `−∞`,
nothing could beat the null device, and the run would report "no shading is worth building here" — a
confident engineering answer it never earned.

**Why 10 is where "extreme" starts.** Both are exchange rates against the same unit, so μ = 10 means
"a device covering the whole window must repay ten times the window's entire admitted beam". Measured
on the controlled north window, material penalties of 5 and above already return NO SHADE with the
best candidate at −4.8 kWh: the answer has stopped depending on the value and only its magnitude
changes. A weighting that can no longer change the recommendation has stopped being a design
parameter. It is still **valid** — it is remarked on, not refused.

### Louvre and fin tilt — what the sign means

`TiltDegrees` runs **−60° to +60°** and both signs are real, buildable devices.

| | horizontal louvres | vertical fins |
|---|---|---|
| **positive** | outer edge drops **below** the fixing line — the ordinary brise-soleil, angled down and out to cut high sun | outer edge swings toward **+X** across the facade |
| **zero** | blade square to the glass | fin square to the glass |
| **negative** | outer edge rises **above** the fixing line — the light-shelf direction, letting low winter sun in under the blade while still cutting high summer sun | outer edge swings toward **−X** |

A negative tilt is not an error state. The manual south test returned **−15°** at λ = 2, which is the
objective doing exactly what it was told: protect wanted solar harder, so tip the blades to let the
low winter beam through. Values outside ±60° clamp to the bound rather than building nonsense.

---

## 6. "No shading needed" is an answer, not a failure

The optimiser scores building nothing at exactly **zero**. When no candidate beats that, the honest
recommendation is to leave the window alone, and the workflow now says so as a **success**:

* `status` = **NO SHADE**, `recommendsNoShading` = true;
* `shadingDevice` carries the **null device**, so `VerifyShading` measures it like any other and
  reports the truth — 0 % of unwanted solar blocked, 100 % of wanted solar retained, and `NaN`
  wherever a denominator is genuinely absent. Nothing is fabricated and nothing errors;
* `shadingGeometry` is **empty**, because there is nothing to build;
* the design that lost moves to `bestCandidateDevice` — always supplied, clearly a **diagnostic**, so
  the recommendation can be checked rather than taken on trust. It is never handed out as "the
  device"; that is how a rejected design ends up built.

Distinct from it, and never dressed as it:

* `status` = **NOT EVALUATED**, termination `EvaluationFailed`. Nothing could be **measured**. This is
  a fault in the setup, and it reports as an error with what to check.

---

## 7. Precedence — nothing connected is ever discarded silently

| decision | order | when overridden |
|---|---|---|
| **Weather** | supplied `_weatherData_` → the model's `WeatherData` → actionable error | — |
| **Hours** | explicit `_HOYs_` → `_analysisPeriod_` → full year | `Remark`: *Explicit HOYs override the connected AnalysisPeriod.* |
| **Brief** | `_desirability_` → `_unwantedPeriod_` / `_wantedPeriod_` → summer unwanted, winter wanted | `Remark`: *_desirability_ overrides the connected unwanted/wanted periods.* |
| **Period on AnalysisPeriod** | `_HOYs_` → custom date range → `_preset_` → full year | `Remark` naming what lost |
| **Apertures** | `_apertures_` (Apertures or Guids) → every external sun-exposed aperture | requested-but-excluded apertures are reported by count and first Guid |

The default brief is **hemisphere-aware**: a southern-hemisphere `Location` swaps summer and winter.
It is a default, not a recommendation, and the node says it is using it.

---

## 8. Defaults, units and naming

| input | default | unit |
|---|---|---|
| `_gridSize_` | 0.5 | m |
| `_sunAngleStep_` | 2.0 (**unchanged from Stage 9**) | ° |
| `_skyModel_` | PerezAnisotropic | — |
| `_albedo_` | 0.2 | — |
| `_sunTimeConvention_` | IntervalStart (+30 min, the SAM/EPW timeline) | — |
| `_maxDepth_` | 1.0 | m |
| `_voxelSize_` | 0.1 | m |
| `_marginAbove_` / `_marginBelow_` / `_marginSides_` | 0.5 / 0.0 / 0.3 | m |
| `_previewMode_` | Points | — |
| `_wantedSolarPenalty_` (λ) | 1.0 | dimensionless |
| `_materialPenalty_` (μ) | 0.1 | dimensionless |
| `_threshold_` | 0.9 (CumulativeCapture) | fraction |
| `_maximumEvaluations_` | 400 | candidates per family |
| `_recalculate_` / `_run` | false | — |

**GridSize is a distance; samplePointCount is a count.**

| | is | unit |
|---|---|---|
| `GridSize` | the **spacing between** analysis sample points | m |
| `samplePointCount` | the **number of** analysis sample locations on the opening | — |

Halving `GridSize` roughly quadruples `samplePointCount`, and the run slows in proportion. The output
was called `cellCounts` before Stage 10.1; "cell" is an internal word that meant nothing to the
engineers who tried the node. It is **not** `gridCount`, which reads as a count of grids. Scripts
placed before the rename keep working — the old output name is still honoured — but replacing the
component through the version menu is the tidy fix.

Energies are **kWh**, densities **kWh/m²**, areas **m²**, lengths **m**, angles and orientations **°**,
percentages **%**. Terminology is fixed: **GridSize** (not CellSize), **SunAngleStep** (not BinSize),
**Recalculate** (not RebuildCache). The word *cache* appears in no component name or description.

Every long calculation is gated by `_run`, and returns immediately while it is false.
`IdealShadingShape` has no `_run`: it is arithmetic over an existing map plus one iso-surface, and a
gate there would be an extra wire for nothing.

---

## 9. Reuse, recalculation and the shared cell space

The expensive part is the geometric pass — casting rays from every analysis sample at every sun
group. It is stored on the model's `SolarModel` and reused whenever the identity of the analysis
matches: context geometry, target geometry, grid size, sun-angle step, horizon cutoff, tolerances,
site latitude/longitude and time zone, the timeline offset, the year, and the sample count.

Stage 10 routes **both** the Stage 4 irradiance path and the Stage 6–9 shading path through one
factory (`Create.ApertureSolarContext`), so:

* the calculation paid for by `ApertureIrradiance` is reused by `ShadingPotentialField`,
  `RationaliseShading` and `VerifyShading`, and vice versa — provided `_gridSize_` and
  `_sunAngleStep_` match;
* changing the analysis period, the weather, the sky model or the brief costs **no** geometric work;
* changing the model, the grid or the sun-angle step rebuilds automatically and the node emits
  `Geometry changed; solar calculation was updated.`;
* `_recalculate_ = true` forces a rebuild;
* `reusedPreviousCalculation` is an output, so a user can always tell which happened.

Stale results cannot be returned: identity is checked before reuse, never after.

**The shared cell space.** To keep the calculation shareable, that factory flattens **every**
analysable aperture's sample points into one visibility result, and each aperture reads its own rows
out of it at its own offset. That is why `_gridSize_` must be kept the same across the chain — a
different grid is a different analysis, not a different view of the same one.

It is also where the Stage 10.1 defect lived. See §15.

---

## 10. Resolution warning

Stage 9 found that a device finer than the analysis grid produces numbers that look like a triumph:
eleven blades over a 1 m opening sampled at 0.5 m can sit so every sample is shaded and none of the
gaps are, reporting *100 % of unwanted solar blocked and 100 % of wanted solar retained* at once.

### The defect manual testing found, and the fix *(Stage 10.2)*

Refining `GridSize` on a north window made the **energy** converge tightly — 61.3 kWh intercepted at
0.20 m, 0.10 m and 0.05 m — while the recommended **geometry** did not, and the run carried a
resolution WARNING at *every* grid size tried. Reducing `GridSize` never cleared it.

**Root cause: two numbers that should have been one.** The optimiser's parameter cap admitted element
pitch down to **1 × `GridSize`**, while the reporting rule warned below **2 × `GridSize`**. On any
aperture where more elements keep helping — which is most of them — the search runs to its own cap and
lands squarely inside the warning band. And because the cap is defined *in grid sizes*, halving the
grid halves the permitted pitch and reproduces exactly the same situation one scale finer. The tool was
warning, correctly, about geometry it had itself just proposed.

At `GridSize` 0.10 m on the 1.0 m-wide north window the cap was `1.0 / 0.1 + 1 = 11` fins — precisely
the `Count 11` the manual test returned, at a pitch of exactly 0.100 m.

**The rule now: element pitch ≥ 2 × `GridSize`** — `Create.MinimumElementPitchInGridSizes`, used by the
cap *and* by the warning, so they cannot disagree again.

**Why two, and not a rounder-looking number.** Performance is measured by asking, per analysis cell,
whether that cell's single interior sample is lit. A blade array of pitch *p* casts lit/shaded stripes
of period *p*; the sample lattice has period *g*; so *p / g* is the number of samples per stripe and
hence the resolution of the estimate. At *p = g* there is exactly **one** sample per period and, the
lattices being commensurate, it sits at the same place in every period — the estimator can return only
0 or 1, and which one is decided by lattice phase rather than by the device. Two is simply the first
ratio at which the estimator has any interior resolution at all. It is not an accuracy target; it is
the point at which the measurement stops being a coin toss.

**Measured** (`ResolutionConvergenceTests`, one fixed device of 0.25 m pitch measured on a series of
grids against a 0.04 m reference, at phases 0 and half a pitch):

| pitch / `GridSize` | worst error in unwanted-solar-blocked |
|---|---|
| **1.0** | **26.5 pp** — and 100.000 % reported where the truth is 97.7 % |
| 1.25 | 6.4 pp |
| 1.5 | 5.0 pp |
| **2.0** | **1.7 pp** |
| 3.0 | 9.0 pp |

Note that the error **does not fall monotonically** with the ratio — 3.0 is worse than 2.0 on this
fixture. There is no convergence plateau to pick a number off, which is exactly why the rule rests on
the degeneracy at ratio 1 rather than on "bigger is safer". Do not raise it to 3 expecting an
improvement; there isn't one. The test asserts this so a later reader cannot quietly do so.

### The rule as it now reads

| element pitch | state | Grasshopper | `status` |
|---|---|---|---|
| ≤ `GridSize` | `BelowResolutionLimit` | **Warning**: *…at or below the analysis grid… at most one sample point per gap… space the elements at least X m apart, or reduce GridSize to at most Y m…* | WARNING |
| < 2 × `GridSize` | `NearResolutionLimit` | **Remark**: *…below the minimum feature size for this analysis…* | WARNING |
| ≥ 2 × `GridSize` | `Resolved` | nothing | OK |

Pitch is `span / (count − 1)`, the spacing the typologies actually build to, measured **up** the
opening for louvres and **across** it for fins. A single element has no pitch.

**Nothing was suppressed.** The bound moved to where the evidence says it belongs; the warning is
unchanged and still fires — for a device typed in by hand, which nothing caps, or one measured on a
coarser grid than it was designed against. What changed is that a result produced inside Stage 9's own
declared reliable bounds now comes back **OK**, and a warning again means something.

**Refining the grid still buys a finer device**, which is what makes "reduce `GridSize` to justify a
finer device" a true instruction — it just no longer comes with a warning attached:

| `GridSize` | most fins permitted (1.0 m opening) | tightest pitch | status |
|---|---|---|---|
| 0.4 m | 2 | 1.00 m | OK |
| 0.2 m | 3 | 0.50 m | OK |
| 0.1 m | 6 | 0.20 m | OK |
| 0.05 m | 11 | 0.10 m | OK |

The 0.05 m row is the manual test's `Count 11` — the same device, now correctly reported as resolved,
because at that grid the analysis really can see it.

### Grid-resolution guidance *(PR 2)*

The Kołobrzeg office convergence study added a second, aperture-level resolution finding: a
plausible-looking shading recommendation can be a **coarse-grid sampling artefact**. The smallest
fixture aperture (0.60 × 1.39 m) recommended a very thin single vertical fin with ~87.9 % blocking at
the default 0.5 m grid, and ~0 % blocking at 0.3 / 0.25 / 0.2 m — where the recommended family also
changed to HorizontalLouvres. The same study showed 0.5 m is adequate for some larger apertures, that
one universal correct grid does not exist, and that geometry alone cannot guarantee convergence.

PR 2 therefore adds **guidance, not enforcement** — the engineer's `_gridSize_` remains authoritative
and nothing changes it:

* `ApertureSolarTargets` gains a `recommendedGridSize` output:

  `recommendedGridSize = min(DesignGradeGridSize, shortestSideOfSmallestAperture / 2)`,

  clamped up to the existing `MinimumGridSize`. `DesignGradeGridSize = 0.25 m` is a **named internal
  constant**, an empirical design-grade guidance value from the Gate-4
  design-resolution evidence and the Kołobrzeg study — **not** a universal physical constant and **not**
  a claim that 0.25 m guarantees convergence. One shared number is calculated for the whole selected
  aperture set, governed by the smallest aperture, so a single grid stays valid for the whole workflow.
* When `_gridSize_` is coarser than the recommendation, the node raises a **Warning**: *the selected
  analysis grid is coarser than the recommended grid size… shading geometry may be under-resolved… this
  is guidance, not a requirement: the calculation continues at your grid size, and the recommendation
  does not guarantee convergence — confirm by re-running at a finer grid and comparing.* The message
  deliberately does **not** say the result is wrong.
* `RationaliseShading` raises a separate **Warning** when the winning device's element count sits
  exactly on the **analysis-grid resolution cap** — i.e. only when the cap genuinely narrowed the
  typology's own maximum **and** the winner equals that narrowed maximum. A winner at an independent
  typology maximum, or below the cap, carries nothing. `status` becomes WARNING in this case.

**Recorded follow-up — the single-element blind spot.** `Query.ShadingResolution` treats a device with
a single repeated element as resolved because there is no pitch to evaluate. The Kołobrzeg study found
exactly such a single-element case (one thin fin) was a coarse-grid artefact, but the evidence is not
yet sufficient to define an element-extent resolution rule robustly. Until it is, the coarse-grid
recommendation warning above covers the aperture-level risk and the cap warning covers grid-limited
optimisation outputs; a single-element extent rule is deferred deliberately, not silently omitted.

---

## 11. Display geometry is not performance geometry

`IdealShadingShape` returns a mesh. It is for **viewing and take-off only**. The Stage 7/8 finding
stands: the mesh intercepts materially less solar than the voxel solid it represents (28.2 % less
interception and 21.5 points more apparent wanted-solar retention in the Gate 0 Review A case).

The component therefore:

* names the output `mesh` and describes it as DISPLAY geometry in both the component description and
  the output description;
* keeps every number — captured **potential** fraction, projected area, volume, depth, region sizes —
  from the **field**, not the mesh;
* reports `meshNote` and warns when no mesh could be produced, while still returning the numbers;
* points the user at `VerifyShading` on a real device for any performance judgement.

This is a known open research question, not a Stage 10 defect, and Stage 10 does not attempt to solve
it.

### `capturedFraction` is a share of the FIELD, not of the solar *(renamed in Stage 10.2)*

`CapturedBenefitFraction` is now **`CapturedPotentialFraction`**, and the Goo string changed with it:

```
IdealShadingResult [3 regions, 790 voxels, holds 90.0 % of the positive shading potential — not verified performance]
```

The quantity is unchanged. The old name asserted something it never measured: both numerator and
denominator are **sums over voxels**, and a single ray contributes to every voxel along its path, so
this is a ratio of spatial potentials. "Captures 90 %" means the region holds 90 % of the field's
positive potential — **not** that a device built there would block 90 % of the unwanted solar. It will
not: the region is a volume, a device is a surface, and this mesh was never traced.

What it *is* good for is the job Stage 7 gives it — choosing a threshold, and comparing two thresholds
on the same field. Files written before the rename still load: `FromJsonObject` reads the old key when
the new one is absent, so a saved schema-1 result keeps its number instead of coming back `NaN`.

### The visual story, end to end

The four steps say different kinds of thing and should not look alike:

| step | geometry | display | what it is |
|---|---|---|---|
| **1. Potential** | `ShadingPotentialField` | red / blue points or voxels + `legend` | **RED = SHADE HERE, BLUE = KEEP OPEN.** A map of instructions. Totals are potentials, not savings. |
| **2. Ideal** | `IdealShadingShape` → `mesh` | **translucent** warm / orange | shading **intent**. Explicitly *not* verified performance geometry. |
| **3. Buildable** | `RationaliseShading` → `shadingGeometry` | a **solid**, distinct colour | the real, rationalised device. |
| **4. Verified** | `VerifyShading` | text: `verificationSummary`, `accountingSummary` | **measured kWh and percentages** on real traced geometry. The only performance truth in the chain. |

Keeping the ideal translucent and the device solid is the point: it stops the ideal mesh being read as
a result. The legends are plain **string outputs** panelled next to the geometry rather than custom
Rhino viewport drawing — a hand-drawn legend has to survive every camera, display mode and DPI in
Rhino, and a broken one is worse than none. A string can also be copied into a report.

---

## 12. Verification

`VerifyShading` is the ground truth: the device is built as real geometry and traced through the same
first-hit engine as everything else.

* `baselineDirectSolar` — direct solar admitted with the surroundings in place and **no** device.
  Only this can be credited to a device, so solar already blocked by a neighbouring building is never
  the device's to claim.
* `directSolarIntercepted`, `directShadingEfficiency` [%], `unwantedSolarBlocked` [%],
  `wantedSolarRetained` [%], with `admittedUnwantedSolar` / `admittedWantedSolar` as the denominators.
* `elementNames` / `elementGuids` / `elementEnergy` — per-element attribution, credited to the element
  the sun reaches **first**, so overlapping parts never double-count.
* `unattributedEnergy` — the residual, reported rather than folded into a total.
* `verificationSummary`, `status`, `apertureGuid`, `azimuth` — the batch-readable form.

**Zero denominators stay unavailable.** Every percentage is `NaN` when its denominator is zero — never
0 % or 100 % — in the result object, in the component output, and in the ratio-to-percentage
conversion between them.

---

## 13. Error and warning quality

Actionable, in the engineer's terms:

* `Please supply a valid SAM AnalyticalModel.`
* `No valid external sun-exposed apertures were found in this model.`
* `WeatherData is required. Supply WeatherData or attach it to the AnalyticalModel.`
* `Explicit HOYs override the connected AnalysisPeriod.`
* `The proposed shading spacing (0.1 m) is below the reliable analysis resolution (GridSize 0.25 m). Reduce GridSize and recalculate before trusting the element-level results.`
* `This device was designed for aperture <a>, but it is being verified against aperture <b>. The result would be a correct measurement of the wrong design. Match the device to its own target…`
* `No candidate could be measured on this window: the aperture, the solar calculation and the candidate geometry do not describe the same analysis points. Check that _gridSize_ is the same value used for the targets and that the target came from THIS model.`
* `_preset_ was not recognised. Use one of: Full Year, Summer, Winter, …`

Enum inputs accept the value, its name (case- and space-insensitive) or its index, and an
unrecognised one is an error naming the accepted values rather than a silent default.

---

## 14. Goo / parameter types

| type | why |
|---|---|
| `GooApertureSolarTarget` (+ `Param`) | previews the opening and its outward normal; casts to Brep/Mesh |
| `GooShadingPotentialField` (+ `Param`) | the red/blue/grey preview, Points or Voxels, and the shared colour rule |
| `GooIdealShadingResult` (+ `Param`) | previews the display mesh; casts to Mesh |

Everything else — `ShadingDevice`, `IShadingTypology`, `OptimisedShadingResult`, `ShadingPerformance`,
`AnalysisPeriod` — travels as `GooSAMObject`, and results as `GooResult`, because SAM's existing
wrappers already handle them. All three Goos derive from `GooJSAMObject<T>`, so they save and reload
through the standard SAM JSON path; the round-trip is tested, including a device's aperture identity.

---

## 15. What the first real manual test found

The single-south-window workflow worked and was useful. Opened on a controlled model with **ten**
apertures across four cardinal orientations, it failed:

* the potential field looked plausible on every orientation;
* `RationaliseShading` produced device families and warnings;
* several orientations reported "no shading beats leaving the aperture unshaded";
* `VerifyShading` failed with *"The device could not be measured on this window."*, and downstream
  values became null/NaN.

### The mechanism

Not data trees, not pairing, not orientation — those were all ruled out by running each aperture
individually, which failed too. **The defect was the shared cell space of §9.**

`ApertureSolarContext` flattens every aperture's sample points into one visibility result. The
attribution side — the part that decides which element of a device the sun hits first — required the
sample points handed to it to be **the whole** of that result, and it was handed **one aperture's**.
On a one-window model those are the same thing, which is why every earlier test passed: every Stage
6–9 test scenario built its cache from a single target.

With ten windows they differ, so attribution came back null for **every** aperture, every candidate
scored `NaN` — and because `NaN > 0` is false, the optimiser read that as *"nothing beats leaving the
window alone"*. The "no shading recommended" results were not physics. They were an unmeasured run
wearing the costume of an answer.

### What changed

1. **The cell dimension is a window, not a mirror.** Attribution covers one aperture's samples,
   indexed locally, starting at that aperture's offset in the shared space. The one place the two are
   paired now reads the baseline at `offset + c` and the attribution at `c`. Reading both at the same
   index — the old behaviour — scored an aperture against another aperture's admitted beam.
2. **A fault cannot wear the costume of an answer.** `EvaluationFailed` is distinct from
   `NoBeneficialCandidate` (§6).
3. **Devices carry their aperture** (§2).
4. **Context is not re-traced per candidate** (§16).

### Verified

Every one of the ten apertures now produces a field, a measured device and a verified result. One
aperture measured as one of ten, against a 150-sample shared calculation, gives numbers **identical
to nine decimal places** to that aperture measured alone against its own 10–20-sample calculation, on
all four orientations. That equivalence — batch equals individual — is what identifies the defect as
the cell space rather than anything on the canvas, and it is now a test.

The controlled model is kept as `Fixtures/MultiAzimuth.sam` (125 KB) and its per-orientation answers
are asserted on every run.

---

## 16. Performance at project scale

Measured on a real project of roughly 1 600 spaces, 8 800 panels and 2 200 apertures — of which 946
are external and sun-exposed, giving **12 540 analysis sample points** at `GridSize` 0.5 m and 683
sun groups at `SunAngleStep` 2°.

| operation | time | notes |
|---|---|---|
| load model | 1.3 s | |
| `ApertureSolarTargets`, all apertures | 0.4 s | 946 targets |
| **first** shading setup for one aperture | 12.8 s | builds the shared calculation over all 12 540 samples |
| `ShadingPotentialField` for that aperture | 0.01 s | 6 500 map locations |
| the same aperture again (reuse) | 0.9 s | no ray casting |
| change the brief only | 0.9 s | no ray casting — periods are arithmetic |
| change `GridSize` (forced rebuild) | 9.8 s | a different grid is a different analysis |
| `RationaliseShading`, one family | 0.35 s | 87 candidates |
| `RationaliseShading`, all eligible families | 1.3 s | 4 families |
| `VerifyShading` | 0.003 s | |

Whole workflow for one aperture on that project: **about 15 seconds cold, under 3 seconds warm.**

### What made it that fast

The first-run cost is honest and is the price of the shared calculation: analysing one window builds
visibility for all of them, and every subsequent window is then nearly free. The **rationalisation**
cost was not honest, and Stage 10.1 removed it.

Each attribution build projects every occluder onto a plane per sun group, and the optimiser
evaluates dozens of candidates — so the whole building was being re-projected 87 times to size one
overhang.

| on that project, one aperture | before | after |
|---|---|---|
| `RationaliseShading`, one family | **300.9 s** | **0.35 s** |
| `RationaliseShading`, all four families | **851.8 s** (14 min) | **1.3 s** |
| `VerifyShading` | **3.5 s** | **0.003 s** |

**No context was removed to get this.** The building still shades the window. Attribution is only
ever consulted where the base visibility calculation says the sample is **lit**, and "lit" means the
same primitive, with the same tolerances and the same ray-start offset, has already established that
no context face is on that ray. For those samples the first thing the sun meets is a candidate
element or nothing — the context faces cannot be first, because they are not on the ray at all.
Tracing them again could only reproduce an answer already paid for.

The argument is not left to stand on its own: the two routes are asserted equal to nine decimal
places across four device families, three orientations and every per-element credit, and on the real
project the optimiser returns the identical winning design, the identical score and the identical
evaluation count either way.

Storage, same project: the visibility result is ~1 MB (683 × 12 540 bits); a candidate's attribution
is ~42 KB (683 × 16 samples × 4 bytes). Whole-model attribution per candidate would have been ~32 MB.

### Fine grids — the profile, the scaling law, and what is left *(Stage 10.2)*

At the **default** `GridSize` 0.5 m the workflow is interactive and always was. The complaint was
about **0.1 m**, where manual testing measured 4–5 minutes for a single south window. Instrumented on
`MultiAzimuth.sam`, south aperture, all four families, 16 cores:

| | `GridSize` 0.5 | 0.2 | 0.1 |
|---|---|---|---|
| analysis samples on this window | 20 | 117 | 425 |
| sun groups | 683 | 683 | 683 |
| context preparation | 2.7 s | 0.6 s | 3.3 s |
| `ShadingPotentialField` | 0.02 s | 0.02 s | 0.01 s |
| candidate **geometry construction** | 0.002 s | 0.005 s | 0.011 s |
| candidate **ray / attribution / accounting** | 2.1 s | 12.4 s | 56.9 s |
| candidates evaluated (4 families) | 199 | 217 | 237 |
| time per candidate | 11 ms | 60 ms | 240 ms |
| peak working set | ~15 MB | ~24 MB | ~3 MB |

**The dominant cost is the per-sample first-hit ray evaluation: >99.9 % of optimisation time.**
Geometry construction is under 0.05 %. Context preparation is roughly flat in `GridSize` and is
*shared* across every window. The field build is negligible. **Stage 10.1's whole-building
reprojection bottleneck has not returned** — that is what the flat setup and the tiny geometry term
confirm.

**Scaling law.** Time ≈ *candidates* × *sun groups* × *samples* × *elements per candidate*. Samples go
as `GridSize⁻²`, and the measured optimisation time (2.1 → 12.4 → 56.9 s against 20 → 117 → 425
samples) is **linear in the sample count**, i.e. **∝ `GridSize⁻²`**. Per candidate at 0.1 m the cost
is about **85 ms fixed + 19 ms per element**.

**What Stage 10.2 changed, both provably equivalent:**

1. **The minimum feature-size rule** (§10) is also the largest performance change here. It roughly
   halves the permitted element count at fine grids — 24 → 13 louvres at 0.1 m — and since cost is
   linear in element count, it roughly halves the cost of the expensive candidates. The correctness
   fix and the speed-up are the same change.
2. **Only the readable samples are traced.** Stage 8 consults attribution strictly inside
   `if (IsLit(b, offset + c))`, so a sample already shaded by context is never asked about. Tracing it
   produced a value nothing read. Measured readable fractions: **10.8 %** on the north window
   (a 9.2× reduction in rays) against **85.2 %** on the fully exposed south (1.2×). The saving is
   largest exactly where the old cost was least justified.

| south window, one aperture, all families | before | after | |
|---|---|---|---|
| `GridSize` 0.5 m | 4.9 s | **4.9 s** | setup-bound |
| `GridSize` 0.2 m | 20.0 s | **14.6 s** | 1.37× |
| `GridSize` 0.1 m | 85.5 s | **60.1 s** | 1.42× |

**The target, and whether it is met.** For one normal aperture with all eligible families:

| | target | measured | met? |
|---|---|---|---|
| `GridSize` 0.5 m (the default) | ≤ 5 s — interactive | 4.9 s | **yes** |
| `GridSize` 0.2 m | ≤ 20 s — a considered run | 14.6 s | **yes** |
| `GridSize` 0.1 m | ≤ 60 s — a deliberate, one-off refinement | 60.1 s | **yes, but only just** |

**0.1 m is not interactive and this work has not made it so.** It is now a minute rather than four or
five, which makes it usable as a final check on a window that matters; it is not something to leave on
a slider.

**Where the remaining headroom is, measured rather than guessed.** Effective parallelism during a
candidate evaluation is **6.9 of 16 cores** (CPU time / wall time, `Parallel.For` over 683 sun groups).
The work partitions cleanly, so the ceiling is allocation pressure and memory bandwidth in the generic
geometry kernel — each point test allocates several short-lived `Point3D` / `Point2D` / `Envelope` /
`Segment3D` objects. An allocation-free first-hit kernel is worth roughly **2.3×** on this evidence.
That is a change to accepted Stage 0–4 physics code, it cannot be justified as "obviously equivalent",
and it is **out of scope for Stage 10.2** — recorded here as the next lever, with the measurement that
sizes it.

**What was considered and rejected.** A staged coarse-to-fine search (evaluate the coarse lattice on a
coarser grid, refine the survivors at full resolution) would cut candidate cost substantially, but it
changes which candidate wins and **no equivalence proof is available for it**. The brief requires
physical verification of final candidates and permits pruning only where it is *proven* deterministic.
Skipping sun groups that carry no desirability energy was measured at 1.04× on the south case and is
not worth the plumbing.

---

## 17. Automated tests

`Stage10ComponentLogicTests` (16), `MultiApertureShadingTests` (13), `MultiAzimuthModelTests` (5).
The components are deliberately thin (read wires → call one method → write wires) precisely so the
behaviour is testable outside Rhino, which cannot host a Grasshopper component in a unit-test process.

| area | covered |
|---|---|
| hour precedence | HOYs override a period and report it; a period is used alone; an empty HOY list does not override; full year is the default |
| resolution warning | below the grid, near the grid, single element, coarse array, fins across vs louvres up |
| the brief | default is summer-unwanted / winter-wanted and flips in the southern hemisphere |
| context | default = every aperture; offsets address the shared space; reuse; `_recalculate_`; a different grid rebuilds |
| invalidation | a different model never reuses another model's calculation |
| weather precedence | supplied wins; the model's own otherwise; no weather returns nothing rather than guessing |
| **shared cell space** | attribution is a window into it; every aperture of many measures exactly as it would alone; reading at the wrong offset is provably a different answer |
| **candidate-only attribution** | identical to whole-model attribution, per family, per orientation, per element |
| **no-shading semantics** | an unmeasurable run is `EvaluationFailed` and recommends nothing; the null device measures as 0 % / 100 %; a percentage with no denominator stays unavailable even for it |
| **device identity** | a device carries its aperture through a save/reload; a device from one orientation is detectably not another's |
| **the controlled model** | ten targets across four orientations, all with valid outward frames; a field, a measured device and a verified result for every one; same orientation → same design, different orientation → different design |
| optimisation metrics | a device rebuilt from the optimiser's parameters reproduces its numbers exactly |
| zero denominators | no wanted solar leaves `wantedSolarRetained` unavailable, through the reporting conversion |
| durability | target, field, ideal result, device and shading device all survive a save/reload |

Added by Stage 10.2 — `ResolutionConvergenceTests` (6), `PenaltyDomainTests` (11),
`SolarAccountingTests` (10), `AttributionPruningTests` (5):

| area | covered |
|---|---|
| **resolution convergence** | one fixed device on a series of grids: worst error 26.5 pp at pitch = `GridSize` against 1.7 pp at 2 × `GridSize`; energy converges within 2 pp on a resolvable device; the non-monotonicity in the ratio is asserted so the rule cannot be "improved" to 3 × |
| **the feature-size rule** | the optimiser can never return a device its own warning would reject, on four orientations at two grids; a finer grid buys more elements and still reports OK; the cap and the warning are provably one number, with no gap either way; a hand-typed fine device is still warned about |
| **penalty domain** | valid / extreme / invalid classification across the range; negative λ demonstrably rewards destroying wanted solar and negative μ rewards buying material; defaults are the documented ones |
| **penalty extremes** | the material sweep 0.1 → 100 reaches NO SHADE through the existing null device with no kWh threshold; score never rises as μ rises; μ = 100 still verifies as a real answer |
| **λ semantics** | λ changes the score by exactly Δλ × harm and no measured energy; λ reorders a fixed candidate set with an exactly computable crossover; a stronger λ never destroys more wanted solar; negative louvre tilt is a real bounded direction |
| **determinism** | two identical searches agree on family, parameters, score, evaluation count and attribution hash |
| **neutral accounting** | both balances close exactly; the north window's "missing" 15.3 kWh is the neutral share; the residual is a physical energy iff weights are within unit magnitude, measured not declared; an over-weighted brief reports a negative residual rather than a false partition; a pre-schema-2 file says "unknown" instead of assuming |
| **field terminology** | the summed potential is 10.2× the physical ceiling on any saving, so it cannot be one; a real optimised device stays under the ceiling; the raw arrays are unchanged |
| **`CapturedPotentialFraction`** | it is a share of the field's positive potential; a schema-1 file keeps its number through the rename |
| **pruning equivalence** | pruned and complete attribution agree to 12 decimal places on 6 scenarios × 5 families, including every per-element credit; entry-by-entry agreement wherever the baseline says lit and an explicit "not evaluated" everywhere else; the same design end-to-end at `GridSize` 0.1 m; the pruned build is measurably quicker; candidate cost stays near-linear in element count |

**Not covered automatically** — parameter visibility, the viewport preview, Grasshopper's own casting
and the canvas experience. Those are the manual checklist.

---

## 18. Known limitations that matter to a user

Inherited from Stages 0–9 and unchanged: no inter-reflection; no perforated or translucent screens;
four device families only; single scalar objective (no Pareto front); local optimality on the search
lattice; device fineness capped by the analysis grid; diffuse solar is not part of shading
desirability; each aperture is optimised independently — **a device here does not shade its
neighbour**; hourly time resolution; `MaterialFraction` is area only, with no thickness, weight,
fixing or money.

Added by Stage 10 and worth knowing:

* the shading components build their context over the model's **whole** default aperture set, so the
  first run on a large model pays for every aperture even when studying one — the trade for making
  the calculation shareable with `ApertureIrradiance`, and measured in §16 at about 13 s on a
  2 200-aperture project;
* `_gridSize_` must be kept the same across the chain or the work is redone;
* one component call studies **one** aperture; a list of targets is handled by Grasshopper's own
  per-item iteration and needs no grafting (§2);
* the ideal shape's mesh is display geometry, not performance truth (§11).

---

## 19. Manual test checklist

The automated tests cover the logic. These cover the canvas, and need 2–3 real projects — at least
one with a neighbouring building or a deep soffit, and at least one with **many windows**.

### Test A — solar analysis only

`AnalyticalModel` → `ApertureSolarTargets` → `ApertureIrradiance` (wire `apertureGuids` into
`_apertures_`, never the targets)

- [ ] every expected window appears; `count` matches the model
- [ ] `azimuths` agree with the model's orientation; the preview arrow points OUT of the room on
      every target (an inward arrow means the model's aperture is wrong, not the analysis)
- [ ] `samplePointCounts` is a COUNT and roughly quadruples when `_gridSize_` is halved
- [ ] internal-wall apertures are absent; asking for one by Guid produces a clear warning, not silence
- [ ] `_run = false` does nothing at all
- [ ] full year vs Summer vs Winter: totals change, and the second run reports
      `reusedPreviousCalculation = true`
- [ ] `_HOYs_` connected alongside `_analysisPeriod_` → the Remark about the override appears
- [ ] `_weatherData_` connected → those results differ from the model-weather run
- [ ] `_recalculate_ = true` → `reusedPreviousCalculation = false` and it takes as long as the first run
- [ ] south exceeds north (northern hemisphere); a UK vertical facade lands in the low hundreds of
      kWh/m² annually

### Test B — potential and ideal shape

`ApertureSolarTargets` → `ShadingPotentialField` → `IdealShadingShape`

- [ ] the map sits in FRONT of the glass, not behind it
- [ ] **red concentrates where shading material would earn its keep**, blue where it would destroy
      wanted solar — read the `legend` output next to it
- [ ] swapping `_unwantedPeriod_` and `_wantedPeriod_` inverts the colours
- [ ] `_previewMode_` Points / Voxels / None: the map looks different, the numbers do not change
- [ ] `_wantedSolarPenalty_` 0.5 vs 1.0 vs 2.0 moves the red/blue boundary
- [ ] `_threshold_` 0.5 vs 0.9 vs 0.99 → the shape grows; `capturedPotentialFraction` tracks the
      request *(renamed from `capturedFraction` in Stage 10.2 — rewire it)*
- [ ] **`positiveShadingPotential` carries NO kWh label**, and the `legend` states plainly that the
      totals are not energy savings *(Stage 10.2)*
- [ ] the field's object string reads `[N voxels | positive potential … | negative potential …]` —
      no "benefit", no "jeopardy" *(Stage 10.2)*
- [ ] the ideal result's string says **"holds X % of the positive shading potential — not verified
      performance"** *(Stage 10.2)*
- [ ] a window with no summer sun problem reports "nothing here is worth shading" rather than failing
- [ ] `ShadingPotentialField` reuses the calculation `ApertureIrradiance` already paid for

### Test C — buildable shading

`ShadingPotentialField` → `RationaliseShading` → `VerifyShading`

- [ ] all four families run: Overhang, HorizontalLouvres, VerticalFins, EggCrate
- [ ] `designSummary` reads as a sentence an engineer would write; `status` is OK
- [ ] the winning geometry looks buildable and its dimensions match `parameterValues`
- [ ] `VerifyShading` on the winner reproduces `RationaliseShading`'s `unwantedSolarIntercepted`,
      `wantedSolarBlocked` and `directSolarIntercepted`
- [ ] `unwantedSolarBlocked` and `wantedSolarRetained` move in opposite directions as depth grows
- [ ] `_wantedSolarPenalty_` 2.0 gives a shallower device than 0.5 on the same window
- [ ] force a fine device **by hand** → the resolution warning appears, names both numbers, and
      `status` is WARNING
- [ ] a window with no unwanted solar → `status` NO SHADE, `shadingGeometry` empty, and
      `VerifyShading` on `shadingDevice` still SUCCEEDS, reporting 0 % blocked / 100 % retained
- [ ] `bestCandidateDevice` shows what the search would have built in that case
- [ ] `_optimise_ = false` returns a comparable, quicker answer

### Test G — Stage 10.2 re-runs *(the ones that found the defects)*

Run on `SolarShading_MultiAzimuth_Test.sam`, the same controlled model as before.

**G1 — the resolution warning must now clear.** North aperture, `_wantedSolarPenalty_` 1,
`_materialPenalty_` 0.1, at `_gridSize_` **0.20 / 0.10 / 0.05 m**:

- [ ] `status` is **OK** at every grid size — not WARNING. This is the whole of Gate A.
- [ ] element counts are lower than before at the same grid (the 0.10 m run should no longer return
      11 fins over a 1 m opening; expect about 6)
- [ ] the pitch reported in any warning message, if one appears at all, is a **hand-typed** device
- [ ] the energy answer is close to the previous runs — it converged before and still does

**G2 — the numbers must reconcile.** Any window, `VerifyShading`:

- [ ] `accountingSummary` reads `Admitted without the device: 61.3 kWh = 46 unwanted + 0 wanted +
      15.3 neither` and the three add to the baseline
- [ ] `admittedNeutralSolar` explains the residual that looked like missing energy
- [ ] `directSolarIntercepted` = `unwantedSolarIntercepted` + `wantedSolarBlocked` +
      `neutralSolarIntercepted`

**G3 — penalties.** North aperture at `_gridSize_` 0.10 m:

- [ ] `_materialPenalty_` **−1** → a clear **error** naming the domain, not a silent result
- [ ] `_wantedSolarPenalty_` **−1** → a clear **error** that also says to swap the periods instead
- [ ] `_materialPenalty_` **50** → a **remark** that it is far above normal, and a valid NO SHADE
- [ ] the sweep 0.1 / 0.5 / 1 / 2 / 5 / 10 / 100 still ends at NO SHADE with `successful = true`

**G4 — fine-grid runtime.** South aperture, all families, `_gridSize_` 0.10 m:

- [ ] time it. Expect **roughly 40 % faster** than the 4–5 minutes previously measured. It should now
      be **1–3 minutes** on your machine, not 4–5.
- [ ] the recommended device differs from before — it will, because the element cap changed — but
      `VerifyShading` must reproduce its numbers exactly
- [ ] `_gridSize_` 0.5 m still returns in a few seconds

**G5 — rewired components.** `RationaliseShading` and `VerifyShading` are at **1.0.2**,
`ShadingPotentialField` at 1.0.2:

- [ ] opening an existing Stage 10.1 definition reports the version change as expected
- [ ] `benefit` / `harm` are now `unwantedSolarIntercepted` / `wantedSolarBlocked` — **rewire these**
- [ ] `positiveTotal` / `negativeTotal` are now `positiveShadingPotential` /
      `negativeShadingPotential`, and the negative one is a **positive** number

### Test D — many windows *(new in Stage 10.1)*

`ApertureSolarTargets` (all windows, no grafting) → the whole chain.

- [ ] every aperture produces a result; none reports NOT EVALUATED
- [ ] `status` / `apertureGuid` / `azimuth` panelled together read as a table
- [ ] windows of the same orientation reach the same design; different orientations do not
- [ ] running ONE of those windows alone gives the same numbers as running all of them
- [ ] deliberately cross-wire a device from one window onto another window's target →
      **VerifyShading refuses with an error naming both apertures**, rather than reporting a number

### Test E — context

An aperture partly shaded by another building or a deep soffit.

- [ ] `baselineDirectSolar` is visibly lower than an equivalent unobstructed window
- [ ] the optimiser does NOT buy material for solar the context already blocks — expect a minimal
      device and a small benefit
- [ ] `unattributedEnergy` is zero (a non-zero value is a real finding: report it)

### Test F — a real project

- [ ] the first shading run on a large model takes tens of seconds, not minutes, and says
      `reusedPreviousCalculation = false`
- [ ] the second window is nearly free
- [ ] comparing all four families on one window takes seconds, not minutes

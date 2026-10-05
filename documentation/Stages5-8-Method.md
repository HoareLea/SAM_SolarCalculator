# Stages 5–8 implementation — method, conventions and measured validation

Companion to `Stages0-4-Method.md`. This documents what was **actually built**, including the
places where it diverges from `ShadingOptimisation-Plan.md` and the places where the plan's own
test intent turned out to be asking for the wrong physics.

Everything in Stages 0–4 is unchanged. Three defects were found and fixed in code that Stage 5–8
work sits on; they are recorded in §2.4, §3.6 and §5.5.

---

## 1. Stage 5 — desirability weighting

### 1.1 What it is

A signed, pluggable, per-hour weighting of an aperture's direct beam energy, aggregated per sun
group so Stage 6 can march it through space without revisiting the 8760-hour timeline.

```
IDesirabilityStrategy.Weight(DateTime, WeatherHour, ApertureSolarTarget) -> double
```

### 1.2 Sign convention

| Weight | Meaning |
|---|---|
| `> 0` | Blocking this solar is **beneficial** — unwanted gain |
| `< 0` | Blocking this solar is **harmful** — wanted gain |
| `= 0` | Neutral |

The magnitude scales the hour's contribution, so a strategy can express "twice as unwanted"
rather than only a yes/no.

### 1.3 Strategies

| Strategy | Basis |
|---|---|
| `SeasonalDesirability` | Two `AnalysisPeriod`s: unwanted season, wanted season |
| `TemperatureDesirability` | Dry-bulb thresholds |
| `IrradianceThresholdDesirability` | Global/diffuse irradiance thresholds |
| `CompositeDesirability` | Weighted combination of other strategies |
| `ExternalDesirability` | Caller-supplied per-hour array — **the TAS/load integration hook** |

`ExternalDesirability` is how a future TAS coupling enters: a heating/cooling load series becomes
the weight array, and nothing else in Stages 6–8 changes.

### 1.4 Energy formula and units

Per hour `h` in sun group `g`:

```
energy(h) = DNI(h) x max(0, cos thetaI(h)) x 1 h / 1000        [kWh/m2]
DNI(h)    = max(0, GHI(h) - DHI(h)) / max(sin(elevation), sin 5 deg)
```

Identical to `Query.CachedIrradiance`: **raw GHI/DHI only**, never the ambiguous
`DirectSolarRadiation` field. The sun is sampled at `h + SunPositionShiftInMinutes` taken from the
cache, so desirability and irradiance can never sit on different timelines.

```
DirectEnergy[g]   += energy(h)                  (all hours)
UnwantedEnergy[g] += w(h) x energy(h)           (hours with w > 0)
WantedEnergy[g]   += -w(h) x energy(h)          (hours with w < 0)
```

All three are **kWh/m²**. Weighting is **energy-weighted, not hour-counted**: a zero-energy hour
(night, back-facing sun, fully overcast) contributes exactly zero whatever its weight.

### 1.5 Context visibility is deliberately NOT applied here

Desirability is a property of the sun's energy *toward* the aperture. Stage 6 combines it with the
Stage 0–4 lit bits, which is what stops a voxel being credited for blocking sun that existing
context already blocks. Applying visibility twice would double-count the obstruction.

### 1.6 A result that looks wrong and is not

For a **south vertical facade** in the synthetic weather, winter *wanted* direct energy exceeds
summer *unwanted* direct energy. This is correct: low winter sun strikes a vertical south facade
at near-normal incidence, while high summer sun arrives at a glancing angle. Do not "fix" it.

---

## 2. Stage 6 — the shading potential field

### 2.1 The field equation

For each sun group `g` and each analysis cell `a` **lit at `g`**, a ray is marched from the cell's
interior point toward the sun through the voxel grid. Every voxel `v` the ray traverses receives:

```
Unwanted[v] += area(a) x UnwantedEnergy[g]
Wanted[v]   += area(a) x WantedEnergy[g]
```

The scalar score is computed **on demand**, so the penalty can be swept without rebuilding:

```
Score(v, lambda) = Unwanted[v] - lambda x Wanted[v]
```

`lambda` = `wantedSolarPenalty`. Positive score = worth filling with material; negative = must stay
open.

### 2.2 Units — verified dimensionally

Stage 5 gives **kWh/m²**; the analysis-cell area is **m²**; so the raw voxel field is **kWh**. It
is the aperture-plane beam energy, desirability-weighted over the year, that material at `v` would
intercept.

Each traversed voxel receives the **full** cell contribution, not a share. The question the field
answers is per-voxel and independent: "if material were placed at `v`, how much energy would it
intercept?" Two voxels on the same ray each intercept that ray in full, considered separately.

### 2.3 Raw value and normalisation both survive

`UnwantedEnergyPerVoxel` and `WantedEnergyPerVoxel` are kept as raw kWh arrays.
`NormalizedScore` maps positives onto `(0, 1]` against the maximum and negatives onto `[-1, 0)`
against the minimum, purely for display. **The raw kWh is never replaced by the dimensionless
value.**

### 2.4 The volume frame — and the frame confusion that broke five tests

`ShadingVolume` is a regular voxel grid whose axes are the aperture's local frame
(`axisZ` = outward normal, `axisX` horizontal, `axisY` up-slope) but whose **origin is the minimum
corner**, i.e. aperture-frame `(minX - left, minY - down, 0)`.

So `ShadingVolume.TryToLocal` returns coordinates **shifted by the margins and never negative**.
The aperture frame has its origin at the **centroid**, so a 2 m × 1 m window spans
`x ∈ [-1, 1]`, `y ∈ [-0.5, 0.5]`.

Reading one as the other is silent and destructive. In the inherited tests, with
`left = 0.5, down = 0.2`, the offset was `(1.5, 0.7)`:

* a band meant to sample the overhang above the head (`y ∈ (0.6, 1.3)`) actually sampled
  aperture `y ∈ (-0.15, 0.55)` — **the glass**, which is correctly *negative* for a south window;
* any predicate testing the left-hand side (`x < -1`) matched **no voxel at all**, which is why
  the east-window south fin measured exactly 0.

`Query.TryGetApertureLocal` / `TryGetApertureLocalCentre` / `TryGetApertureLocalBounds` now exist
as public API so this conversion is done once, correctly, in one place.

### 2.5 DDA traversal, and two real defects in opposite directions

Traversal is Amanatides–Woo 3-D DDA. Validation is against an independent brute-force ray/box
accumulation over every voxel. The two disagreed by **34.46 %**, and diagnosis showed **both** were
wrong:

The trigger is that **any `gridSize` that is a multiple of `voxelSize`** — 0.5 / 0.25 being the
obvious case — puts **every** analysis-cell ray origin exactly on a voxel corner. The degenerate
case was the common case.

**Defect 1, in the DDA (production code).** The start voxel was chosen with `floor()`. On an axis
where the ray steps *negatively*, that names the voxel on the far side of the plane — one the ray
only touches with **zero path length**. Measured: **512 spurious zero-length visits** in the
cross-check workload. `StartVoxel` now steps such an axis back by one, and a ray that merely grazes
the grid at a corner reports nothing at all.

**Defect 2, in the reference (test code).** It counted tangential corner and face touches as
interceptions, via a closed-interval `tMin <= tMax` slab test. Measured: **2632 zero-length hits,
every single one with exactly 0.0 overlap**. It also truncated rays at `MaxDepth + 2 voxels`, which
silently deleted the far end of every shallow low-sun ray — a ray at low sun crosses the full
2.5 m lateral extent, far past a 1.25 m cut-off.

Material occupying a voxel a ray touches at a single point intercepts nothing, so neither side was
entitled to count those. With both fixed:

```
DDA vs brute force: sum|diff| = 0, sum|reference| = 7555.39, relative = 0
```

**Exact agreement.**

### 2.5.1 Two further phantom visits, found at the Stage 9 gate review

The cross-check above passes because it exercises one workload. Probing the traversal *directly*
with synthetic rays — `Query.TraversedVoxels`, which marches the same production code — found two
more ways to report a zero-length visit. Both are fixed; the cross-check above still gives exact
agreement afterwards.

**Defect 3, simultaneous boundary crossings.** A ray leaving a voxel exactly through an **edge or a
corner** enters the diagonal neighbour. The DDA stepped one axis at a time and reported the
intervening *staircase* voxels, each touched with zero path length. From local (0.5, 0.375, 0.375)
along (1, 1, 1) on a 0.25 m grid it reported 6 voxels where 4 are entered. Reachable with any
axis-aligned or diagonal sun direction on a regular grid. Every axis whose boundary falls at the
same point along the ray now steps together.

**Defect 4, the lattice tolerance was not scale-aware.** `LatticeTolerance` was a fixed `1e-9` in
voxel units. The coincidence test runs on `q = p / voxelSize`, and `p` comes from
`ShadingVolume.TryToLocal` as a **difference of world coordinates**, so its error grows with how far
from the origin the model is sited — and an oblique aperture frame turns that into a three-term dot
product, so the cancellation is real rather than exact. Measured on a 37° facade:

| world offset | error in `q` | fixed 1e-9 fires? | scale-aware fires? |
|---|---|---|---|
| 0 | 2.0e-13 | yes | yes |
| 1e5 m | 1.0e-11 | yes | yes |
| 1.2e6 m | 4.9e-10 | yes (marginal) | yes |
| **1e7 m** | **4.8e-9** | **no** | yes |
| **1e8 m** | **3.4e-9** | **no** | yes |

Beyond roughly 1e6 m the test stops firing and the negative-direction start voxel reverts to exactly
the phantom it exists to prevent. This is reachable: UK OSGB northings run to ~1.2e6 m and UTM
southern-hemisphere zones carry a false northing of 1e7 m. The tolerance is now proportional to the
volume origin's world magnitude, expressed in voxels, **floored at the old 1e-9** so behaviour near
the origin is bit-for-bit unchanged.

Verified end to end: 165 ray/grid cases across three world offsets, every step-sign combination, and
origins on / just inside / just outside lattice planes, all match an independent positive-path-length
box traversal with **zero mismatches**.

**Not a defect: rays lying *in* a lattice plane.** A ray with a zero direction component whose origin
sits exactly on that axis's plane has zero cross-section with the voxels on both sides. Crediting
neither would delete a real ray's whole contribution; crediting both would double it. The traversal
credits the **lower-index** side, consistently, and this is documented as a deterministic tie-break
rather than asserted against — it is reachable in practice, since a due-south facade produces a sun
group with no across-facade component at all.

### 2.6 Context filtering — proven, not asserted

A voxel only receives energy from a sun group in which the cell is **lit** in the Stage 0–4
visibility cache. Measured on a south window with a 1.5 m slab 0.4 m above the head:

| | without context | with context |
|---|---|---|
| Positive potential | 10 943.9 kWh | 729.4 kWh |
| Region that would duplicate the slab | 3 358.4 kWh | 55.1 kWh |

### 2.7 Determinism

Sun groups are partitioned into contiguous, ordered ranges; each range accumulates into its own
sparse accumulator; accumulators are merged in range order. No per-voxel locking, no `Interlocked`.
The field is bit-identical across runs and thread counts.

### 2.8 Resolution

The field inherits the analysis resolution: `gridSize` in the aperture plane (one ray per analysis
cell, from its guaranteed-interior point, exactly as the visibility cache tests it) and
`sunAngleStep` in direction space (group-centre directions).

### 2.9 Analytical validation — Cases A–E

| Case | What it tests | Result |
|---|---|---|
| **A** south window | Overhang region positive, glass region negative | overhang band **+4122.2 kWh**, in-front winter path **−5408.7 kWh**, positive-score centroid at local **y = 0.761 m** (head = 0.5) |
| **A′** profile angle | Zero crossing against analytical bounds | see §2.10 |
| **B** east window | Lateral asymmetry vs a symmetric control | east **+X/−X = 34.1×**; south control asymmetric by **0.2 %** |
| **C** existing context | Duplicated potential collapses | §2.6 |
| **D** no unwanted solar | No positive region at all | `PositiveTotal = 0`, `MaxScore <= 0`, empty selection, `NaN` threshold |
| **E** no wanted solar | Interception never harmful | `MinScore = 0`; every lit cell's own voxel positive |

### 2.10 The profile-angle test — why a bracket replaced a tolerance

The plan asked for the field's zero crossing along the row just above the head to match the
closed-form Arumi-Noe cut-off `D = H / tan(VSA_cut)` within ~15 %. It measured **160 %** off, and
the tolerance, not the field, was the problem.

The closed form integrates over a **continuous** window. The field marches rays from analysis-cell
**centres**, so the deepest-reaching ray it can ever see aims at the topmost cell centre, not at
the window head. Just above the head that difference is first-order:

* lever arm to the head: `yRow − 0.5 = 0.026 m`
* lever arm to the topmost cell centre: `yRow − 0.5 + gridSize/2 = 0.151 m`

a **6× difference in the limiting profile angle**, which pushes the discrete crossing deeper.

Both bounds are computed per hour, straight from sun position and weather, touching neither the
DDA nor the sun binning nor the Stage 5 aggregation:

| | crossing |
|---|---|
| Continuous-window bound (lower) | **0.125 m** |
| **Field, gridSize 0.25** | **0.325 m** |
| Cell-sampled bound (upper) | **0.475 m** |

The field sits inside the bracket, and a voxel being a finite box rather than a point is why it
recovers part of the continuum instead of sitting at the top.

**Convergence** confirms this is discretisation rather than an artefact:

| gridSize | field crossing | error vs continuum |
|---|---|---|
| 0.25 | 0.325 m | 0.200 m |
| 0.125 | 0.225 m | 0.100 m |

The error **halves as the grid halves** — first-order convergence, exactly as the argument predicts.

### 2.11 Case B — the test that was asking for the wrong answer

The plan's Case B asserted that a vertical fin must beat an overhang on an east window. It does
not, and forcing it would have been wrong.

Most summer beam on an east facade arrives **near normal incidence** (azimuth ≈ 90°) at 30–40°
elevation — overhang territory. A fin only helps at azimuths far off normal, where
`cos(incidence)` is already small. Measured: fin **553.5 kWh** vs overhang **4231.1 kWh** total,
and **0.74 vs 1.57 kWh/voxel** — the overhang wins per voxel too, so it is not a region-size
artefact.

The east window's real signature is **lateral asymmetry**, which the test now measures against a
south-facing control that must come out even under solar-symmetric weather. That control is what
makes the result a physical statement rather than a coordinate accident.

---

## 3. Stage 7 — ideal shading geometry

### 3.1 Extraction algorithm: marching **tetrahedra**

The plan named marching cubes. The divergence is deliberate and is about correctness risk.

A cube has 256 sign configurations reducing to 15 base cases **plus genuinely ambiguous ones** —
a face with two diagonally opposite corners inside can be joined either way — and resolving those
consistently needs an extended table or asymptotic-decider logic. Getting it wrong punches holes in
the surface. A tetrahedron has 16 configurations, **all unambiguous**, each a single triangle or a
single quad.

The cost is roughly twice the triangles for the same lattice, which is irrelevant here because
**the mesh is never the source of truth**.

* Six tetrahedra per cube sharing the **0–7 main diagonal**, in fixed order, so neighbouring cubes
  split their shared face identically and the surface cannot crack.
* Every tetrahedron normalised to **positive orientation** before its case is emitted, so one
  winding rule covers everything.
* Vertices welded by **lattice edge** — keyed on the ordered pair of sample indices — so there is
  no tolerance-based point merging and no dependence on floating-point equality.

### 3.2 The winding rule (and the bug it caught)

With inside `{a,b}` and outside `{c,d}`, the four cut edges form the perimeter cycle
`(a-c, a-d, b-d, b-c)`. That cycle winds outward **exactly when `(a,b,c,d)` is an EVEN permutation
of `(0,1,2,3)`**. Two of the six two-in-two-out cases — `(0,2,1,3)` and `(1,3,0,2)` — are odd and
must be emitted reversed.

Emitting all six unreversed produced a **closed** surface that looked fine, but the two inverted
cases cancelled against the rest and the sphere came out at **58 % of its true volume**. Closure
alone does not prove a level set; volume does.

### 3.3 Threshold selection

| Method | Meaning |
|---|---|
| `Absolute` | A raw kWh score supplied by the caller |
| `CumulativeCapture` | Keep the top X % of the field's positive benefit |
| `MaxFraction` | A fraction of the maximum voxel score |

Plus optional `requireFacadeContact` (drop regions that never reach back to the aperture plane) and
`keepLargestRegionOnly`. Regions are 6-connected, size-ordered, deterministic.

### 3.4 The field remains the source of truth

Every number in `IdealShadingResult` — selected voxels, captured benefit fraction, projected area,
enclosed volume, max depth, region breakdown — is computed from the **scalar field**. The `Mesh3D`
is a derived, disposable display and take-off artefact. Meshing runs inside a guard: a failure
leaves `Mesh` null with `MeshFailureReason` set and **every number still populated**. An empty
field is an ordinary answer, not an error.

### 3.5 Validation

| Test | Result |
|---|---|
| Sphere (smooth field) | volume within **0.2 %** of `4/3 π r³`, **0 open edges** |
| Lattice box (binary field) | **1.31 %** volume deficit, 0 open edges |
| Two disconnected blobs | both meshed, **2.27 %** deficit, 0 open edges |
| Empty / degenerate input | empty mesh, never an exception or null chaos |
| Solar funnel | boundary profile angle **35.1°**, between winter **16.3°** and summer **60.7°** |

The box deficit is **expected and asserted to be a deficit**. A binary field carries no gradient,
so every cut — including the cube diagonals — lands at the midpoint, which **bevels convex
arrises** instead of reproducing a sharp 90° edge. Inherent to marching tetrahedra on a step
function, roughly `edge length × spacing²`. The sphere, where interpolation carries real
information, lands at 0.2 %.

The solar-funnel test is the analytical one: the extracted shape's upper boundary must reach far
enough to intercept the energy-weighted summer sun and stop short of the winter sun path. Both
reference angles are computed per hour from sun position and weather, independently of the field
and the mesh.

### 3.6 Defect fixed: threshold vs selector

`ThresholdForCumulativeCapture` documented the set `{ Score >= tau }` but `IdealShadingVoxels`
selects with strict `>`. The voxel that tipped the total over the target — and everything tied with
it — was dropped, so a request for 90 % of the benefit quietly returned slightly less. The
threshold now steps down to the next distinct score below the tipping one, so the strict selector
reproduces the documented set exactly.

---

## 4. Stage 8 — rationalised shading and performance

### 4.1 Typologies

| Typology | Parameters |
|---|---|
| `Overhang` | Depth, RiseAboveHead, ExtensionBeyondJambs |
| `HorizontalLouvres` | Depth, Count, TiltDegrees |
| `VerticalFins` | Depth, Count, TiltDegrees |
| `EggCrate` | Depth, LouvreCount, FinCount |

All parameters are bounded and clamp on assignment. Element Guids are **deterministic**, derived
from family + parameter values + ordinal. Random Guids would make candidates incomparable and
attribution caches unreusable; derived ones also guarantee that a geometry change moves the
attribution table hash.

### 4.2 Perforated screens are UNSUPPORTED, deliberately

A screen's whole point is partial transmission, and the direct ray engine under every stage here is
**binary**. The plausible shortcut — an opaque face plus a porosity scalar on the energy — is wrong
in a way that matters: real effective transmission depends on **incidence angle** and on the
**depth-to-opening ratio** of each perforation, and varies through the day by far more than the
nominal open-area ratio suggests. It would produce numbers that look like a screen's and are not,
in exactly the metrics an engineer would use to size one.

Until the engine can carry angle-dependent transmission, this family is absent and says so.

### 4.3 First-hit attribution

`Query.CellFirstHit` mirrors `CellVisibility` exactly — same sun-perpendicular projection plane,
same STRtree candidate narrowing, same `tolerance_Snap` ray-start offset so a ray running in a
coplanar occluder's plane cannot self-intersect it — but returns **which** face was hit first, from
`IntersectionTuples(segment3D, candidates, sort: true, tolerance)`. Verified in the SAM source:
`sort: true` orders ascending by distance from `segment3D[0]`, so `tuples[0]` is the physically
first interception.

Sentinels: `FirstHitVisible = -1`, `FirstHitBackFacing = -2`.

### 4.4 `SolarAttributionCache` — a separate structure

The Stage 0–4 visibility cache is **1 bit per cell per sun group** and is relied on upstream.
Widening it to carry occluder identity would multiply it by 32 and change a released on-disk
format for the benefit of one downstream stage. The attribution cache is built alongside and can be
discarded without touching the visibility result.

Storage: a **GUID table stored once** plus **one 32-bit index per sample** — 4 bytes, against the
16 a repeated GUID would cost. `int`, not `ushort`: a large model's context can exceed 65 535 faces
and silently wrapping a real building's attribution is not a trade worth two bytes.

Identity covers context hash, target hash **and a hash of the ordered GUID table**. Matching
geometry hashes are not enough: reordering the table leaves every stored index resolvable and
silently pointing at the wrong element. There is no lookup against a mutable `SolarModel` at read
time — attribution is only meaningful against the geometry it was built from, so that geometry's
identity travels with it.

### 4.5 The energy-accounting rule

```
V_A = rays admitted with existing context in place and NO candidate
      (exactly the Stage 0-4 lit bits of the base visibility cache)
```

**Only rays in `V_A` can be credited.** For each, the attribution cache — built over context **plus**
the candidate — says which face the sun met first. If it is a candidate element, that element is
credited. If it is a context face, the baseline and the attribution disagree about the same
geometry, which should be impossible; that energy goes to `UnattributedInterceptedEnergy` rather
than being folded into a total.

Because attribution is first-hit, **overlapping elements never double-count**: a ray stopped by a
louvre that would also have met the fin behind it credits the louvre alone. Measured on an egg
crate: per-element sum plus residual reconciles with the total to 1e-6, residual **exactly 0**.

### 4.6 Performance metrics

All **energy-weighted, in kWh**. Ray counts are never used as a percentage — a grazing December ray
and a normal-incidence June ray are not interchangeable.

| Metric | Definition |
|---|---|
| Direct Solar Intercepted [kWh] | Base-admitted beam the candidate now stops |
| Direct Shading Efficiency [%] | intercepted / unshaded admitted direct |
| Unwanted Solar Blocked [%] | intercepted unwanted / unshaded admitted unwanted |
| Wanted Solar Retained [%] | still-admitted wanted / unshaded admitted wanted |

**Every percentage returns `NaN` on a zero denominator.** "Wanted retained 100 %" when there is no
wanted solar reads as a device preserving all the useful sun; the honest answer is that the
question does not apply.

### 4.7 Rationalisation and the fit score

The depth sweep is **seeded from the field's own zero crossing** — the profile-angle construction
`D = H / tan(VSA_cut)` read off the physics rather than hard-coded — then sampled at multiples of
the seed. Measured seed for the test south window: **0.45 m**.

```
Score = UnwantedSolarIntercepted
      - wantedSolarPenalty x WantedSolarBlocked
      - materialPenalty    x MaterialFraction x AdmittedUnwantedEnergy
```

**All three terms are positive quantities and the two costs are SUBTRACTED.** The original draft
wrote `Capture + λ·Harm − μ·MaterialFraction`, which would have **rewarded** a device for
destroying winter sun. Names state what quantities *are* rather than which way they point —
`WantedSolarBlocked`, `wantedSolarPenalty` — and a test asserts that raising the penalty *lowers*
the score of a device that blocks wanted solar.

The material term is scaled by admitted unwanted energy so the penalty is in kWh and comparable to
the other two, rather than an arbitrary mix of units.

**The choice of *which* energy was re-examined at the Stage 9 gate review.** Scaling by an energy is
right — it is what makes the objective scale linearly with the site's radiation, so two identical
buildings under weather differing only in magnitude get the same device. Measured over a 300×
radiation change, the energy-scaled cost/benefit ratio moves by **0 %** while a bare-fraction cost
moves by **300×**. But `AdmittedUnwantedEnergy` specifically goes to **exactly zero** on an aperture
with no unwanted solar, taking the whole cost term with it, and that is precisely the case where the
right answer is "build nothing". Stage 8's behaviour is **unchanged** — it is only ever used on
apertures that have an unwanted-solar problem — and Stage 9 defaults to `AdmittedDirectEnergy`
instead. See `Stage9-Method.md` §3.

This is **not** Stage 9. It is a small, ordered, fully deterministic candidate evaluation.

### 4.8 Ideal vs rationalised — measured

2 m × 1 m south window, London, summer unwanted / winter wanted, `gridSize` 0.25, `voxelSize` 0.1.

**The ideal is represented by the boundary faces of its selected voxel set**, not by a plate at each
voxel centre. The plate stand-in was measured at the Stage 9 gate review and is badly biased: a
shallow ray crosses a voxel without ever meeting the single mid-height plate inside it. All three
representations of the *same* selection, through the *same* first-hit engine:

| representation | elements | intercepted | efficiency | unwanted blocked | wanted retained |
|---|---|---|---|---|---|
| plates (superseded) | 2005 | 1230.3 kWh | 45.5 % | 97.4 % | 98.7 % |
| **voxel solid** | 2500 | **1714.1 kWh** | **63.4 %** | **100 %** | **77.2 %** |
| Stage 7 mesh (triangles) | 13968 | 1026.5 kWh | 38.0 % | 88.1 % | 99.5 % |

Plates understate interception by **28.2 %** and overstate wanted-solar retention by **21.5 points**.
The voxel solid is the correct reference because the Stage 6 field credits a voxel when a ray
**enters** it, so the boundary surface of the selected voxels intercepts exactly the rays the field
counted. The extracted mesh is smaller again because the iso-surface interpolates *inside* the
boundary voxels; it is the shape a user is shown, and `Query.ShadingElements(IdealShadingResult)`
converts it for the ray engine when that is what is wanted.

The headline comparison, on the corrected representation:

| | Direct intercepted | Efficiency | Unwanted blocked | Wanted retained |
|---|---|---|---|---|
| **Ideal (Stage 7, voxel solid)** | 1714.1 kWh | 63.4 % | **100 %** | **77.2 %** |
| **Rationalised** (0.34 m overhang) | 688.7 kWh | 25.5 % | **59.5 %** | **94.2 %** |

Baseline admitted: direct **2704.1 kWh**, unwanted **450.9 kWh**, wanted **1091.2 kWh**.

Simplification cost: **40.5 points** of unwanted blocked, and simplification *gains* **17 points** of
wanted retained.

That sign is the interesting part and it was hidden by the plate representation. The ideal blocks
*everything* unwanted — and pays 22.8 points of winter sun for it. The Stage 6 field is a **per-voxel
marginal value**: it answers "how useful would material at *this* voxel be", independently. Filling
every above-threshold voxel is not a jointly optimised solid, and the union over-shades. This is the
direct argument for Stage 9 optimising against the energy objective rather than fitting geometry to
the Stage 7 mesh, and it is why Stage 9 is not required to beat the ideal.

### 4.9 Monotonic metric sanity

| Overhang depth | Intercepted | Efficiency | Unwanted blocked | Wanted retained |
|---|---|---|---|---|
| 0.2 m | 365.8 kWh | 13.5 % | 33.9 % | 100 % |
| 0.4 m | 770.5 kWh | 28.5 % | 63.0 % | 91.2 % |
| 0.6 m | 1093.6 kWh | 40.4 % | 80.4 % | 82.0 % |
| 1.0 m | 1385.5 kWh | 51.2 % | 85.1 % | 74.8 % |

---

## 5. SunAngleStep resolution study

Run with `SAM_SOLAR_BENCHMARKS=1`. Workload: 8 m × 4.5 m south wall, `gridSize` 0.12,
**2546 analysis cells**, 3 occluders — sized so raycasting rather than setup dominates. The earlier
small-ModelB timing was noise.

Accuracy is against an **exact per-hour baseline**: a bin size small enough that no two sun
positions share a bin gives every daylight hour its own bin (4264 bins over 4264 daylight hours),
which is by construction the per-hour `CellVisibility` answer, reachable through the public API.

| step | groups | cells | build ms | relative | disagreeing cell-hours | storage |
|---|---|---|---|---|---|---|
| 1° | 1362 | 2546 | 2518 | 1.36× | 0.605 % | 425.6 KiB |
| **2°** | **675** | **2546** | **1848** | **1.00×** | **1.111 %** | **210.9 KiB** |
| 5° | 252 | 2546 | 1412 | 0.76× | 2.781 % | 78.8 KiB |
| exact | 4264 | 2546 | 5456 | 2.95× | 0 % (by definition) | 1.3 MiB |

**2° remains justified and is unchanged.** 5° buys back only 24 % of the build time while
disagreeing 2.5× more often; 1° halves the error but costs 36 % more time and twice the storage.
2° is the knee.

**Caveat on the timing column.** Build time is *not* proportional to bin count here — about
**1.0 ms per bin plus 1161 ms fixed** for whole-year sun positions and cell preparation — because
three occluders cannot outweigh the fixed cost. On a real model with hundreds of occluders the
per-bin term dominates and the relative column approaches the ratio of the group counts, which
makes 5° more attractive on cost and leaves 2° still the better accuracy trade.

### 5.1 Attribution storage

| | per sample | 2546 cells × 675 groups |
|---|---|---|
| Visibility (Stage 0–4) | 1 bit | 210.9 KiB |
| Attribution (Stage 8) | 32 bits | ~6.6 MiB + 16 B/occluder |

**Ratio ≈ 32×.** This is why attribution is a separate, discardable cache rather than a widening of
the visibility format.

**Measured rather than extrapolated**, on an 8 m × 4.5 m south wall at `gridSize` 0.25 with a
36-element egg crate:

| SunGroups | cells | samples | elements | visibility storage | attribution storage | ratio |
|---|---|---|---|---|---|---|
| 675 | 576 | 388 800 | 12 | 47.5 kB | 1518.9 kB | **32×** |

Managed allocation during the build is asserted **≥ `StorageBytes`**, so the reported figure is a
real model of the cost rather than an underestimate that ignores the jagged array and object
headers. Only that one-sided claim is asserted: the counter (`GC.GetTotalAllocatedBytes`) is
process-wide and xUnit runs test classes concurrently, so the upper side is unusable in a full run.
Measured in isolation the ratio is about **2×** — the payload allocated once in the builder and again
by the cache's defensive clone. Build rate **0.745 M samples/s**.

**Attribution cannot be reused across candidates.** Every candidate's own faces are in the occluder
set, so the first hit changes with every parameter and the attribution table hash differs for every
candidate — verified, 5 distinct hashes over 5 depths. Only the **base visibility cache** is
reusable, and it is the expensive one to compute. Measured **46–66 ms per candidate** at 144 cells
(the spread is contention with concurrently running tests, not variance in the work). This is the
cost model Stage 9's search is built against.

---

## 6. Recorded resolutions and limitations

| Item | Value / status |
|---|---|
| GridSize (aperture analysis resolution) | Caller-set; 0.25–0.5 m in tests, 0.12 m in the scaling study |
| Voxel size (field resolution) | Caller-set; 0.05–0.25 m in tests |
| SunAngleStep (direction / attribution resolution) | **2° default, unchanged** |
| Inter-reflection | **None.** See §6.1 |
| Time resolution | **Hourly only** |
| Timezone | **A valid, resolvable TimeZone is required** |
| Perforated screens | **Unsupported** — see §4.2 |
| Interior solar penetration | **Not implemented.** See §6.2 |

### 6.1 No inter-reflection

The engine models **sun → blocked** or **sun → passes shade → aperture**. There are no secondary
reflected rays, no shade material reflectance and no multiple bounce. Intercepted solar must
**never** be described as "reflected solar". Future scope.

### 6.2 Interior floor/wall penetration — future, and additive

Not implemented. The future concept is sun → passes shade → aperture → first interior surface hit,
yielding floor/wall solar kWh and a floor-solar reduction percentage. The current external context
intentionally excludes interior and two-space panels, so a later interior pass uses a **different
geometry set**. The Stage 8 first-hit structures are designed so that this is **additive** — a
second attribution cache over the interior geometry set — rather than requiring redesign.

---

## 7. Release note (retained)

> A `Location` without valid/resolvable `TimeZone` information previously could silently calculate
> using Greenwich. It now **fails safely** and requires valid timezone information.

---

## 8. Divergences from `ShadingOptimisation-Plan.md`

| Plan | Built | Why |
|---|---|---|
| Stage 7 marching **cubes** | Marching **tetrahedra** | 16 unambiguous cases vs 256 with genuinely ambiguous configurations; the mesh is not the source of truth so the extra triangles do not matter (§3.1) |
| Stage 6 Case B: fin must beat overhang | Lateral **asymmetry** vs a symmetric control | The physics says otherwise for an east window and forcing it would be wrong (§2.11) |
| Stage 6 profile angle within 15 % | **Bracket** between two derived bounds, plus a convergence test | The 15 % gate compared a continuum quantity against a coarsely discretised one (§2.10) |
| Stage 8 `Score = Capture + λ·Harm − μ·Material` | `− wantedSolarPenalty × WantedSolarBlocked` | The draft sign would have rewarded destroying wanted solar (§4.7) |
| Stage 8 `PerforatedScreen` typology | **Omitted and documented** | Binary ray engine cannot express angle-dependent porosity (§4.2) |

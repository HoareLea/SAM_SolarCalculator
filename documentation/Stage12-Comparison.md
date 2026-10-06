# Stage 12 — Unified Shading Scheme Verification, Comparison and Selection

**Status:** implemented (`feature/unified-shading-comparison`)
**Supersedes:** the per-window `VerifyShading` workflow as the *decision* tool; `VerifyShading` remains the per-window ground truth.

## 1. What this stage answers

An engineer with several windows and several candidate shading designs must be able to answer,
defensibly:

> **Which of these do I build, by how much does it win, and on what evidence?**

Stages 9–11 answer that for ONE window at a time. They cannot answer it for a facade, because:

- per-window optimisation scores each window **independently** — a family total is a sum of
  separate searches, not one physical proposal;
- the grouped awning is scored **correctly** but on a different footing from the per-window
  families, with nothing proving the two are comparable;
- nothing measures **cross-shading** — one window's device shading its neighbour;
- nothing proves two options were assessed on the **same basis**;
- nothing records **why** an option was chosen.

Stage 12 builds exactly that: a common *scheme* object, one verification path that measures a
complete physical proposal against a complete aperture set, a comparability proof, a deterministic
ranking, a report — and, as a separate downstream step, the engineer's recorded decision.

## 2. The workflow

```
ApertureSolarTargets
        |
        +--> RationaliseShading  --> optimisedShadingResults
        |                                        |
        +--> RationaliseAwningGroup --> groupedAwningResults
                                                 |
                                                 v
                                    AssembleShadingSchemes
                                                 |
                                                 v
                                          ShadingScheme[]
                                                 |
                              +------------------+------------------+
                              |                                     |
                              v                                     v
                        VerifyShading                        CompareShading
                     (one scheme, detail)          (all schemes, ranked + report)
                                                                    |
                                                                    v
                                               analytical leader + report
                                                                    |
                                                                    v
                                                          SelectShadingScheme
                                                       (engineer records the choice)
                                                                    |
                                                                    v
                                            selection decision + final report
```

**The software analyses and ranks. The engineer decides and records why.** Rank 1 is the
**analytical leader** — the option with the highest verified score under the stated model,
objective and resolution — and is always reported. It is *not* an automatic engineering selection:
choosing a scheme to build is the separate `SelectShadingScheme` step, which records who chose what
and why, by `SchemeGuid`.

## 3. The objects

| Type | Role |
|---|---|
| `ShadingScheme` | One complete physical proposal over one exact aperture set. Carries devices, never geometry. Deterministic `SchemeGuid`. |
| `ShadingSchemePerformance` | The whole-scheme measurement: energies summed over members, percentages from summed energies, material charged once per physical element. |
| `VerifiedShadingSchemeResult` | The scheme, its measured performance and the analysis-basis signature. |
| `ShadingAnalysisSignature` | The comparable fingerprint of the analysis basis. |
| `ShadingAnalysisHours` | How much of the year the study looked at, and what the surroundings remove. |
| `ShadingComparisonRow` / `ShadingComparisonResult` | The ranked rows and the comparison outcome. |
| `ShadingSelectionDecision` | The engineer's recorded choice, alignment and reason. |

### Identity — I1 and I2

`SchemeGuid` is `MD5` over the name, the design method, the sorted aperture scope, and every
placement (in a fixed order) with its typology name and parameters iterated in the typology's own
`ParameterNames` order. It is independent of input order and contains no runtime data.

Scheme **elements** are re-identified per placement: `SchemeElementGuid(placementKey, elementGuid)`.
The typology's own `ElementGuid` hashes family + parameters + ordinal only, so three identical
overhangs on three windows collide; the scheme-scoped wrapping makes every physical element
distinct, which is what keeps the material accounting correct (I7) and per-element attribution
meaningful (I9).

### Verification — I3

Every member aperture is measured against the **complete** scheme element set through the existing
shared-elements accounting — no new physics. An overhang over window A that overhangs window B is
credited on B. The design-time score (the sum of the per-window searches) is carried separately and
never ranks; the gap between the two is the cross-shading blind spot this stage exists to surface.

## 4. Comparability — I8, I11, I12

`ShadingAnalysisSignature` is built entirely from the resolved `ApertureSolarContext` plus the
brief: aperture set and cell counts, context/target geometry hashes, grid size, sun-angle step,
year, time shift, site and time zone, a weather-series digest, and the brief's canonical JSON hash.
Two verified schemes are comparable exactly when every identity field matches; the first differing
field is named in the report. The weather comparison never goes by file name.

One solar context is built **per comparison**, never per scheme.

## 5. The ranking — I10, I11, I12, I13

Every row is scored under **one** supplied objective:

```
Score = UnwantedSolarIntercepted − λ · WantedSolarBlocked − μ · MaterialFraction · ReferenceEnergy
```

The ranking is a **total order**: score (descending, `double.CompareTo`), then device area, then
wanted solar blocked, then device count, then **No Shade first** (level 5 — the baseline wins any
exact tie through level 4), then `SchemeGuid`. `NaN` is excluded before sorting; a scheme with
unusable material and μ > 0 is `NotRankable` — unknown material is not free material.

The **No Shade baseline** is a real zero-device scheme verified through the identical path. Its
score is *computed* as 0, and its measured baseline is the anchor every other scheme's baseline is
checked against (I8). A scheme scoring below 0 cannot be the analytical leader while No Shade is
present.

**Recommendation confidence** is separate from the ranking and is never allowed to delete it:

| Status | Meaning |
|---|---|
| `READY` | Rank 1 is suitable to take forward on the evidence modelled (rare — needs confirmed convergence). |
| `PROVISIONAL` | Rank 1 stands, but open checks remain (grid coarser than recommended, resolution caps, budget exhaustion, decision-sensitive defaults, convergence not demonstrated, incomplete comparison). |
| `INDETERMINATE` | A ranking exists mathematically, but the leading options cannot be separated (the rank-1/rank-2 margin is within the documented sun-group quantisation indicator). Rank 1 is **still reported**. |
| `NO SHADING RECOMMENDED` | No Shade is rank 1. |
| `NO DECISION` | Nothing was comparable and rankable. |

## 6. The report

`reportMarkdown` leads with the decision and follows with the evidence:

1. **Engineering Recommendation** — analytical result, recommendation status, why rank 1 leads, what
   remains open, what must be done before design freeze, project inputs requiring confirmation,
   operating assumptions for retractable devices — and the engineering decision block.
2. **Scope and Analysis Basis**
3. **Hours Analysed** — the denominator of every energy.
4. **Numerical Resolution and Convergence** — aperture sampling, device-feature sampling, sun-group
   quantisation screening, grid convergence.
5. **Objective and Assumptions**, with the measured unshaded baseline.
6. **Complete Ranking** — every supplied scheme, plus the non-score-led physical answer per option.
7. **Analytical Leader — the Arithmetic**, with the energy balance and the per-aperture breakdown.
8. **Decision Robustness** — break-even λ and μ against every challenger, the axis diagrams, the
   STABLE / MARGINAL / SENSITIVE verdicts.
9. **Per-Window Design versus Whole-Scheme Verification** — ΔBenefit − λ·ΔHarm − μ·ΔCost.
10. **Excluded and Incomparable Options**
11. **Diagnostics** — baseline anchoring, objective match, unattributed energy, search effort.
12. **Fitness For Purpose, and What Is Not Covered**
13. **Reproducibility and Validation**
14. **Variables explained** — the glossary, rendered inside the report, always.

`reportCsv` carries one row per supplied scheme with `NaN` as an empty field. All report text is
generated in core (`Query.ShadingComparisonReport`) with `InvariantCulture`, no timestamps, and is
byte-identical between runs and across shuffled input (I11, I12).

## 7. The selection step

`SelectShadingScheme` records the engineer's decision downstream of the comparison:

- the scheme is identified by `SchemeGuid` — never by name or rank, because ranks change after
  recalculation while the identity is deterministic;
- only a **verified, comparable and rankable** scheme may be selected;
- a reason is **mandatory** when the chosen scheme is not rank 1; optional otherwise;
- the selection **never changes** scores, ranks or robustness figures;
- a stale selection (its `SchemeGuid` no longer in the comparison) is invalidated, not silently
  re-pointed;
- an `INDETERMINATE` comparison retains its deterministic ranks for audit but produces no single
  analytical recommendation; the engineer may still select explicitly, with a reason.

The final report always shows both the analytical leader and the engineering-selected option, their
alignment, the score and physical trade-offs between them, and the engineer's reason. Where the
alternative is preferred because λ, μ or the desirability brief does not represent the project,
those inputs should be revised and the comparison rerun rather than a manual override recorded —
manual selection is for what the objective does not model: capital cost, maintenance, aesthetics,
planning, structure, installation, product availability, operation and controls.

## 8. Invariants

| # | Invariant |
|---|---|
| I1 | `SchemeGuid` is deterministic and input-order independent. |
| I2 | Scheme elements carry unique per-placement GUIDs. |
| I3 | Verification measures the complete scheme against every aperture — not a sum of per-window verifications. |
| I4/I5 | Admitted and intercepted energies split into unwanted + wanted + neutral by construction. |
| I6 | Scheme ratios come from summed numerators over summed denominators. |
| I7 | Each physical element is charged once per scheme. |
| I8 | Every scheme's admitted baseline equals the No Shade scheme's. |
| I9 | Per-element energies plus the residual reconcile with the intercepted total. |
| I10 | The No Shade score is computed as 0, not asserted. |
| I11 | Ranking is a total order; reports are byte-identical between runs. |
| I12 | Input order changes no rank, no score, no report byte. |
| I13 | `NaN` never wins and never silently becomes a number. |
| I14 | Existing public behaviour is unchanged (see the backward-compatibility contract). |
| I15 | Hour counts balance: Timeline = Evaluated + Missing; Beam = Unwanted + Wanted + Neutral. |
| I16 | Baseline anchoring is reported against the No Shade scheme, or as unanchored. |

## 9. Notes recorded during implementation

- **Hours semantics.** `ApertureDesirability.EvaluatedHours` is a per-aperture *contributing*-hour
  count (front-facing sun above the horizon gate), so the scope-level evaluated count is derived as
  Timeline − Missing instead; the tests assert the desirability fields agree across apertures.
- **Front-facing sign.** `SunBin.RepresentativeDirection` is stored sun → surface, so "the sun is in
  front" is `dot(direction, outwardNormal) < 0` — the same convention the raycast uses.
- **Weather identity.** `WeatherYear.GetValues(WeatherDataType)` provides the stable ordered series
  used in the weather digest; absent series contribute "none" rather than a filename.
- **Known failure modes guarded by test:** duplicated targets, scope mismatches, element-GUID
  collisions, per-member material fractions leaking into scheme totals, `NaN` reaching the sort,
  mixed-bin hour double-counting, and culture-dependent formatting.

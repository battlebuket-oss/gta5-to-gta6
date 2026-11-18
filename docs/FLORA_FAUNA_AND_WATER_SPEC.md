# Flora, fauna and water realism implementation specification

## Product requirement

Deliver a GTA V Legacy Story Mode transformation that feels richer and more convincing while retaining the complete original ecological world. The product must preserve all original land and aquatic animal species, all original terrestrial and underwater plant species, all original model identities and population entries, and the identity/behaviour of existing water systems. It may add new species only as append-only additions.

## Workstreams

| Workstream | Required outcome | Release evidence |
|---|---|---|
| Vanilla ecology baseline | Exact-build inventory of every land/aquatic species, vegetation identity, model, population entry and habitat link | Reproducible inventory, source hashes, tool versions and signed digest |
| Water realism | Improve surface, waves, reflections, refraction, foam, shore interaction, underwater colour, caustics, depth attenuation and weather response without losing existing water-body behaviour | Before/after captures and in-game regression matrix for ocean, river, lake, pond and coast |
| Aquatic fauna | Add distinct fish and other aquatic species without replacing vanilla species or reusing their identities | Unique model/metadata IDs, habitat and population diff, animation/AI/collision tests |
| Underwater flora | Add seagrass, kelp-like vegetation, reeds, submerged/floating plants and suitable wetland flora | Unique asset IDs, placement/habitat review, underwater visibility and collision tests |
| Terrestrial flora | Expand region-appropriate trees, shrubs, grasses, flowers and ground cover | Original roster retained, new IDs unique, LOD and streaming tests |
| Terrestrial fauna | Add region-appropriate animals with distinct models and functioning animation/AI/habitat behaviour | Original roster retained, spawn-table append-only diff, AI and performance tests |
| Performance | Control streaming, draw calls, texture memory, LOD transitions and population density | Repeatable frame-time/VRAM/RAM benchmark on minimum and recommended hardware |
| Package governance | Licence, publisher identity, build compatibility, ecological review and rollback verified | Signed package metadata and reproducible QA record |

## Water quality acceptance criteria

- Preserve distinct ocean, river, lake, pond, pool and coastal water behaviour.
- Preserve existing water colour/depth relationships and weather/time-of-day response unless an improvement has passed side-by-side regression testing.
- Avoid excessive mirror-like reflections, incorrect refraction, uniform underwater colour, missing shore foam or unrealistic wave scale.
- Underwater visibility must respond plausibly to depth, lighting and environment.
- Caustics and volumetric light must not cause distracting flicker or severe performance regressions.
- Aquatic vegetation and animals must remain visible and correctly lit underwater, with no obvious surface clipping or floating/embedded geometry.
- Do not ship a water package that breaks mission scenes, scripted water interactions or existing water-body types.

## Species expansion acceptance criteria

- Existing vanilla species and their model identifiers remain present.
- Existing vanilla spawn/population entries remain unchanged.
- Every added species receives a unique identity; it cannot borrow or overwrite a vanilla identity.
- New population entries are additive and habitat-aware.
- Added assets include appropriate LODs, collision, animation, texture/material sets and streaming budgets.
- Density tuning cannot make original species disappear from practical gameplay.
- No species package is approved solely on the basis of a manifest statement or author-provided screenshot.

## Suggested implementation order

1. Build and freeze the exact-build vanilla inventory before touching ecology assets.
2. Implement and validate archive extraction/provenance adapters for supported GTA V asset formats; the parser-independent semantic snapshot comparator is now implemented.
3. Build water and ecology regression tooling and capture the unmodified baseline.
4. Evaluate water-only visual improvements first, because they can be tested separately from species changes.
5. Evaluate underwater vegetation and aquatic fauna as separate packages, then test their combinations.
6. Evaluate terrestrial flora and fauna additions in separate packages.
7. Run mission, world-streaming, performance and rollback tests.
8. Promote only individually verified, licensed and signed packages.

## Current status

This is a requirements and QA specification. It does not claim that additional species, water shaders or in-game transformations have already been installed. A parser-independent semantic inventory comparator and tests now exist, but the current Hub continues to block ecology-sensitive packages until trusted exact-build extraction, water baselines and package-preflight evidence integration are complete. The Windows app currently manages packages; it does not itself implement GTA V's in-game water shader, animal AI, vegetation renderer, title screen, pause menu, map or HUD.

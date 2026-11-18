# Flora, fauna and water preservation policy

## Non-negotiable biodiversity and water requirements

The transformation must retain the complete original GTA V Legacy ecological roster and the character of its water systems. It may expand biodiversity, but additions must be strictly additive.

The protected vanilla baseline includes:

- Every terrestrial animal species, model identity, species-specific behaviour and population entry.
- Every fish and other aquatic animal species, model identity, habitat association and population entry.
- Every plant, tree, shrub, flower, grass and terrestrial vegetation species and model identity.
- Every aquatic plant and underwater vegetation species, including submerged, floating, shoreline and wetland vegetation.
- Existing species-to-model, texture, animation, behaviour, habitat and population relationships.
- Existing water-system behaviour and visual identity, including surface appearance, wave response, reflections, refraction, foam, underwater visibility, colour, depth response and weather/time-of-day interaction.

New animal and plant species are allowed only as additions. A new species must never replace, rename, repurpose or remove a vanilla species/model. Existing vanilla population entries must remain intact; new population entries may be appended only when the exact-build verifier can prove that the original entries are unchanged. A package that cannot demonstrate this is blocked.

## Water realism target

The objective is to preserve and improve the existing water experience rather than replace it with an unrelated water system. Candidate improvements include:

- More physically coherent reflections and refraction while retaining the existing water body's identity and behaviour.
- Better wave detail, shore interaction, foam, ripples and wake response.
- More convincing underwater light attenuation, depth colour, visibility, caustics and volumetric light.
- Improved wet-surface materials, shoreline transitions and weather/time-of-day response.
- Higher-quality underwater terrain and environmental materials where supported by the game engine.

Water changes must be checked against oceans, rivers, lakes, ponds, pools and coastal areas. No package may flatten, remove or globally replace distinct water behaviours or make underwater visibility/colour inconsistent with depth and lighting.

## Flora and fauna expansion target

The approved design direction is to expand the world with additional species while retaining all vanilla species. Candidate categories for research and compatibility testing include:

### Aquatic and coastal fauna
- Additional fish species with distinct silhouettes, sizes, schooling and habitat preferences.
- Additional sharks, rays and other marine animals.
- Aquatic mammals and coastal wildlife where animation, AI, collision and habitat support are feasible.
- Freshwater and saltwater species kept in appropriate habitats.

### Aquatic and wetland flora
- Seagrass and other submerged vegetation.
- Kelp and other larger underwater plant forms where the setting supports them.
- Reeds, rushes, cattails and wetland plants.
- Floating plants, lily pads and shoreline vegetation.
- Coral/reef-like environmental assets only where they are technically and geographically appropriate.

### Terrestrial flora and fauna
- Additional region-appropriate trees, shrubs, grasses, flowers and ground cover.
- Additional terrestrial wildlife with distinct models, animation, AI, collision and population behaviour.
- Greater habitat variety and visual density without deleting or replacing original vegetation or wildlife.

These are design targets, not claims that any species or asset is already implemented or approved. Each candidate requires a licence review, exact-build compatibility test, performance test and ecological identity review.

## Acceptable changes

- Higher-resolution textures and physically based materials for the same existing species.
- Better fur, feathers, scales, bark, leaves, flowers, underwater surfaces and ground shading.
- Improved lighting, shadows, ambient occlusion, water reflections/refraction, caustics and weather response.
- Better LODs, geometry and animation for the same original species.
- Additional distinct flora and fauna species, provided they are added without replacing or removing any vanilla identity or original population entry.
- Carefully validated density and habitat expansion that preserves the existing species roster and spawn definitions.

## Prohibited changes

- Replacing one vanilla animal or plant species with another.
- Removing or disabling any original animal, fish, plant, tree, shrub, flower, grass or underwater vegetation species.
- Replacing vanilla models while retaining misleading names or identifiers.
- Removing, rewriting or repurposing existing vanilla population/spawn entries.
- Replacing the water system with a materially different system that loses original water-body behaviour or visual identity.
- Adding species by overwriting an existing species slot or reusing its model, metadata or population identity.
- Any package whose effects on species, population, habitat, water or model identity cannot be established.

## Package review contract

Any package whose impact areas or destination paths indicate ecology, wildlife, flora, fauna, vegetation, aquatic life, underwater assets, water, marine/coastal environments, population data, models, textures, maps, animations or metadata is ecology-sensitive.

A package must affirm preservation of all vanilla terrestrial and aquatic animal species, all vanilla plant and vegetation species, all vanilla model identifiers, all original population entries and the original water-system identity. If it adds species, it must explicitly attest that additions are append-only and do not overwrite or alter vanilla identities or entries.

A manifest assertion, inventory hash or evidence URL is not proof by itself. Approval requires a reproducible review against the exact supported GTA V Legacy build and recorded baseline inventory.

## Baseline inventory and verification

Before an ecology-sensitive package can be promoted to a tested release:

1. Capture the original terrestrial and aquatic species/model identifiers from a clean, verified GTA V Legacy installation.
2. Inventory land vegetation, aquatic vegetation, fish, marine animals, population tables, habitat associations, water materials, water configuration and relevant animations/metadata.
3. Record the game build, source archive/file hashes and extraction tool versions.
4. Compare every candidate package's model, texture, metadata, population, habitat, water and spawn-table changes against that baseline.
5. Reject every replacement, removal, identity collision, vanilla-entry mutation or unreviewed water-system regression.
6. For additive species, prove that all original identities and population entries remain byte-for-byte or semantically unchanged, and that new entries are unique additions.
7. Verify the visual result in-game across land, freshwater, ocean, coastal and underwater environments.
8. Store the inventory digest, toolchain versions, review record, test evidence and package digest with the signed manifest.

A SHA-256 digest establishes inventory integrity, not the truth of the review. The review record and in-game evidence remain mandatory.

## Current implementation boundary

The Hub currently classifies common GTA model, texture, map, metadata and animation formats, and now explicitly classifies aquatic, underwater, marine, fish, seagrass, kelp, coral, reef, water, ocean, river, lake, pond, coast, shore and submerged paths as ecology-sensitive.

The Hub now includes a parser-independent semantic inventory comparison engine. It can compare parser-produced snapshots and detect removed or altered vanilla species, model identities, habitats, population entries and water-body simulation/interaction identities. It also permits unique append-only terrestrial and aquatic species additions and controlled water rendering-profile changes.

Extracted Meta/PSO JSON payloads now receive a bounded structural summary in the metadata dump index. The summary records JSON object/array/scalar counts, maximum depth and capped property-name frequencies. It intentionally does not interpret a property called "animal", "fish", "water" or similar as proof of a species, habitat, spawn rule or water-system definition. This structural index is an aid for format research and parser development only.

The Hub still does not contain validated extractors for every proprietary RPF/model/metadata format, a trusted complete species inventory for each supported GTA V Legacy build, or a verified water-system baseline. The comparison engine is not yet fed by a trusted exact-build extractor or bound to package preflight. All ecology-sensitive packages therefore remain blocked regardless of manifest claims. The classifier, comparator and tests are safeguards; they do not yet prove compatibility of arbitrary game assets.

When in doubt, the package is blocked rather than assumed safe.

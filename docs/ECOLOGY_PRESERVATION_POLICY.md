# Ecological identity preservation

## Non-negotiable product requirement

The transformation preserves the original GTA V Legacy ecology. The goal is a more convincing rendering of the same world, not replacement of its biodiversity.

The following identities must remain the same as the installed GTA V Legacy baseline:

- Every animal species and its model identity.
- Every plant, tree, shrub, flower, grass and vegetation species and its model identity.
- Species-specific spawn/population tables and the original species roster.
- The identity links used by the game to associate models, textures, behaviours and population data.

Examples of acceptable work:
- Higher-resolution textures for an existing animal or plant.
- Better physically based materials, fur, feathers, bark, leaves and ground shading.
- Improved lighting, shadows, ambient occlusion and weather response.
- Improved animation fidelity, provided species identity and core behaviour remain intact.
- Better LODs and geometry for the same original species.
- Visual density tuning that does not delete or replace species.

Examples of prohibited work:
- Replacing one animal species with another.
- Adding new animal or plant species to the transformation's approved baseline.
- Removing original species.
- Replacing the original species population/spawn tables with unrelated populations.
- Replacing GTA V vegetation with a different ecosystem or unrelated species.
- Packages whose effects on species identity cannot be established.

## Package review contract

Any package whose declared impact areas or destination paths indicate ecology-sensitive assets must carry an EcologyPreservationContract. The current conservative classifier also requires review for GTA model, texture, map, metadata and animation asset extensions, even when a package author omits an ecology impact label.

The contract must affirm:
- ReviewStatus = Approved.
- Impact is limited to None, VisualOnly, DensityOnly, AudioOnly or BehaviourOnly.
- Vanilla animal, plant and vegetation species are preserved.
- Vanilla model identifiers are preserved.
- No new species, replacement, removal or species-spawn-table changes occur.
- A SHA-256 fingerprint exists for the reviewed vanilla identity inventory.
- An HTTPS evidence URI points to the review record.

A manifest assertion is not proof by itself: approval must be backed by a reproducible review against the exact supported GTA V build and a recorded baseline inventory.

## Baseline inventory and verification

Before any ecology-sensitive package can be promoted to a tested release:

1. Capture the original model and species identifiers from a clean, verified GTA V Legacy installation.
2. Record the game build, source archive/file hashes and extraction tool versions.
3. Compare the candidate package's model, metadata, population and spawn-table changes against that baseline.
4. Reject identity additions, replacements, removals or unreviewed population-table changes.
5. Verify visual-only assets in-game for the same species.
6. Store the inventory digest and review evidence with the package's signed manifest.

A SHA-256 digest establishes inventory integrity, not the truth of the review. The review record and test evidence remain mandatory.

## Current implementation boundary

The Hub now has a manifest contract and preflight enforcement for ecology-sensitive packages. It does not yet contain a complete extractor for every proprietary RPF archive, a complete species inventory for every GTA V Legacy build, or an automated binary model/metadata semantic diff. Therefore, no package should be marked ecology-approved until those missing verification capabilities have been completed and run on the exact target build.

When in doubt, the package is blocked rather than assumed safe.

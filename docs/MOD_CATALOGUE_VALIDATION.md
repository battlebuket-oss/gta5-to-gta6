# Mod catalogue validation and approval gates

The catalogue is descriptive research data; it is not an installer feed. The Windows CI pipeline runs `scripts/Validate-Catalogue.ps1` against the catalogue before packaging.

## Enforced invariants

- JSON parses and the supported catalogue schema version is explicit.
- Category and package IDs are unique; every package references a declared category.
- Every package has attribution, a source, a licence statement and a status.
- Every source is an absolute HTTPS URI.
- Dependency/conflict/external-requirement lists cannot contain empty or duplicate values.
- Dependencies and profile selections must reference existing package IDs.
- An `Approved` entry must have a 64-character SHA-256, numeric minimum and maximum game builds, an implemented adapter, and no unresolved licence language.

The approval gate is intentionally strict. A package must not be promoted to Approved merely because a download page exists. The archive itself still needs inspection, exact-build support evidence, a complete file manifest, dependency and conflict review, licence clearance, a reversible deployment plan and real-game test evidence.

## Current limitation

This validator checks catalogue metadata consistency. It does not download or inspect third-party archives, establish author permission, authenticate an external source, prove game compatibility, or run gameplay tests. Those remain separate approval requirements. CI success means the catalogue is internally consistent, not that its packages are safe or compatible.

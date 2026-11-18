# Mod Catalogue and Distribution Policy

## What is pre-installed in the platform
The application includes a built-in catalogue database, category definitions, compatibility metadata, curated profile templates, and verified source links. The catalogue is visible immediately after app installation.

## What is not automatically bundled
Third-party mod archives, game assets, proprietary Rockstar files, and paid mod files are not bundled unless redistribution is explicitly permitted by their rights holder. A catalogue entry does not imply that the binary is included.

## Package acquisition
Each entry records:
- Stable package ID and display name
- Author and original project page
- Licence and redistribution permission
- Version and supported GTA V build range
- Supported mod loader/runtime
- Package URL or user import instructions
- SHA-256 digest for the exact release artifact, when available
- File manifest and target paths
- Dependencies and conflicts
- Install adapter and uninstall/restore instructions
- Performance tier and known issues
- Verification status and last review date

Downloads must use HTTPS, validate the expected host, enforce size limits, verify SHA-256, reject path traversal and unsafe archive entries, and never execute an installer silently. If a trustworthy digest or compatible install procedure is unavailable, the package is marked unverified and cannot be applied automatically.

## Catalogue states
- Planned: candidate only; not downloadable or installable.
- Researching: source and compatibility review in progress.
- Verified: source, licence, version, install and restore paths reviewed.
- Tested: verified on a documented game build and test environment.
- Deprecated: no longer recommended; existing installation can be removed safely.
- Blocked: unsafe, incompatible, unlicensed for the proposed distribution, or otherwise unsuitable.

## User control
The app explains what each package changes before installation. Users can inspect source and licence details. No package is installed or enabled without an explicit user action.

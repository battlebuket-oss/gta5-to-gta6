# Package acquisition and OIV support

## Direct downloads

The desktop Hub accepts a direct HTTPS URL to a ZIP or OIV package. It does not scrape HTML mod pages or bypass author-controlled download flows.

The downloader:
- Requires HTTPS on port 443.
- Rejects localhost and local/private DNS destinations.
- Revalidates every redirect.
- Limits redirects to five.
- Limits the download to 2 GiB.
- Computes SHA-256 while streaming to a temporary file.
- Atomically renames a completed download.
- Can validate an expected SHA-256 digest when supplied by a future signed catalogue.

A download hash proves byte integrity only. It does not establish publisher identity, licence permission, malware safety or GTA build compatibility.

## OIV import boundary

The OIV importer supports only OIV 2.2 packages targeting GTA V whose content instructions consist exclusively of direct loose-file add operations.

It rejects:
- RPF archive edits.
- Delete operations.
- XML/text patch operations.
- Script or other command types.
- Ambiguous/missing payload paths.
- Unsafe paths, links, duplicate destinations and excessive archive sizes.

Imported OIV packages are marked compatibility-unverified and cannot be deployed until a separate review process verifies the exact GTA V Legacy build and package contents.

This is deliberate. OIV's full installation semantics are broader than a safe copy-only installer. A later RPF adapter must use a vetted parser/editor, operate on backups, verify the output archive, and have its own integration tests. It must not execute an OIV installer script.

## Still required for a production release

- Signed catalogue metadata and signed update manifests.
- Publisher identity verification and licence/redistribution checks.
- DNS-pinned outbound connections to close DNS-rebinding gaps.
- A complete, audited OIV/RPF adapter for supported operations.
- Real installation tests against clean GTA V Legacy builds.

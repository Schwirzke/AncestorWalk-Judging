# Judging snapshot privacy review

Prepared November 4, 2026. This snapshot was assembled from selected working
files into a fresh Git repository. No development Git objects or history were
copied. It has not been published by this preparation step.

## Included

- Unity application source, project/package settings and required assets.
- Fictional GEDCOM examples, 74 fictional-family historical panoramas and eight
  generated fictional portraits, with provenance and verified image hashes.
- Setup instructions, the MIT license and third-party notices.
- Existing synthetic tests; the private-family import test harness is excluded.

## Excluded

Backend, development history, standalone helper/authoring scripts, helper credentials/configuration,
Keychain tools, private family archives/photos/caches, build artifacts, logs,
Unity Library/Temp/UserSettings, local editor layout, benchmark assets and private
working documents. The retired submission repository was not used.

## Checks performed

- Byte-pattern scans for common API/access-token and private-key formats,
  private machine paths, known local identifiers, and credential file extensions.
- Additional review for literal secret assignments, JWTs and email addresses.
  The only textual email matches are a retained font license contact and a
  synthetic example.com test URL.
- All demo panorama, preview and portrait SHA-256 hashes match the bundle manifest.
- The original 81 PNGs retain their reviewed standard XMP identifying the
  images as AI-generated. No EXIF chunks were found. AI-origin provenance is kept.
- No serialized asset GUID references point to assets excluded from the snapshot.
- Demo binaries use Git LFS; source and judge documents remain normal Git files.

No private data or live credentials were identified in this reviewed snapshot.
Pattern scanning is not a proof that arbitrary secrets can never occur; future
changes must be reviewed and rescanned before publishing. Do not add personal
imports, local helper settings, caches, environment files or signing keys.

This review covers source privacy and bundle integrity. A fresh-machine Unity
import and final Quest installation/live-key walkthrough remain separate release
checks. Publishing requires a final review of the exact staged files and use of
a public-safe commit author email.

## Persistent demo checkpoint

The authored snapshot includes 37 fictional people, 74 panoramas (including the
already-saved Maya 2022 scene), eight photos, locations and exact-year settings.
The additional scene has no text/EXIF chunks. The version-three manifest pins the
authored document by SHA-256. New demo media is stripped of text/EXIF metadata;
local photo paths and raw source archive text are excluded. Private/copied trees,
credentials, development history and Backend remain excluded.

Editor authoring is opt-in under Tools → Ancestor Walk → Demo Authoring. Saved
demo changes become ordinary working-tree files; saving does not commit or push.
Any later edits require review before publication.

## Final staging review — November 4

The updated working tree was rescanned before staging. Its authored people match
the fictional GEDCOM, all eight portrait hashes match the reviewed fictional
catalog, and original photo paths and raw archive text are absent. The current
74 scene images and previews and all eight portraits match the bundle manifest;
the authored document also matches its pinned SHA-256. Only the reviewed
fictional-demo authoring code/data and presentation updates were added.

The exact staged index is verified against the local audit manifest, including
all 156 Git LFS payload hashes and sizes. There are no commits or remotes. Later
demo saves may update the working tree automatically and require another audit.

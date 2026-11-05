# Ancestor Walk

**Life is but a dream.**

Ancestor Walk turns a family tree into an immersive journey through people,
places and time, for Meta Quest 3/3S and desktop exploration in Unity.
Family records anchor the journey; generated scenery is an imaginative historical
interpretation, not archival evidence or proof of an ancestor's lived experience.

## Start here

Read the [judge instructions](Docs/JudgeInstructions.md) for installation,
controls, API-key setup and a complete sample journey.

Watch the [three-minute demo](https://youtu.be/vuxJsWuUHrw).

Open this project with **Unity 6000.6.2f1**, open
`Assets/AAA/WordChain.unity`, and press Play. Install Git LFS and run
`git lfs pull` before opening Unity so the fictional demo media is downloaded.

Choose **Trees → Fictional demo** to explore Hartwell Through Time: 37 fictional
people, 74 bundled historical panoramas and eight fictional portraits. These
saved scenes work without an API key. Use **Copy demo to edit** for live tests.

For live AI, enter your own Meta Model API key in **Settings → Scenes → AI Setup**.
AI Setup opens at startup without a valid session key; close it to browse saved
scenes. It opens again when an AI action needs a key. Quest starts in full VR;
passthrough is available from the room toggle. The VR API panel includes an
attached keyboard with Shift, symbols, caret movement, correction and reveal controls.
Quest uses Unity Activity for native keyboard compatibility in other text fields.
Requests use HTTPS directly from Unity. The key is session-only. No credentials,
private family archives, private photographs, author scene caches, local backend,
or development Git history are supplied. Optional developer helper source and
its setup windows are omitted; the direct API journey does not require them.

## Meta models

- `muse-spark-1.3`: researched setting briefs and historical question answering.
- `muse-image-1.0`: panoramas and photo/exact-year panorama edits.
- `muse-voice-transcribe-1.0`: spoken locations, details and questions to text.
- `sam-3.1`: implemented cutout integration; not part of the verified core demo.

No model weights need to be installed. API access and quota are provided by the
evaluator. Offline timeline transitions blend saved images; they do not generate
an independently researched image for every intermediate year.

## Data and artwork

The bundled family records and portraits are entirely fictional. Demo panorama
and portrait provenance is recorded in
`Assets/StreamingAssets/AncestorWalkDemo/manifest.json`. The Meta demo content is
separate from the original OpenAI ImageGen symbolic/backdrop artwork:
[tree panorama](Docs/tree-panorama.txt), [All Time](Docs/all-time-environment.txt),
and [original Clara environment](Docs/clara-environment.txt).

The supplied GEDCOM files in Samples are fictional. Importing your own family
archive is optional and unnecessary for judging. Do not commit imported family
data, photographs, generated personal caches or API credentials.

## License and notices

This is a judging snapshot under the [Proprietary Evaluation License](LICENSE),
not an open-source license. Public visibility does not grant general reuse rights.
Third-party licenses remain applicable. Preserve the font notices under Assets
and the [SAM codec notice](Docs/ThirdParty/MetaSam-NOTICE.txt) and its accompanying
license. Unity packages are restored according to Packages/manifest.json and
retain their respective licenses.

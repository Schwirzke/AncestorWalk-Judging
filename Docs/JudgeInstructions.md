# Ancestor Walk — judge instructions

Life is but a dream. A fictional family journey through people, places and time.

Prepared November 4, 2026 for the Meta AI submission. Use this source checkout
for the desktop route. A final Quest APK and candidate-build validation remain
pending.

Watch the [demo video](https://youtu.be/vuxJsWuUHrw). It shows desktop Unity
capture with fictional records, prerecorded speech, synthetic narration and
shortened waits identified in the presentation.

## Install and launch

### Desktop through Unity Editor

1. Obtain the authorized project checkout. Install Git LFS and run `git lfs pull`
   in that checkout so the bundled images are actual binary files, not pointers.
2. Install Unity **6000.6.2f1** through Unity Hub. Add the `AncestorWalk` project
   folder (the folder containing Assets, Packages and ProjectSettings).
3. Open it, let Unity restore the packages and finish importing, then open
   `Assets/AAA/WordChain.unity` and press **Play**. Focus the Game view for controls.
4. Open **Trees → Fictional demo**. The tree is named
   **Hartwell Through Time · fictional**. Fresh installs start with the demo;
   an existing installation may restore its last saved tree instead.

AI Setup opens on launch without a valid session key. Close it for the offline
journey below; saved scenes do not require a key.

The demo bundle contains about 340 MiB of images/previews. A first import can take
longer than later launches. A headset, Python service, backend and locally
installed AI models are not needed for the desktop demo. A clean-machine package
restore still needs verification against the final delivery revision.

### Optional Quest 3 / 3S route

Use only the final supplied judging APK, when one is designated. With developer
mode enabled and a USB debugging connection authorized on the headset, install
that APK using Meta Quest Developer Hub's APK installation interface, then launch
Ancestor Walk from the headset's **Unknown Sources** app list. Use Touch controllers.
Allow microphone access if testing voice input; typed input remains available.

A final APK/link and fresh-headset validation are pending; historical development
APKs are not designated judging builds. Judges do not need to build Android for
the desktop route. Building from source for Quest additionally requires Unity's
Android Build Support, SDK/NDK and OpenJDK modules.

## Essential controls

| Action | Desktop | Quest controllers |
| --- | --- | --- |
| Select a person / approach their node | Left-click the person's cube | Point and press trigger |
| Enter a historical environment | Click the person's scene portal | Point at the portal and press trigger |
| Select a year | Click and drag the timeline | Point, hold trigger and drag |
| Look around | Hold right mouse and move; scene arrow keys also look | Turn your head; right stick turns |
| Move in default Fly mode | WASD; Q/E down/up; Shift faster | Left stick moves relative to look direction |
| Return to the tree | Back | Back |
| Reset the tree view | Home | Home |
| Open tutorial | ? Help or H | ? Help |
| Open tree library | Trees or T | Trees |
| Open settings | Settings or P | Settings |

Movement presets can be changed; Help adapts to the selected preset. Back returns
to the prior tree view; Home resets it. In a scene, the **clock icon** toggles the
timeline. Gold year markers indicate saved scenes. Dragging browses/blends saved
images and does not request new generation. An intermediate year is not proof
that a separate image was generated for that year.

Each launch starts in full VR. Use the HUD's room toggle for passthrough.
Passthrough scene opacity defaults to 25%; adjust **Settings → Scenes →
Passthrough scene opacity** if the environment is too faint.

## First journey — no API key required

1. Open **Trees → Fictional demo** and explore the connected family. All 37 people
   and eight portraits are fictional. The tree spans 15 generation rows, back to 1600.
2. Find **Clara Whitfield**, approach her node and enter her scene portal.
3. Select the saved **1915 Edinburgh** scene, then **2002 London**. Look around
   each environment and drag through the timeline to see the saved-image blend.
4. Use **Back** to return to the tree and follow a connection to another generation.

This judging snapshot bundles 74 panoramas and eight portraits. Generated environments and portraits are
imaginative reconstructions, not archival family evidence. The built-in demo is
read-only. Choose **Copy demo to edit** before trying new locations or photo edits;
the independent copy retains the demo's scenes, portraits and dated moves.

## Enable live AI with your own key

1. Have a Meta Model API key with access to the models below and available quota.
   A normal Meta/Quest account alone does not establish model API access.
2. AI Setup opens at startup and when an AI action needs a valid key. You can
   also open **Settings → Scenes → AI Setup**. Use the attached keyboard in VR, or type/paste on desktop,
   then choose **Test & use key**. This checks the model catalog; it does not
   generate an image. Successful catalog access does not guarantee every model's
   availability or remaining generation quota.
   In VR, the keyboard sits directly beneath the form at 1.2 metres. The number
   row stays numeric with **Shift** on; symbols have a separate row. Use **Shift**
   for uppercase, **Left/Right** for the caret, and **Backspace/Clear** to correct.
   Keys highlight on hover and flash on tap. The eye button reveals or masks text.
   Failed validation retains your entry for correction; success activates the
   session key and clears the entry. **Passthrough / Full VR** changes the view
   temporarily; closing setup restores the previous viewing mode.
3. Use **Test current key** to check the session, or **Forget key** after AI work
   has finished. The key lasts only for this app/Play session and is not saved in
   assets, builds, PlayerPrefs, scene caches or logs. Closing the panel clears
   its entry box, not the active session key.

New requests use direct HTTPS from Unity to `https://api.meta.ai/v1/`.
Quest builds use your session key; no local server is required.
Live generation/transcription needs internet access
and consumes the key's provider quota; browsing bundled scenes does not.

If Meta rejects session credentials or model access, AI Setup opens again. Test
a replacement key before retrying. Unfinished location text is retained while
you enter the key.

| Model ID | Role |
| --- | --- |
| `muse-spark-1.3` | Web-researched setting briefs and contextual historical answers |
| `muse-image-1.0` | Panorama generation and photograph/exact-year panorama edits |
| `muse-voice-transcribe-1.0` | Spoken locations, scene details and questions to text |
| `sam-3.1` | Implemented photo cutout integration; live end-to-end verification pending, outside the core walkthrough |

No model weights need to be downloaded or installed. MusicGen and SeamlessM4T are
not dependencies of this judging journey.

## Try a new scene and a question

1. Choose **Copy demo to edit**, return to Clara, enter her scene and select **1940**.
   This year is a new live test, not one of her two bundled endpoints.
2. Open **Edit Location** from the scene timeline. Type **Covent Garden, London**,
   or tap the Location microphone, speak those words and pause for 1.5 seconds
   to finish automatically. Tap again to finish sooner; Scene details works the same way.
   Review the transcription before continuing. Scene details are optional.
3. Choose **Generate**. Keep the selected tree open while research, generation
   and saving finish. Inspect the actual returned scene; output and timing vary.
4. Optionally open Edit Location's photo option, choose Clara's fictional 1940
   portrait and generate an edit of the existing 1940 scene. This is a separate
   model request. Different-year reference editing is currently disabled.
5. Open **Ask** and type “What would everyday life have been like around here in
   1940?” then select Ask. Alternatively tap its microphone, speak the question,
   and pause for 1.5 seconds to finish and send (or tap again to finish sooner).
   Read the returned text. Changing the year or
   leaving the scene cancels the active question. Audio playback and visible
   citation controls are not part of this walkthrough.
6. Return to a completed saved year to reuse its scene without regenerating it.
   Use **Regenerate** only when intentionally requesting a replacement.

## Troubleshooting

- **No bundled images:** confirm Git LFS downloaded the real files before opening
  Unity, and select the fictional demo rather than an unrelated saved tree.
- **Key/access error:** reopen AI Setup. Check the key, model access and quota.
  No shared developer credential is included.
- **Voice unavailable:** allow microphone permission, or type the same input.
- **Rate limit or interrupted request:** follow the displayed status and wait.
  Avoid repeatedly pressing Generate; a request already sent may still finish.
  Existing saved imagery is preserved after failures.
- **Controls seem captured:** close an editor/tutorial first, then focus the
  Game view. Use Help for the active movement preset.

## Delivery verification still required

Before distributing these instructions, attach the approved source/build and
video links, record the candidate revision, and run this journey from a fresh
checkout/install. Editor live-call evidence is available in development records;
it does not establish that the final Quest APK has passed the direct-key journey.

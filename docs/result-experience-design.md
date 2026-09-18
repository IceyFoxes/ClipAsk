# ClipAsk Result Experience

## Status

Implementation in progress. The result surface now uses tested adaptive stacked/split geometry, proportional previews, capture-monitor centering, a compact draggable header, bounded growth while streaming or after a manual move, taskbar minimization, explicit PNG saving with a remembered destination folder, and Markdown/LaTeX response rendering. Native system-theme materials, backdrop integration, motion, and the full accessibility verification matrix remain to be implemented.

## Product thesis

**Select anything. Ask only when you need to.**

ClipAsk is a screenshot-to-response utility connected to the user's ChatGPT account, not a small chat application. Its default path removes the context switch, paste, and prompt-writing steps from a familiar screenshot gesture:

```text
Ctrl+Alt+S → left-drag → release → useful response appears centered on the capture monitor
```

When the picture alone is not enough, the same gesture has an intentional alternate path:

```text
Ctrl+Alt+S → right-drag → add one instruction → response appears centered on the capture monitor
```

The experience must feel lighter than opening an AI chat even while remote inference is still running. It should communicate immediacy through spatial continuity and honest feedback, not fake progress or decorative motion.

## Audience and promise

The initial audience is people who repeatedly encounter questions, errors, charts, interfaces, images, or passages on screen and want a useful response without leaving their current task.

The promise is:

> Select anything on screen. Get a useful response right there.

This requires the product to be understandable without onboarding, quiet when not needed, trustworthy about uploads, and visually polished enough that installing it feels worthwhile.

## Goals

1. Preserve the capture's natural proportions and legibility.
2. Make the result feel physically connected to the selected region.
3. Show useful local feedback immediately without delaying the model request.
4. Put the screenshot and answer above application chrome.
5. Use restrained, contemporary Windows materials, typography, and motion.
6. Remain usable with transparency disabled, high contrast enabled, keyboard-only input, and mixed-DPI displays.
7. Keep the existing WPF capture and provider pipeline unless a demonstrated platform limitation justifies replacing the presentation layer.

## Non-goals for this pass

- A chat transcript or conversation history.
- Follow-up composition as a primary interaction.
- OCR tools, screenshot editing, annotations, or local models.
- Multiple providers or a model selector in the primary capture path. Eligible account-advertised models may be chosen in the secondary Response options surface.
- Speculative uploads before the user releases a valid selection.
- Changes to ordinary Print Screen behavior.
- Hiding provider latency with fake percentages or fabricated stages.

## Design principles

### Content, not containers

The current fixed thumbnail, header, footer, borders, and text buttons make the interface read as a dashboard. The redesign should feel like two pieces of content—the capture and its answer—with only the controls needed at that moment.

### Natural geometry

The image must never be distorted or forced into a generic square. The capture's aspect ratio determines the result layout and preferred window size. Minimalism means removing unnecessary chrome, not making source material too small to inspect.

### Spatial continuity

The result opens in the center of the work area on the monitor where the screenshot was captured. It remains centered through automatic response-height changes unless the user drags it, and automatic captures do not steal focus. The captured preview preserves the relationship between source and response after the selection overlay disappears.

### Calm during real latency

Observed time to first text is approximately four to six seconds and is primarily outside the presentation layer. The result surface should appear immediately, preserve its geometry, and use subtle indeterminate activity. Animation must never delay capture encoding, request submission, or text rendering.

### Native rather than imitative

“Apple-like” means disciplined hierarchy, precise spacing, natural motion, and visual restraint—not copying macOS controls. The implementation should use Windows system theme, native compositor materials, and familiar accessibility behavior.

### Trust by default

The selected image remains local until the user commits the applicable action: releasing a left-drag for automatic analysis, or sending the instruction after a right-drag. Connection and account details stay available but out of the main path. Errors are stated plainly. The UI must not imply that cancellation can undo an image already sent.

## Core interaction

### Connected path

1. The user presses `Ctrl+Alt+S`.
2. The current display freezes under the selection overlay.
3. The user left-drags a region for automatic analysis.
4. On release, the crop is committed and copied to the clipboard.
5. The result surface appears beside the crop immediately.
6. PNG encoding and the analysis request proceed without another click.
7. A restrained activity state occupies the reserved answer plane.
8. The first answer text replaces that state as soon as it arrives.
9. The user may copy, dismiss, retry, or start another capture.

### Prompted path

1. The user right-drags a region in the same overlay.
2. On release, ClipAsk displays the crop with a focused one-line instruction field.
3. The crop remains local while the user types; no model request starts yet.
4. Pressing `Enter` or **Send** submits the image and instruction as one request.
5. The composer gives way to the same streaming response surface as the automatic path.

### Disconnected path

A capture is still preserved and displayed. The answer plane contains one clear **Connect ChatGPT** action. Successful browser sign-in returns to the same capture so the user can start the answer without recapturing it.

### Cancellation

While answering, the secondary action becomes **Stop**. Dismissal also cancels the active local request. A later capture invalidates every earlier response so text can never attach to the wrong image.

## Adaptive geometry

### Coordinate model

Layout is calculated in device-independent pixels (DIPs). Convert the captured pixel dimensions using the DPI of the capture monitor:

```text
sourceWidthDip  = pixelWidth  × 96 / monitorDpi
sourceHeightDip = pixelHeight × 96 / monitorDpi
aspectRatio     = sourceWidthDip / sourceHeightDip
```

Use the monitor work area, not the full monitor bounds, for size and placement constraints.

### Invariants

- Preserve source aspect ratio exactly.
- Never upscale the image above its natural DIP size in the initial result.
- Keep the whole window inside the monitor work area with a 16 DIP safe margin.
- Let the response pane use the natural content width of a stacked window; split layouts may cap the text column at 620 DIPs.
- Reserve the answer plane before streaming begins; incoming tokens must not cause horizontal relayout.
- Prefer avoiding the selected rectangle. If no side fits, choose the candidate with the least overlap and keep the result fully visible.
- Grow the response plane in coalesced, content-measured steps while text streams, capped by the monitor work area; once capped, scroll within the answer plane.

### Layout modes

#### Banner / wide capture

Use a stacked composition when `aspectRatio >= 1.35` or the natural source height is at most 180 DIPs.

```text
╭────────────────────────────────────────────╮
│ screenshot at its own aspect ratio         │
├────────────────────────────────────────────┤
│ Answer text                         actions │
╰────────────────────────────────────────────╯
```

The image spans the available media width, with its height derived from its aspect ratio. The surface may grow wider than an ordinary answer card so a thin horizontal capture is not compressed into a thumbnail.

Proposed constraints:

```text
bannerMaxWidth = min(1200, workAreaWidth - 32)
cardWidth      = clamp(sourceWidthDip + 32, 360, bannerMaxWidth)
mediaWidth     = min(sourceWidthDip, cardWidth - 32)
mediaHeight    = sourceHeightDip × mediaWidth / sourceWidthDip
answerHeight   = clamp(measuredAnswerHeight, 112, 260)
```

A capture that was 900 DIPs wide should therefore remain approximately 900 DIPs wide when the work area permits. An extremely wide capture scales to the work area rather than into a narrow side column.

#### Portrait / tall capture

Use a split composition when `aspectRatio <= 0.80` and at least 280 DIPs remain for the answer.

```text
╭──────────────────┬─────────────────────────╮
│                  │ Answer text             │
│ screenshot       │                         │
│                  │                 actions │
╰──────────────────┴─────────────────────────╯
```

Proposed constraints:

```text
cardMaxWidth     = min(760, workAreaWidth - 32)
cardMaxHeight    = min(640, workAreaHeight - 32)
answerMinWidth   = 280
previewMaxWidth  = min(sourceWidthDip, 0.44 × cardMaxWidth)
previewMaxHeight = cardMaxHeight - 32
previewScale     = min(1, previewMaxWidth / sourceWidthDip,
                          previewMaxHeight / sourceHeightDip)
```

The preview column is derived from the scaled image width; it is never a fixed thumbnail width.

#### Balanced capture

For captures between the two thresholds, calculate both stacked and split candidates. Choose the candidate with the larger preview scale, provided the answer retains at least 280 DIPs of width and 112 DIPs of height. Prefer split on a tie because it keeps short answers closer to the crop.

#### Tiny capture

If the image is smaller than 240 × 140 DIPs, render it at natural size without upscaling. The answer plane may still establish the minimum surface width. Align the image to the leading edge rather than stretching or centering it inside an oversized decorative box.

#### Oversized capture

Scale down only as much as needed to fit the safe work area. Selecting the image opens an actual-size, pannable view; this is a secondary inspection action, not a separate editor. The transmitted PNG remains full resolution regardless of preview scale.

### Placement

Center the result in the capture monitor's work area. Recenter it after bounded automatic response-height adjustments so the full card stays visible. Once the user drags the header, preserve that manual position for the rest of the capture.

## Visual language

### Surface

The result is a transient utility surface rather than a conventional application window.

- 16 DIP outer corner radius.
- One subtle one-pixel keyline derived from the system theme.
- Native compositor shadow rather than a painted heavy drop shadow.
- 16 DIP content inset; 12 DIP gap between media and answer planes.
- Screenshot remains fully opaque and color-accurate.
- No nested border around the screenshot unless contrast against the surface requires a one-pixel separator.

### Material

Use Windows Desktop Acrylic for the transient result surface when supported. Apply a sufficiently opaque theme tint behind answer text so desktop detail cannot reduce readability.

Fallback order:

1. Desktop Acrylic through the native DWM backdrop API on supported Windows 11 builds.
2. Near-opaque theme surface when transparency effects are disabled, Battery Saver suppresses Acrylic, Remote Desktop is active, or the compositor rejects the effect.
3. System high-contrast colors in high-contrast mode.

Mica is not the first choice because this is a transient, light-dismiss surface rather than a long-lived application window. Do not use WPF `AllowsTransparency=True` merely to simulate blur; it can degrade rendering and window behavior.

### Theme and color

Follow the Windows system theme by default. Avoid a hard-coded navy palette.

Semantic tokens should include:

- `SurfaceTint`
- `SurfaceFallback`
- `TextPrimary`
- `TextSecondary`
- `Separator`
- `ControlHover`
- `ControlPressed`
- `FocusStroke`
- `ErrorText`

All text and icons must meet WCAG AA contrast against both the Acrylic tint and fallback surface. Status must never rely on color alone.

### Typography

Use installed system fonts only:

```text
Primary:  Segoe UI Variable Text
Fallback: Segoe UI
```

- Answer: 15 DIP, regular weight, approximately 1.45 line height.
- Secondary status: 12 DIP, regular weight.
- Action labels when required: 12–13 DIP, semibold.
- Maximum split-layout answer line length: approximately 70 characters or 620 DIPs; stacked layouts use the window's natural content width.

Do not display a persistent “Screenshot” or “Answer” heading when the content already communicates the state.

### Icons and controls

Replace text glyphs such as `...` and `×` with consistent vector icons. Use a small reviewed set: Copy, More, Close, Stop, Retry, Capture, and Open at Actual Size.

- Visible icon: 16 DIPs.
- Pointer target: at least 28 × 28 DIPs for this compact desktop utility.
- Keyboard focus target: at least 32 × 32 DIPs where space permits.
- Every icon has an accessible name and tooltip.
- Controls live in the answer plane and become visually prominent only on hover, keyboard focus, or when action is required.

The completed default state should expose Copy, More, and Close. Stop replaces Copy while busy only if keeping Copy would create ambiguity. Connection, image copy, retry, quota-window usage, and new capture remain in More. First-text and completion timing stay diagnostic-only.

## State design

### Preparing

Show the screenshot immediately. Automatic captures reserve only a compact preparing plane; prompted captures show the composer without an empty response region beneath it. Use a small indeterminate pulse or three-dot motion beside a concise label such as **Looking…**. Do not show internal provider stages or a fake percentage.

### Streaming

Replace the preparing indicator on the first non-whitespace text. Grow the answer plane in a small number of bounded stages, not on every token, up to its work-area cap; longer content scrolls internally. Batch visual updates to a frame-friendly cadence if per-token WPF layout becomes expensive, but never delay the first chunk. Keep the viewport pinned to the beginning for short answers; do not force-scroll a user who has moved within a long answer.

### Complete

Remove activity and fit short answers to their rendered content. Keep long answers at the work-area cap with an internal scrollbar. Show Copy, More, and Close quietly. A copied state may temporarily replace the Copy icon for approximately one second without displaying a persistent footer.

### Error

Display the actionable error in the answer plane. Preserve the screenshot. Offer one primary recovery action—Connect, Retry, or Capture Again—based on the error. Keep technical details and account information in More.

### Disconnected

Use one primary **Connect ChatGPT** button. Explain in one short line that sign-in uses the user's existing eligible ChatGPT account. Do not introduce provider choice or account creation for this application.

## Motion

Motion reinforces continuity but must not sit on the critical path.

- Result entrance: 120–160 ms opacity plus scale from 0.98 to 1.00, anchored at the center of the capture monitor work area.
- Control hover/press: 80–120 ms color transition.
- Recenter after coalesced content-measured growth so streaming never produces per-token jitter. Stop automatic recentering after a manual drag.
- Dismissal: at most 100 ms fade; cancellation begins before the animation.
- Disable nonessential motion when Windows reduced-motion preferences are active.
- Do not animate window bounds while answer text streams.

## Focus and window behavior

- Show without activation after capture.
- Restore the previously foreground application after the overlay closes.
- A pointer click inside the answer permits selection and keyboard interaction.
- `Escape` dismisses the result when it has focus; during capture it cancels the overlay.
- Copy and menu actions must be keyboard accessible.
- The surface stays topmost while visible and appears in the taskbar so it can be minimized without cancelling an in-flight response.
- Dragging is available from the compact visible header.

## Accessibility

The implementation must be verified with:

- 100%, 150%, and 200% display scaling.
- System light and dark themes.
- Transparency effects disabled.
- High contrast mode.
- Reduced motion.
- Keyboard-only navigation.
- Narrator names for every icon and state.
- Long translated labels without clipped required actions.

Answer text remains selectable. Focus indicators must remain visible on Acrylic and solid fallbacks. The screenshot needs an accessible description such as “Captured screenshot preview”; generated OCR is not required.

## Performance budgets

These budgets cover local presentation, not remote model latency:

- Selection release to visible result surface: target at or below 100 ms at p95 after the crop exists.
- Request preparation begins in parallel with showing the surface.
- Layout calculation performs no image re-encoding and no network work.
- First streamed text is scheduled for rendering immediately.
- Subsequent stream updates may be coalesced to one update per display frame.
- Backdrop and shadow effects must not cause visible resizing, black frames, or capture-overlay delay.

The full-resolution PNG remains the inference input. Preview optimization must never silently reduce model input quality.

## Technical direction

### Keep the existing stack initially

The current C#/.NET/WPF implementation already handles capture, DPI, native positioning, tray lifecycle, account connection, cancellation, and streaming. WPF is not the cause of the current fixed thumbnail or visual styling.

Implement the redesign within `ResultWindow` first, using native DWM integration for backdrop, corners, dark mode, and shadow. Avoid introducing a UI framework dependency until the native spike establishes a concrete missing capability.

### Isolate layout policy

Add a pure, testable layout calculator to the core project:

```csharp
public enum ResultLayoutMode { Stacked, Split }

public readonly record struct ResultLayoutInput(
    double SourceWidthDip,
    double SourceHeightDip,
    double WorkAreaWidthDip,
    double WorkAreaHeightDip);

public readonly record struct ResultLayout(
    ResultLayoutMode Mode,
    double WindowWidth,
    double WindowHeight,
    double PreviewWidth,
    double PreviewHeight,
    double AnswerWidth,
    double AnswerHeight);
```

The calculator owns aspect thresholds and size constraints. `ResultWindow` consumes its result; it must not duplicate geometry constants in XAML and code-behind.

### WPF presentation structure

Use two templates or visual states over the same data:

- `StackedResultTemplate`
- `SplitResultTemplate`

Both share the same answer view and action cluster. Switching templates occurs once when a capture is committed, before the window becomes visible. Streaming may grow the answer plane vertically in coalesced, bounded steps, but never changes the stacked/split mode or image geometry.

### Backdrop spike

Validate these native behaviors before committing to a library or WinUI rewrite:

- DWM transient system backdrop on the borderless WPF window.
- Rounded corner preference.
- Correct light/dark appearance.
- Solid fallback with transparency disabled.
- Nonactivating `SetWindowPos` behavior.
- Rendering at mixed DPI.
- No black frame in native screenshots or Remote Desktop fallback.

If native WPF cannot satisfy those concrete checks reliably, preserve `ClipAsk.Core` and evaluate replacing only `ClipAsk.Desktop` with WinUI 3. A visual preference alone is not sufficient reason to rewrite the working provider and capture pipeline.

## Verification matrix

The automated smoke renderer should produce and validate at least these fixtures:

| Fixture | Source size | Expected mode |
|---|---:|---|
| Thin horizontal strip | 1600 × 120 | Stacked |
| Wide question | 1200 × 450 | Stacked |
| Balanced capture | 700 × 600 | Best-scale candidate |
| Tall panel | 450 × 1200 | Split |
| Tiny label | 180 × 80 | Natural-size stacked |
| Very large capture | 2560 × 1440 | Work-area constrained |

Each fixture should be rendered in preparing, streaming, complete, and error states where relevant. Geometry tests must assert:

- The image aspect ratio is preserved within 0.5 DIP.
- No window exceeds its work area.
- The answer retains its minimum readable dimensions.
- Wide captures never enter a fixed narrow thumbnail column.
- Tiny captures are not upscaled.
- Placement handles negative monitor coordinates and all screen edges.

Native manual checks should cover focus restoration, dragging, dismissal, clipboard behavior, Acrylic fallback, animation, and fresh real-account streaming. Synthetic UI verification must not be presented as proof of live model behavior.

## Acceptance criteria

The design pass is complete when:

1. A thin horizontal capture is immediately legible and uses a wide stacked composition.
2. A tall capture uses a proportional side preview without squeezing the answer below 280 DIPs.
3. No capture is distorted, arbitrarily squared, or silently downsampled for inference.
4. The result appears centered on the capture monitor without taking focus for automatic captures.
5. The preparing-to-streaming transition remains centered and causes no per-token window jitter.
6. The default completed surface contains no persistent title or routine status footer.
7. Copy, More, Close, Stop, Retry, and Connect use consistent accessible icons or labels.
8. System theme, transparency-off fallback, high contrast, and reduced motion all remain usable.
9. The full automated suite, layout matrix, and native smoke checks pass.
10. Real capture testing confirms that centering remains predictable across mixed-DPI and negative-coordinate monitors.

## Deferred decisions

- Whether an optional Fast service-tier setting belongs in a later preferences surface.
- Whether actual-size image inspection should open inline or in a separate transient viewer.
- Whether measured selection behavior provides enough safe lead time for opt-in speculative inference.

# ClipAsk Store listing (en-US)

## Product name

ClipAsk

## Category

Productivity

## Short description

Paste the plain text in [short-description-en-US.txt](short-description-en-US.txt). It is the longer caption shown near the top of the listing.

## Description

Paste only the plain text in [description-en-US.txt](description-en-US.txt). Partner Center treats this field as plain text: Markdown headings, links, and bullet syntax appear literally. Keep website, support, and privacy URLs in their dedicated fields.

## Screenshots

Upload the PNGs in [screenshots](screenshots) in numeric order. They are 1920 × 1080 and contain no taskbar or added marketing text. The first two are cropped from a real capture-and-answer session; the third is a WPF-rendered formatting sample; the fourth shows the current response-options dialog. Suggested optional captions, entered in Partner Center rather than burned into the images:

1. Select a chart and add a question before sending.
2. Read an answer next to the content you captured.
3. Follow structured answers with lists and mathematical notation.
4. Choose a model, reasoning effort, optional Fast mode, and a default instruction.

## Features

- Global Ctrl+Alt+S capture shortcut
- Prompted or immediate screenshot questions
- Streaming Markdown, code, tables, and LaTeX
- Resizable result window that can be minimized
- Account-advertised model and reasoning controls, plus optional Fast mode
- Copy responses and save captures as PNG
- Managed ChatGPT browser sign-in with Windows credential storage
- No API key required

## What's new

- Updated the bundled Codex runtime.
- Added optional Fast mode and made Luna with low reasoning the Automatic preference when available.
- Improved the taskbar icon and Store screenshots.
- Kept the result window at normal window level so other apps can cover it.

## URLs

- Website: https://github.com/IceyFoxes/ClipAsk
- Support: https://github.com/IceyFoxes/ClipAsk/issues
- Privacy policy: https://github.com/IceyFoxes/ClipAsk/blob/main/PRIVACY.md

## Certification notes

ClipAsk declares `runFullTrust` because it is a native WPF desktop utility that must register a global hotkey, display a region-selection overlay, use a notification-area icon, access the clipboard, save user-requested PNG files, and launch its bundled pinned Codex subprocess. It runs without elevation.

The Windows App Certification Kit's optional blocked-executables test can report process-launch APIs and executable-name strings in the self-contained .NET runtime and the bundled official Codex runtime. ClipAsk deliberately launches only its pinned `codex.exe` provider process and user-requested Windows/browser surfaces. Its provider configuration disables shell execution, editing, browsing, external tools, apps, connectors, and plugins, and ClipAsk rejects tool requests.

The app sends only the user-selected screenshot region after the user releases a valid selection and commits the applicable action. Left-drag opens an instruction field and requires Send; right-drag sends immediately. Pressing Esc cancels capture before upload.

The package declares one startup task, enabled after first launch, which invokes `ClipAsk.exe --startup`. This registers the tray and global shortcut without displaying the main window. Users can disable it from Windows Startup Apps settings.

An active ChatGPT account with Codex access is required for answers. Sign-in uses the official browser flow; no API key is accepted. Reviewers can inspect the UI and capture workflow before signing in.

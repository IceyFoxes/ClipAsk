# ClipAsk Store listing (en-US)

## Product name

ClipAsk

## Category

Productivity

## Short description

Select part of your screen, add an optional instruction, and get a focused ChatGPT answer.

## Description

ClipAsk is a focused screenshot-to-answer utility for Windows.

Press Ctrl+Alt+S, select a region, and ask ChatGPT about exactly what you captured. Left-drag lets you add an instruction before sending. Right-drag asks immediately. Responses stream into a compact floating window with Markdown, tables, code blocks, and LaTeX rendering.

ClipAsk supports model and reasoning-effort choices advertised by your ChatGPT account, proportional screenshot scaling, resizable results, clipboard copy, optional PNG saving, taskbar minimization, and startup support.

Your capture stays local until you complete an explicit capture-and-answer action. ClipAsk uses the official managed ChatGPT browser sign-in and does not ask for an API key or store your password.

ClipAsk is free and open-source under GPL-3.0-only. ChatGPT availability, models, and usage limits depend on your account. ClipAsk is independent software and is not affiliated with or endorsed by OpenAI.

## Features

- Global Ctrl+Alt+S capture shortcut
- Prompted or immediate screenshot questions
- Streaming Markdown, code, tables, and LaTeX
- Resizable, always-available result window
- Account-advertised model and reasoning controls
- Copy responses and save captures as PNG
- Managed ChatGPT browser sign-in with Windows credential storage
- No API key required

## What's new

- Added proportional result-window resizing.
- Made left-drag the prompted flow and right-drag the immediate flow.
- Centered initial windows and first-run guidance.
- Fixed excess spacing during prompt and streaming resize transitions.

## URLs

- Website: https://github.com/IceyFoxes/ClipAsk
- Support: https://github.com/IceyFoxes/ClipAsk/issues
- Privacy policy: https://github.com/IceyFoxes/ClipAsk/blob/main/PRIVACY.md

## Certification notes

ClipAsk declares `runFullTrust` because it is a native WPF desktop utility that must register a global hotkey, display a region-selection overlay, use a notification-area icon, access the clipboard, save user-requested PNG files, and launch its bundled pinned Codex subprocess. It runs without elevation.

The app sends only the user-selected screenshot region after the user releases a valid selection and commits the applicable action. Left-drag opens an instruction field and requires Send; right-drag sends immediately. Pressing Esc cancels capture before upload.

The package declares one startup task, enabled after first launch, which invokes `ClipAsk.exe --startup`. This registers the tray and global shortcut without displaying the main window. Users can disable it from Windows Startup Apps settings.

An active ChatGPT account with Codex access is required for answers. Sign-in uses the official browser flow; no API key is accepted. Reviewers can inspect the UI and capture workflow before signing in.

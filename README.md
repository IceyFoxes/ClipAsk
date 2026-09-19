<p align="center">
  <img src="assets/brand/clipask-icon.svg" alt="ClipAsk icon" width="112" />
</p>

# ClipAsk

**Clip your screen. Ask ChatGPT.**

A free, open-source Windows utility for getting an answer about a selected screen region without copying it into a chat window.

Press **Ctrl+Alt+S**, then:

- **Left-drag** to capture and enter an instruction before sending.
- **Right-drag** to capture and ask about the selection immediately when connected.
- Press **Esc** during selection to cancel.

Responses stream into a resizable floating window with Markdown, code blocks, tables, and LaTeX rendering. The screenshot scales with the window without changing its proportions. The window grows with the answer until you resize it manually, can be dragged or minimized to the taskbar, and opens in the center of the capture monitor.

## Status

ClipAsk is available as a public beta for Windows 10/11 x64. The installer and portable ZIP are self-contained, but remain unsigned until a trusted signing path is configured. Automatic updates are not yet available.

## Download

Download **[ClipAsk 0.1.0 Beta 3](https://github.com/IceyFoxes/ClipAsk/releases/tag/v0.1.0-beta.3)**. For most people, use `ClipAsk-0.1.0-win-x64-Setup.exe`; the ZIP is the portable alternative.

Because this beta is unsigned, Windows SmartScreen may show an unknown-publisher warning. Verify the adjacent SHA-256 checksum before running it. The installer is per-user and does not require administrator access.

## Features

- Screen-region capture from any visible desktop application.
- Optional instructions and eligible account-advertised model selection.
- ChatGPT account connection through the managed browser sign-in flow; no API key setup.
- Copy responses, copy captures, or save PNGs with the last save folder remembered.
- Image right-click menu, system tray controls, and optional start on startup.
- Available account usage details in the menu.

ClipAsk focuses on a single capture and response. It has no follow-up chat or capture-history browser. Its value is the short interaction: select, read, return to your work.

## Account and privacy

The app is free; model access and usage limits depend on your ChatGPT account and the models available through the Codex runtime. Free software does not mean unlimited AI usage. ClipAsk is an independent project and is not affiliated with or endorsed by OpenAI.

Captures stay local until you commit the applicable action. A left-drag waits for your instruction and **Send**; a right-drag sends the completed selection immediately when connected. Captures are also copied to the Windows clipboard. See [Privacy](PRIVACY.md) for local storage and provider behavior.

## Build on Windows

Prerequisites: Windows with the .NET **10.0.401 SDK** (see `global.json`), Git, and the official native Windows **Codex CLI 0.151.0** executable. The prototype checks that exact Codex version. Obtain the matching Windows architecture from the [official Codex release](https://github.com/openai/codex/releases/tag/rust-v0.151.0); do not use an unverified third-party binary.

```powershell
git clone https://github.com/IceyFoxes/ClipAsk.git
cd ClipAsk
dotnet build ClipAsk.slnx -c Release --nologo
dotnet test tests/ClipAsk.Core.Tests/ClipAsk.Core.Tests.csproj -c Release --no-build
$env:CLIPASK_CODEX_PATH = 'C:\path\to\codex.exe'
dotnet run -c Release --project src/ClipAsk.Desktop/ClipAsk.Desktop.csproj
```

Click **Connect ChatGPT** on first launch and complete the browser sign-in. Keep the app running in the tray to use the shortcut. The installer offers a checked-by-default option to start ClipAsk when you sign in to Windows; you can change it later from the ClipAsk menu.

Without `CLIPASK_CODEX_PATH`, the app expects `codex.exe` beside `ClipAsk.exe`. Optional `CLIPASK_STATE_DIR` overrides runtime storage and must point to a local Windows drive, not a WSL/UNC share.

### Existing project-local development setup

The development scripts expect an already-provisioned Windows SDK under `.devin/tools/dotnet-win` and Codex under `.devin/tools/codex-win`. These ignored tools are not supplied by cloning the repository.

```bash
bash scripts/dev.sh run
bash scripts/dev.sh verify
```

The WSL launcher translates paths into Windows form. Verification builds Release, runs the core tests, renders synthetic UI cases, and checks isolated provider configuration. It does not send a live screenshot question or run a model benchmark. Native Windows equivalents are `scripts/run.ps1` and `scripts/verify.ps1`, subject to your normal PowerShell execution policy.

## Package a Windows release

The release package is self-contained, so end users do not need to install .NET or Codex separately. Packaging requires the project-local .NET SDK and complete pinned Codex runtime tree described above.

```bash
bash scripts/package.sh
```

On native Windows, run `scripts/package.ps1`. Pass `-BuildInstaller` there, or set `BUILD_INSTALLER=1` for the bash launcher, when Inno Setup 6 is installed. Packaging verifies the exact Codex runtime allowlist and hashes, removes symbols, embeds corresponding ClipAsk source, and runs the published executable's synthetic UI and isolated provider checks before creating the portable ZIP. Clean artifacts and SHA-256 files are written under `artifacts/releases`; validation evidence is written under `artifacts/validation`; all build output is ignored by Git. Release packaging rejects tracked changes by default so its embedded source commit identifies the corresponding source. `ALLOW_DIRTY=1` (bash) or `-AllowDirty` (PowerShell) writes only to `artifacts/local-releases` and is for local package testing.

The installer is per-user and does not require elevation. Its visible startup option is checked by default, can be unchecked during setup, and remains available from the app; its registry entry is removed on uninstall. ChatGPT passwords are never stored by ClipAsk; the managed sign-in token is kept in Windows Credential Manager. By default, uninstall retains account/runtime state and preferences under `%LOCALAPPDATA%\ClipAsk` for a future reinstall. The interactive uninstaller offers an unchecked option to sign out and remove that local data. See [the release checklist](docs/releasing.md) before distributing an artifact.

Microsoft Store submission uses a separate unsigned MSIX build that Microsoft signs after acceptance. It requires the exact identity assigned after reserving ClipAsk in Partner Center and a Windows SDK installation containing `MakeAppx.exe`. See [the Store preparation guide](store/README.md); do not guess or commit account-specific identity values.

## Project structure

- `src/ClipAsk.Core`: capture geometry, response layout, provider policy, prompts, and streaming protocol.
- `src/ClipAsk.Desktop`: native WPF UI, Windows capture, hotkeys, tray, and response rendering.
- `tests/ClipAsk.Core.Tests`: provider, protocol, policy, and geometry tests.
- `docs`: interaction design and release preparation.

The stack is C#/.NET 10, WPF, Win32 interop, Markdig, WpfMath, and a pinned Codex app-server process.

## Contribute

Try it, report reproducible bugs, or improve the code. See [CONTRIBUTING.md](CONTRIBUTING.md). If ClipAsk is useful to you, a GitHub star helps others discover it.

## License

Copyright (c) 2026 IceyFoxes and ClipAsk contributors.

ClipAsk is licensed under **GNU GPL version 3 only**, SPDX identifier `GPL-3.0-only`. See [LICENSE](LICENSE). Distributed derivatives must meet GPLv3's source and licensing requirements; commercial use is allowed. There is no warranty. Dependencies retain their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

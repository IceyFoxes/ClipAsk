# Preparing the first ClipAsk beta

The repository is still in preparation. This checklist is not evidence that packaging, signing, or clean-machine validation has passed.

## Identity and source

- Product: ClipAsk; executable: `ClipAsk.exe`; solution: `ClipAsk.slnx`.
- Repository: https://github.com/IceyFoxes/ClipAsk
- License: GPL-3.0-only; retain upstream dependency notices.
- Initial development version: 0.1.0. Keep desktop metadata, manifest, and provider initialization version aligned when bumping it.
- Review the complete pending diff before committing. Do not include private reference images, credentials, local tool caches, or diagnostic output.

## Remaining release work

- [x] Add reproducible self-contained win-x64 ZIP packaging with the matching pinned Codex runtime.
- [ ] Audit every executable, library, font, and runtime asset in the final publish output. Packaging includes the known NuGet, Codex, and .NET license/notice material, but the binary-output audit remains a release gate.
- [x] Include GPL license text, build metadata, and a source URL pinned to the packaged commit.
- [ ] Add an About/license entry and application icon.
- [ ] Add first-run guidance for left-drag auto-send, right-drag instructions, account limits, and clipboard behavior.
- [ ] Make hotkey conflicts visible when started in the background.
- [x] Add a per-user Inno Setup installer definition. It deliberately leaves startup opt-in to the app.
- [x] Compile the installer and validate a silent install, packaged smoke/provider checks, and uninstall in an isolated folder on the development machine.
- [ ] Validate interactive install, upgrade, startup, and uninstall on a clean Windows account.
- [ ] Sign artifacts and verify the publisher and signatures. Record SHA-256 checksums.
- [ ] Test on a clean Windows machine without the development SDK, tool folders, or cached credentials.
- [ ] Test install, managed sign-in, capture/cancel, Markdown/math, minimize/new capture, streaming while dragging, saved directory, startup, upgrade, and uninstall.
- [ ] Check multiple displays and DPI scales, offline failures, quota exhaustion, and unsupported account/model states.
- [ ] Record a short demo using synthetic or explicitly shareable content, showing the real capture-to-answer delay.
- [ ] Review repository contents and privacy before making it public, then publish a beta release with tested requirements, known issues, binaries, checksums, and matching source.

## Verification

In the provisioned WSL development environment, run `bash scripts/dev.sh verify`. On native Windows with the provisioned tools, use `scripts/verify.ps1`. Do not silently add live model calls to release checks.

The existing WPF/Windows Forms DPI analyzer warning should be reviewed before release; changing DPI configuration needs display-scaling regression checks.

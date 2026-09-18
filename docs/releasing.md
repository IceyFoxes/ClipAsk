# Releasing ClipAsk betas

This checklist records what release packaging validates and which manual coverage is still outstanding.

## Identity and source

- Product: ClipAsk; executable: `ClipAsk.exe`; solution: `ClipAsk.slnx`.
- Repository: https://github.com/IceyFoxes/ClipAsk
- License: GPL-3.0-only; retain upstream dependency notices.
- Initial development version: 0.1.0. Keep desktop metadata, manifest, and provider initialization version aligned when bumping it.
- Review the complete pending diff before committing. Do not include private reference images, credentials, local tool caches, or diagnostic output.

## Remaining release work

- [x] Add reproducible self-contained win-x64 ZIP packaging with the matching pinned Codex runtime.
- [x] Audit every executable, library, font, and runtime asset in the first-beta publish output. The audited set is ClipAsk, Markdig, WpfMath/XamlMath and its fonts, the pinned Codex/ripgrep/PCRE2 tree, and the self-contained .NET Desktop Runtime; matching license and notice material is included.
- [x] Include GPL license text, build metadata, source URL, and a corresponding-source archive pinned to the packaged commit.
- [x] Add About/license access from the window and tray menus.
- [x] Apply the final application icon to the executable, windows, tray, installer, and repository branding.
- [x] Add first-run guidance for left-drag auto-send, right-drag instructions, account limits, privacy, and clipboard behavior.
- [x] Make hotkey conflicts visible when started in the background.
- [x] Add a per-user Inno Setup installer definition with a visible, checked-by-default startup option.
- [x] Remove the optional startup registry entry on uninstall while retaining user state by default; offer an explicit sign-out and local-data cleanup option.
- [x] Compile the installer and validate a silent install, packaged smoke/provider checks, and uninstall in an isolated folder on the development machine.
- [ ] Validate interactive install, upgrade, startup, and uninstall on a clean Windows account.
- [x] Record the first-beta signing posture: publish unsigned artifacts with an explicit SmartScreen warning and SHA-256 checksums.
- [ ] Add trusted signing in a later release through a qualifying open-source signing service, the Microsoft Store, or a verified publisher certificate.
- [ ] Test on a clean Windows machine without the development SDK, tool folders, or cached credentials.
- [ ] Test install, managed sign-in, capture/cancel, Markdown/math, minimize/new capture, streaming while dragging, saved directory, startup, upgrade, and uninstall.
- [ ] Check multiple displays and DPI scales, offline failures, quota exhaustion, and unsupported account/model states.
- [ ] Record a short demo using synthetic or explicitly shareable content, showing the real capture-to-answer delay.
- [x] Review repository contents and privacy before making it public, then publish a beta release with tested requirements, known issues, binaries, checksums, and matching source.

Never upload output from `artifacts/local-releases`; it was intentionally built from tracked changes. A clean release run clears stale same-version ZIP, installer, source, and checksum files before producing the requested artifact set. The repository or the exact tagged source must be publicly accessible before distributing GPL binaries.

## Verification

In the provisioned WSL development environment, run `bash scripts/dev.sh verify`. On native Windows with the provisioned tools, use `scripts/verify.ps1`. Do not silently add live model calls to release checks.

The WPF/Windows Forms DPI analyzer warning is resolved by selecting PerMonitorV2 before either UI stack creates a window. Multi-monitor display-scaling regression checks remain part of clean-machine validation.

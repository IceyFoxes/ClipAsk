# ClipAsk privacy notes

These notes describe the current prototype, not a promise about the storage policies of external providers.

## Capture and sending

Starting capture creates a local image of the selected monitor for the selection overlay. Cancelling does not send an image. Releasing a valid selection creates the cropped image and copies it to the Windows clipboard.

If connected, left-drag capture waits for an instruction and **Send**. Right-drag capture immediately sends the crop and the configured optional instruction to the provider. Analyze again explicitly resends the current capture. Other areas of the capture monitor are not included in the answer request.

ClipAsk sends requests through the official Codex runtime using your ChatGPT account. It does not route screenshots through a ClipAsk-operated server. OpenAI's account terms, limits, and data policies apply to provider processing. Review sensitive content before capturing it.

## Local state

- Runtime state defaults to `%LOCALAPPDATA%\ClipAsk`, including the isolated Codex home and workspace. A developer can override it with `CLIPASK_STATE_DIR`.
- Authentication uses the runtime's managed sign-in and keyring credential storage. ClipAsk does not request API keys or copy credentials from other installations.
- The interactive uninstaller keeps account/runtime state by default and offers an unchecked option to sign out and remove `%LOCALAPPDATA%\ClipAsk`. Saved screenshots outside that directory are not deleted.
- The latest capture and response are held for the active window. There is no app-level history browser. Provider threads are requested as ephemeral and runtime history persistence is disabled; this is not a claim that the remote provider retains no data or that runtime diagnostic files can never exist.
- Save image as writes a PNG only when requested. `last-save-folder.txt` remembers the destination directory. The old `%LOCALAPPDATA%\Screenshot` preference is read as a fallback after the rename; credentials are not migrated.
- The Windows clipboard can retain captures independently of the app. Windows clipboard history or sync, if enabled by you, follows your Windows settings.
- Installer and portable builds use a per-user Windows Run registry entry when startup is enabled. The Microsoft Store package instead declares a Windows Startup Task; Windows exposes its control in Startup Apps settings.
- Development verification writes synthetic UI images and diagnostic results under `.devin/evidence`, with an isolated probe state under the Windows app-data diagnostics folder.

## Tools and telemetry

The provider is configured for image responses with browsing, shell execution, editing, external tools, analytics, and feedback disabled. ClipAsk rejects tool requests. This does not prevent the underlying account service from maintaining operational records according to its own policies.

## Reporting issues

Before sharing logs or screenshots, remove personal details, account identifiers, tokens, and confidential image content. Public issues are visible to everyone once the repository is public.

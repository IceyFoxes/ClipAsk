# Microsoft Store preparation

ClipAsk should use the MSIX submission path. Microsoft re-signs accepted MSIX packages and hosts their updates; the existing Inno Setup EXE would require a separately purchased trusted signing certificate for Store submission.

## Partner Center inputs required

1. Create or activate a Partner Center developer account.
2. Create a new **MSIX or PWA app** and reserve **ClipAsk**.
3. Open **Product management → Product identity** and copy these values exactly:
   - Package/Identity/Name
   - Package/Identity/Publisher
   - Publisher display name

Identity values are case-sensitive. Do not guess them or commit account-specific publisher identifiers to this repository.

## Build the Store package

Install the Windows 10/11 SDK so `MakeAppx.exe` is available, then run from WSL:

```bash
CLIPASK_STORE_IDENTITY_NAME='value from Partner Center' \
CLIPASK_STORE_PUBLISHER='CN=value from Partner Center' \
CLIPASK_STORE_PUBLISHER_DISPLAY_NAME='public publisher name' \
bash scripts/package-store.sh
```

The initial Store package uses version `1.0.0.0`. Microsoft reserves the fourth component and does not accept `0.x` as the first version component. The application may continue to identify this public beta as `0.1.0` internally.

The script:

- repeats the normal release verification unless `SKIP_VERIFY=1`;
- publishes the self-contained x64 app and pinned Codex runtime;
- includes GPL corresponding source and third-party notices;
- inserts the exact Partner Center identity into the manifest;
- declares full-trust desktop execution and the `--startup` startup task;
- uses MakeAppx to validate the package structure and emit an unsigned `.msix` plus SHA-256 checksum.

Store submissions do not require a CA-trusted signature because Microsoft re-signs accepted MSIX packages. The unsigned output is not intended for direct sideloading. Sideload testing requires a trusted test certificate or loose-file developer registration.

## Before certification

- Build using the exact Partner Center identity.
- Install/test the package on a clean Windows 10/11 x64 machine.
- Run the Windows App Certification Kit.
- Test first launch, managed ChatGPT sign-in, both drag modes, Esc cancellation, Markdown/LaTeX, resizing, minimize/restore, startup, upgrade, and uninstall.
- Confirm the packaged startup entry appears once in Windows Startup Apps and launches with no foreground window.
- Upload at least one Store screenshot; four or more are recommended.
- Complete pricing/availability, properties, age ratings, package, listing, and submission options.
- Explain the restricted `runFullTrust` capability using the certification note in `listing/en-US.md`.

Do not submit until these clean-machine checks pass.

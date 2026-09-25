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

Install the Windows 10/11 SDK so `MakeAppx.exe` and `MakePri.exe` are available, then run from WSL:

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
- generates `resources.pri` so Windows can select the transparent, unplated taskbar icon variants;
- uses MakeAppx to validate the package structure and emit an unsigned `.msix` plus SHA-256 checksum.

Store submissions do not require a CA-trusted signature because Microsoft re-signs accepted MSIX packages. The normal unsigned Store output is not intended for direct sideloading. For local testing without a Store developer account, use the separate signed test package below.

## Test the taskbar icon locally

Use a separate test identity so an installed Store copy stays untouched. Build and sign the local test package with:

```bash
bash scripts/package-local-test.sh
```

The script keeps the signing key only in a temporary directory. It writes `ClipAsk-1.0.9.0-win-x64-signed-local.msix` and the public `ClipAsk-LocalTaskbarTest.cer` under `artifacts/local-store-releases` when the working tree has tracked changes. Exit any running ClipAsk instance. In **PowerShell as administrator**, copy both files to a local drive, trust the test certificate in `TrustedPeople`, then install the signed MSIX:

```powershell
$source = '\\wsl.localhost\Ubuntu-24.04\home\kevin\ClipAsk\artifacts\local-store-releases'
$certificate = Join-Path $env:TEMP 'ClipAsk-LocalTaskbarTest.cer'
$testPackage = Join-Path $env:TEMP 'ClipAsk-TaskbarTest-Signed.msix'
Copy-Item (Join-Path $source 'ClipAsk-LocalTaskbarTest.cer') $certificate -Force
Copy-Item (Join-Path $source 'ClipAsk-1.0.9.0-win-x64-signed-local.msix') $testPackage -Force
Import-Certificate -FilePath $certificate -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
Add-AppxPackage -Path $testPackage
$package = Get-AppxPackage -Name 'ClipAsk.LocalTaskbarTest'
Start-Process explorer.exe "shell:AppsFolder\$($package.PackageFamilyName)!ClipAsk"
```

Press Ctrl+Alt+S, make a selection, and inspect the result window's taskbar icon. If an older ClipAsk icon is pinned, unpin it before comparing. When finished, remove the test package and its trusted certificate in elevated PowerShell:

```powershell
Get-AppxPackage -Name 'ClipAsk.LocalTaskbarTest' | Remove-AppxPackage
$thumbprint = ([System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certificate)).Thumbprint
Remove-Item "Cert:\LocalMachine\TrustedPeople\$thumbprint"
```

The certificate trust step changes the local Windows certificate store and should be removed after testing. This test uses no Developer Mode or Store developer account.

## Before certification

- Build using the exact Partner Center identity.
- Install/test the package on a clean Windows 10/11 x64 machine.
- Run the Windows App Certification Kit.
- Test first launch, managed ChatGPT sign-in, both drag modes, Esc cancellation, Markdown/LaTeX, resizing, minimize/restore, startup, upgrade, and uninstall.
- Check the taskbar icon after installing the new MSIX version. Windows can keep an older pinned icon until it is unpinned and pinned again.
- Confirm the packaged startup entry appears once in Windows Startup Apps and launches with no foreground window.
- Upload at least one Store screenshot; four or more are recommended.
- The prepared en-US copy and four 1920 × 1080 screenshots are in `store/listing/`. Paste the `.txt` files into the Short description and Description fields as plain text; upload the PNGs in numeric order. Enter product features as separate fields, not as Markdown bullets.
- Complete pricing/availability, properties, age ratings, package, listing, and submission options.
- Explain the restricted `runFullTrust` capability using the certification note in `listing/en-US.md`.

Do not submit until these clean-machine checks pass.

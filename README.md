# VeriPDF - Windows

Offline PDF digital-signature verification. Nothing leaves your device.

## Build the installer

Double-click **build.bat** (or run it from a terminal). It will:

1. install the dependencies (`npm install`)
2. bundle the UI and the verification core (`npm run bundle`)
3. produce `release/VeriPDF-1.0.0-x64-Setup.exe` (installer) and
   `release/VeriPDF-1.0.0-portable-win-x64.exe` (no install needed)

Then run the installer, or just double-click the portable `.exe`.

## Run it without building

```bash
npm install
npm run bundle
npm start
```

## Layout

```
electron/main.js      the Electron main process (file dialogs, save, print)
electron/preload.js   the only bridge the UI gets
web/                  the user interface
core/                 the signature verification core (pure JS)
tools/build.mjs       bundles web/ + core/ into app/dist/
```

## What it does

Open a PDF and it tells you, in plain language, whether the document was
changed after it was signed and whether the signer chains to a trusted root
certificate. The seven **CCA India** root certificates are bundled, so real
Aadhaar and e-District certificates validate properly - something Adobe
Acrobat cannot do, because Adobe's trust list does not include the Indian
government PKI.

A verification report (PDF and JSON) can be exported for any document.

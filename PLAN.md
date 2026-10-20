# PaperKit — plan

**Goal:** a lightweight, fully-offline **PDF toolbox** for Windows + Android: the
whole iLovePDF tool set in one app, working on the device, with no upload, no
cloud, no account. The signature verifier (which Acrobat cannot do properly for
Indian government documents) is one of the tools inside it.

**Status (2026-10-20):** the app is **fully native** — no Electron, no WebView,
no Capacitor. One C# code base (Avalonia UI + PDFsharp 6) builds both the
Windows app (`PaperKit.exe`) and the Android app (`PaperKit.apk`).

* the toolbox UI, drag & drop, native file dialogs — done
* the **7 page tools** (Merge, Split, Organise, Rotate, Crop, Page numbers,
  Watermark) — done in C#, **16/16 tests green**
* the **signature verifier** — the JavaScript prototype of it is the reference,
  the C# port is the next piece of work
* the remaining 22 tools are listed in the app and marked *soon*

---

## 1. What works today

```
native/PaperKit.Core/     the engine
  PdfTools.cs             merge, split, organise, rotate, crop, page numbers,
                          watermark - all on PDFsharp 6, all on the device
  Fonts.cs                the font resolver: system font when there is one,
                          the bundled metric compatible font otherwise
  ToolCatalog.cs          the 30 tools, their groups and their options
native/PaperKit.App/      the shared view models (plain C#)
native/PaperKit.Ui/       the XAML - one window for both platforms
native/PaperKit.Windows/  the Windows head
native/PaperKit.Android/  the Android head
native/PaperKit.Shot/     renders the real window to PNG
native/PaperKit.Spec/     16 xUnit tests against real PDF files
trust-roots/              the 7 official CCA India roots (.cer + .der)
fixtures/                 real signed PDFs used by the tests
```

## 2. The tool inventory (iLovePDF = 31)

| Group | Tools | State |
|---|---|---|
| Organise | Merge, Split, Organise pages, Rotate, Crop, Page numbers, Watermark | **done** |
| Organise | Repair PDF | soon |
| Convert to PDF | JPG to PDF, Scan to PDF, Word to PDF, PowerPoint to PDF, Excel to PDF, HTML to PDF | soon |
| Convert from PDF | PDF to JPG, PDF to Word, PDF to PowerPoint, PDF to Excel, PDF to Markdown | soon |
| Optimise | Compress, OCR, PDF to PDF/A, Edit PDF, Compare PDF | soon |
| Secure | Protect, Unlock, Redact, Sign, Fill forms | soon |
| Verify | Verify digital signature | prototype done, C# port next |

> AI Summarizer and Translate PDF are **not** included - a model on the device
> would break the "light and fully offline" promise.

## 3. Next steps

1. **Verify signature in C#** - CMS/PKCS#7 parsing, the chain check against the
   7 embedded CCA India roots, the four verdicts and the exportable report.
2. **Protect / Unlock** - the two security tools that need the most work.
3. **Compress** - recompress the images and the content streams.
4. **PDF to JPG** - the first conversion tool.
5. Then the remaining conversions, one by one, each fully offline.

Every tool follows the same shape: a `ToolDescriptor` with options in
`ToolCatalog.cs`, a method in `PdfTools.cs`, and a test in `PaperKit.Spec`.

## 4. Rules that do not change

* **Nothing leaves the device.** No upload, no cloud, no account, no telemetry.
* **No internet permission on Android.** The app does not even ask for it.
* **Native only.** No Electron, no Chromium, no Capacitor, no web view.
* **One codebase.** The Windows and the Android app share the UI and the engine.
* **Trust the roots you ship.** The 7 CCA India roots are embedded and pinned
  by SHA-256; a verdict is only "valid" when every link of the chain verifies.

## 5. Build and publish

```bash
bash tools/setup_native_build.sh     # once: .NET 8, android workload, JDK 17, SDK
bash tools/build_native.sh           # tests + Windows exe + Android apk
bash tools/push_to_github.sh USER    # push the repo, tag v1.0.0, GitHub builds
```

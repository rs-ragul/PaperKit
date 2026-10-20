# PaperKit - the native apps

One C# code base, two real native apps. **No Electron, no Chromium, no WebView,
no Capacitor.** The window, the tools and the PDF engine are plain C# on
[Avalonia UI](https://avaloniaui.net) and [PDFsharp](https://www.pdfsharp.net).

```
PaperKit.Core/     the engine - PDFsharp 6, the tool catalogue, the trust store
PaperKit.App/      the shared view models (plain C#, no UI framework)
PaperKit.Ui/       the XAML - one window, shared by both platforms
PaperKit.Windows/  the Windows head   -> PaperKit.exe
PaperKit.Android/  the Android head   -> PaperKit.apk
PaperKit.Shot/     renders the real window to a PNG (screenshots, visual check)
PaperKit.Spec/     the test suite (xUnit, 15 tests against real PDFs)
```

## Build

```bash
dotnet test native/PaperKit.Spec/PaperKit.Spec.csproj

# Windows - self contained, no .NET install needed on the PC
dotnet publish native/PaperKit.Windows/PaperKit.Windows.csproj \
  -c Release -r win-x64 --self-contained true -o out/win

# Android
dotnet workload install android
dotnet publish native/PaperKit.Android/PaperKit.Android.csproj \
  -c Release -f net8.0-android -o out/android
```

The Android build needs a JDK 17 and an Android SDK (platform 34). On a
machine with little memory add `-m:1 -p:JavaMaximumHeapSize=640M`.

## How one window ends up on two platforms

`PaperKit.Ui` holds the XAML. Both heads *link* those files, so the Windows app
and the Android app draw the very same window:

```xml
<AvaloniaXaml Include="..\PaperKit.Ui\**\*.axaml" Link="%(RecursiveDir)%(Filename)%(Extension)" />
<Compile Include="..\PaperKit.Ui\App.axaml.cs" Link="App.axaml.cs" />
<Compile Include="..\PaperKit.Ui\MainWindow.axaml.cs" Link="MainWindow.axaml.cs" />
```

Only the entry point differs - `PaperKit.Windows/Program.cs` starts the desktop
lifetime, `PaperKit.Android/MainActivity.cs` starts the Android activity.

## Screenshots

```bash
dotnet run --project native/PaperKit.Shot/PaperKit.Shot.csproj -- out.png
```

It opens the real window in a headless Skia renderer and saves the home screen
and one tool screen, so the pictures can never drift away from the app.

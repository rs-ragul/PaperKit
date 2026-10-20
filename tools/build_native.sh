#!/usr/bin/env bash
# Builds the two native apps and puts them in deliverables/.
#
#   bash tools/build_native.sh
#
# Needs the tool chain - run tools/setup_native_build.sh once before.
set -e
cd "$(dirname "$0")/.."

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export JAVA_HOME="${JAVA_HOME:-$HOME/jdk17}"
export ANDROID_HOME="${ANDROID_HOME:-$HOME/android-sdk}"
export ANDROID_SDK_ROOT="$ANDROID_HOME"
export TMPDIR="${TMPDIR:-$HOME/work}"
mkdir -p "$TMPDIR"

echo "==> tests"
dotnet test native/PaperKit.Spec/PaperKit.Spec.csproj -c Release | tail -3

echo "==> windows"
rm -rf out/win
dotnet publish native/PaperKit.Windows/PaperKit.Windows.csproj \
  -c Release -r win-x64 --self-contained true -m:1 \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o out/win

echo "==> android"
rm -rf out/android
dotnet publish native/PaperKit.Android/PaperKit.Android.csproj \
  -c Release -f net8.0-android -m:1 \
  -p:AndroidSdkDirectory="$ANDROID_HOME" \
  -p:JavaSdkDirectory="$JAVA_HOME" \
  -p:JavaMaximumHeapSize=640M \
  -o out/android

echo "==> deliverables"
mkdir -p deliverables
cp out/android/in.paperkit.app-Signed.apk deliverables/PaperKit-1.0.0-android.apk
cp out/win/PaperKit.exe deliverables/PaperKit-1.0.0-windows-x64.exe

echo ""
echo "  deliverables/PaperKit-1.0.0-android.apk"
echo "  deliverables/PaperKit-1.0.0-windows-x64.exe"

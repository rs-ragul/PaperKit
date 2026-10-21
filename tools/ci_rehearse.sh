#!/usr/bin/env bash
# Runs exactly what the GitHub Actions workflow runs, on this machine.
#
#   bash tools/ci_rehearse.sh
#
# Use it to check the CI steps locally before pushing, so a broken workflow
# never reaches GitHub again.
set -e

export DOTNET_ROOT="$HOME/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export JAVA_HOME="$HOME/jdk17"
export PATH="$JAVA_HOME/bin:$PATH"
export ANDROID_HOME="$HOME/android-sdk"
export ANDROID_SDK_ROOT="$ANDROID_HOME"
export TMPDIR="${TMPDIR:-$HOME/work}"
export DOTNET_gcServer=0
mkdir -p "$TMPDIR"

cd "$(dirname "$0")/.."

echo "== which SDK is used =="
dotnet --list-sdks
echo "selected SDK: $(dotnet --version)"

echo "== android workload =="
dotnet workload install android

echo "== android platform and build tools =="
SDKMANAGER="$ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager"
if [ -x "$SDKMANAGER" ]; then
  yes | "$SDKMANAGER" --sdk_root="$ANDROID_HOME" --licenses > /dev/null 2>&1 || true
  "$SDKMANAGER" --sdk_root="$ANDROID_HOME" "platform-tools" "platforms;android-34" "build-tools;34.0.0" || true
else
  echo "cmdline-tools is not on the image, the workload installs what it needs"
fi

echo "== android dependencies (platform, build tools, licences) =="
dotnet build native/PaperKit.Android/PaperKit.Android.csproj \
  -t:InstallAndroidDependencies -f net8.0-android \
  -p:AndroidSdkDirectory="$ANDROID_HOME" \
  -p:JavaSdkDirectory="$JAVA_HOME" \
  -p:AcceptAndroidSDKLicenses=true

echo "== the tests =="
dotnet test native/PaperKit.Spec/PaperKit.Spec.csproj -c Release
dotnet test native/PaperKit.Ui.Tests/PaperKit.Ui.Tests.csproj -c Release

echo "== publish the android app =="
dotnet build-server shutdown >/dev/null 2>&1 || true
rm -rf out/android
dotnet publish native/PaperKit.Android/PaperKit.Android.csproj \
  -c Release -f net8.0-android \
  -p:AndroidSdkDirectory="$ANDROID_HOME" \
  -p:JavaSdkDirectory="$JAVA_HOME" \
  -p:JavaMaximumHeapSize="${PAPERKIT_JAVA_HEAP:-2G}" \
  -o out/android

echo ""
echo "== result =="
ls -la out/android/*.apk

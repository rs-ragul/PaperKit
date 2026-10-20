#!/usr/bin/env bash
# Installs everything the native build needs (once).
#
#   bash tools/setup_native_build.sh
#
# 1. the .NET 8 SDK           -> $HOME/dotnet
# 2. the android workload     -> dotnet workload install android
# 3. a JDK 17                 -> $HOME/jdk17
# 4. the Android SDK          -> $HOME/android-sdk
#
# Everything is installed inside the home folder, nothing is touched system wide.
set -e

export TMPDIR="${TMPDIR:-$HOME/work}"
mkdir -p "$TMPDIR"

DOTNET_DIR="$HOME/dotnet"
if [ ! -x "$DOTNET_DIR/dotnet" ]; then
  echo ">> installing the .NET 8 SDK"
  curl -sSL https://dot.net/v1/dotnet-install.sh -o "$TMPDIR/dotnet-install.sh"
  bash "$TMPDIR/dotnet-install.sh" --channel 8.0 --install-dir "$DOTNET_DIR" --no-path
fi
export DOTNET_ROOT="$DOTNET_DIR"
export PATH="$DOTNET_ROOT:$PATH"
dotnet --version

echo ">> installing the android workload"
dotnet workload install android

JDK_DIR="$HOME/jdk17"
if [ ! -x "$JDK_DIR/bin/java" ]; then
  echo ">> installing Temurin JDK 17"
  curl -sSL -o "$TMPDIR/jdk17.tar.gz" \
    "https://api.adoptium.net/v3/binary/latest/17/ga/linux/x64/jdk/hotspot/normal/eclipse"
  rm -rf "$JDK_DIR" && mkdir -p "$JDK_DIR"
  tar -xzf "$TMPDIR/jdk17.tar.gz" -C "$JDK_DIR" --strip-components=1
  chmod -R +x "$JDK_DIR/bin"
fi
export JAVA_HOME="$JDK_DIR"

SDK="$HOME/android-sdk"
export ANDROID_HOME="$SDK"
export ANDROID_SDK_ROOT="$SDK"
mkdir -p "$SDK/cmdline-tools"
if [ ! -x "$SDK/cmdline-tools/latest/bin/sdkmanager" ]; then
  echo ">> installing the Android command line tools"
  curl -sSL -o "$TMPDIR/cmdline-tools.zip" \
    "https://dl.google.com/android/repository/commandlinetools-linux-11076708_latest.zip"
  rm -rf "$TMPDIR/cmdline-tools" && mkdir -p "$TMPDIR/cmdline-tools"
  (cd "$TMPDIR/cmdline-tools" && unzip -q "$TMPDIR/cmdline-tools.zip")
  rm -rf "$SDK/cmdline-tools/latest"
  mv "$TMPDIR/cmdline-tools/cmdline-tools" "$SDK/cmdline-tools/latest"
  chmod -R +x "$SDK/cmdline-tools/latest/bin"
fi
SDKMANAGER="$SDK/cmdline-tools/latest/bin/sdkmanager"
echo ">> accepting the Android licences"
yes | "$SDKMANAGER" --sdk_root="$SDK" --licenses > /dev/null 2>&1 || true
echo ">> installing the Android platform packages"
rm -rf "$SDK/platform-tools"
"$SDKMANAGER" --sdk_root="$SDK" "platform-tools" "platforms;android-34" "build-tools;34.0.0" 2>&1 | tail -2

echo ""
echo ">> ready. Now run:  bash tools/build_native.sh"

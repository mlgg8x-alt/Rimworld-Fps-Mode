#!/usr/bin/env bash
# Builds the Android app: dist/RimWorldFPS-Android.apk (arm64, Android 8 to 16+, OpenGL ES 3;
# its native library is 16 KB page aligned, as Android 15+ devices may need). The APK carries the
# handoff and the source in assets/.
#
# Needs the .NET 10 SDK, the Android NDK (r27c tested; ANDROID_NDK_HOME), the Android SDK with
# build-tools 34.0.0 and platform android-34 (ANDROID_HOME), Java (keytool), git, make and zip.
# Steps: raylib 6.0 for Android (once, into build/), the client compiled ahead of time into
# libmain.so with raylib linked in, then the APK packed, aligned and signed with a debug key
# (build/debug.keystore, made on first use; keep it to install updates over an older build).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
NDK="${ANDROID_NDK_HOME:-/opt/android/android-ndk-r27c}"
SDK="${ANDROID_HOME:-/opt/android/sdk}"
BT="$SDK/build-tools/34.0.0"
JAR="$SDK/platforms/android-34/android.jar"
LLVM="$NDK/toolchains/llvm/prebuilt/linux-x86_64/bin"
build="$here/build"
mkdir -p "$build"

if [ ! -f "$build/libraylib.a" ]; then
  [ -d "$build/raylib" ] || git clone --depth 1 --branch 6.0 https://github.com/raysan5/raylib.git "$build/raylib"
  make -C "$build/raylib/src" -j8 PLATFORM=PLATFORM_ANDROID ANDROID_NDK="$NDK" ANDROID_ARCH=arm64 \
    ANDROID_API_VERSION=26 GRAPHICS=GRAPHICS_API_OPENGL_ES3 RAYLIB_LIBTYPE=STATIC
  cp "$build/raylib/src/libraylib.a" "$build/libraylib.a"
fi

dotnet publish "$here/RimFpsAndroid.csproj" -c Release -p:AndroidNdk="$NDK" -o "$build/publish"

stage="$build/stage"
rm -rf "$stage" && mkdir -p "$stage/lib/arm64-v8a"
"$LLVM/llvm-strip" --strip-debug -o "$stage/lib/arm64-v8a/libmain.so" "$build/publish/libmain.so"
# The handoff and the whole source ride along in the APK (assets/), so the project can be
# picked up from the installed app alone (an APK opens as a zip).
assets="$build/assets"
rm -rf "$assets" && mkdir -p "$assets"
cp "$root/HANDOFF.md" "$root/TESTING.md" "$root/README.md" "$assets/"
(cd "$root" && git ls-files -z | grep -zv '^dist/' | xargs -0 zip -q -9 "$assets/source.zip")
git -C "$root" rev-parse HEAD > "$assets/COMMIT"
"$BT/aapt2" link -o "$build/unsigned.apk" --manifest "$here/AndroidManifest.xml" -I "$JAR" -A "$assets"
(cd "$stage" && zip -q -r "$build/unsigned.apk" lib)
"$BT/zipalign" -f -p 4 "$build/unsigned.apk" "$build/aligned.apk"
if [ ! -f "$build/debug.keystore" ]; then
  keytool -genkeypair -keystore "$build/debug.keystore" -storepass android -keypass android -alias rimfps \
    -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=RimWorld FPS" >/dev/null 2>&1
fi
mkdir -p "$root/dist"
"$BT/apksigner" sign --ks "$build/debug.keystore" --ks-pass pass:android --key-pass pass:android \
  --out "$root/dist/RimWorldFPS-Android.apk" "$build/aligned.apk"
"$BT/apksigner" verify "$root/dist/RimWorldFPS-Android.apk"
ls -la "$root/dist/RimWorldFPS-Android.apk"

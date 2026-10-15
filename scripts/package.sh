#!/usr/bin/env bash
# Builds the mod and both clients and packages them as one RimWorld mod folder, zipped:
#
#   RimWorldFPS/
#     About/About.xml
#     1.6/Assemblies/RimWorldFPS.dll
#     Client/win-x64/RimFpsClient.exe
#     Client/linux-x64/RimFpsClient
#     HANDOFF.md            the project handoff, for picking the work up in any AI tool
#     TESTING.md, README.md
#     Source/               the whole repository at this commit (git archive, minus dist/)
#
# RimWorld ignores Source/ and the .md files. Usage: scripts/package.sh [name]  (default
# RimWorldFPS); writes dist/<name>.zip. Needs the .NET 10 SDK, zip and git.
set -euo pipefail
cd "$(dirname "$0")/.."
name="${1:-RimWorldFPS}"
out="$(mktemp -d)"
mod="$out/RimWorldFPS"
publish=(-c Release --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
  -p:PublishTrimmed=true -p:TrimMode=partial -p:JsonSerializerIsReflectionEnabledByDefault=true
  -p:EnableCompressionInSingleFile=true)

dotnet build rimworld-mod/Source/RimWorldFPS.csproj -c Release
for rid in win-x64 linux-x64; do
  dotnet publish client/RimFpsClient.csproj -r "$rid" "${publish[@]}" -o "$out/publish-$rid"
done

mkdir -p "$mod/Client/win-x64" "$mod/Client/linux-x64" "$mod/Source"
cp -r rimworld-mod/About rimworld-mod/1.6 "$mod/"
rm -f "$mod/1.6/Assemblies/.gitkeep"
cp "$out/publish-win-x64/RimFpsClient.exe" "$mod/Client/win-x64/"
cp "$out/publish-linux-x64/RimFpsClient" "$mod/Client/linux-x64/"
chmod +x "$mod/Client/linux-x64/RimFpsClient"
cp HANDOFF.md TESTING.md README.md "$mod/"
# The source as committed, plus any uncommitted changes to tracked files.
git ls-files -z | grep -zv '^dist/' | xargs -0 -I{} cp --parents {} "$mod/Source/"
git rev-parse HEAD > "$mod/Source/COMMIT"

mkdir -p dist
rm -f "dist/$name.zip"
(cd "$out" && zip -qr9 - RimWorldFPS) > "dist/$name.zip"
rm -rf "$out"
ls -la "dist/$name.zip"

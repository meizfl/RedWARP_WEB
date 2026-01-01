#!/bin/bash
set -e

# Full list of RIDs (current as of early 2026 / .NET 10 era)
# Based on portable RIDs recommended for .NET 8+
rids=(
  "win-x64" "win-x86" "win-arm64"
  "linux-x64" "linux-arm" "linux-arm64"
  "linux-musl-x64" "linux-musl-arm" "linux-musl-arm64"
  "osx-x64" "osx-arm64"
)

base_cmd="dotnet publish -c Release \
  -p:PublishSingleFile=true \
  -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishTrimmed=false"

project_name=$(basename *.csproj 2>/dev/null | sed 's/\.csproj$//')
if [ -z "$project_name" ]; then
  echo "Error: no .csproj file found in the current directory!"
  exit 1
fi

# Toolchain information
dotnet_version=$(dotnet --version)
sdk_info=$(dotnet --info)
roslyn_version=$(echo "$sdk_info" | grep "Microsoft.CodeAnalysis" -m1 | awk '{print $NF}')
build_date=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

echo "It's taking a long time, you can go make some tea ☕"
echo "Project: $project_name"
echo ".NET SDK: $dotnet_version"
echo "Build date (UTC): $build_date"
echo "════════════════════════════════════════════════════════════"

rm -fr ./bin/Release/
rm -fr ./obj/Release/
mkdir -p bin/Release/publish

for rid in "${rids[@]}"; do
    output_dir="bin/Release/publish/$rid"
    echo "Building → $rid"

    if ! $base_cmd -r "$rid" --output "$output_dir" > /dev/null 2>&1; then
        echo "Build failed for $rid 😭"
        continue
    fi

    # Binary name
    if [[ $rid == win* ]]; then
        exe_name="${project_name}.exe"
    else
        exe_name="${project_name}"
    fi

    exe_file="$output_dir/$exe_name"
    if [ ! -f "$exe_file" ]; then
        echo "Binary not found for $rid"
        continue
    fi

    # Remove everything except the single binary
    find "$output_dir" -type f ! -name "$exe_name" -delete
    find "$output_dir" -mindepth 1 -type d -empty -delete

    # Determine platform and architecture
    os=$(echo "$rid" | cut -d- -f1)
    arch=$(echo "$rid" | cut -d- -f2-)

    # Manifest file
    manifest="$output_dir/${project_name}.manifest"
    cat > "$manifest" <<EOF
Project : $project_name
RID : $rid
OS : $os
Architecture : $arch
.NET SDK : $dotnet_version
Roslyn : ${roslyn_version:-unknown}
Build date UTC : $build_date
Binary : $exe_name
EOF

    # SHA256 checksum
    sha256sum "$exe_file" > "$output_dir/${exe_name}.sha256"

    # Packaging with maximum compression
    archive_name="${project_name}-${rid}.tar.zst"
    tar -C "$output_dir" -cf - \
        "$exe_name" \
        "$(basename "$manifest")" \
        "${exe_name}.sha256" \
        | zstd -19 -T0 -o "bin/Release/publish/$archive_name"

    size=$(du -h "bin/Release/publish/$archive_name" | cut -f1)
    echo "✔ Done:"
    echo " → $archive_name ($size)"
    echo "------------------------------------------------------------"
done

echo "🎉 All done! Archives (.tar.zst) containing the binary, manifest, and SHA256 checksum are in bin/Release/publish."

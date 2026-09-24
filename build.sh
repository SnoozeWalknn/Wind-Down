#!/usr/bin/env bash
# Builds the self-contained Linux x64 release of Wind Down (CachyOS / Arch and other systemd distros).
#   ./build.sh           build artifacts/Wind-Down-1.0-Linux-x64 and its .tar.gz
#   ./build.sh --smoke   also run the scheduling and time-policy checks (no systemd needed)
#   ./build.sh --test    also run real systemd --user timer checks with the inert worker
# Needs the .NET 8 SDK: sudo pacman -S --needed dotnet-sdk-8.0
set -euo pipefail
cd "$(dirname "$(readlink -f "$0")")"
mode="${1:-}"
case "$mode" in ""|--smoke|--test) ;; *) echo "Usage: ./build.sh [--smoke|--test]" >&2; exit 2 ;; esac
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false AVALONIA_TELEMETRY_OPTOUT=1
name=Wind-Down-1.0-Linux-x64
out="artifacts/$name"
rm -rf "$out" "artifacts/$name.tar.gz"

dotnet publish src/WindDown.Linux/WindDown.Linux.csproj -c Release -r linux-x64 --self-contained true -p:RestoreLockedMode=true -p:DebugType=none -o "$out"
cp packaging/linux/install.sh packaging/linux/uninstall.sh packaging/linux/wind-down packaging/linux/wind-down.desktop packaging/linux/wind-down.svg packaging/linux/README.md "$out/"
cp src/WindDown.Linux/Assets/WindDown.png "$out/wind-down.png"
chmod +x "$out/WindDown" "$out/wind-down" "$out/install.sh" "$out/uninstall.sh"

if [ "$mode" = --test ]; then
  dotnet run --project tests/WindDown.Linux.Tests -c Release -p:RestoreLockedMode=true -- --integration "$out/WindDown"
elif [ "$mode" = --smoke ]; then
  dotnet run --project tests/WindDown.Linux.Tests -c Release -p:RestoreLockedMode=true
fi

tar -C artifacts -czf "artifacts/$name.tar.gz" "$name"
echo "Built $out and artifacts/$name.tar.gz"

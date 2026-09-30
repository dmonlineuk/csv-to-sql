#!/usr/bin/env bash
# Publishes a self-contained single-file CsvToDb binary. Usage: scripts/publish.sh [runtime-id] (default linux-x64)
set -euo pipefail
rid="${1:-linux-x64}"
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/artifacts/$rid"
dotnet publish "$root/src/CsvToSql.Cli/CsvToSql.Cli.csproj" -c Release -r "$rid" --self-contained true -o "$out"
echo "Published $out/CsvToDb"

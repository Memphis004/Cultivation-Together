#!/usr/bin/env bash
set -euo pipefail
WORKSPACE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GEN_CLIENT="$WORKSPACE/Tools/Luban/Luban.dll"
CONF_ROOT="$WORKSPACE/DataTables"

dotnet "$GEN_CLIENT" \
    -t client \
    -c cs-simple-json \
    -d json \
    --conf "$CONF_ROOT/luban.conf" \
    -x outputCodeDir="$WORKSPACE/UnityProject/Assets/Scripts/Data/Gen" \
    -x outputDataDir="$WORKSPACE/UnityProject/Assets/Resources/DataTables"

echo ""
echo "Done. Regenerated code into UnityProject/Assets/Scripts/Data/Gen"
echo "and data into UnityProject/Assets/Resources/DataTables."

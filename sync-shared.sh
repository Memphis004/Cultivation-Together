#!/usr/bin/env bash
# Copies the canonical source in Shared/ into both consumers.
# Run this after editing anything in Shared/.
#
# Not a real shared package yet (no project reference / plugin DLL) - that's
# a deliberate deferral while the schema is still moving fast. Revisit once
# GameMessages.cs / SectEconomyState.cs stop changing every session.

set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

UNITY_DEST="$ROOT/UnityProject/Assets/Scripts/Shared"
BRIDGE_DEST="$ROOT/McpBridge/Shared"

mkdir -p "$UNITY_DEST" "$BRIDGE_DEST"

cp "$ROOT"/Shared/*.cs "$UNITY_DEST"/
cp "$ROOT"/Shared/*.cs "$BRIDGE_DEST"/

echo "Synced $(ls "$ROOT"/Shared/*.cs | wc -l | tr -d ' ') file(s) into:"
echo "  $UNITY_DEST"
echo "  $BRIDGE_DEST"

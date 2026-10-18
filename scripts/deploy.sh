#!/usr/bin/env bash
# Deploy the build from scripts/build-on-server.sh into the kus/cs2-modded-server install on the OptiPlex.
#
# Only the plugin's own files are copied. `dotnet publish` also emits the CounterStrikeSharp API and its
# dependencies; those must NOT land in the plugin folder or CSS will load a second copy of its own API.
#
# Files go to custom_files (merged over the install on every container start, so they survive updates)
# and to the live install path, then the plugin is hot-reloaded over RCON.
#
# Usage: scripts/deploy.sh [--no-reload]
set -euo pipefail
HOST=${CS2_HOST:-quinn@192.168.4.20}
RELOAD=1; [ "${1:-}" = "--no-reload" ] && RELOAD=0

ssh "$HOST" bash -s "$RELOAD" <<'REMOTE_SCRIPT'
set -euo pipefail
RELOAD=$1
OUT=~/cs2/build/OpenPrefirePrac/out
SRC=~/cs2/build/OpenPrefirePrac
REL=addons/counterstrikesharp/plugins/disabled/OpenPrefirePrac
CUSTOM=~/cs2/custom_files/$REL
LIVE=~/cs2/install/game/csgo/$REL

[ -f "$OUT/OpenPrefirePrac.dll" ] || { echo "no build output at $OUT; run scripts/build-on-server.sh first"; exit 1; }

for dest in "$CUSTOM" "$LIVE"; do
  mkdir -p "$dest/lang"
  cp "$OUT"/OpenPrefirePrac.dll "$OUT"/OpenPrefirePrac.pdb "$OUT"/OpenPrefirePrac.deps.json "$dest/"
  cp "$OUT"/MaxMind.*.dll "$dest/" 2>/dev/null || true
  cp "$SRC"/lang/*.json "$dest/lang/"
done
echo "deployed $(stat -c %y "$OUT/OpenPrefirePrac.dll" | cut -d. -f1) build to custom_files and live install"

if [ "$RELOAD" = 1 ]; then
  python3 ~/cs2/rcon.py 'say [OpenPrefirePrac] plugin reloading, your practice will reset in a moment' >/dev/null
  sleep 2
  python3 ~/cs2/rcon.py 'css_plugins unload "Open Prefire Prac"' 'css_plugins load "plugins/disabled/OpenPrefirePrac/OpenPrefirePrac.dll"' | tail -4
  sleep 2
  python3 ~/cs2/rcon.py 'css_plugins list' | grep -i prefire || { echo "plugin did not load; check: docker logs cs2"; exit 1; }
fi
REMOTE_SCRIPT

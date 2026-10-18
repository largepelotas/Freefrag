#!/usr/bin/env bash
# Deploy the build from scripts/build-on-server.sh into the kus/cs2-modded-server install on the OptiPlex.
#
# Copied: the plugin's own files, MaxMind (GeoIP), and Microsoft.Data.Sqlite + SQLitePCLRaw + the native
# libe_sqlite3.so for the scores database (CounterStrikeSharp does not ship Sqlite).
# `dotnet publish` also emits the CounterStrikeSharp API and its dependencies;
# those must NOT land in the plugin folder or CSS will load a second copy of its own API.
#
# Files go to custom_files (merged over the install on every container start, so they survive updates)
# and to the live install path, then the plugin is hot-reloaded over RCON.
#
# kus's cfg/prefire.cfg and cfg/unload_plugins.cfg name the plugin by its old OpenPrefirePrac path and
# "Open Prefire Prac" module name. Every deploy rewrites the install's copies to the Freefrag names and
# stores them in custom_files/cfg, so the override survives kus updates (and masks upstream edits to them).
#
# The first deploy after the rename moves plugins/disabled/OpenPrefirePrac to plugins/disabled/Freefrag,
# carrying scores.db, default_cfg.json, recorded routes and resources across. It needs a reload, so it
# refuses to run while humans are connected unless FORCE_RELOAD=1.
#
# Usage: scripts/deploy.sh [--no-reload]      FORCE_RELOAD=1 reloads even with players connected
set -euo pipefail
HOST=${CS2_HOST:-quinn@192.168.4.20}
RELOAD=1; [ "${1:-}" = "--no-reload" ] && RELOAD=0

ssh "$HOST" bash -s "$RELOAD" "${FORCE_RELOAD:-0}" <<'REMOTE_SCRIPT'
set -euo pipefail
RELOAD=$1
FORCE_RELOAD=$2
OUT=~/cs2/build/Freefrag/out
SRC=~/cs2/build/Freefrag
REL=addons/counterstrikesharp/plugins/disabled/Freefrag
OLD_REL=addons/counterstrikesharp/plugins/disabled/OpenPrefirePrac
CUSTOM=~/cs2/custom_files/$REL
LIVE=~/cs2/install/game/csgo/$REL

[ -f "$OUT/Freefrag.dll" ] || { echo "no build output at $OUT; run scripts/build-on-server.sh first"; exit 1; }

HUMANS=$(python3 ~/cs2/rcon.py status | grep -oE '[0-9]+ humans' | grep -oE '[0-9]+' || echo 0)

MIGRATE=0
[ -d ~/cs2/install/game/csgo/$OLD_REL ] && [ ! -d "$LIVE" ] && MIGRATE=1
if [ "$MIGRATE" = 1 ]; then
  if [ "$RELOAD" = 0 ] || { [ "${HUMANS:-0}" != 0 ] && [ "$FORCE_RELOAD" != 1 ]; }; then
    echo "first Freefrag deploy moves the plugin folder and needs a reload; nothing changed."
    echo "Re-run without --no-reload when the server is empty, or with FORCE_RELOAD=1."
    exit 1
  fi
  python3 ~/cs2/rcon.py "say [Klitch's Freefrag] plugin reloading, your practice will reset in a moment" >/dev/null
  sleep 2
  # Unload first so scores.db is closed before its folder moves.
  python3 ~/cs2/rcon.py 'css_plugins unload "Open Prefire Prac"' | tail -2
  for root in ~/cs2/install/game/csgo ~/cs2/custom_files; do
    if [ -d "$root/$OLD_REL" ] && [ ! -d "$root/$REL" ]; then
      mv "$root/$OLD_REL" "$root/$REL"
      rm -f "$root/$REL"/OpenPrefirePrac.dll "$root/$REL"/OpenPrefirePrac.pdb "$root/$REL"/OpenPrefirePrac.deps.json
      echo "moved $root/$OLD_REL -> Freefrag"
    fi
  done
fi

mkdir -p ~/cs2/custom_files/cfg
for f in prefire.cfg unload_plugins.cfg; do
  sed -e 's#plugins/disabled/OpenPrefirePrac/OpenPrefirePrac\.dll#plugins/disabled/Freefrag/Freefrag.dll#' \
      -e 's#css_plugins unload "Open Prefire Prac"#css_plugins unload "Freefrag"#' \
      ~/cs2/install/game/csgo/cfg/$f > ~/cs2/custom_files/cfg/$f.tmp
  mv ~/cs2/custom_files/cfg/$f.tmp ~/cs2/custom_files/cfg/$f
  cp ~/cs2/custom_files/cfg/$f ~/cs2/install/game/csgo/cfg/$f
done

for dest in "$CUSTOM" "$LIVE"; do
  mkdir -p "$dest/lang" "$dest/resources/bt"
  cp "$OUT"/Freefrag.dll "$OUT"/Freefrag.pdb "$OUT"/Freefrag.deps.json "$dest/"
  cp "$OUT"/MaxMind.*.dll "$dest/" 2>/dev/null || true
  cp "$OUT"/Microsoft.Data.Sqlite.dll "$OUT"/SQLitePCLRaw.*.dll "$dest/"
  mkdir -p "$dest/runtimes/linux-x64/native"
  cp "$OUT"/runtimes/linux-x64/native/libe_sqlite3.so "$dest/runtimes/linux-x64/native/"
  cp "$OUT"/runtimes/linux-x64/native/libe_sqlite3.so "$dest/"      # SimpleAdmin ships it in both places
  cp "$SRC"/lang/*.json "$dest/lang/"
  cp "$SRC"/resources/bt/*.kv3 "$dest/resources/bt/"                # hard_mode.kv3 points at this folder
done
echo "deployed $(stat -c %y "$OUT/Freefrag.dll" | cut -d. -f1) build to custom_files and live install"

if [ "$RELOAD" = 1 ]; then
  if [ "$MIGRATE" = 0 ]; then
    # A reload resets every active practice and, right after a build, is the main way players get
    # interrupted. Refuse while humans are connected unless FORCE_RELOAD=1.
    if [ "${HUMANS:-0}" != 0 ] && [ "$FORCE_RELOAD" != 1 ]; then
      echo "$HUMANS human(s) connected; files copied but NOT reloaded. Re-run with FORCE_RELOAD=1 to reload anyway, or wait for the nightly restart."
      exit 0
    fi
    python3 ~/cs2/rcon.py "say [Klitch's Freefrag] plugin reloading, your practice will reset in a moment" >/dev/null
    sleep 2
    python3 ~/cs2/rcon.py 'css_plugins unload "Freefrag"' | tail -2
  fi
  python3 ~/cs2/rcon.py 'css_plugins load "plugins/disabled/Freefrag/Freefrag.dll"' | tail -4
  sleep 2
  python3 ~/cs2/rcon.py 'css_plugins list' | grep -i freefrag || { echo "plugin did not load; check: docker logs cs2"; exit 1; }
fi
REMOTE_SCRIPT

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
# Layout: Freefrag lives in plugins/Freefrag (not plugins/disabled), so CounterStrikeSharp auto-loads it once at
# server start, like GameModeManager. It is never unloaded by unload_plugins.cfg and no cfg loads it. Instead the
# plugin stays loaded in every game mode and is dormant outside Prefire. The mode cfgs tell it which mode is
# running with the console command css_freefrag_mode:
#   prefire.cfg         kus's `css_plugins load ".../OpenPrefirePrac.dll"` line becomes `css_freefrag_mode Prefire`
#   unload_plugins.cfg  kus's `css_plugins unload "Open Prefire Prac"` line becomes `css_freefrag_mode none`
# Every deploy rewrites the install's copies of both cfgs (from the kus original or from any earlier rewrite)
# and stores them in custom_files/cfg, so the override survives kus updates (and masks upstream edits to them).
# A load line must never come back: css_plugins load has no duplicate check and would start a second instance.
#
# The first deploy after the move relocates plugins/disabled/Freefrag (or the older plugins/disabled/OpenPrefirePrac)
# to plugins/Freefrag, carrying scores.db, default_cfg.json, recorded routes and resources across. It needs a
# reload, so it refuses to run while humans are connected unless FORCE_RELOAD=1.
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
REL=addons/counterstrikesharp/plugins/Freefrag
OLD_RELS="addons/counterstrikesharp/plugins/disabled/Freefrag addons/counterstrikesharp/plugins/disabled/OpenPrefirePrac"
CUSTOM=~/cs2/custom_files/$REL
LIVE=~/cs2/install/game/csgo/$REL

[ -f "$OUT/Freefrag.dll" ] || { echo "no build output at $OUT; run scripts/build-on-server.sh first"; exit 1; }

HUMANS=$(python3 ~/cs2/rcon.py status | grep -oE '[0-9]+ humans' | grep -oE '[0-9]+' || echo 0)

MIGRATE=0
if [ ! -d "$LIVE" ]; then
  for old in $OLD_RELS; do
    if [ -d ~/cs2/install/game/csgo/$old ]; then MIGRATE=1; fi
  done
fi
if [ "$MIGRATE" = 1 ]; then
  if [ "$RELOAD" = 0 ] || { [ "${HUMANS:-0}" != 0 ] && [ "$FORCE_RELOAD" != 1 ]; }; then
    echo "first Freefrag deploy moves the plugin folder and needs a reload; nothing changed."
    echo "Re-run without --no-reload when the server is empty, or with FORCE_RELOAD=1."
    exit 1
  fi
  python3 ~/cs2/rcon.py "say [Klitch's Freefrag] plugin reloading, your practice will reset in a moment" >/dev/null
  sleep 2
  # Unload first so scores.db is closed before its folder moves.
  # Whichever module name is loaded (only one is); the other unload just reports "not found".
  python3 ~/cs2/rcon.py 'css_plugins unload "Freefrag"' | tail -2 || true
  python3 ~/cs2/rcon.py 'css_plugins unload "Open Prefire Prac"' | tail -2 || true
  for root in ~/cs2/install/game/csgo ~/cs2/custom_files; do
    for old in $OLD_RELS; do
      if [ -d "$root/$old" ] && [ ! -d "$root/$REL" ]; then
        mv "$root/$old" "$root/$REL"
        rm -f "$root/$REL"/OpenPrefirePrac.dll "$root/$REL"/OpenPrefirePrac.pdb "$root/$REL"/OpenPrefirePrac.deps.json
        echo "moved $root/$old -> $REL"
      fi
    done
  done
fi

mkdir -p ~/cs2/custom_files/cfg
# Idempotent from the kus original or from any earlier rewrite: whole lines are replaced by the mode signal.
for f in prefire.cfg unload_plugins.cfg; do
  case $f in
    prefire.cfg)        SIGNAL='css_freefrag_mode Prefire' ;;
    unload_plugins.cfg) SIGNAL='css_freefrag_mode none' ;;
  esac
  sed -E \
      -e 's#^[[:space:]]*css_plugins[[:space:]]+load[[:space:]]+"?plugins/disabled/(OpenPrefirePrac/OpenPrefirePrac|Freefrag/Freefrag)\.dll"?.*$#css_freefrag_mode Prefire#' \
      -e 's#^[[:space:]]*css_plugins[[:space:]]+unload[[:space:]]+"?(Open Prefire Prac|Freefrag)"?.*$#css_freefrag_mode none#' \
      ~/cs2/install/game/csgo/cfg/$f > ~/cs2/custom_files/cfg/$f.tmp
  # The rewrite must leave no load/unload line behind and exactly one signal line of the right kind.
  if grep -qE 'Freefrag\.dll|unload[[:space:]]+"?Freefrag' ~/cs2/custom_files/cfg/$f.tmp \
     || [ "$(grep -c "^$SIGNAL" ~/cs2/custom_files/cfg/$f.tmp)" != 1 ]; then
    echo "cfg/$f: rewrite failed (leftover Freefrag load/unload line, or not exactly one '$SIGNAL'); nothing installed."
    rm -f ~/cs2/custom_files/cfg/$f.tmp
    exit 1
  fi
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
# The nightly merge copies custom_files over the install, so a leftover disabled/Freefrag there would resurrect
# a second copy of the plugin next to the real one.
rm -rf ~/cs2/custom_files/addons/counterstrikesharp/plugins/disabled/Freefrag
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
  python3 ~/cs2/rcon.py 'css_plugins load "plugins/Freefrag/Freefrag.dll"' | tail -4
  # No `css_freefrag_mode Prefire` is sent here: the plugin reads the current mode from GameModeManager's API in
  # Load(), so a hot reload during Prefire wakes up active, and in any other mode stays dormant. (rcon.py's
  # css_currentmode output format was not verified, so it is deliberately not parsed.)
  sleep 2
  python3 ~/cs2/rcon.py 'css_plugins list' | grep -i freefrag || { echo "plugin did not load; check: docker logs cs2"; exit 1; }
fi
REMOTE_SCRIPT

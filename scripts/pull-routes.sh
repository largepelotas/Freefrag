#!/usr/bin/env bash
# After recording guiding lines in game (!freefrag record save), the plugin writes them into the LIVE
# install, which kus overwrites on every container restart. This script:
#   1. copies the live maps/ folder into custom_files (so it survives restarts), and
#   2. pulls it into this repo so the routes can be committed.
#
# Usage: scripts/pull-routes.sh           then review `git diff maps/` and commit.
set -euo pipefail
HOST=${CS2_HOST:-quinn@192.168.4.20}
REL=addons/counterstrikesharp/plugins/Freefrag/maps
cd "$(dirname "$0")/.."

ssh "$HOST" "mkdir -p ~/cs2/custom_files/$REL && cp -r ~/cs2/install/game/csgo/$REL/. ~/cs2/custom_files/$REL/ && tar czf - -C ~/cs2/install/game/csgo/$REL ." \
  | tar xzf - -C maps
# Stage so git re-hashes the files; identical content shows as nothing, real changes show below.
git add -- maps/
echo "changed route files:"; git diff --cached --stat -- maps/ | cat

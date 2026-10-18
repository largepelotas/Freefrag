#!/usr/bin/env bash
# Sync this repo to the OptiPlex and build + test it inside the official .NET 10 SDK container
# (CounterStrikeSharp >= 1.0.3xx targets net10.0). Nothing is installed on the local machine.
#
# Usage: scripts/build-on-server.sh [test|publish|all]   (default: all = test, then publish)
# Output lands in ~/cs2/build/OpenPrefirePrac/out on the server; scripts/deploy.sh copies it into the game.
set -euo pipefail
HOST=${CS2_HOST:-quinn@192.168.4.20}
REMOTE=cs2/build/OpenPrefirePrac          # relative to the remote home directory
MODE=${1:-all}
cd "$(dirname "$0")/.."

tar czf - --exclude=bin --exclude=obj --exclude=out --exclude=.git . \
  | ssh "$HOST" "rm -rf $REMOTE && mkdir -p $REMOTE cs2/build/nuget && tar xzf - -C $REMOTE"

# Run the SDK as the host user so build output stays deletable; the NuGet cache persists in ~/cs2/build/nuget.
ssh "$HOST" bash -s "$MODE" <<'REMOTE_SCRIPT'
set -euo pipefail
MODE=$1
cd ~/cs2/build/OpenPrefirePrac
docker run --rm -u "$(id -u):$(id -g)" \
  -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp -e NUGET_PACKAGES=/nuget -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -v "$PWD:/src" -v "$HOME/cs2/build/nuget:/nuget" -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 bash -c "
    set -e
    if [ '$MODE' != publish ]; then dotnet test tests/OpenPrefirePrac.Tests -v q 2>&1 | tail -15; fi
    if [ '$MODE' != test ]; then dotnet publish OpenPrefirePrac.csproj -c Release -o /src/out -v q 2>&1 | tail -5 && echo '--- out:' && ls /src/out; fi
  "
REMOTE_SCRIPT

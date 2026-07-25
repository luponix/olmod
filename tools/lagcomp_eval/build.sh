#!/bin/bash
# Build the standalone lag-comp evaluator. Works from any cwd.
# Compiles the REAL prediction core (GameMod/MPShipPredictionCore.cs) against
# the minimal UnityShim -- no game/Unity DLLs needed, only mono's mcs.
set -e

DIR="$(cd "$(dirname "$0")" && pwd)"

if ! command -v mcs &> /dev/null; then
    echo "ERROR: needs the mono 'mcs' compiler (e.g. package 'mono-devel')." >&2
    exit 1
fi

mcs -optimize+ -warn:4 \
    -out:"$DIR/lagcomp_eval.exe" \
    "$DIR/UnityShim.cs" \
    "$DIR/OlrReader.cs" \
    "$DIR/Evaluator.cs" \
    "$DIR/../../GameMod/MPShipPredictionCore.cs"

echo "built $DIR/lagcomp_eval.exe"

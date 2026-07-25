#!/bin/bash
# Build-if-stale, then run the evaluator forwarding all arguments.
# Default replay dir (when no paths are given): ~/.config/unity3d/Revival/Overload/Replays
set -e

DIR="$(cd "$(dirname "$0")" && pwd)"
EXE="$DIR/lagcomp_eval.exe"

stale=0
if [ ! -f "$EXE" ]; then
    stale=1
else
    for src in "$DIR/UnityShim.cs" "$DIR/OlrReader.cs" "$DIR/Evaluator.cs" \
               "$DIR/../../GameMod/MPShipPredictionCore.cs"; do
        if [ "$src" -nt "$EXE" ]; then
            stale=1
            break
        fi
    done
fi

if [ "$stale" = 1 ]; then
    "$DIR/build.sh" >&2
fi

exec mono "$EXE" "$@"

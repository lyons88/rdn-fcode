#!/bin/sh
# Runs a calls file through Linux's atom.c (build/atomref) and through
# src/atom.fs under gforth, and compares the register traces.
#   sh test/compare.sh [calls.txt] [vbios.rom]
set -e
cd "$(dirname "$0")/.."
CALLS=${1:-test/calls/asic.txt}
ROM=${2:-reference/vbios/visiontek6770.rom}
name=$(basename "$CALLS" .txt)
mkdir -p build
if [ ! -x build/atomref ] || [ test/atomref.c -nt build/atomref ]; then
	cc -O1 -w -Itest/ref/atom -Itest/ref -o build/atomref test/atomref.c test/ref/atom/atom.c
fi
build/atomref "$ROM" "$CALLS" > "build/ref-$name.txt"
gforth test/prelude.fs src/consts.fs src/atom.fs test/asic.fs \
	-e "s\" $ROM\" s\" $CALLS\" run-calls bye" > "build/forth-$name.txt" 2> "build/forth-$name.err" || {
	cat "build/forth-$name.err"; exit 1; }
grep -v '^#' "build/ref-$name.txt" > "build/ref-$name.trace" || true
grep -v '^#' "build/forth-$name.txt" > "build/forth-$name.trace" || true
lines=$(wc -l < "build/ref-$name.trace" | tr -d ' ')
if diff -u "build/ref-$name.trace" "build/forth-$name.trace" > "build/diff-$name.txt"; then
	echo "$name: identical ($lines lines)"
else
	echo "$name: DIFFERENT (see build/diff-$name.txt)"
	head -20 "build/diff-$name.txt"
	exit 1
fi

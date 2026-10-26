#!/bin/sh
# All host tests: sh test/run.sh
#  1. the interpreter against Linux's atom.c, register for register
#  2. the PLL divider search against RadeonNI's C version
#  3. DDC/EDID through a simulated EDID chip
#  4. display-start end to end, against a saved log (test/golden/display.txt)
#  5. the probe and the display node methods, Open Firmware played by
#     test/of-stub.fs (checks, and a log compared with test/golden/node.txt)
#  6. rom/rdn.fth through the FCode tokenizer (toke): only FCode words used
#  7. the ROM builder and checker (test/rom.sh)
set -e
cd "$(dirname "$0")/.."
mkdir -p build
SRC="test/prelude.fs src/consts.fs src/atom.fs src/card.fs src/i2c.fs src/modeset.fs"
SIM="test/i2csim.fs test/edid-data.fs"
fail=0

for c in test/calls/*.txt; do
	sh test/compare.sh "$c" || fail=1
done

cc -O1 -w -o build/pllref test/pll/pllref.c
build/pllref > build/pll-ref.txt
gforth $SRC test/pll/pll.fs 2>/dev/null > build/pll-forth.txt
if cmp -s build/pll-ref.txt build/pll-forth.txt; then
	echo "pll: identical ($(wc -l < build/pll-ref.txt | tr -d ' ') cases)"
else
	echo "pll: DIFFERENT"; diff build/pll-ref.txt build/pll-forth.txt | head; fail=1
fi

gforth $SRC $SIM test/edid.fs 2>&1 | grep -a 'edid:\|FAIL' | tee build/edid.txt
grep -q 'all checks passed' build/edid.txt || fail=1

# The register trace of display-start, minus the DDC bit-banging (quiet).
gforth $SRC $SIM test/display.fs 2>&1 | sed 's/^redefined scratch  redefined d>s  //' > build/display.txt
if [ -f test/golden/display.txt ] && cmp -s test/golden/display.txt build/display.txt; then
	echo "display: same as test/golden/display.txt ($(wc -l < build/display.txt | tr -d ' ') lines)"
else
	echo "display: differs from test/golden/display.txt (see build/display.txt)"
	[ -f test/golden/display.txt ] && diff test/golden/display.txt build/display.txt | head -20
	fail=1
fi
# The probe and the node. The "address" property is a host pointer: masked.
gforth $SRC $SIM test/of-stub.fs src/platform.fs src/probe.fs src/display-node.fs test/node.fs 2>&1 \
	| sed 's/redefined [^ ]*  //g; s/^property address: .*/property address: (host)/' > build/node.txt
grep -a 'FAIL\|node:' build/node.txt
grep -q 'node: all checks passed' build/node.txt || fail=1
if [ -f test/golden/node.txt ] && cmp -s test/golden/node.txt build/node.txt; then
	echo "node: same as test/golden/node.txt ($(wc -l < build/node.txt | tr -d ' ') lines)"
else
	echo "node: differs from test/golden/node.txt (see build/node.txt)"
	[ -f test/golden/node.txt ] && diff test/golden/node.txt build/node.txt | head -20
	fail=1
fi

# toke writes rom/rdn.fc next to its input
if (cd rom && toke rdn.fth > ../build/toke.log 2>&1); then
	other=$(grep '^Warning\|^Error' build/toke.log | grep -vc 'longer than 31' || true)
	echo "toke: rom/rdn.fc, $(wc -c < rom/rdn.fc | tr -d ' ') bytes of FCode, $other warnings besides long names"
	[ "$other" = 0 ] || { grep '^Warning\|^Error' build/toke.log | grep -v 'longer than 31' | head; fail=1; }
else
	echo "toke: FAILED"; grep -i 'error' build/toke.log | head; fail=1
fi
sh test/rom.sh || fail=1
exit $fail

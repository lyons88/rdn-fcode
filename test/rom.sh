#!/bin/sh
# The ROM builder and checker: sh test/rom.sh
#  - build from the Visiontek dump, check it, and run the driver's own
#    VBIOS search (rom-copy) on the result;
#  - the builder refuses: an image that would overwrite data, a dump that
#    already has an Open Firmware image, a broken FCode file;
#  - the checker catches a corrupted image.
set -e
cd "$(dirname "$0")/.."
D=build/romtest; mkdir -p $D
VB=reference/vbios/visiontek6770.rom
fail=0
expect_fail() { # what command...
	what=$1; shift
	if "$@" > $D/out.txt 2>&1; then echo "rom: FAIL $what was accepted"; fail=1
	else echo "rom: refused: $what ($(tail -1 $D/out.txt))"; fi
}

python3 tools/mkrom.py $VB rom/rdn.fc $D/ok.rom > /dev/null
python3 tools/checkrom.py $D/ok.rom $VB rom/rdn.fc > $D/check.txt || { cat $D/check.txt; fail=1; }
tail -1 $D/check.txt
gforth test/prelude.fs src/consts.fs src/atom.fs src/card.fs src/i2c.fs src/modeset.fs \
	test/of-stub.fs src/platform.fs test/romimg.fs \
	-e "s\" $D/ok.rom\" run bye" 2>&1 | grep -ao 'romimg:.*' | tee $D/romimg.txt
grep -q 'finds the x86' $D/romimg.txt || fail=1

# data right after the x86 image, where the FCode would go
python3 -I -c "
import sys; d=bytearray(open('$VB','rb').read()); d[0x10100]=0x42
open('$D/busy.rom','wb').write(d)"
expect_fail "data in the way" python3 tools/mkrom.py $D/busy.rom rom/rdn.fc $D/x.rom
expect_fail "a second Open Firmware image" python3 tools/mkrom.py $D/ok.rom rom/rdn.fc $D/x.rom
python3 -I -c "
d=bytearray(open('rom/rdn.fc','rb').read()); d[100]^=1
open('$D/bad.fc','wb').write(d)"
expect_fail "an FCode file with a bad checksum" python3 tools/mkrom.py $VB $D/bad.fc $D/x.rom
python3 -I -c "
d=bytearray(open('$D/ok.rom','rb').read()); d[0x10200]^=1
open('$D/corrupt.rom','wb').write(d)"
expect_fail "checkrom on a corrupted image" python3 tools/checkrom.py $D/corrupt.rom
python3 -I -c "
d=bytearray(open('$D/ok.rom','rb').read()); d[0x1f9]|=0x80
open('$D/twolast.rom','wb').write(d)"
expect_fail "checkrom with the x86 image still marked last" python3 tools/checkrom.py $D/twolast.rom
exit $fail

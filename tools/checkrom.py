#!/usr/bin/env python3
"""Check a flash image before it goes near a card.

    python3 tools/checkrom.py out.rom [card.rom [rom/rdn.fc]]

Checks every PCI image in the chain (signatures, lengths, exactly one
last-image flag, on the last image), the x86 image's checksum and AtomBIOS
header, and the Open Firmware image (FCode pointer, start token, FCode length
and checksum, the image's byte sum, vendor and device as the x86 image's).
Detokenizes the FCode with detok when it is installed.

With the original dump: only the x86 image's last-image flag and checksum
byte, and erased flash where the FCode image now is, may differ. With the
FCode file: the image must hold exactly that FCode.
Exits 1 on any failure.
"""
import os
import shutil
import struct
import subprocess
import sys
import tempfile

fails = 0


def ok(cond, what):
    global fails
    print(("  ok    " if cond else "  FAIL  ") + what)
    if not cond:
        fails += 1
    return cond


def main():
    if not 2 <= len(sys.argv) <= 4:
        sys.exit("usage: checkrom.py out.rom [card.rom [rdn.fc]]")
    rom = open(sys.argv[1], "rb").read()
    print("%s: %d KB" % (sys.argv[1], len(rom) // 1024))

    chain, off = [], 0
    while off + 0x1A <= len(rom) and rom[off:off + 2] == b"\x55\xaa":
        pcir = off + struct.unpack_from("<H", rom, off + 0x18)[0]
        if rom[pcir:pcir + 4] != b"PCIR":
            break
        vendor, device = struct.unpack_from("<HH", rom, pcir + 4)
        length = struct.unpack_from("<H", rom, pcir + 0x10)[0] * 512
        ctype, ind = rom[pcir + 0x14], rom[pcir + 0x15]
        chain.append((off, pcir, length, ctype, ind & 0x80, vendor, device))
        print("image at 0x%05x: code type %d, %d bytes, %04x:%04x%s"
              % (off, ctype, length, vendor, device, ", last" if ind & 0x80 else ""))
        if ind & 0x80 or length == 0:
            break
        off += length

    ok(len(chain) >= 2, "two or more images in the chain")
    ok(sum(1 for c in chain if c[4]) == 1 and chain and chain[-1][4],
       "exactly one last-image flag, on the last image")
    ok(all(c[0] + c[2] <= len(rom) for c in chain), "every image inside the flash")
    x86 = [c for c in chain if c[3] == 0]
    of = [c for c in chain if c[3] == 1]
    ok(len(x86) == 1, "one x86 image")
    ok(len(of) == 1, "one Open Firmware image")
    if not x86 or not of:
        sys.exit(1)

    xo, xp, xl = x86[0][:3]
    n = rom[xo + 2] * 512
    ok(n <= xl and sum(rom[xo:xo + n]) & 0xFF == 0,
       "x86 image sums to zero over its %d bytes" % n)
    hdr = xo + struct.unpack_from("<H", rom, xo + 0x48)[0]
    ok(rom[hdr + 4:hdr + 8] == b"ATOM", "x86 image has the AtomBIOS header")

    fo, fp, fl = of[0][:3]
    ok(of[0][5:7] == x86[0][5:7], "Open Firmware image names the same device")
    ok(rom[fp + 0x0D:fp + 0x10] == rom[xp + 0x0D:xp + 0x10], "same class code")
    ok(sum(rom[fo:fo + fl]) & 0xFF == 0, "Open Firmware image sums to zero")
    fptr = struct.unpack_from("<H", rom, fo + 2)[0]
    fc_at = fo + fptr
    ok(fptr >= 0x1A and fc_at + 8 <= fo + fl, "FCode pointer 0x%x inside the image" % fptr)
    ok(rom[fc_at] in (0xF0, 0xF1, 0xF2, 0xF3), "FCode starts with a start token")
    csum, flen = struct.unpack_from(">HI", rom, fc_at + 2)
    ok(fc_at + flen <= fo + fl, "FCode (%d bytes) inside the image" % flen)
    fc = rom[fc_at:fc_at + flen]
    ok(sum(fc[8:]) & 0xFFFF == csum, "FCode checksum")

    if len(sys.argv) >= 3 and chain[0][3] == 1:
        # --fcode-first: the dump follows the FCode image unchanged
        orig = open(sys.argv[2], "rb").read()
        ok(rom[fl:] == orig, "the dump follows the FCode image unchanged")
    elif len(sys.argv) >= 3:
        orig = open(sys.argv[2], "rb").read()
        ok(len(orig) == len(rom), "same size as the dump")
        allowed = {xp + 0x15, xo + 0x21}
        diff = [i for i in range(min(len(orig), len(rom)))
                if orig[i] != rom[i] and i not in allowed
                and not (fo <= i < fo + fl and orig[i] == 0xFF)]
        ok(not diff, "nothing else changed" +
           ("" if not diff else " (first difference at 0x%x)" % diff[0]))
        ok(all(orig[i] == 0xFF for i in range(fo, fo + fl)),
           "the Open Firmware image went into erased flash")
    if len(sys.argv) >= 4:
        ok(fc == open(sys.argv[3], "rb").read(), "the image holds %s" % sys.argv[3])

    if shutil.which("detok"):
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "fcode.fc")
            open(p, "wb").write(fc)
            r = subprocess.run(["detok", p], capture_output=True, text=True)
            out = r.stdout + r.stderr
            ok(r.returncode == 0 and "end0" in out and "Unexpected" not in out,
               "detok reads the FCode to its end")
    else:
        print("  (detok not installed: FCode not detokenized)")

    print("checkrom: " + ("all checks passed" if not fails else "%d FAILED" % fails))
    sys.exit(1 if fails else 0)


if __name__ == "__main__":
    main()

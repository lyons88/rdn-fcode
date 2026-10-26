#!/usr/bin/env python3
"""Build a flash image: the card's own ROM dump with the FCode driver appended
as an Open Firmware PCI image.

    python3 tools/mkrom.py card.rom rom/rdn.fc out.rom
    python3 tools/mkrom.py --fcode-first card.rom rom/rdn.fc out.rom

--fcode-first puts the FCode image in front of the dump, which is left as
it is. That is for firmware that only looks at a ROM's first image, such as
OpenBIOS in QEMU; the file is larger than the flash chip. Not for flashing.

What it does:
  - walks the dump's PCI image chain and puts the new image right after the
    last one (the x86 image with the AtomBIOS tables);
  - lays the new image out as Apple's Mac ROMs do: 55 AA, a pointer to the
    FCode at offset 2 (0x40), the PCI data structure at 0x20 (the card's vendor,
    device and class, code type 1, last image), the FCode at 0x40, padded to
    512 bytes with 0xFF and a last byte that makes the image sum to zero;
  - clears the last-image flag of the image before it, and, for an AtomBIOS
    image, puts its checksum (offset 0x21) right again;
  - writes only into erased flash (0xFF): anything else in the dump, such as
    the MCuC block some cards keep after the images, is left alone, and the
    build stops if the new image would not fit.

The output is the size of the dump. tools/checkrom.py checks the result.
"""
import struct
import sys

FCODE_OFF = 0x40          # where the FCode starts inside the new image
PCIR_OFF = 0x20           # where its PCI data structure starts
ATOM_CHECKSUM = 0x21      # AtomBIOS images keep their checksum byte here


def die(msg):
    sys.exit("mkrom: " + msg)


def images(rom):
    """The PCI image chain: (offset, pcir, length, code type, last?)."""
    out, off = [], 0
    while True:
        if off + 0x1A > len(rom) or rom[off:off + 2] != b"\x55\xaa":
            die("no PCI image at 0x%x" % off)
        pcir = off + struct.unpack_from("<H", rom, off + 0x18)[0]
        if rom[pcir:pcir + 4] != b"PCIR":
            die("no PCI data structure for the image at 0x%x" % off)
        length = struct.unpack_from("<H", rom, pcir + 0x10)[0] * 512
        if length == 0:
            die("image at 0x%x has length 0" % off)
        last = bool(rom[pcir + 0x15] & 0x80)
        out.append((off, pcir, length, rom[pcir + 0x14], last))
        if last:
            return out
        off += length


def is_atom(rom, off):
    hdr = off + struct.unpack_from("<H", rom, off + 0x48)[0]
    return rom[hdr + 4:hdr + 8] == b"ATOM"


def check_fcode(fc):
    if len(fc) < 8 or fc[0] not in (0xF0, 0xF1, 0xF2, 0xF3):
        die("the FCode does not start with a start token")
    csum, length = struct.unpack_from(">HI", fc, 2)
    if length != len(fc):
        die("the FCode header says %d bytes, the file has %d" % (length, len(fc)))
    if sum(fc[8:]) & 0xFFFF != csum:
        die("the FCode checksum is wrong")


def fcode_image(fc, vendor, device, klass, last=True):
    size = FCODE_OFF + len(fc) + 1          # at least one byte to balance the sum
    size = (size + 511) // 512 * 512
    img = bytearray(b"\xff" * size)
    img[0:2] = b"\x55\xaa"
    struct.pack_into("<H", img, 2, FCODE_OFF)
    img[4:0x18] = bytes(0x14)
    struct.pack_into("<H", img, 0x18, PCIR_OFF)
    img[0x1A:PCIR_OFF] = bytes(PCIR_OFF - 0x1A)
    pcir = bytearray(0x18)
    pcir[0:4] = b"PCIR"
    struct.pack_into("<HHHHB", pcir, 4, vendor, device, 0, 0x20, 0)   # length as Apple's ROMs have it
    pcir[0x0D:0x10] = klass
    struct.pack_into("<HHBB", pcir, 0x10, size // 512, 0, 1, 0x80 if last else 0)
    img[PCIR_OFF:PCIR_OFF + 0x18] = pcir
    img[PCIR_OFF + 0x18:FCODE_OFF] = bytes(FCODE_OFF - PCIR_OFF - 0x18)
    img[FCODE_OFF:FCODE_OFF + len(fc)] = fc
    img[-1] = (-sum(img[:-1])) & 0xFF
    return img


def main():
    args = sys.argv[1:]
    first = args[:1] == ["--fcode-first"]
    if first:
        args = args[1:]
    if len(args) != 3:
        die("usage: mkrom.py [--fcode-first] card.rom rdn.fc out.rom")
    rom = bytearray(open(args[0], "rb").read())
    fc = open(args[1], "rb").read()
    check_fcode(fc)

    chain = images(rom)
    if any(t == 1 for _, _, _, t, _ in chain):
        die("the dump already has an Open Firmware image")
    first_off, first_pcir = chain[0][0], chain[0][1]
    vendor, device = struct.unpack_from("<HH", rom, first_pcir + 4)
    klass = bytes(rom[first_pcir + 0x0D:first_pcir + 0x10])
    if vendor != 0x1002:
        die("the first image is not AMD's (vendor 0x%04x)" % vendor)
    if not any(t == 0 and is_atom(rom, o) for o, _, _, t, _ in chain):
        die("no x86 image with AtomBIOS tables in the dump")

    if first:
        img = fcode_image(fc, vendor, device, klass, last=False)
        open(args[2], "wb").write(img + rom)
        print("mkrom: %s: FCode image first (%d bytes of FCode), then the dump "
              "unchanged; %d KB, for emulators only"
              % (args[2], len(fc), (len(img) + len(rom)) // 1024))
        return

    last_off, last_pcir, last_len, last_type, _ = chain[-1]
    at = last_off + last_len
    img = fcode_image(fc, vendor, device, klass)
    end = at + len(img)
    if end > len(rom):
        die("the image needs 0x%x-0x%x but the flash ends at 0x%x"
            % (at, end, len(rom)))
    used = [i for i in range(at, end) if rom[i] != 0xFF]
    if used:
        die("0x%x-0x%x is not erased flash (data at 0x%x): the image does not fit"
            % (at, end, used[0]))

    # the image before ours is no longer the last; keep its checksum right
    rom[last_pcir + 0x15] &= 0x7F
    if last_type == 0:
        n = rom[last_off + 2] * 512
        if is_atom(rom, last_off):
            c = last_off + ATOM_CHECKSUM
            rom[c] = (rom[c] - sum(rom[last_off:last_off + n])) & 0xFF
        if sum(rom[last_off:last_off + n]) & 0xFF:
            die("could not keep the x86 image's checksum at zero")

    rom[at:end] = img
    open(args[2], "wb").write(rom)
    print("mkrom: %s: FCode image at 0x%x-0x%x (%d bytes of FCode), "
          "device %04x:%04x, flash %d KB"
          % (args[2], at, end, len(fc), vendor, device, len(rom) // 1024))


if __name__ == "__main__":
    main()

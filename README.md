# rdn-fcode — Open Firmware FCode for Juniper Radeons

An Open Firmware (FCode) ROM driver that gives a Radeon HD 5000/6000 "Juniper"
card a boot picture in a PCI Express Power Mac G5: the Open Firmware screen,
the grey Apple screen and the Mac OS X console. It is flashed next to the
card's own PC BIOS, so the card keeps working in a PC.

The same FCode runs on a real card in a real G5 and on the emulated card in
qemu-g5. Details of how it works: `docs/internals.md`.

## Supported cards

| Card | PCI ID |
|---|---|
| Radeon HD 5770 | 1002:68B8 |
| Radeon HD 6770 | 1002:68BA |
| Radeon HD 5750 | 1002:68BE |
| Radeon HD 6750 | 1002:68BF |

Any vendor's board with one of these chips: the FCode POSTs the card through
the board's own AtomBIOS, so memory type and clocks come from that board.

Boot picture on **DVI or HDMI only** (single-link, up to 165 MHz, e.g.
1920x1200 at 60 Hz). DisplayPort is not driven by the FCode; it works once
the Mac OS X driver (RadeonNI) loads.

Machines: PCI Express Power Mac G5 (Late 2005: Dual, Dual-core, Quad).

## What you need

| Tool | macOS | Linux |
|---|---|---|
| `toke`, `detok` (OpenBIOS fcode-utils) | build from https://github.com/openbios/fcode-utils (`make`, put `toke`/`detok` on PATH) | same |
| Python 3 | Xcode Command Line Tools or Homebrew | `apt install python3` |
| gforth (tests only) | `brew install gforth` | `apt install gforth` |
| C compiler (tests only) | `xcode-select --install` | `apt install build-essential` |

To flash: a PC (DOS or Windows) with ATIFlash 3.x / ATIWinFlash, and ideally
a CH341A SPI programmer with a SOIC-8 clip as a safety net.

## 1. Dump your card's ROM

On a PC with the card installed:

    atiflash -i                  (find the adapter number and flash chip)
    atiflash -s 0 card.rom       (save the full flash chip)

Keep copies of `card.rom` somewhere safe. It belongs to the card's vendor:
don't share it.

## 2. Build the FCode

    cd rom && toke rdn.fth && cd ..        (writes rom/rdn.fc)

## 3. Build and check the flash image

    python3 tools/mkrom.py card.rom rom/rdn.fc rdn-card.rom
    python3 tools/checkrom.py rdn-card.rom card.rom rom/rdn.fc

`mkrom.py` adds the FCode as a second (Open Firmware) image after the PC
image, clears the PC image's last-image flag and fixes its checksum. It only
writes into erased space and stops if the image won't fit the chip.
`checkrom.py` must report no errors before you flash.

## 4. Flash

On the PC:

    atiflash -p 0 rdn-card.rom          (add -f if it refuses the subsystem ID)

The PC image is unchanged, so the card still works in a PC and can always be
re-flashed there.

## 5. Use it

- **Real G5:** put the card in, connect a DVI or HDMI display, power on.
  Hold Cmd-Opt-O-F for the Open Firmware prompt to check the card node
  (`ATY,JuniperParent`).
- **Emulator:** qemu-g5 (branch radeon-juniper, `-device radeon-juniper`) with
  OpenBIOS patched by `tools/openbios-juniper.patch`. Paths to your builds go
  in the variables at the top of `tools/qemu-run.sh`, then
  `sh tools/qemu-run.sh`.

## Tests (optional)

Put your dump at `reference/vbios/visiontek6770.rom` (see the note there),
then:

    sh test/run.sh

Runs the AtomBIOS interpreter against Linux's atom.c, the PLL, EDID, mode
set, Open Firmware node and ROM builder tests on the host with gforth.

## Status

Working in the emulator with the Visiontek HD 6770 BIOS. First tests on real
cards: in progress. Report what your card does (vendor, card, display,
result) so the supported list can be confirmed.

## License

MIT (see `LICENSE`). `NOTICE` lists the RadeonNI and Linux radeon code (MIT)
this is ported from. `tools/openbios-juniper.patch` is GPL v2, as a change to
OpenBIOS. No VBIOS images are included.

#!/bin/sh
# Boot the emulated G5 (qemu-g5, branch radeon-juniper) with the emulated
# Radeon HD 6770 and the real flash image (x86 image first, FCode second),
# as the card itself: PCI ID 1002:68ba.
#   sh tools/qemu-run.sh                    a window (Cocoa), stops at the prompt
#   sh tools/qemu-run.sh headless           no window: serial log and a
#                                           monitor socket in build/qemu/
#   sh tools/qemu-run.sh -- <qemu args>     extra QEMU arguments, e.g. a disk:
#       sh tools/qemu-run.sh -- -drive file=Tiger.img,format=raw,if=ide,index=0,media=disk -boot c
#     (with extra arguments auto-boot? is left alone, so it boots)
#
# Needs the OpenBIOS with tools/openbios-juniper.patch (it knows the Juniper
# IDs and finds the FCode image after the x86 one); stock builds skip the card.
# Environment: QEMU, OPENBIOS, ROM override the defaults below.
cd "$(dirname "$0")/.."
QEMU=${QEMU:-$HOME/qemu-g5/build-juniper/qemu-system-ppc64}
OPENBIOS=${OPENBIOS:-$HOME/cat7-openbios-68ba/obj-ppc/openbios-qemu.elf}
ROM=${ROM:-out/rdn-visiontek6770.rom}
CARD="radeon-juniper,bus=pci.0,addr=0x10,romfile=$PWD/$ROM"

MODE=window
case "$1" in
headless) MODE=headless; shift ;;
esac
[ "$1" = "--" ] && shift
STOP="-prom-env auto-boot?=false"
[ $# -gt 0 ] && STOP=""

if [ "$MODE" = headless ]; then
	mkdir -p build/qemu
	exec "$QEMU" -M mac99,via=pmu -m 1G -bios "$OPENBIOS" -vga none \
		-display none -device "$CARD" $STOP \
		-serial file:build/qemu/serial.log \
		-monitor unix:build/qemu/mon.sock,server,nowait "$@"
fi
exec "$QEMU" -M mac99,via=pmu -m 1G -bios "$OPENBIOS" -vga none \
	-display cocoa -device "$CARD" $STOP -serial stdio "$@"

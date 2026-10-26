\ The FCode driver: what goes into the card's expansion ROM as the Open
\ Firmware image, next to the card's own x86 image (which holds the AtomBIOS
\ tables this code runs). Tokenize with:  toke rom/rdn.fth  (writes rom/rdn.fc)
\
\ The card node (ATY,JuniperParent) POSTs the card, sets the mode and has a
\ child per head; the console head is made here, between the heads before
\ and after it, so that its methods are compiled into it and the children
\ are listed in order.
fcode-version2
hex
headerless
\ the card code is used from the display child too: toke keeps a word list
\ per device node unless the words are global
global-definitions
fload ../src/platform.fs
fload ../src/consts.fs
fload ../src/atom.fs
fload ../src/card.fs
fload ../src/i2c.fs
fload ../src/modeset.fs
fload ../src/probe.fs
device-definitions
rdn-probe
heads-before
new-device
fload ../src/display-node.fs
rdn-display-node
finish-device
heads-after
fcode-end

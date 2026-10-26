\ The probe and the display node methods, with Open Firmware played by
\ test/of-stub.fs and the card by the fake register file. Checks the reg
\ property, the VBIOS search in the expansion ROM, register byte order,
\ delays, and what open, close, the colour and rectangle methods do.
\ Prints a log that test/run.sh compares with test/golden/node.txt.

hex
: u32 ( n -- n' ) ffffffff and ;   \ platform.fs made it a no-op for Open Firmware

0 value fails
: check ( got want a n -- )
   2swap 2dup = if 2drop 2drop exit then
   ." FAIL " 2swap type ." : got " swap .h ."  want " .h cr  fails 1+ to fails ;

\ ---- the expansion ROM ----
create vbios 10000 allot
: load-vbios ( -- )
   s" reference/vbios/visiontek6770.rom" r/o bin open-file throw >r
   vbios 10000 r@ read-file throw drop r> close-file throw ;
\ a stand-in FCode image: header, PCIR, code type 1
: fcode-image ( a last? -- )
   >r dup 800 erase
   55 over c! aa over 1+ c!  1c over 18 + c!
   dup 1c + s" PCIR" rot swap move
   4 over 1c 10 + + c!  1 over 1c 14 + + c!
   r> if 80 else 0 then swap 1c 15 + + c! ;
1e4 15 + constant x86-indicator
: rom-x86-first ( -- )
   fake-rom 20000 erase  vbios fake-rom 10000 move
   0 fake-rom x86-indicator + c!               \ not the last image any more
   fake-rom 10000 + true fcode-image ;
: rom-fcode-first ( -- )
   fake-rom 20000 erase  fake-rom false fcode-image
   vbios fake-rom 800 + 10000 move ;
: rom-ok? ( src -- f ) rom 10000 comp 0= /rom 10000 = and ;

: test-rom ( -- )
   load-vbios
   rom-x86-first rom-copy 1 and 1 s" rom-copy, x86 image first" check
   fake-rom rom-ok? 1 and 1 s" VBIOS copied, x86 first" check
   rom-fcode-first rom-copy 1 and 1 s" rom-copy, FCode image first" check
   fake-rom 800 + rom-ok? 1 and 1 s" VBIOS copied, FCode first" check
   30 cfg@ 1 and 0 s" ROM BAR as on entry" check
   fake-rom 20000 erase rom-copy 0 s" rom-copy, empty ROM" check ;

\ ---- register byte order (platform.fs reg@ reg!) ----
: le@ ( a -- u ) dup c@ over 1+ c@ 8 lshift or over 2 + c@ 10 lshift or swap 3 + c@ 18 lshift or ;
: test-regs ( -- )
   fake-regs to regs  fake-regs 20000 erase
   12345678 10 reg!
   fake-regs 40 + le@ 12345678 s" reg! stores little-endian" check
   10 reg@ 12345678 s" reg@ reads it back" check
   cafe 9000 reg!                               \ beyond the BAR: MM_INDEX/DATA
   fake-regs le@ 24000 s" MM_INDEX" check
   fake-regs 4 + le@ cafe s" MM_DATA" check ;

\ ---- delays ----
: test-delays ( -- )
   true to have-us? init-delays us-xt 0<> 1 and 1 s" uses the firmware's us" check
   false to have-us? init-delays us-xt 0 s" no us: calibrated loop" check
   spins/ms 0> 1 and 1 s" spins per ms" check
   true to have-us? init-delays ;

\ ---- configuration space: the same through bytes as through cells ----
: test-cfg ( -- )
   true to have-cfg-l? init-cfg cfg-bytes? 0 s" config-l@ found" check
   18 cfg@ 4 cfgw@
   false to have-cfg-l? init-cfg cfg-bytes? 1 and 1 s" bytes only" check
   4 cfgw@ s" command register by bytes" check
   18 cfg@ s" BAR 2 by bytes" check
   18 bar-size 20000 s" BAR 2 sized by bytes" check
   30 bar-size drop rom-size 20000 s" ROM sized by bytes" check
   12345 4 cfgw! 4 cfgw@ 2345 s" command written by bytes" check
   7 4 cfgw!  true to have-cfg-l? init-cfg ;

\ ---- the probe and the node ----
: rgb ( r g b -- n ) swap 8 lshift or swap 10 lshift or ;
: decode? ( -- f ) 4 cfg@ 2 and 0<> ;
: fb ( x y -- a ) line-bytes * + aper + ;
create rbuf 100 allot

: edid-ok? ( i -- f ) dup edid-len @ d# 128 = swap edid# test-edid d# 128 comp 0= and ;
: test-node ( -- )
   rom-x86-first
   s" reference/vbios/visiontek6770.rom" load-rom
   400 CONFIG_MEMSIZE 2 rshift model-poke        \ the card is posted
   atom-init drop read-outputs
   2 select-output o-i2c i2c-bus drop sim-attach
   0 make-edid test-edid sim-mem 100 move
   6 4 cfg!                       \ memory and bus master on, as OpenBIOS leaves it
   ['] rdn-decode is parent-rdn-decode
   true to hide-ddc?
   rdn-probe
   heads-before
   ." -- the display child" cr
   rdn-display-node
   heads-after
   false to hide-ddc?
   rdn-ok? 1 and 1 s" probe set a mode" check
   active# 2 s" driven output" check
   2 edid-ok? 1 and 1 s" EDID of output 2 kept" check
   0 edid-len @ 0 s" no EDID on DisplayPort" check
   1 edid-len @ 0 s" no EDID on HDMI (no display)" check
   2 display-type s" LCD" compare 0 s" display-type LCD" check
   1 display-type s" NONE" compare 0 s" display-type NONE" check
   2 child-name s" ATY,Phantom_C" compare 0 s" name of head 2" check
   s" 2" decode-unit 2 s" decode-unit" check
   1 encode-unit s" 1" compare 0 s" encode-unit" check
   0 child-name s" ATY,Phantom_A" compare 0 s" name of head 0" check
   0 main @ 2 s" head A: DVI on UNIPHY1" check
   0 alt @ -1 s" head A: no alternate" check
   1 main @ 3 s" head B: DVI on UNIPHY0" check
   1 alt @ 1 s" head B: HDMI as alternate" check
   2 main @ 0 s" head C: DisplayPort" check
   2 alt @ -1 s" head C: no alternate" check
   active-head 0 s" console on head A" check
   0 edid-output 2 s" head A shows output 2's EDID" check
   2 edid-output -1 s" head C: no EDID" check
   4 cfg@ ffff and 6 s" command register as on entry" check
   30 cfg@ 1 and 0 s" ROM BAR as on entry" check
   2 4 cfg!                       \ the firmware's business from here on
   0 4 cfg!
   width d# 1920 s" width" check  height d# 1080 s" height" check
   line-bytes d# 1920 s" line-bytes" check
   aper fake-fb s" framebuffer mapped" check
   0 0 fb c@ 0 s" screen cleared to colour 0" check
   0 color@ rgb ffffff s" colour 0 is white" check
   ff color@ rgb 0 s" colour 255 is black" check
   d# 214 color@ rgb 33 s" colour 214 (cube)" check
   d# 215 color@ rgb ee0000 s" colour 215 (red ramp)" check
   d# 254 color@ rgb 111111 s" colour 254 (grey ramp)" check

   ." -- open" cr
   install-xt execute
   decode? 1 and 1 s" decode on while open" check
   frame-buffer-adr aper s" frame-buffer-adr" check
   install-xt execute
   ." -- colours" cr
   11 22 33 5 color!
   5 color@ rgb 112233 s" color@" check
   EVERGREEN_DC_LUT_30_COLOR 2 rshift model-peek
   11 d# 22 lshift 22 d# 12 lshift or 33 2 lshift or s" LUT entry" check
   rbuf 9 erase  1 rbuf c! 2 rbuf 1+ c! 3 rbuf 2 + c!  fe rbuf 8 + c!
   rbuf 10 3 set-colors
   rbuf 10 + 10 3 get-colors
   rbuf rbuf 10 + 9 comp 0 s" set-colors / get-colors" check
   dimensions d# 1080 s" dimensions h" check d# 1920 s" dimensions w" check
   ." -- rectangles" cr
   7 10 20 30 4 fill-rectangle
   10 20 fb c@ 7 s" fill: first pixel" check
   3f 23 fb c@ 7 s" fill: last pixel" check
   40 23 fb c@ 0 s" fill: right of it" check
   10 24 fb c@ 0 s" fill: below it" check
   rbuf 100 0 do i over i + c! loop drop
   rbuf 100 200 10 8 draw-rectangle
   100 200 fb c@ 0 s" draw: (0,0)" check
   10f 207 fb c@ 7f s" draw: (15,7)" check
   rbuf 80 erase rbuf 100 200 10 8 read-rectangle
   rbuf 7f + c@ 7f s" read back" check
   ." -- close" cr
   remove-xt execute decode? 1 and 1 s" still open once" check
   remove-xt execute decode? 0 s" decode off after the last close" check ;

: run ( -- )
   test-cfg
   false to have-cfg-l?                 \ the probe as under OpenBIOS
   test-rom test-regs test-delays test-node
   fails 0= if ." node: all checks passed" cr then ;
decimal
run bye

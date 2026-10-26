\ The display: the child node of the output being driven (src/probe.fs
\ makes the card node and the other outputs' children). Loaded between
\ new-device and finish-device, so the methods below are this node's.
\
\ Open Firmware's console is 8 bits per pixel; the colour table starts as
\ the Mac OS default (0 white, 255 black). open, write, draw-logo and
\ restore come from is-install, close from is-remove, as in Apple's and
\ NVIDIA's Mac ROMs. Apple's boot screen also calls dimensions, the
\ rectangle methods and the colour methods (RadeonNI, docs/OPEN-FIRMWARE.md).

hex

external
\ fb8-install expects the driver to define these (found on the G5 by RadeonNI)
0 value width
0 value height
0 value line-bytes
headerless

0 value #opens

\ ---- the colour table: a copy in memory, and the card's LUT ----
300 buffer: pal
: pal! ( r g b i -- ) 3 * pal + >r r@ 2 + c! r@ 1+ c! r> c! ;
: pal@ ( i -- r g b ) 3 * pal + dup c@ over 1+ c@ rot 2 + c@ ;
: pal>lut ( start count -- ) bounds ?do i pal@ i lut! loop ;

\ The Mac OS 8-bit system palette: a 6x6x6 cube from white to black, then
\ ten-step ramps of red, green, blue and grey, then black.
create ramp ee c, dd c, bb c, aa c, 88 c, 77 c, 55 c, 44 c, 22 c, 11 c,
: mac-clut ( i -- r g b )
   dup d# 215 < if
      dup d# 36 / 5 swap - 33 *
      over 6 / 6 mod 5 swap - 33 *
      rot 6 mod 5 swap - 33 *
      exit
   then
   dup ff = if drop 0 0 0 exit then
   d# 215 - d# 10 /mod swap ramp + c@ swap            ( v ramp# )
   case
      0 of 0 0 endof
      1 of 0 swap 0 endof
      2 of 0 0 rot endof
      >r dup dup r>
   endcase ;

\ ---- rectangles, 8 bits per pixel ----
0 value ra  0 value rx  0 value ry  0 value rw  0 value rh
: rect ( a x y w h -- ) to rh to rw to ry to rx to ra ;
: row ( i -- a ) ry + line-bytes * rx + aper + ;

\ ---- opening and closing ----
: rdn-install ( -- )
   #opens 0= if true " rdn-decode" $call-parent then
   #opens 1+ to #opens
   aper to frame-buffer-adr
   default-font set-font
   width height  over char-width /  over char-height /  fb8-install ;

\ The last close turns memory decode off: Mac OS X hangs at boot when it
\ finds the card still decoding (RadeonNI). Scanout does not need it.
: rdn-remove ( -- )
   #opens if #opens 1- to #opens then
   #opens 0= if false " rdn-decode" $call-parent then ;

external

: dimensions ( -- width height ) width height ;

: fill-rectangle ( index x y w h -- )
   rect rh 0 ?do i row rw ra fill loop ;
: draw-rectangle ( adr x y w h -- )
   rect rh 0 ?do ra i rw * + i row rw move loop ;
: read-rectangle ( adr x y w h -- )
   rect rh 0 ?do i row ra i rw * + rw move loop ;

: color! ( r g b index -- ) dup >r pal! r> 1 pal>lut ;
: color@ ( index -- r g b ) pal@ ;
: set-colors ( adr start count -- )
   >r tuck 3 * pal + r@ 3 * move r> pal>lut ;
: get-colors ( adr start count -- )
   3 * >r 3 * pal + swap r> move ;

headerless

\ ---- this node's properties ----
: rdn-display-node ( -- )
   rdn-ok? 0= if
      " ATY,Phantom" device-name
      " no picture" encode-string " status" property exit
   then
   active-head head-props  active# select-output
   m-hd to width  m-vd to height  fb-pitch to line-bytes
   100 0 do i mac-clut i pal! loop  0 100 pal>lut
   aper line-bytes height * 0 fill                      \ white
   " display" device-type
   " ISO8859-1" encode-string " character-set" property
   width encode-int " width" property
   height encode-int " height" property
   8 encode-int " depth" property
   line-bytes encode-int " linebytes" property
   aper encode-int " address" property
   ['] rdn-install is-install
   ['] rdn-remove is-remove ;

decimal

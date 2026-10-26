\ Just enough of Open Firmware, in gforth, to run src/platform.fs and
\ src/display.fs: the PCI parent's methods over a fake configuration space,
\ map-in over host buffers, property encoding, and the display package words.
\ Load after the card code (test/prelude.fs, src/*.fs through modeset.fs).

hex
: external ; : headers ; : headerless ;
: " ( "text" -- a n ) [char] " parse postpone sliteral ; immediate

\ the CPU is big-endian: l@ and l! put the high byte first
: l@ ( a -- u ) dup c@ 18 lshift over 1+ c@ 10 lshift or over 2 + c@ 8 lshift or swap 3 + c@ or ;
: l! ( u a -- ) over 18 rshift over c!  over 10 rshift ff and over 1+ c!
   over 8 rshift ff and over 2 + c!  swap ff and swap 3 + c! ;
: lbflip ( u -- u' )
   dup ff and 18 lshift over ff00 and 8 lshift or
   over 10 rshift ff and 8 lshift or swap 18 rshift ff and or ;

0 value msecs
: get-msecs ( -- ms ) msecs dup 1+ to msecs ;
: ms ( n -- ) ." D " d# 1000 * .h cr ;
\ this firmware has a "us"; test/node.fs also tries it without
true value have-us?
: us ( n -- ) ." D " .h cr ;
: $find ( a n -- xt true | a n false )
   2dup s" us" compare 0= have-us? and if 2drop ['] us true else false then ;

\ ---- the card's configuration space ----
1000 constant my-space
create cfg 100 allot  cfg 100 erase
: cfg-l ( off -- a ) cfg + ;
\ BARs: 10 aperture 256 MB 64-bit prefetchable, 18 registers 128 KB 64-bit,
\ 20 I/O 256 bytes, 30 ROM 128 KB
create bar-masks
   f0000000 , ffffffff , fffe0000 , ffffffff , ffffff00 , 0 , 0 , 0 ,
create bar-types  c , 0 , 4 , 0 , 1 , 0 , 0 , 0 ,
: bar? ( off -- i true | false ) dup 10 28 within if 10 - 4 / true else drop false then ;
: config-l! ( v phys -- )
   my-space - dup bar? if                ( v off i )
      >r swap r@ cells bar-masks + @ and r> cells bar-types + @ or swap
   else dup 30 = if swap fffe0001 and swap then then
   cfg-l ! ;
: config-l@ ( phys -- v ) my-space - cfg-l @ ;
: config-w@ ( phys -- w ) config-l@ ffff and ;
\ byte accesses go through the 32-bit register, so BAR masks apply
: config-b@ ( phys -- b ) dup 3 invert and config-l@ swap 3 and 8 * rshift ff and ;
: config-b! ( b phys -- )
   dup 3 and 8 * >r 3 invert and                     ( b a )  ( r: shift )
   dup config-l@ ff r@ lshift invert and  rot ff and r> lshift or  swap config-l! ;
: config-w! ( w phys -- )
   tuck config-l@ ffff invert and or swap my-space - cfg-l ! ;

\ addresses the "firmware" assigned
90000000 10 cfg-l !  0 14 cfg-l !  80140004 18 cfg-l !  0 1c cfg-l !
f001 20 cfg-l !  80120000 30 cfg-l !

\ map-in: host buffers stand in for the BARs
1000000 constant /fake-fb
/fake-fb allocate throw constant fake-fb
20000 allocate throw constant fake-regs
20000 allocate throw constant fake-rom
fake-regs 20000 erase  fake-rom 20000 erase
: map-in ( lo mid hi size -- va )
   >r nip nip ff and r> drop
   case 10 of fake-fb endof 18 of fake-regs endof 30 of fake-rom endof
      ." map-in of an unknown BAR" cr 0 swap endcase ;
: map-out ( va size -- ) 2drop ;
\ Apple's firmware has config-l@ and the rest; OpenBIOS only config-b@/b!
true value have-cfg-l?
0 constant my-parent
: ihandle>phandle ( ih -- ph ) ;
: find-method ( a n ph -- xt true | false )
   drop 2dup s" config-l@" compare 0= have-cfg-l? and
   if 2drop ['] config-l@ true else 2drop false then ;
\ the display child reaches the card node's rdn-decode; set by the test
defer parent-rdn-decode ( on? -- )
: $call-parent ( ... a n -- ... )
   2dup s" config-l@" compare 0= if 2drop config-l@ exit then
   2dup s" config-l!" compare 0= if 2drop config-l! exit then
   2dup s" config-w@" compare 0= if 2drop config-w@ exit then
   2dup s" config-w!" compare 0= if 2drop config-w! exit then
   2dup s" config-b@" compare 0= if 2drop config-b@ exit then
   2dup s" config-b!" compare 0= if 2drop config-b! exit then
   2dup s" map-in" compare 0= if 2drop map-in exit then
   2dup s" rdn-decode" compare 0= if 2drop parent-rdn-decode exit then
   2dup s" map-out" compare 0= if 2drop map-out exit then
   ." unknown parent method " type cr abort ;

\ ---- property encoding: big-endian cells, printed when set ----
: encode-int ( n -- a len ) 4 allocate throw tuck l! 4 ;
0 value ea1  0 value el1  0 value ea2  0 value el2
: encode+ ( a1 l1 a2 l2 -- a l )
   to el2 to ea2 to el1 to ea1
   el1 el2 + allocate throw
   ea1 over el1 move  ea2 over el1 + el2 move  el1 el2 + ;
\ phys.hi first, then phys.mid, phys.lo
: encode-phys ( lo mid hi -- a len )
   encode-int rot encode-int encode+ rot encode-int encode+ ;
: printable? ( a len -- f )         \ printable text and a final NUL
   dup 2 < if 2drop false exit then
   2dup + 1- c@ if 2drop false exit then
   1- bounds ?do i c@ bl 7f within 0= if unloop false exit then loop true ;
: property ( a len name-a name-n -- )
   ." property " type ." :"
   2dup printable? over 3 and 0<> or if
      space [char] " emit 1- 0 max type [char] " emit cr exit then
   bounds ?do space i l@ .h 4 +loop cr ;
: encode-string ( a n -- a' n' )
   dup 1+ allocate throw >r  r@ swap dup >r move  0 r> r@ + c!
   r> 0 begin 2dup + c@ while 1+ repeat 1+ ;
: encode-bytes ( a n -- a' n ) dup allocate throw swap 2dup 2>r move 2r> ;
: device-name ( a n -- ) ." device-name " type cr ;
: new-device ( -- ) ." new-device" cr ;
: finish-device ( -- ) ." finish-device" cr ;
: $number ( a n -- n false | true )
   base @ >r hex 0 0 2swap >number nip nip r> base ! if drop true else false then ;
: (u.) ( u -- a n ) base @ >r hex 0 <# #s #> r> base ! ;
: delete-property ( a n -- ) ." delete-property " type cr ;
: device-type ( a n -- ) ." device-type " type cr ;

\ ---- the display package ----
0 value install-xt  0 value remove-xt
: is-install ( xt -- ) to install-xt ;
: is-remove ( xt -- ) to remove-xt ;
0 value frame-buffer-adr
0 value char-width  0 value char-height
: default-font ( -- a w h adv min #glyphs ) 0 8 10 8 20 5f ;
: set-font ( a w h adv min #glyphs -- ) 2drop drop to char-height to char-width drop ;
0 value fb8-args
: fb8-install ( w h cols lines -- )
   ." fb8-install " 2swap swap .h space .h space swap .h space .h cr ;
decimal

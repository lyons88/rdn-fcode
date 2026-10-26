\ The Open Firmware side of the driver: PCI configuration space, the BARs
\ and the "reg" property, register access, delays, logging, and the VBIOS
\ copied out of the card's expansion ROM. Supplies what src/atom.fs and
\ src/card.fs need from a platform (test/prelude.fs does the same for gforth).
\
\ Facts this relies on, from RadeonNI's work on the PowerMac11,2 (Open
\ Firmware 5.2.7f1, docs/OPEN-FIRMWARE.md there):
\  - plain l@ / l! on a map-in address work for the card's registers, with
\    lbflip for the byte order (the registers are little-endian);
\  - memory decode is off until the driver turns it on, and Mac OS X hangs at
\    boot if the card is left decoding: decode is on only while we need it.

hex

\ Cells are 32 bits in the G5's Open Firmware: nothing to mask.
: u32 ( n -- n ) ;

\ ---- logging ----
: atom-warn ( a n -- ) ." rdn: " type cr ;

\ ---- delays ----
\ Open Firmware has ms; a microsecond word is not standard. Use one called
\ "us" if this firmware has it, else a busy loop timed against get-msecs.
0 value us-xt
0 value spins/ms
: spin ( n -- ) 0 ?do get-msecs drop loop ;
: calibrate ( -- )
   get-msecs begin get-msecs over <> until drop        \ start on a tick
   0 get-msecs d# 10 +                                  ( count end )
   begin swap 1+ swap dup get-msecs - 0<= until drop
   d# 10 / 1 max to spins/ms ;
: init-delays ( -- )
   " us" $find if to us-xt else 2drop 0 to us-xt calibrate then ;
: atom-us ( n -- )
   us-xt ?dup if execute exit then
   d# 1000 /mod ?dup if ms then                         ( rest )
   spins/ms * d# 1000 / 1+ spin ;
: atom-ms@ ( -- ms ) get-msecs ;

\ ---- PCI configuration space ----
\ The PCI binding gives the parent config-b@ w@ l@ and their stores. Some
\ firmware (OpenBIOS) has only the byte ones: then 16- and 32-bit accesses
\ are made of bytes, which PCI allows.
false value cfg-bytes?
: init-cfg ( -- )
   " config-l@" my-parent ihandle>phandle find-method
   if drop false else true then to cfg-bytes? ;
: cfgb@ ( off -- b ) my-space + " config-b@" $call-parent ;
: cfgb! ( b off -- ) my-space + " config-b!" $call-parent ;
: cfgn@ ( off n -- v )                 \ n bytes, little-endian
   0 swap 0 ?do  over i + cfgb@ i 8 * lshift or  loop nip ;
: cfgn! ( v off n -- )
   0 ?do  over i 8 * rshift ff and  over i + cfgb!  loop 2drop ;
: cfg@  ( off -- l )
   cfg-bytes? if 4 cfgn@ exit then  my-space + " config-l@" $call-parent ;
: cfg!  ( l off -- )
   cfg-bytes? if 4 cfgn! exit then  my-space + " config-l!" $call-parent ;
: cfgw@ ( off -- w )
   cfg-bytes? if 2 cfgn@ exit then  my-space + " config-w@" $call-parent ;
: cfgw! ( w off -- )
   cfg-bytes? if 2 cfgn! exit then  my-space + " config-w!" $call-parent ;

\ Memory decode (command bit 1) on or off. Bus mastering (bit 2) is never
\ needed and is always cleared.
: decode ( on? -- )
   4 cfgw@ 6 invert and swap if 2 or then 4 cfgw! ;

\ ---- the BARs ----
\ Size a BAR (decode must be off): write ones, read back, restore.
: bar-size ( off -- size )
   dup cfg@ >r
   ffffffff over cfg!  dup cfg@  r> rot cfg!            ( v )
   dup 0= if exit then
   dup 1 and if fffffffc else fffffff0 then and          \ I/O or memory
   invert 1+ ffffffff and ;
: rom-size ( -- size )
   30 cfg@ >r  fffff800 30 cfg!  30 cfg@  r> 30 cfg!
   fffff800 and dup if invert 1+ ffffffff and then ;

\ phys.hi of a BAR in the PCI binding's 3-cell address: the space code
\ (01 I/O, 02 32-bit memory, 03 64-bit memory), prefetchable, the register.
0 value bv
: bar-hi ( off -- phys.hi )
   dup 30 = if 02000000 else
      dup cfg@ to bv
      bv 1 and if 01000000 else
         bv 6 and 4 = if 03000000 else 02000000 then
         bv 8 and if 40000000 or then
      then
   then  or my-space or ;
: 64-bit? ( off -- f ) cfg@ 7 and 4 = ;

\ the "reg" property: configuration space, then every BAR and the ROM
: +reg ( prop len off size -- prop' len' )
   >r 0 0 rot bar-hi encode-phys encode+  0 encode-int encode+
   r> encode-int encode+ ;
0 value ro
: make-reg ( -- )
   0 0 my-space encode-phys 0 encode-int encode+ 0 encode-int encode+
   10 to ro
   begin ro 28 < while
      ro bar-size ?dup if ro swap +reg then
      ro dup 64-bit? if 8 else 4 then + to ro
   repeat
   rom-size ?dup if 30 swap +reg then
   " reg" property ;

: map ( off size -- va ) >r >r 0 0 r> bar-hi r> " map-in" $call-parent ;
: unmap ( va size -- ) " map-out" $call-parent ;

\ ---- registers: BAR 2, 128 KB, little-endian ----
0 value regs
20000 constant /regs
: (reg@) ( off -- v ) regs + l@ lbflip ;
: (reg!) ( v off -- ) regs + >r lbflip r> l! ;
\ by dword index, as AtomBIOS counts; beyond the BAR through MM_INDEX/DATA
: reg@ ( idx -- v ) 4 * dup /regs u< if (reg@) else 0 (reg!) 4 (reg@) then ;
: reg! ( v idx -- ) 4 * dup /regs u< if (reg!) else 0 (reg!) 4 (reg!) then ;
\ AtomBIOS's indirect I/O goes to the same registers (no I/O BAR is used)
: ioreg@ ( idx -- v ) reg@ ;
: ioreg! ( v idx -- ) reg! ;
\ Evergreen has no separate PLL or MC register space
: pll@ ( idx -- 0 ) drop " invalid PLL read" atom-warn 0 ;
: pll! ( v idx -- ) 2drop " invalid PLL write" atom-warn ;
: mc@  ( idx -- 0 ) drop " invalid MC read" atom-warn 0 ;
: mc!  ( v idx -- ) 2drop " invalid MC write" atom-warn ;

\ ---- the VBIOS ----
\ The expansion ROM holds the card's x86 image, which carries the AtomBIOS
\ tables, and this FCode as another image. Copy the x86 image to memory.
0 value rom            \ the VBIOS image in memory, for src/atom.fs
0 value /rom
0 value rom-va  0 value /rombar  0 value rom-sig
: rom-c@ ( off -- b ) rom-va + c@ ;
: rom-w@ ( off -- w ) dup rom-c@ swap 1+ rom-c@ 8 lshift or ;
: sig? ( off a n -- f )
   0 ?do
      over i + rom-c@  over i + c@  <> if 2drop false unloop exit then
   loop 2drop true ;

\ ( -- off len true | false ) the first x86 image with an ATOM header
: find-atom ( -- off len true | false )
   0 begin dup 1+ /rombar u< while
      dup rom-w@ aa55 <> if drop false exit then
      dup 18 + rom-w@ over +                            ( off pcir )
      dup " PCIR" sig? 0= if 2drop false exit then
      dup 14 + rom-c@ 0= if                             \ code type 0: x86
         over dup 48 + rom-w@ + 4 + " ATOM" sig? if
            10 + rom-w@ 200 * true exit
         then
      then
      dup 15 + rom-c@ 80 and if 2drop false exit then  \ the last image
      10 + rom-w@ 200 * +
   repeat drop false ;

: rom-copy ( -- ok? )
   rom-size dup to /rombar 0= if false exit then
   30 cfg@ >r                                           \ as the firmware has it
   \ Map with the enable bit clear: OpenBIOS takes bit 0 of the BAR it
   \ maps for the I/O-space bit, which on the ROM BAR is the enable.
   r@ 1 invert and 30 cfg!
   30 /rombar map to rom-va
   r@ 1 or 30 cfg!                                      \ ROM decode on
   0 rom-w@ to rom-sig                                  \ for the log
   find-atom if
      dup to /rom alloc-mem to rom                      ( off )
      rom-va + rom /rom move  true
   else false then
   rom-va /rombar unmap
   \ The firmware may still be reading this FCode out of the ROM.
   r> 30 cfg! ;

decimal

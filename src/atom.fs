\ AtomBIOS command-table interpreter: a Forth port of Linux's
\ drivers/gpu/drm/radeon/atom.c (Stanislaw Skowronek, MIT licence).
\ It follows atom.c step by step, quirks included, so its register trace
\ can be compared with the C version (see test/).
\
\ Needs from the platform prelude:
\   u32 ( n -- n' )         keep the low 32 bits
\   reg@ ( idx -- v )       MMIO read, dword index (cail_reg_read)
\   reg! ( v idx -- )       MMIO write, dword index
\   ioreg@ ioreg!           the same, for indirect-IO programs
\   pll@ pll! mc@ mc!       not used on Evergreen; report and return 0
\   atom-us ( n -- )        delay in microseconds
\   atom-ms@ ( -- ms )      millisecond clock, for the stuck-loop check
\   atom-warn ( a n -- )    report a problem
\   rom ( -- a )            the VBIOS image in RAM
\   alloc-mem ( n -- a )

hex

\ ---- VBIOS reads: little-endian, a byte at a time ----
: cu8  ( off -- b ) rom + c@ ;
: cu16 ( off -- w ) dup cu8 swap 1+ cu8 8 lshift or ;
: cu32 ( off -- l ) dup cu16 swap 2 + cu16 10 lshift or ;

: le-l@ ( a -- l )
   dup c@  over 1+ c@ 8 lshift or  over 2 + c@ 10 lshift or  swap 3 + c@ 18 lshift or ;
: le-l! ( l a -- )
   over ff and over c!  over 8 rshift ff and over 1+ c!
   over 10 rshift ff and over 2 + c!  swap 18 rshift ff and swap 3 + c! ;

\ ---- interpreter state (struct atom_context) ----
0 value cmd-table   0 value data-table
0 value data-block  0 value fb-base   0 value io-attr  0 value reg-block
0 value a-shift     0 value cs-equal  0 value cs-above 0 value io-mode
0 value divmul0     0 value divmul1
0 value iio                    \ 100 cells: offset of each IIO program, 0 = none
0 value scratch     0 value scratch-bytes

\ ---- execution frames (atom_exec_context), one per nested table ----
0 value fp          \ the current frame
0 value fstack      \ where frames are carved from
0 value ip          \ instruction pointer: an offset into the VBIOS
: e-ps     ( -- a ) fp ;                 \ byte address of the parameter space
: e-pssize ( -- a ) fp 1 cells + ;       \ its size in dwords
: e-psshift ( -- a ) fp 2 cells + ;
: e-start  ( -- a ) fp 3 cells + ;
: e-wssize ( -- a ) fp 4 cells + ;
: e-lastj  ( -- a ) fp 5 cells + ;
: e-lastms ( -- a ) fp 6 cells + ;
: e-abort  ( -- a ) fp 7 cells + ;
: e-prev   ( -- a ) fp 8 cells + ;       \ the caller's frame
9 cells constant /frame
: ws-adr ( idx -- a ) cells fp + /frame + ;

: ip-u8  ( -- b ) ip cu8  ip 1+ to ip ;
: ip-u16 ( -- w ) ip cu16 ip 2 + to ip ;
: ip-u32 ( -- l ) ip cu32 ip 4 + to ip ;

\ ---- operand alignment tables ----
create arg-mask  ffffffff , ffff , ffff00 , ffff0000 , ff , ff00 , ff0000 , ff000000 ,
create arg-shift 0 , 0 , 8 , 10 , 0 , 8 , 10 , 18 ,
create dst>src
   0 , 0 , 0 , 0 ,   1 , 2 , 3 , 0 ,   1 , 2 , 3 , 0 ,   1 , 2 , 3 , 0 ,
   4 , 5 , 6 , 7 ,   4 , 5 , 6 , 7 ,   4 , 5 , 6 , 7 ,   4 , 5 , 6 , 7 ,
create def-dst 0 , 0 , 1 , 2 , 0 , 1 , 2 , 3 ,
: amask  ( align -- m ) cells arg-mask + @ ;
: ashift ( align -- s ) cells arg-shift + @ ;
\ atom_dst_to_src[(attr >> 3) & 7][(attr >> 6) & 3]
: d>s ( attr -- align )
   dup 3 rshift 7 and 4 *  swap 6 rshift 3 and +  cells dst>src + @ ;

cdcdcdcd constant poison

\ Shift counts of 32 or more act as on x86, where atom.c normally runs.
: <<32 ( x n -- x' ) 1f and lshift u32 ;
: >>32 ( x n -- x' ) 1f and rshift ;

\ ---- parameter space (little-endian dwords) and work space ----
: ps@ ( idx -- v )
   dup e-pssize @ u< if 4 * e-ps @ + le-l@
   else drop s" ATOM: PS index out of range" atom-warn poison then ;
: ps! ( v idx -- )
   dup e-pssize @ u< if 4 * e-ps @ + le-l!
   else 2drop s" ATOM: PS index out of range" atom-warn then ;

: ws@ ( idx -- v )
   dup 40 = if drop divmul0 exit then
   dup 41 = if drop divmul1 exit then
   dup 42 = if drop data-block exit then
   dup 43 = if drop a-shift exit then
   dup 44 = if drop 1 a-shift <<32 exit then
   dup 45 = if drop 1 a-shift <<32 invert u32 exit then
   dup 46 = if drop fb-base exit then
   dup 47 = if drop io-attr exit then
   dup 48 = if drop reg-block exit then
   dup e-wssize @ u< if ws-adr @ exit then
   drop s" ATOM: WS index out of range" atom-warn poison ;

: ws! ( v idx -- )
   dup 40 = if drop to divmul0 exit then
   dup 41 = if drop to divmul1 exit then
   dup 42 = if drop ffff and to data-block exit then
   dup 43 = if drop ff and to a-shift exit then
   dup 44 = if 2drop exit then
   dup 45 = if 2drop exit then
   dup 46 = if drop to fb-base exit then
   dup 47 = if drop ffff and to io-attr exit then
   dup 48 = if drop ffff and to reg-block exit then
   dup e-wssize @ u< if ws-adr ! exit then
   2drop s" ATOM: WS index out of range" atom-warn ;

\ the FB scratch area; atom.c's bounds test lets one dword past the end through
: fb-ok? ( idx -- f ) 4 * fb-base + scratch-bytes u> 0= ;
: fb-adr ( idx -- a ) fb-base 2 rshift + cells scratch + ;

\ ---- indirect IO programs (atom_iio_execute) ----
0 value iio-temp  0 value iio-base
: bits ( n -- mask ) ffffffff swap 20 swap - >>32 ;
: iio-b ( n -- byte ) iio-base + cu8 ;
\ temp &= ~(bits(b1) << b3); temp |= ((src >> b2) & bits(b1)) << b3
: iio-move ( src -- )
   iio-temp  1 iio-b bits 3 iio-b <<32 invert and to iio-temp
   2 iio-b >>32  1 iio-b bits and  3 iio-b <<32  iio-temp or to iio-temp
   iio-base 4 + to iio-base ;
: iio-exec ( index data base -- v )
   to iio-base  poison to iio-temp
   begin
      0 iio-b
      dup 0 = if drop iio-base 1+ to iio-base else
      dup 2 = if drop 1 iio-base + cu16 ioreg@ to iio-temp  iio-base 3 + to iio-base else
      dup 3 = if drop iio-temp 1 iio-base + cu16 ioreg!  iio-base 3 + to iio-base else
      dup 4 = if drop iio-temp 1 iio-b bits 2 iio-b <<32 invert and to iio-temp
                 iio-base 3 + to iio-base else
      dup 5 = if drop iio-temp 1 iio-b bits 2 iio-b <<32 or to iio-temp
                 iio-base 3 + to iio-base else
      dup 6 = if drop over iio-move else
      dup 8 = if drop dup iio-move else
      dup 7 = if drop io-attr iio-move else
      dup 9 = if drop 2drop iio-temp exit else
      drop 2drop s" ATOM: unknown IIO opcode" atom-warn 0 exit
      then then then then then then then then then
   again ;

\ ---- reading operands (atom_get_src_int) ----
: reg-read ( idx -- v )
   reg-block +
   io-mode 0 = if reg@ exit then
   io-mode 1 = if drop s" ATOM: PCI registers are not implemented" atom-warn 0 exit then
   io-mode 2 = if drop s" ATOM: SYSIO registers are not implemented" atom-warn 0 exit then
   io-mode 80 and 0= if drop s" ATOM: bad IO mode" atom-warn 0 exit then
   io-mode 7f and cells iio + @ ?dup 0= if
      drop s" ATOM: undefined indirect IO read method" atom-warn 0 exit then
   0 swap iio-exec ;

: imm ( align -- v )
   dup 0 = if drop ip-u32 exit then
   4 < if ip-u16 else ip-u8 then ;

\ ( attr -- val saved ): val is the aligned field, saved the whole value.
: get-src-int ( attr -- val saved )
   dup 3 rshift 7 and swap 7 and        ( align arg )
   dup 0 = if drop ip-u16 reg-read else
   dup 1 = if drop ip-u8 ps@ else
   dup 2 = if drop ip-u8 ws@ else
   dup 3 = if drop ip-u8 dup fb-ok? if fb-adr @ else drop
                 s" ATOM: fb read beyond scratch region" atom-warn 0 then else
   dup 4 = if drop ip-u16 data-block + cu32 else
   dup 5 = if drop imm dup exit else
   dup 6 = if drop ip-u8 pll@ else
              drop ip-u8 mc@
   then then then then then then then   ( align v )
   tuck  swap dup amask rot and  swap ashift rshift  swap ;

: get-src ( attr -- v ) get-src-int drop ;
: dst-attr ( arg attr -- attr' ) d>s 3 lshift or ;
: get-dst ( arg attr -- val saved ) dst-attr get-src-int ;
: src-direct ( align -- v ) imm ;

: skip-src ( attr -- )
   dup 7 and
   dup 0 = over 4 = or if 2drop ip 2 + to ip exit then
   5 = if 3 rshift 7 and dup 0 = if drop 4 else 4 < if 2 else 1 then then
   else drop 1 then  ip + to ip ;
: skip-dst ( arg attr -- ) dst-attr skip-src ;

\ ---- writing operands (atom_put_dst) ----
: reg-write ( v idx -- )
   reg-block +
   io-mode 0 = if dup 0= if swap 2 <<32 swap then reg! exit then
   io-mode 1 = if 2drop s" ATOM: PCI registers are not implemented" atom-warn exit then
   io-mode 2 = if 2drop s" ATOM: SYSIO registers are not implemented" atom-warn exit then
   io-mode 80 and 0= if 2drop s" ATOM: bad IO mode" atom-warn exit then
   \ atom.c indexes the table with io_mode & 0xFF here, not & 0x7F
   io-mode ff and cells iio + @ ?dup 0= if
      2drop s" ATOM: undefined indirect IO write method" atom-warn exit then
   >r swap r> iio-exec drop ;

0 value pd-arg  0 value pd-align  0 value pd-val  0 value pd-ip
: put-dst ( arg attr dptr val saved -- )
   >r to pd-val  ip to pd-ip  to ip  d>s to pd-align  to pd-arg
   pd-val pd-align ashift <<32  pd-align amask and
   r> pd-align amask invert and or to pd-val
   pd-arg 0 = if pd-val ip-u16 reg-write else
   pd-arg 1 = if pd-val ip-u8 ps! else
   pd-arg 2 = if pd-val ip-u8 ws! else
   pd-arg 3 = if ip-u8 dup fb-ok? if pd-val swap fb-adr ! else drop
                 s" ATOM: fb write beyond scratch region" atom-warn then else
   pd-arg 6 = if pd-val ip-u8 pll! else
   pd-arg 7 = if pd-val ip-u8 mc! then
   then then then then then
   pd-ip to ip ;

\ ---- opcodes ----
defer exec-table ( params pssize index -- error? )

\ the common read-modify-write shape: ( arg -- arg attr dptr dst saved src )
: rmw ( arg -- arg attr dptr dst saved src )
   ip-u8 ip  2 pick 2 pick get-dst  3 pick get-src ;

: op-move ( arg -- )
   ip-u8 ip                          ( arg attr dptr )
   over 3 rshift 7 and if 2 pick 2 pick get-dst nip
   else 2 pick 2 pick skip-dst poison then   ( arg attr dptr saved )
   2 pick get-src swap put-dst ;
\ ( dst saved src ) rot -> ( saved src dst )
: op-and ( arg -- ) rmw rot and swap put-dst ;
: op-or  ( arg -- ) rmw rot or swap put-dst ;
: op-xor ( arg -- ) rmw rot xor swap put-dst ;
: op-add ( arg -- ) rmw rot + u32 swap put-dst ;
: op-sub ( arg -- ) rmw rot swap - u32 swap put-dst ;

: op-mask ( arg -- )
   ip-u8 ip  2 pick 2 pick get-dst     ( arg attr dptr dst saved )
   3 pick 3 rshift 7 and src-direct    ( ... mask )
   4 pick get-src                      ( arg attr dptr dst saved mask src )
   >r rot and r> or swap put-dst ;

\ CLEAR, SHIFT_LEFT, SHIFT_RIGHT: the destination alignment is the default one
: fix-attr ( attr -- attr' ) 38 and dup 3 rshift cells def-dst + @ 6 lshift or ;
: op-clear ( arg -- )
   ip-u8 fix-attr ip  2 pick 2 pick get-dst nip  0 swap put-dst ;
: op-shift-left ( arg -- )
   ip-u8 fix-attr ip  2 pick 2 pick get-dst  4 src-direct ff and
   rot swap <<32 swap put-dst ;
: op-shift-right ( arg -- )
   ip-u8 fix-attr ip  2 pick 2 pick get-dst  4 src-direct ff and
   rot swap >>32 swap put-dst ;

\ SHL, SHR: shift the whole saved value, then take the field
: shx ( arg -- arg attr dptr saved shift align )
   ip-u8 ip  2 pick 2 pick get-dst nip  2 pick get-src ff and  3 pick d>s ;
: op-shl ( arg -- )
   shx >r over swap <<32  r@ amask and  r> ashift rshift  swap put-dst ;
: op-shr ( arg -- )
   shx >r over swap >>32  r@ amask and  r> ashift rshift  swap put-dst ;

: op-mul ( arg -- )
   ip-u8 tuck get-dst drop swap get-src * u32 to divmul0 ;
: op-div ( arg -- )
   ip-u8 tuck get-dst drop swap get-src
   ?dup if >r 0 r> um/mod to divmul0 to divmul1
   else drop 0 to divmul0 0 to divmul1 then ;
: op-compare ( arg -- )
   ip-u8 tuck get-dst drop swap get-src
   2dup = to cs-equal  u> to cs-above ;
: op-test ( arg -- )
   ip-u8 tuck get-dst drop swap get-src and 0= to cs-equal ;

: op-setport ( arg -- )
   dup 0 = if drop ip-u16 ?dup if 80 or else 0 then to io-mode exit then
   1 = if 1 else 2 then to io-mode  ip 1+ to ip ;
: op-setregblock ( arg -- ) drop ip-u16 to reg-block ;
: op-setfbbase ( arg -- ) drop ip-u8 get-src to fb-base ;
: op-setdatablock ( arg -- )
   drop ip-u8
   dup 0 = if drop 0 to data-block exit then
   dup ff = if drop e-start @ to data-block exit then
   2 * 4 + data-table + cu16 to data-block ;

: op-jump ( cond -- )
   ip-u16 swap                        ( target cond )
   dup 0 = if drop cs-above else
   dup 1 = if drop cs-above cs-equal or else
   dup 2 = if drop true else
   dup 3 = if drop cs-above cs-equal or 0= else
   dup 4 = if drop cs-above 0= else
   dup 5 = if drop cs-equal else
              drop cs-equal 0=
   then then then then then then
   0= if drop exit then
   e-start @ +                        ( dest )
   dup e-lastj @ = if
      atom-ms@ e-lastms @ - u32 d# 5000 u> if
         s" ATOM: atombios stuck in loop for more than 5secs aborting" atom-warn
         true e-abort !
      then
   else dup e-lastj !  atom-ms@ e-lastms ! then
   to ip ;

: op-switch ( arg -- )
   drop ip-u8 dup get-src              ( attr src )
   begin ip cu16 5a5a <> while
      ip cu8 63 = if
         ip 1+ to ip
         over 38 and 5 or get-src     ( attr src val )
         over = if 2drop e-start @ ip cu16 + to ip exit then
         ip 2 + to ip
      else 2drop s" ATOM: bad case" atom-warn exit then
   repeat 2drop ip 2 + to ip ;

: op-delay ( unit -- ) ip-u8 swap 1 = if d# 1000 * then atom-us ;

: op-calltable ( arg -- )
   drop ip-u8 dup 2 * 4 + cmd-table + cu16 if
      >r  e-ps @ e-psshift @ 4 * +  e-pssize @ e-psshift @ -  r>
      ip >r exec-table r> to ip
      if true e-abort ! then
   else drop then ;

: op-postcard ( arg -- ) drop ip 1+ to ip ;
: op-nop ( arg -- ) drop ;
: op-beep ( arg -- ) drop s" ATOM BIOS beeped!" atom-warn ;
: op-unimpl ( arg -- ) drop s" ATOM: unimplemented opcode" atom-warn ;

\ ---- the opcode table (opcode_table[] in atom.c) ----
7b constant #ops
#ops 2 * cells buffer: optab
: op! ( xt arg op -- ) 2 * cells optab + tuck cell+ ! ! ;
\ six forms of an opcode, for REG PS WS FB PLL MC
: op6 ( xt first -- )
   6 0 do 2dup i dup 3 > if 2 + then swap i + op! loop 2drop ;
: init-optab ( -- )
   #ops 0 do 0 0 i op! loop
   ['] op-move 1 op6        ['] op-and 7 op6        ['] op-or d# 13 op6
   ['] op-shift-left d# 19 op6   ['] op-shift-right d# 25 op6
   ['] op-mul d# 31 op6     ['] op-div d# 37 op6    ['] op-add d# 43 op6
   ['] op-sub d# 49 op6
   ['] op-setport 0 d# 55 op!   ['] op-setport 1 d# 56 op!   ['] op-setport 2 d# 57 op!
   ['] op-setregblock 0 d# 58 op!   ['] op-setfbbase 0 d# 59 op!
   ['] op-compare d# 60 op6     ['] op-switch 0 d# 66 op!
   ['] op-jump 2 d# 67 op!   ['] op-jump 5 d# 68 op!   ['] op-jump 3 d# 69 op!
   ['] op-jump 0 d# 70 op!   ['] op-jump 4 d# 71 op!   ['] op-jump 1 d# 72 op!
   ['] op-jump 6 d# 73 op!
   ['] op-test d# 74 op6
   ['] op-delay 1 d# 80 op!   ['] op-delay 0 d# 81 op!
   ['] op-calltable 0 d# 82 op!   ['] op-unimpl 0 d# 83 op!
   ['] op-clear d# 84 op6
   ['] op-nop 0 d# 90 op!   ['] op-nop 0 d# 91 op!
   ['] op-mask d# 92 op6
   ['] op-postcard 0 d# 98 op!   ['] op-beep 0 d# 99 op!
   ['] op-unimpl 0 d# 100 op!   ['] op-unimpl 0 d# 101 op!
   ['] op-setdatablock 0 d# 102 op!
   ['] op-xor d# 103 op6    ['] op-shl d# 109 op6    ['] op-shr d# 115 op6
   ['] op-unimpl 0 d# 121 op! ;

d# 91 constant op-eot

\ ---- running a table (atom_execute_table_locked) ----
: (exec-table) ( params pssize index -- error? )
   2 * 4 + cmd-table + cu16 ?dup 0= if 2drop true exit then   ( params pssize base )
   \ push a frame
   fp if fp /frame + e-wssize @ cells + else fstack then   ( params pssize base nfp )
   dup /frame erase
   fp over 8 cells + !  to fp
   dup e-start !
   dup 4 + cu8 dup e-wssize !  0 ws-adr swap cells erase
   dup 5 + cu8 7f and 4 / e-psshift !
   6 + to ip
   e-pssize !  e-ps !
   0 e-lastj !  false e-abort !
   false                                              ( error? )
   begin
      ip-u8                                           ( err op )
      e-abort @ if drop drop
         s" ATOM: atombios stuck executing a table" atom-warn true true
      else
         dup 0<> over #ops 1- u< and if
            dup 2 * cells optab + dup @ ?dup if
               swap cell+ @ swap execute  op-eot =
            else 2drop true then
         else drop true then
      then
   until
   e-prev @ to fp ;
' (exec-table) is exec-table

\ atom_execute_table: reset the per-call globals, then run
: atom-exec ( params pssize index -- error? )
   0 to data-block  0 to reg-block  0 to fb-base  0 to io-mode
   0 to divmul0  0 to divmul1
   0 to fp
   exec-table ;

\ ---- setup (atom_parse, atom_index_iio, atom_allocate_fb_scratch) ----
create iio-len 1 , 2 , 3 , 3 , 3 , 3 , 4 , 4 , 4 , 3 ,
: index-iio ( base -- )
   begin dup cu8 1 = while
      dup 2 + over 1+ cu8 cells iio + !
      2 +
      begin dup cu8 9 <> while dup cu8 cells iio-len + @ + repeat
      3 +
   repeat drop ;

: data-start ( index -- off|0 ) 2 * 4 + data-table + cu16 ;

: atom-init ( -- ok? )
   0 cu16 aa55 <> if false exit then
   48 cu16 dup 4 + cu8 [char] A =  over 5 + cu8 [char] T = and
   over 6 + cu8 [char] O = and  over 7 + cu8 [char] M = and 0= if drop false exit then
   dup 1e + cu16 to cmd-table  20 + cu16 to data-table
   100 cells alloc-mem to iio  iio 100 cells erase
   data-table 32 + cu16 4 + index-iio
   \ the firmware's VRAM scratch area, 20 KB if the table names none
   DATA-VRAM_UsageByFirmware data-start ?dup if 8 + cu16 d# 1024 * else 0 then
   ?dup 0= if d# 20 d# 1024 * then to scratch-bytes
   scratch-bytes 4 / 1+ cells dup alloc-mem to scratch  scratch swap erase
   4000 cells alloc-mem to fstack
   init-optab
   true ;

\ a 16-dword parameter block for callers
40 constant /ps
/ps buffer: ps-buf
: ps-clear ( -- ) ps-buf /ps erase ;
: ps-c! ( b off -- ) ps-buf + c! ;
: ps-w! ( w off -- ) 2dup ps-c!  swap 8 rshift swap 1+ ps-c! ;
: ps-l! ( l off -- ) ps-buf + le-l! ;
: ps-c@ ( off -- b ) ps-buf + c@ ;
: ps-l@ ( off -- l ) ps-buf + le-l@ ;
: run-table ( index -- error? ) ps-buf d# 16 rot atom-exec ;

\ atom_asic_init: the cold POST
: atom-asic-init ( -- error? )
   ps-clear
   0c data-table + cu16                  ( firmware info )
   dup 8 + cu32 0 ps-l!  0c + cu32 4 ps-l!
   0 ps-l@ 0= 4 ps-l@ 0= or if true exit then
   4 cmd-table + cu16 0= if true exit then
   0 run-table ;

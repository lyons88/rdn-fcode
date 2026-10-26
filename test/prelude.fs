\ Host prelude for running src/*.fs under gforth (64-bit) against the same
\ fake register file as test/atomref.c, printing the same trace format.
\
\   gforth test/prelude.fs src/consts.fs src/atom.fs test/asic.fs -e bye

\ ---- FCode / Open Firmware words gforth lacks ----
: d# ( "n" -- n ) base @ >r decimal parse-name s>number? 0= abort" d#?" drop r> base !
   state @ if postpone literal then ; immediate
: h# ( "n" -- n ) base @ >r hex parse-name s>number? 0= abort" h#?" drop r> base !
   state @ if postpone literal then ; immediate
[undefined] buffer: [if] : buffer: ( n "name" -- ) create allot ; [then]
: alloc-mem ( n -- a ) allocate throw ;
: free-mem ( a n -- ) drop free throw ;
: u32 ( n -- n' ) $ffffffff and ;
: comp ( a1 a2 n -- n ) tuck compare ;

\ ---- trace output: lowercase hex, no padding ----
: .h ( u -- ) base @ >r hex 0 <# #s #> r> base !
   bounds ?do i c@ dup [char] A [char] F 1+ within if $20 or then emit loop ;
: .sp space ;

\ ---- the register model (model_read / model_write in atomref.c) ----
$100000 constant #regs
#regs cells allocate throw constant regs
regs #regs cells erase
-1 value last-idx  0 value repeat#
\ status bits the hardware sets on its own (as ready_bits() in atomref.c)
: ready-bits ( idx -- bits )
   dup $a88 = if drop 1 17 lshift exit then
   $a74 = if 1 8 lshift else 0 then ;
: model@ ( idx -- v )
   #regs 1- and
   dup last-idx = if repeat# 1+ to repeat# else 0 to repeat# then
   dup to last-idx
   dup cells regs + @ swap ready-bits or
   repeat# $40 >= if 0 to repeat# invert u32 then ;
: model! ( v idx -- ) #regs 1- and cells regs + !  -1 to last-idx ;

\ Tests can hang devices on the register file (test/i2csim.fs): after-write
\ sees every write; quiet stops the trace for long bit-banged transfers.
defer after-write ( idx -- )  ' drop is after-write
false value quiet
\ hide-ddc? leaves the DDC GPIO registers (6430-64b0) and the I2C bit delays
\ out of the trace
false value hide-ddc?
: hidden? ( idx -- f )
   quiet if drop true exit then
   hide-ddc? if 4 * $6430 $64b4 within else drop false then ;
: reg@ ( idx -- v ) dup model@ over hidden? if nip exit then ." R " swap .h .sp dup .h cr ;
: reg! ( v idx -- ) dup hidden? 0= if ." W " dup .h .sp over .h cr then
   tuck model! after-write ;
: model-peek ( idx -- v ) #regs 1- and cells regs + @ ;
: model-poke ( v idx -- ) #regs 1- and cells regs + ! ;
: ioreg@ reg@ ;
: ioreg! reg! ;
: pll@ ( idx -- 0 ) ." P " .h cr 0 ;
: pll! ( v idx -- ) ." P " .h cr drop ;
: mc@ ( idx -- 0 ) ." M " .h cr 0 ;
: mc! ( v idx -- ) ." M " .h cr drop ;
: atom-us ( n -- )
   quiet hide-ddc? 2 pick $a = and or if drop exit then ." D " .h cr ;
0 value fake-ms
: atom-ms@ ( -- ms ) fake-ms dup 1+ to fake-ms ;
: atom-warn ( a n -- ) ." # " type cr ;

\ ---- the VBIOS image ----
$20000 constant /rom
/rom allocate throw constant rom-buf
: rom ( -- a ) rom-buf ;
: load-rom ( a n -- )
   rom-buf /rom erase
   r/o bin open-file throw >r
   rom-buf /rom r@ read-file throw drop
   r> close-file throw ;

\ Card bring-up for the Juniper (Radeon HD 5750/5770/6750/6770, DCE4):
\ register access, POST detection and ASIC_Init.
\ Follows RadeonNI's hw/rdn_post.c and Linux's radeon_device.c
\ (radeon_card_posted) and radeon_atombios.c
\ (radeon_atom_initialize_bios_scratch_regs).
\
\ Needs src/atom.fs and src/consts.fs, and from the platform:
\   reg@ reg! ( dword index )  atom-us  atom-ms@  atom-warn

hex

\ ---- unsigned arithmetic that behaves the same with 32- and 64-bit cells ----
: u/    ( u1 u2 -- q ) >r 0 r> um/mod nip ;
: umod  ( u1 u2 -- r ) >r 0 r> um/mod drop ;
: div-up      ( n d -- q ) tuck + 1- swap u/ ;
: div-closest ( n d -- q ) tuck 2/ + swap u/ ;
: clamp ( v lo hi -- v' ) rot min max ;

\ ---- registers by byte offset (all display registers are below 128 KB) ----
: rreg ( off -- v ) 2 rshift reg@ ;
: wreg ( v off -- ) 2 rshift reg! ;
: reg-clear ( mask off -- ) dup >r rreg swap invert and u32 r> wreg ;
: reg-set   ( mask off -- ) dup >r rreg or r> wreg ;

: rdn-log ( a n -- ) atom-warn ;

\ Building a log line with numbers in it: log( s" x " +s 12 +hex )log
d# 120 constant /log-buf
/log-buf buffer: log-buf
0 value log-len
: log( ( -- ) 0 to log-len ;
: +c ( c -- ) log-len /log-buf < if log-buf log-len + c! log-len 1+ to log-len else drop then ;
: +s ( a n -- ) bounds ?do i c@ +c loop ;
: +hex ( u -- ) u32 dup f u> if dup 4 rshift recurse then f and dup 9 > if 57 else 30 then + +c ;
: +dec ( u -- ) u32 dup 9 u> if dup d# 10 u/ recurse then d# 10 umod 30 + +c ;
: +sdec ( n -- ) dup 0< if [char] - +c negate then +dec ;
: )log ( -- ) log-buf log-len rdn-log ;

\ ---- POST ----
create crtc-offsets
   0 , EVERGREEN_CRTC1_REGISTER_OFFSET , EVERGREEN_CRTC2_REGISTER_OFFSET ,
   EVERGREEN_CRTC3_REGISTER_OFFSET , EVERGREEN_CRTC4_REGISTER_OFFSET ,
   EVERGREEN_CRTC5_REGISTER_OFFSET ,
: crtc-offset ( n -- off ) cells crtc-offsets + @ ;

R600_BIOS_0_SCRATCH 2 4 * + constant R600_BIOS_2_SCRATCH
R600_BIOS_0_SCRATCH 6 4 * + constant R600_BIOS_6_SCRATCH

false value crtc-on?          \ CRTC 0 is scanning out

\ radeon_card_posted(): a CRTC running, or the memory size set
: card-posted? ( -- f )
   0  6 0 do
      i crtc-offset EVERGREEN_CRTC_CONTROL + rreg
      i 0= if dup EVERGREEN_CRTC_MASTER_EN and 0<> to crtc-on? then
      or
   loop
   EVERGREEN_CRTC_MASTER_EN and if true exit then
   CONFIG_MEMSIZE rreg 0<> ;

\ radeon_atom_initialize_bios_scratch_regs()
: init-bios-scratch ( -- )
   R600_BIOS_2_SCRATCH rreg
   ATOM_S2_VRI_BRIGHT_ENABLE ATOM_S2_DEVICE_DPMS_STATE or invert and u32
   R600_BIOS_6_SCRATCH rreg ATOM_S6_ACC_BLOCK_DISPLAY_SWITCH or
   swap R600_BIOS_2_SCRATCH wreg  R600_BIOS_6_SCRATCH wreg ;

\ Linux also checks the 3D engine for hangs and soft-resets it here
\ (evergreen_asic_reset). A card coming out of power-on has never run it,
\ so that is left out.
: card-post ( -- error? )
   card-posted? if s" card is posted on entry" else s" card is not posted on entry" then rdn-log
   init-bios-scratch
   card-posted? if false exit then
   s" posting the card: ASIC_Init" rdn-log
   atom-asic-init dup if s" ASIC_Init failed" rdn-log then ;

decimal

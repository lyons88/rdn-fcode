\ DDC: a bit-banged I2C master on the GPIO pins the VBIOS's GPIO_I2C_Info
\ table names, and EDID reading and parsing.
\ Follows RadeonNI's hw/rdn_i2c.c and hw/rdn_mode.c, which follow Linux's
\ radeon_i2c.c (pre/post transfer pin set-up) and the I2C bit protocol.
\
\ Error codes: 0 ok, -1 clock held low (timeout), -2 no acknowledge,
\ -3 no such bus, -4 bad EDID.

hex

\ ---- the bus: one GPIO_I2C_Info entry in the VBIOS ----
0 value gi-base                    \ ROM offset of the selected entry

\ ( -- off count ) the entries, after the 4-byte table header
: gpio-i2c ( -- off count )
   DATA-GPIO_I2C_Info data-start ?dup 0= if 0 0 exit then
   dup cu16 4 - /gpio-i2c u/  swap 4 + swap ;

\ Select the bus with this AtomBIOS line id (ATOM_I2C_RECORD's ucAccess).
: i2c-bus ( id -- found? )
   gpio-i2c 0 ?do                         ( id off )
      2dup gi-id + cu8 = if to gi-base drop true unloop exit then
      /gpio-i2c +
   loop 2drop false ;

: gi-reg ( field -- off ) gi-base + cu16 4 * ;
: gi-bit ( field -- mask ) gi-base + cu8 1 swap lshift ;
\ ( shift-field reg-field -- mask off )
: pin ( sf rf -- mask off ) gi-reg swap gi-bit swap ;

: hw-capable? ( -- f ) gi-id gi-base + cu8 80 and 0<> ;

\ ---- pin handling (radeon_i2c.c pre_xfer / post_xfer) ----
: i2c-pre ( -- )
   \ switch the pads to DDC mode
   hw-capable? if 10000 gi-clk-mask-reg gi-reg reg-clear then
   \ clear the output values, then make both pins inputs
   gi-clk-a-shift gi-clk-a-reg pin reg-clear
   gi-data-a-shift gi-data-a-reg pin reg-clear
   gi-clk-en-shift gi-clk-en-reg pin reg-clear
   gi-data-en-shift gi-data-en-reg pin reg-clear
   \ take the pins for software use
   gi-clk-mask-shift gi-clk-mask-reg pin reg-set   gi-clk-mask-reg gi-reg rreg drop
   gi-data-mask-shift gi-data-mask-reg pin reg-set gi-data-mask-reg gi-reg rreg drop ;

: i2c-post ( -- )
   gi-clk-mask-shift gi-clk-mask-reg pin reg-clear   gi-clk-mask-reg gi-reg rreg drop
   gi-data-mask-shift gi-data-mask-reg pin reg-clear gi-data-mask-reg gi-reg rreg drop ;

: pin@ ( sf rf -- f ) pin rreg and 0<> ;
: scl@ ( -- f ) gi-clk-y-shift gi-clk-y-reg pin@ ;
: sda@ ( -- f ) gi-data-y-shift gi-data-y-reg pin@ ;

\ Open drain: high releases the line, low drives it (sets the enable bit).
: line! ( high? sf rf -- )
   pin dup >r rreg over invert and u32   ( high? mask v )
   rot if nip else or then r> wreg ;
: scl! ( high? -- ) gi-clk-en-shift gi-clk-en-reg line! ;
: sda! ( high? -- ) gi-data-en-shift gi-data-en-reg line! ;

\ ---- the bit level ----
d# 10 constant i2c-half-us          \ 50 kHz, well inside DDC's 100 kHz
d# 50 constant i2c-stretch-ms       \ how long a slave may hold the clock

: half ( -- ) i2c-half-us atom-us ;

\ release the clock and wait for it to rise: a slave may stretch it
: scl-high ( -- err )
   true scl!
   scl@ 0= if
      atom-ms@
      begin scl@ 0= while
         atom-ms@ over - u32 i2c-stretch-ms u> if drop -1 exit then
         i2c-half-us atom-us
      repeat drop
   then half 0 ;
: scl-low ( -- ) false scl! half ;

\ START (also a repeated START): data falls while the clock is high
: i2c-start ( -- err )
   true sda! half
   scl-high ?dup if exit then
   false sda! half scl-low 0 ;
\ STOP: data rises while the clock is high
: i2c-stop ( -- ) false sda! half scl-high drop true sda! half ;

: wbit ( bit -- err ) sda! half scl-high scl-low ;
\ ( -- bit | err ) err is negative
: rbit ( -- n )
   true sda! half
   scl-high ?dup if exit then
   sda@ 1 and scl-low ;

\ ( byte -- err ) -2 when the slave does not acknowledge
: wbyte ( b -- err )
   8 0 do
      dup 7 i - rshift 1 and wbit ?dup if nip unloop exit then
   loop drop
   rbit dup 0< if exit then
   if -2 else 0 then ;

\ ( ack? -- byte | err )
: rbyte ( ack? -- n )
   0 8 0 do
      rbit dup 0< if nip nip unloop exit then
      swap 2* or
   loop
   swap 0= wbit dup 0< if nip exit then drop ;

\ ---- a register read: write the offset, then read len bytes ----
0 value i2c-addr  0 value i2c-off  0 value i2c-buf  0 value i2c-len

: (i2c-read) ( -- err )   \ after START, before STOP
   i2c-addr 2* wbyte ?dup if exit then
   i2c-off wbyte ?dup if exit then
   i2c-start ?dup if exit then
   i2c-addr 2* 1 or wbyte ?dup if exit then
   i2c-len 0 ?do
      i i2c-len 1- <> rbyte dup 0< if unloop exit then
      i2c-buf i + c!
   loop 0 ;

\ ( addr off buf len -- err ) on the selected bus
: i2c-read ( addr off buf len -- err )
   to i2c-len to i2c-buf to i2c-off to i2c-addr
   gi-base 0= if -3 exit then
   gi-clk-mask-reg gi-reg 0= if -3 exit then
   i2c-pre
   i2c-start dup 0= if drop (i2c-read) i2c-stop then
   i2c-post ;

\ ---- EDID ----
d# 128 constant /edid-block
4 constant #edid-blocks
/edid-block #edid-blocks * buffer: edid
50 constant ddc-addr

create edid-header 0 c, ff c, ff c, ff c, ff c, ff c, ff c, 0 c,

: edid-block-ok? ( a base? -- f )
   if dup edid-header 8 comp if drop false exit then then
   0 swap /edid-block bounds do i c@ + loop ff and 0= ;

\ ( -- len | err ) the EDID of the display on the selected bus
: edid-read ( -- n )
   edid /edid-block #edid-blocks * erase
   ddc-addr 0 edid /edid-block i2c-read ?dup if exit then
   edid true edid-block-ok? 0= if -4 exit then
   \ byte 126: the number of extension blocks
   edid d# 126 + c@ 1+ #edid-blocks min
   1 swap 1 ?do
      \ the offset is one byte: blocks past the second need E-DDC segments
      ddc-addr i /edid-block * ff and edid i /edid-block * + /edid-block i2c-read
      edid i /edid-block * + false edid-block-ok? 0= or if
         s" EDID extension block unreadable or invalid" rdn-log leave
      then
      1+
   loop
   /edid-block * ;

\ ---- display modes ----
\ The mode to set; clock in kHz.
0 value m-clock
0 value m-hd  0 value m-hss  0 value m-hse  0 value m-ht
0 value m-vd  0 value m-vss  0 value m-vse  0 value m-vt
0 value m-flags
1 constant mode-nhsync
2 constant mode-nvsync

d# 330000 constant max-mode-clock     \ dual-link DVI

\ 1024x768 at 60 Hz (VESA DMT), for when there is no EDID
: default-mode ( -- )
   d# 65000 to m-clock
   d# 1024 to m-hd  d# 1048 to m-hss  d# 1184 to m-hse  d# 1344 to m-ht
   d# 768 to m-vd   d# 771 to m-vss   d# 777 to m-vse   d# 806 to m-vt
   mode-nhsync mode-nvsync or to m-flags ;

0 value dtd
: d@ ( off -- b ) dtd + c@ ;
\ A detailed timing descriptor (18 bytes) into m-*; false if it is not a
\ progressive timing within range.
: dtd>mode ( a -- ok? )
   to dtd
   0 d@ 1 d@ 8 lshift or d# 10 *                     ( clock )
   dup 0= if drop false exit then
   d# 17 d@ 80 and  over max-mode-clock > or if drop false exit then
   to m-clock
   2 d@ 4 d@ 4 rshift 8 lshift or to m-hd
   5 d@ 7 d@ 4 rshift 8 lshift or to m-vd
   3 d@ 4 d@ f and 8 lshift or m-hd + to m-ht
   6 d@ 7 d@ f and 8 lshift or m-vd + to m-vt
   8 d@ d# 11 d@ 6 rshift 3 and 8 lshift or m-hd + to m-hss
   9 d@ d# 11 d@ 4 rshift 3 and 8 lshift or m-hss + to m-hse
   d# 10 d@ 4 rshift d# 11 d@ 2 rshift 3 and 4 lshift or m-vd + to m-vss
   d# 10 d@ f and d# 11 d@ 3 and 4 lshift or m-vss + to m-vse
   0
   \ digital separate sync: bit 1 horizontal, bit 2 vertical polarity
   d# 17 d@ 18 and 18 = if
      d# 17 d@ 2 and 0= if mode-nhsync or then
      d# 17 d@ 4 and 0= if mode-nvsync or then
   then to m-flags
   true ;

\ the first detailed timing is the preferred mode
d# 54 constant edid-dtd
d# 18 constant /dtd
: edid-preferred ( -- ok? ) edid edid-dtd + dtd>mode ;

decimal

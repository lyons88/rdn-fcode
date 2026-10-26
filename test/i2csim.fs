\ An I2C EEPROM (a display's EDID chip) on the GPIO lines of the DDC bus
\ src/i2c.fs has selected, hung on the fake register file of test/prelude.fs.
\ The master drives a line low by setting its EN bit and reads it back from
\ the Y register; the slave can also pull SDA low.

hex
50 value sim-addr
create sim-mem 100 allot  sim-mem 100 erase

0 value s-en-clk  0 value s-en-clk-m  0 value s-en-data  0 value s-en-data-m
0 value s-y-clk   0 value s-y-clk-m   0 value s-y-data   0 value s-y-data-m

true value s-scl  true value s-sda     \ the line levels last seen
false value s-drive                    \ the slave holds SDA low
0 value s-state                        \ 0 idle 1 receive 2 ack 3 send 4 master ack
0 value s-phase                        \ receiving: 0 address 1 offset 2 data
0 value s-bits  0 value s-byte  0 value s-ptr  false value s-mack
0 value starts  0 value stops

: peek ( off -- v ) 2 rshift model-peek ;
: poke ( v off -- ) 2 rshift model-poke ;
: levels ( -- scl sda )
   s-en-clk peek s-en-clk-m and 0=
   s-en-data peek s-en-data-m and 0=  s-drive 0= and ;

: y! ( level off mask -- )
   swap >r r@ peek over invert and  rot if or else nip then r> poke ;
: publish ( -- )
   s-scl s-y-clk s-y-clk-m y!  s-sda s-y-data s-y-data-m y! ;

: load-byte ( -- ) sim-mem s-ptr ff and + c@ to s-byte  0 to s-bits
   s-byte 80 and 0= to s-drive  3 to s-state ;

: on-start ( -- ) 1 to s-state 0 to s-phase 0 to s-bits 0 to s-byte false to s-drive
   starts 1+ to starts ;
: on-stop  ( -- ) 0 to s-state false to s-drive  stops 1+ to stops ;

: on-rise ( sda -- )
   s-state 1 = if 1 and s-byte 2* or ff and to s-byte s-bits 1+ to s-bits exit then
   s-state 4 = if 0= to s-mack exit then
   drop ;

: on-fall ( -- )
   s-state case
   1 of s-bits 8 = if
           s-phase 0= if
              s-byte 1 rshift sim-addr = if true to s-drive 2 to s-state
              else 0 to s-state then
           else
              s-phase 1 = if s-byte to s-ptr then
              true to s-drive 2 to s-state
           then
        then endof
   2 of false to s-drive
        s-phase 0= s-byte 1 and and if load-byte else
        s-phase 2 min 1+ to s-phase  1 to s-state 0 to s-bits 0 to s-byte then
        endof
   3 of s-bits 1+ to s-bits
        s-bits 8 = if false to s-drive 4 to s-state
        else s-byte 7 s-bits - rshift 1 and 0= to s-drive then endof
   4 of s-mack if s-ptr 1+ to s-ptr load-byte else 0 to s-state false to s-drive then endof
   endcase ;

0 value n-scl  0 value n-sda
: step ( -- )
   levels to n-sda to n-scl
   s-scl n-scl and if
      s-sda n-sda <> if n-sda if on-stop else on-start then then
   else
      s-scl 0= n-scl and if n-sda on-rise then
      s-scl n-scl 0= and if on-fall then
   then
   n-scl to s-scl  levels nip to s-sda
   publish ;

: sim-after ( idx -- )
   4 * dup s-en-clk = swap s-en-data = or if step then ;

\ Attach to the bus src/i2c.fs selected with i2c-bus.
: sim-attach ( -- )
   gi-clk-en-shift gi-clk-en-reg pin to s-en-clk to s-en-clk-m
   gi-data-en-shift gi-data-en-reg pin to s-en-data to s-en-data-m
   gi-clk-y-shift gi-clk-y-reg pin to s-y-clk to s-y-clk-m
   gi-data-y-shift gi-data-y-reg pin to s-y-data to s-y-data-m
   0 to s-state false to s-drive  true to s-scl true to s-sda  publish
   ['] sim-after is after-write ;
decimal

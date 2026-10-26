\ display-start on the fake register file: a posted card (CONFIG_MEMSIZE
\ set, so ASIC_Init is skipped), a display with the EDID of test/edid.fs on
\ the first DVI port. Prints the log and every command table call with its
\ parameter block; test/run.sh compares that with test/golden/display.txt.

hex
: run ( -- )
   s" reference/vbios/visiontek6770.rom" load-rom
   atom-init 0= abort" atom-init"
   400 CONFIG_MEMSIZE 2 rshift model-poke     \ 1 GB: the card is posted
   read-outputs
   \ the display hangs on the first DVI output's DDC line
   2 select-output o-i2c i2c-bus drop sim-attach
   0 make-edid test-edid sim-mem 100 move
   true to debug-tables?
   true to hide-ddc?
   display-start
   false to hide-ddc?
   ." display-start: " . cr
   ." mode " m-hd . m-vd . m-clock . ." pitch " fb-pitch . cr ;
decimal
run bye

\ Reads an EDID through the bit-banged DDC code and the simulated EEPROM of
\ test/i2csim.fs, and parses its preferred mode.
\   (see test/run.sh; needs test/i2csim.fs and test/edid-data.fs)

hex
0 value fails
: check ( got want a n -- )
   2swap 2dup = if 2drop 2drop exit then
   ." FAIL " 2swap type ." : got " swap . ." want " . cr  fails 1+ to fails ;

: run ( -- )
   s" reference/vbios/visiontek6770.rom" load-rom
   atom-init 0= abort" atom-init"
   read-outputs
   #outs 0 > 1 and 1 s" outputs found" check
   0 select-output
   o-i2c i2c-bus 1 and 1 s" DDC bus found" check
   sim-attach

   \ one block plus an extension
   1 make-edid  test-edid sim-mem 100 move
   0 to starts 0 to stops
   true to quiet  edid-read  false to quiet
   d# 256 s" edid-read length" check
   edid test-edid d# 256 comp 0 s" EDID bytes" check
   starts 4 s" START conditions (2 per read)" check
   stops 2 s" STOP conditions" check
   edid-preferred 1 and 1 s" preferred mode parsed" check
   m-clock d# 148500 s" clock" check
   m-hd d# 1920 s" hdisplay" check  m-hss d# 2008 s" hsync start" check
   m-hse d# 2052 s" hsync end" check  m-ht d# 2200 s" htotal" check
   m-vd d# 1080 s" vdisplay" check  m-vss d# 1084 s" vsync start" check
   m-vse d# 1089 s" vsync end" check  m-vt d# 1125 s" vtotal" check
   m-flags 0 s" sync flags" check

   \ a bad checksum
   0 make-edid  test-edid sim-mem 100 move  sim-mem 7f + dup c@ 1+ swap c!
   true to quiet edid-read false to quiet  -4 s" bad checksum" check

   \ nobody at 0x50
   0 make-edid  test-edid sim-mem 100 move  51 to sim-addr
   true to quiet edid-read false to quiet  -2 s" no acknowledge" check
   50 to sim-addr

   fails 0= if ." edid: all checks passed" cr then ;
decimal
run bye

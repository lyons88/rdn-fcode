\ src/platform.fs's rom-copy on a ROM image built by tools/mkrom.py: it must
\ find the x86 image and copy exactly its 64 KB.
\   (see test/rom.sh)
hex
: u32 ( n -- n' ) ffffffff and ;
create built 20000 allot
: run ( a n -- )
   r/o bin open-file throw >r built 20000 r@ read-file throw drop r> close-file throw
   built fake-rom 20000 move
   rom-copy if
      /rom 10000 = rom built 10000 comp 0= and
      if ." romimg: rom-copy finds the x86 image" else ." romimg: FAIL wrong copy" then
   else ." romimg: FAIL no AtomBIOS image found" then cr ;
decimal

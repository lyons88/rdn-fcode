\ The Forth compute-pll over the same grid as test/pll/pllref.c
decimal
create flagsets
   hex 40 , 440 , 404 , 1400 , 1404 , decimal
: run-grid ( -- )
   2700 to pll-ref-freq  60000 to pll-out-min  120000 to pll-out-max
   5 0 do
      340000 1+ 20000 do
         j cells flagsets + @ to pll-flags
         i 7 umod 2 + to pll-ref-div
         i 5 umod 4 + to pll-post-div
         j 0 .r ."  " i 0 .r ." : "
         i compute-pll >r >r >r 0 .r space r> 0 .r space r> 0 .r space r> 0 .r cr
      137 +loop
   loop ;
run-grid bye

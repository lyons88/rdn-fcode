\ Runs a calls file through the Forth interpreter and prints the same trace
\ as build/atomref.  Usage (see test/compare.sh):
\   gforth test/prelude.fs src/consts.fs src/atom.fs test/asic.fs \
\          -e 's" rom" s" calls" run-calls bye'

: .err ( f -- ) 0<> 1 and .h ;

: next-hex ( a n -- a' n' u true | a n false )
   begin dup while over c@ bl <= while 1 /string repeat then
   dup 0= if false exit then
   2dup bl scan 2swap 2 pick - ( a' n' a n0 )
   base @ >r hex s>number? r> base ! 0= if 2drop false exit then
   drop true ;

: do-call ( a n -- )
   next-hex 0= if 2drop exit then >r
   ps-clear
   d# 16 0 do next-hex 0= if leave then i 4 * ps-l! loop 2drop
   ." CALL " r@ .h cr
   r> run-table ." END " .err
   d# 16 0 do space i 4 * ps-l@ .h loop cr ;

create line-buf 400 chars allot
: run-calls ( rom-a rom-n calls-a calls-n -- )
   2swap load-rom
   atom-init 0= if ." atom-init failed" cr exit then
   r/o open-file throw >r
   begin line-buf 3fe r@ read-line throw while
      line-buf swap
      2dup s" asic" string-prefix? if 2drop
         ." CALL asic" cr atom-asic-init ." END " .err cr
      else 2dup s" call" string-prefix? if 4 /string do-call
      else 2drop then then
   repeat drop r> close-file throw ;

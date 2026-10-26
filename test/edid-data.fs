\ The test display's EDID, for test/edid.fs and test/display.fs.
hex
\ A 1920x1080 display: header, one detailed timing (CEA 1080p60, positive
\ syncs), one extension block that is only a valid checksum.
create test-edid 100 allot  test-edid 100 erase
: e! ( b off -- ) test-edid + c! ;
: make-edid ( extensions -- )
   test-edid 100 erase
   0 0 e!  7 1 do ff i e! loop  0 7 e!
   1 d# 18 e!  3 d# 19 e!                        \ EDID 1.3
   80 d# 20 e!                                    \ a digital input
   \ detailed timing 1 at 54: 148.5 MHz
   02 36 e! 3a 37 e! 80 38 e! 18 39 e! 71 3a e!  \ h 1920 + 280
   38 3b e! 2d 3c e! 40 3d e!                     \ v 1080 + 45
   58 3e e! 2c 3f e! 45 40 e! 00 41 e!            \ sync 88/44, 4/5
   1e 47 e!                                       \ digital separate, +h +v
   dup 7e e!
   if 2 80 e! 3 81 e! then                        \ a CEA block, revision 3
   0 7f 0 do test-edid i + c@ + loop negate ff and 7f e!
   0 ff 80 do test-edid i + c@ + loop negate ff and ff e! ;

decimal

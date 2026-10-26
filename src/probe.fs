\ The card's own node: POST, the mode, and a child node for every output on
\ the board, laid out as Apple's Mac Radeon ROMs do (ATY,PrionaceParent with
\ ATY,Prionace_A and _B under it). The card is ATY,JuniperParent, after the
\ chip's code name; its children are ATY,Phantom_A, _B, ... one per output,
\ in the board's order: a Mac card that never existed, so no ATI NDRV or
\ Mac OS X driver matches these names. The child of the output being driven
\ is the display (src/display-node.fs); the others carry what is known about
\ their connector: its EDID if a display answered, and its wiring.

hex

\ The firmware reads this FCode out of the ROM while it runs: leave the
\ PCI command register as it was when the probe started.
0 value cmd-entry
: restore-cmd ( -- ) cmd-entry 4 cfgw! ;
: give-up ( a n -- ) atom-warn restore-cmd ;

false value rdn-ok?        \ a mode is set: the display child shows it
0 value active#            \ the output being driven
0 value aper               \ the framebuffer, mapped

\ ---- the EDID of every output ----
200 constant /edid-slot
#max-outs /edid-slot * buffer: edids
#max-outs cells buffer: edid-lens
: edid# ( i -- a ) /edid-slot * edids + ;
: edid-len ( i -- a ) cells edid-lens + ;

: read-edids ( -- )
   out
   #outs 0 ?do
      i select-output  0 i edid-len !
      o-dp 0= if                   \ DisplayPort needs AUX, not done here
         o-i2c i2c-bus if
            edid-read dup 0> if
               dup i edid-len !  edid i edid# rot move
            else drop then
         then
      then
   loop
   to out ;

\ ---- what every output's child carries ----
: conn-name ( -- a n )
   o-dp if " DisplayPort" exit then
   o-conn CONNECTOR_OBJECT_ID_HDMI_TYPE_A =
   o-conn CONNECTOR_OBJECT_ID_HDMI_TYPE_B = or if " HDMI" exit then
   o-conn CONNECTOR_OBJECT_ID_SINGLE_LINK_DVI_I =
   o-conn CONNECTOR_OBJECT_ID_DUAL_LINK_DVI_I = or if " DVI-I" exit then
   " DVI-D" ;
: +int ( n a len -- ) rot encode-int 2swap property ;
: +str ( a len name len -- ) 2swap encode-string 2swap property ;

\ display-type as Apple's ROMs give it: LCD for a digital display, CRT for
\ an analog one, NONE when no EDID was read
: display-type ( i -- a n )
   dup edid-len @ 0= if drop " NONE" exit then
   edid# d# 20 + c@ 80 and if " LCD" else " CRT" then ;

\ ---- three heads ----
\ The card shows three displays at once: heads A and B on the first two
\ DVI connectors, C on DisplayPort. HDMI is B's alternate connector (or a
\ head of its own on a board with fewer DVI ports). Every head gets a node,
\ with the head number as its unit address (reg),
\ connected or not; the DisplayPort head is a stub until AUX is done.
3 constant #heads
#heads cells buffer: head-main     \ output index, or -1
#heads cells buffer: head-alt      \ a second connector of the head, or -1
: main ( h -- a ) cells head-main + ;
: alt  ( h -- a ) cells head-alt + ;

: dvi? ( -- f ) o-dp 0= o-conn CONNECTOR_OBJECT_ID_HDMI_TYPE_A <> and
   o-conn CONNECTOR_OBJECT_ID_HDMI_TYPE_B <> and ;
: hdmi? ( -- f ) o-dp 0= dvi? 0= and ;

\ put output i on the first of heads h0..h1 with a free main, else alt slot
0 value pl-out
: place ( i h1 h0 -- )
   rot to pl-out
   2dup swap 1+ swap ?do
      i main @ 0< if pl-out i main ! 2drop unloop exit then
   loop
   swap 1+ swap ?do
      i alt @ 0< if pl-out i alt ! unloop exit then
   loop ;

: assign-heads ( -- )
   #heads 0 do -1 i main ! -1 i alt ! loop
   out
   #outs 0 ?do i select-output dvi?  if i 1 0 place then loop
   #outs 0 ?do i select-output hdmi? if i 1 1 place then loop
   #outs 0 ?do i select-output o-dp  if i 2 2 place then loop
   to out ;

\ the head an output is on (A if it is on none)
: head-of ( i -- h )
   #heads 0 do
      dup i main @ = over i alt @ = or if drop i unloop exit then
   loop drop 0 ;
0 value active-head

\ ATY,Phantom_A for head 0, _B for 1, _C for 2
create child-name-buf 10 allot
: child-name ( h -- a n )
   " ATY,Phantom_" child-name-buf swap move
   [char] A + child-name-buf d# 12 + c!
   child-name-buf d# 13 ;

\ the wiring of output i, as rdn,... or (alt? true) rdn,alt-...
: wire-props ( i alt? -- )
   >r dup select-output
   r@ if " rdn,alt-output" else " rdn,output" then +int
   conn-name r@ if " rdn,alt-connector" else " rdn,connector" then +str
   o-conn r@ if " rdn,alt-connector-id" else " rdn,connector-id" then +int
   o-txsel r@ if " rdn,alt-uniphy" else " rdn,uniphy" then +int
   o-linkb if " B" else " A" then r@ if " rdn,alt-link" else " rdn,link" then +str
   o-dig r@ if " rdn,alt-dig" else " rdn,dig" then +int
   o-i2c r> if " rdn,alt-ddc-line" else " rdn,ddc-line" then +int ;

\ the output whose EDID the head shows: the main one's, else the alternate's
: edid-output ( h -- i | -1 )
   dup main @ dup 0< 0= if dup edid-len @ if nip exit then then drop
   alt @ dup 0< 0= if dup edid-len @ if exit then then drop -1 ;

: head-props ( h -- )
   dup child-name device-name
   dup " reg" +int                    \ unit address: ATY,Phantom_A@0 ...
   dup " rdn,head" +int
   dup edid-output dup 0< if
      drop " NONE" " display-type" +str
   else
      dup display-type " display-type" +str
      dup " rdn,edid-output" +int
      dup edid# swap edid-len @ encode-bytes " EDID" property
   then
   dup main @ dup 0< if drop else false wire-props then
   alt @ dup 0< if drop else true wire-props then ;

: children ( h1 h0 -- )            \ the heads h0 up to h1-1
   rdn-ok? 0= if 2drop exit then
   out -rot
   ?do new-device i head-props finish-device loop
   to out ;
\ Firmware lists a node's children in the order they were made: make the
\ heads below the console head, then it (rom/rdn.fth), then the rest.
: heads-before ( -- ) active-head 0 children ;
: heads-after ( -- ) #heads active-head 1+ children ;

\ ---- methods of the card node ----
external
\ Opening the display child opens every node on its path, this one too.
: open ( -- ok? ) true ;
: close ( -- ) ;
\ the heads' unit addresses (0, 1, 2) in device paths: ATY,Phantom_B@1
: decode-unit ( a n -- head ) $number if 0 then ;
: encode-unit ( head -- a n ) (u.) ;
\ the display child's open and close switch memory decode through this:
\ only the card node can reach the PCI bus's config space
: rdn-decode ( on? -- ) decode ;
headerless

\ ---- probing ----
: rdn-probe ( -- )
   init-cfg
   4 cfgw@ to cmd-entry
   init-delays
   false decode  make-reg  true decode
   18 /regs map to regs
   rom-copy 0= if
      log( s" expansion ROM of " +s /rombar +hex s"  bytes starts " +s rom-sig +hex )log
      " no AtomBIOS image in the expansion ROM" give-up exit
   then
   atom-init 0= if " the VBIOS is not an AtomBIOS" give-up exit then
   8 to fb-bpp  0 to fb-offset
   display-start if " no picture: the card is not a display" give-up exit then
   out outs - /out / to active#
   10 fb-pitch m-vd * map to aper
   read-edids
   assign-heads
   active# head-of to active-head
   \ The display is the child; some firmware (OpenBIOS) gives the card node
   \ device_type "display" from its class code, and would pick it as the
   \ screen first.
   " device_type" delete-property
   " ATY,JuniperParent" device-name
   1 encode-int " #address-cells" property
   0 encode-int " #size-cells" property
   active# select-output
   true to rdn-ok?
   restore-cmd ;

decimal

\ Mode setting for the Juniper (DCE4) through AtomBIOS command tables:
\ CRTC 0 driving one DVI or HDMI connector (TMDS), single or dual link.
\
\ The sequence and the PLL divider search follow RadeonNI's hw/rdn_modeset.c,
\ moved to the table revisions this chip's VBIOS has, which Linux handles in
\ atombios_crtc.c and atombios_encoders.c:
\   DIGxEncoderControl 1.2         DIG_ENCODER_CONTROL_PARAMETERS_V3
\   UNIPHYTransmitterControl 1.3   DIG_TRANSMITTER_CONTROL_PARAMETERS_V3
\   SetPixelClock 1.5              PIXEL_CLOCK_PARAMETERS_V5
\   EnableSpreadSpectrumOnPPLL 1.2 ENABLE_SPREAD_SPECTRUM_ON_PPLL_V2
\   AdjustDisplayPll 1.3, SelectCRTC_Source 1.2: as on DCE5
\
\ Not done here:
\  - DisplayPort (AUX channel and link training): DP connectors are skipped.
\  - HDMI infoframes and audio: an HDMI connector is driven in DVI mode,
\    which every HDMI display accepts.
\  - Display watermarks: CRTC 0 gets the whole line buffer and no priority
\    marks, which is plenty for a boot console.
\  - Spread spectrum: it is turned off on the pixel PLL, as Linux does for
\    TMDS, and the display PLL's is left as ASIC_Init set it.

hex

\ ---- the board's outputs, from the VBIOS object table ----
\ One 8-byte record each:
\   0 connector object id   1 encoder (UNIPHY) object id   2 UNIPHY 0-2
\   3 link B?   4 DIG encoder 0-5   5 DDC line id   6 DisplayPort?
6 constant #max-outs
8 constant /out
#max-outs /out * buffer: outs
0 value #outs
0 value out                            \ the selected record

: select-output ( n -- ) /out * outs + to out ;
: o-conn  ( -- b ) out c@ ;
: o-enc   ( -- b ) out 1+ c@ ;
: o-txsel ( -- b ) out 2 + c@ ;
: o-linkb ( -- b ) out 3 + c@ ;
: o-dig   ( -- b ) out 4 + c@ ;
: o-i2c   ( -- b ) out 5 + c@ ;
: o-dp    ( -- b ) out 6 + c@ ;

: .output ( -- )
   o-dp if s" DisplayPort" else
   o-conn CONNECTOR_OBJECT_ID_HDMI_TYPE_A = o-conn CONNECTOR_OBJECT_ID_HDMI_TYPE_B = or
   if s" HDMI" else s" DVI" then then +s
   s"  on UNIPHY" +s o-txsel +dec o-linkb if s"  link B" else s"  link A" then +s
   s" , DDC line " +s o-i2c +hex ;

0 value ob-base  0 value ob-con  0 value ob-paths
0 value rec  -1 value rec-i2c

: rec-ok? ( -- f )
   rec 1+ cu8 0<>  rec cu8 0<> and  rec cu8 ATOM_MAX_OBJECT_RECORD_NUMBER > 0= and ;
: scan-records ( rec -- )
   to rec
   d# 16 0 do
      rec-ok? 0= if leave then
      rec cu8 ATOM_I2C_RECORD_TYPE = if rec 2 + cu8 to rec-i2c then
      rec 1+ cu8 rec + to rec
   loop ;

\ the DDC line in the connector object's I2C record, or -1
: conn-i2c ( conn -- i2c )
   -1 to rec-i2c
   ob-con cu8 0 ?do
      ob-con 4 + i 8 * +             ( conn obj )
      2dup cu16 = if 4 + cu16 ob-base + scan-records leave then
      drop
   loop drop rec-i2c ;

: uniphy? ( enc-id -- f )
   dup ENCODER_OBJECT_ID_INTERNAL_UNIPHY =
   over ENCODER_OBJECT_ID_INTERNAL_UNIPHY1 = or
   swap ENCODER_OBJECT_ID_INTERNAL_UNIPHY2 = or ;
: digital-conn? ( conn-id -- f )
   case
      CONNECTOR_OBJECT_ID_SINGLE_LINK_DVI_I of true endof
      CONNECTOR_OBJECT_ID_DUAL_LINK_DVI_I of true endof
      CONNECTOR_OBJECT_ID_SINGLE_LINK_DVI_D of true endof
      CONNECTOR_OBJECT_ID_DUAL_LINK_DVI_D of true endof
      CONNECTOR_OBJECT_ID_HDMI_TYPE_A of true endof
      CONNECTOR_OBJECT_ID_HDMI_TYPE_B of true endof
      CONNECTOR_OBJECT_ID_DISPLAYPORT of true endof
      false swap
   endcase ;

0 value p-conn  0 value p-enc  0 value p-i2c
\ One display path: keep it if it is a digital connector on a UNIPHY.
: add-path ( path -- )
   dup 4 + cu16 to p-conn  0 to p-enc
   dup 2 + cu16 8 - 2/ 0 ?do
      dup 8 + i 2* + cu16
      dup OBJECT_TYPE_MASK and OBJECT_TYPE_SHIFT rshift GRAPH_OBJECT_TYPE_ENCODER =
      p-enc 0= and if to p-enc else drop then
   loop drop
   p-conn conn-i2c to p-i2c
   p-enc 0= p-i2c 0< or if exit then
   p-enc OBJECT_ID_MASK and uniphy? 0= if exit then
   p-conn OBJECT_ID_MASK and digital-conn? 0= if exit then
   #outs #max-outs = if exit then
   #outs select-output  out /out erase
   p-conn OBJECT_ID_MASK and out c!
   p-enc OBJECT_ID_MASK and out 1+ c!
   \ which UNIPHY; link B is the encoder object's enum 2
   p-enc OBJECT_ID_MASK and
   dup ENCODER_OBJECT_ID_INTERNAL_UNIPHY = if drop 0 else
   ENCODER_OBJECT_ID_INTERNAL_UNIPHY1 = if 1 else 2 then then out 2 + c!
   p-enc ENUM_ID_MASK and ENUM_ID_SHIFT rshift 2 = 1 and out 3 + c!
   \ radeon_atom_pick_dig_encoder() for DCE4: two per UNIPHY, B second
   o-txsel 2* o-linkb + out 4 + c!
   p-i2c out 5 + c!
   o-conn CONNECTOR_OBJECT_ID_DISPLAYPORT = 1 and out 6 + c!
   log( s" output " +s #outs +dec s" : " +s .output )log
   #outs 1+ to #outs ;

\ radeon_get_atom_connector_info_from_object_table(), digital outputs only
: read-outputs ( -- )
   0 to #outs
   DATA-Object_Header data-start ?dup 0= if
      s" the VBIOS has no object table" rdn-log exit then
   dup to ob-base 3 + cu8 2 < if
      s" the VBIOS object table is too old" rdn-log exit then
   ob-base 6 + cu16 ob-base + to ob-con
   ob-base 0e + cu16 ob-base + to ob-paths
   ob-paths 4 +
   ob-paths cu8 d# 16 min 0 ?do              ( path )
      dup 2 + cu16                            ( path size )
      dup 8 < over d# 40 > or if drop leave then
      over add-path +
   loop drop ;

\ ---- command tables ----
: cmd-rev ( index -- frev crev )
   2 * 4 + cmd-table + cu16 ?dup if dup 2 + cu8 swap 3 + cu8 else 0 0 then ;

\ Refuse a table whose revision is not the one this code was written for.
0 value nr-f  0 value nr-c  0 value nr-a  0 value nr-n
: need-rev ( index frev crev a n -- error? )
   to nr-n to nr-a to nr-c to nr-f
   cmd-rev 2dup nr-c = swap nr-f = and if 2drop false exit then
   log( nr-a nr-n +s s"  is revision " +s swap +dec [char] . +c +dec
   s" , need " +s nr-f +dec [char] . +c nr-c +dec )log true ;

false value debug-tables?      \ log every command table call and its block
: .ps ( index -- )
   log( s" table " +s +hex s" :" +s
   ps-buf d# 24 bounds do bl +c i c@ d# 16 u< if [char] 0 +c then i c@ +hex loop )log ;

: exec ( index -- error? )
   debug-tables? if dup .ps then
   dup run-table dup if
      log( s" command table " +s over +hex s"  failed" +s )log
   then nip ;

: crtc-cmd ( state index -- error? )
   ps-clear  ATOM_CRTC1 0 ps-c!  swap 1 ps-c!  exec ;
: lock-crtc   ( state -- error? ) COMMAND-UpdateCRTC_DoubleBufferRegisters crtc-cmd ;
: enable-crtc ( state -- error? ) COMMAND-EnableCRTC crtc-cmd ;
: memreq      ( state -- error? ) COMMAND-EnableCRTCMemReq crtc-cmd ;
: blank-crtc  ( state -- error? ) COMMAND-BlankCRTC crtc-cmd ;

\ Dual-link DVI uses both halves of one UNIPHY above 165 MHz.
d# 165000 constant single-link-max
: duallink? ( -- f )
   o-dp 0= m-clock single-link-max > and
   o-conn CONNECTOR_OBJECT_ID_DUAL_LINK_DVI_I =
   o-conn CONNECTOR_OBJECT_ID_DUAL_LINK_DVI_D = or and ;
: lanes ( -- n ) duallink? if 8 else 4 then ;

\ ---- the pixel PLL ----
4   constant pll-use-ref-div
40  constant pll-prefer-low-ref-div
400 constant pll-use-frac-fb-div
1000 constant pll-use-post-div

0 value pll-flags  0 value pll-ref-freq  0 value pll-ref-div  0 value pll-post-div
0 value pll-out-min  0 value pll-out-max
: frac? ( -- f ) pll-flags pll-use-frac-fb-div and 0<> ;

\ radeon_atom_get_clock_info(), the pixel PLL; FirmwareInfo is 2.1 here
0 value fwi
: get-pll-info ( -- error? )
   DATA-FirmwareInfo data-start ?dup 0= if true exit then  to fwi
   fwi 2 + cu8 2 < fwi 3 + cu8 2 < and if
      s" FirmwareInfo 1.1 is not supported" rdn-log true exit then
   fwi fwi-ref-clk + cu16 to pll-ref-freq
   fwi fwi-min-pix-pll-out + cu32 to pll-out-min
   fwi fwi-max-pix-pll-out + cu32 to pll-out-max
   pll-out-min 0= if d# 64800 to pll-out-min then
   0 to pll-ref-div  0 to pll-post-div
   false ;

\ the divider limits radeon_get_clock_info() gives AVIVO pixel PLLs
2 constant min-post-div   7f constant max-post-div
2 constant min-ref-div    3ff constant max-ref-div
4 constant min-fb-div     7ff constant max-fb-div

\ avivo_reduce_ratio()
0 value rn  0 value rd
: reduce-ratio ( nom den nom-min den-min -- nom' den' )
   2swap to rd to rn
   rn rd begin ?dup while tuck umod repeat     ( nmin dmin gcd ) \ gcd
   dup rn swap u/ to rn  rd swap u/ to rd
   swap dup rn u> if rn div-up dup rn * u32 to rn rd * u32 to rd else drop then
   dup rd u> if rd div-up dup rn * u32 to rn rd * u32 to rd else drop then
   rn rd ;

\ avivo_get_fb_ref_div()
0 value g-post  0 value g-fbmax
: fb-ref-div ( nom den post fb-max ref-max -- fb ref )
   >r to g-fbmax to g-post
   d# 100 g-post u/ 1 r> clamp >r            ( nom den )   \ ref * post <= 100
   dup g-post u/ 1 r> clamp                  ( nom den ref )
   >r swap r@ * g-post * u32 swap div-closest r>    ( fb ref )
   over g-fbmax u> if g-fbmax * u32 over u/ nip g-fbmax swap then ;

\ radeon_compute_pll_avivo(); freq in kHz
0 value tc  0 value fbmin  0 value fbmax  0 value refmin  0 value refmax
0 value pmin  0 value pmax  0 value nom  0 value den  0 value pbest  0 value dbest
0 value vmin  0 value vmax
: compute-pll ( freq -- fb frac ref post )
   frac? 0= if d# 10 u/ then to tc
   min-fb-div to fbmin  max-fb-div to fbmax
   frac? if fbmin d# 10 * to fbmin  fbmax d# 10 * to fbmax then
   pll-flags pll-use-ref-div and if pll-ref-div else min-ref-div then to refmin
   frac? pll-flags pll-use-ref-div and 0<> and if pll-ref-div else max-ref-div then to refmax
   pll-flags pll-use-post-div and if
      pll-post-div dup to pmin to pmax
   else
      pll-out-min pll-out-max frac? if d# 10 * swap d# 10 * swap then to vmax to vmin
      vmin tc u/ dup tc * u32 vmin u< if 1+ then min-post-div max to pmin
      vmax tc u/ dup tc * u32 vmax u> if 1- then max-post-div min to pmax
   then
   tc pll-ref-freq fbmin pmin reduce-ratio to den to nom
   pmax to pbest  ffffffff to dbest
   pmin begin dup pmax u> 0= while           ( post )
      >r nom den r@ fbmax refmax fb-ref-div  ( fb ref )
      r@ * u32 swap pll-ref-freq * u32 swap u/   ( got )
      dup tc u> if tc - else tc swap - then  ( diff )
      dup dbest u> if drop else to dbest r@ to pbest then
      r> 1+
   repeat drop
   nom den pbest fbmax refmax fb-ref-div     ( fb ref )
   fbmin refmin reduce-ratio                 ( fb ref )
   \ avoid high jitter with small fractional dividers
   frac? if over d# 10 umod ?dup if
      9 swap - d# 20 * d# 50 + fbmin max to fbmin
      over fbmin u< if fbmin 2 pick div-up tuck * u32 >r * u32 r> then
   then then
   swap frac? if dup d# 10 u/ swap d# 10 umod else 0 then   ( ref fb frac )
   rot pbest ;

0 value adj-clock
\ atombios_adjust_pll(): the BIOS's correction of the PLL frequency for this
\ transmitter; AdjustDisplayPll 1.3. Returns kHz, 0 on error.
: adjust-pll ( -- khz )
   pll-prefer-low-ref-div to pll-flags
   \ Linux on DCE3.2 and later: fractional feedback divider above 165 MHz
   m-clock single-link-max > if pll-flags pll-use-frac-fb-div or to pll-flags then
   COMMAND-AdjustDisplayPll 1 3 s" AdjustDisplayPll" need-rev if 0 exit then
   ps-clear
   m-clock d# 10 u/ adj3-clock ps-w!
   o-enc adj3-transmitter ps-c!
   ATOM_ENCODER_MODE_DVI adj3-encoder-mode ps-c!
   DISPPLL_CONFIG_COHERENT_MODE duallink? if DISPPLL_CONFIG_DUAL_LINK or then
   adj3-config ps-c!
   COMMAND-AdjustDisplayPll exec if 0 exit then
   adj3o-ref-div ps-c@ ?dup if to pll-ref-div
      pll-flags pll-use-frac-fb-div or pll-use-ref-div or to pll-flags then
   adj3o-post-div ps-c@ ?dup if to pll-post-div
      pll-flags pll-use-frac-fb-div or pll-use-post-div or to pll-flags then
   adj3o-freq ps-l@ d# 10 * ;

\ ---- the rest of the CRTC ----
create dig-ids
   ASIC_INT_DIG1_ENCODER_ID c, ASIC_INT_DIG2_ENCODER_ID c, ASIC_INT_DIG3_ENCODER_ID c,
   ASIC_INT_DIG4_ENCODER_ID c, ASIC_INT_DIG5_ENCODER_ID c, ASIC_INT_DIG6_ENCODER_ID c,

\ atombios_set_encoder_crtc_source(), SELECT_CRTC_SOURCE_PARAMETERS_V2
: select-crtc-source ( -- error? )
   COMMAND-SelectCRTC_Source 1 2 s" SelectCRTC_Source" need-rev if true exit then
   ps-clear
   ATOM_CRTC1 scs2-crtc ps-c!
   dig-ids o-dig + c@ scs2-encoder ps-c!
   ATOM_ENCODER_MODE_DVI scs2-mode ps-c!
   COMMAND-SelectCRTC_Source exec ;

\ atombios_crtc_program_ss(ATOM_DISABLE) for pixel PLL 1: all zero
: disable-ss ( -- error? )
   ps-clear COMMAND-EnableSpreadSpectrumOnPPLL exec ;

0 value pl-fb  0 value pl-frac  0 value pl-ref  0 value pl-post
\ atombios_crtc_program_pll(), PIXEL_CLOCK_PARAMETERS_V5
: program-pll ( -- error? )
   COMMAND-SetPixelClock 1 5 s" SetPixelClock" need-rev if true exit then
   ps-clear
   ATOM_CRTC1 pc5-crtc ps-c!
   m-clock d# 10 u/ pc5-clock ps-w!
   pl-fb pc5-fb-div ps-w!
   pl-post pc5-post-div ps-c!
   pl-ref pc5-ref-div ps-c!
   ATOM_PPLL1 pc5-ppll ps-c!
   o-enc pc5-transmitter ps-c!
   ATOM_ENCODER_MODE_DVI pc5-encoder-mode ps-c!
   0 pc5-misc ps-c!
   pl-frac d# 100000 * pc5-frac ps-l!
   COMMAND-SetPixelClock exec ;

\ atombios_set_crtc_dtd_timing()
: set-dtd ( -- error? )
   ps-clear
   m-hd dtd-h-size ps-w!
   m-ht m-hd - dtd-h-blank ps-w!
   m-vd dtd-v-size ps-w!
   m-vt m-vd - dtd-v-blank ps-w!
   m-hss m-hd - dtd-h-sync-off ps-w!
   m-hse m-hss - dtd-h-sync-w ps-w!
   m-vss m-vd - dtd-v-sync-off ps-w!
   m-vse m-vss - dtd-v-sync-w ps-w!
   0 m-flags mode-nvsync and if ATOM_VSYNC_POLARITY or then
   m-flags mode-nhsync and if ATOM_HSYNC_POLARITY or then dtd-misc ps-w!
   ATOM_CRTC1 dtd-crtc ps-c!
   COMMAND-SetCRTC_UsingDTDTiming exec ;

: overscan ( -- error? )
   ps-clear ATOM_CRTC1 ovs-crtc ps-c! COMMAND-SetCRTC_OverScan exec ;
: scaler ( -- error? )
   ps-clear ATOM_CRTC1 scl-crtc ps-c! ATOM_SCALER_DISABLE scl-enable ps-c!
   COMMAND-EnableScaler exec ;

\ ---- the framebuffer ----
8 value fb-bpp            \ 8 (indexed, the Open Firmware console) or 32
0 value fb-offset         \ where scanout starts in the aperture (BAR 0)
0 value fb-pitch          \ pixels per line

\ Pitch alignment: Linux's radeon_align_pitch() asks 256 pixels at 8 bpp,
\ but RadeonNI's 8 bpp console ran at a 1920-pixel pitch on DCE5 hardware,
\ so 128 pixels (128 bytes) is enough there; 64 pixels at 16 and 32 bpp.
: align-pitch ( width -- pitch )
   fb-bpp 8 = if 7f else 3f then
   tuck + swap invert and u32 ;

\ dce4_crtc_do_set_base(), linear. The CPU writes big-endian pixels, so 16-
\ and 32-bit ones are byte-swapped on the way out. The aperture shows the
\ card's memory from where MC_VM_FB_LOCATION puts it, as the BIOS left it.
: set-base ( -- )
   fb-bpp 8 = if 0 0 else fb-bpp d# 16 = if 1 1 else 2 2 then then  ( format swap )
   MC_VM_FB_LOCATION rreg ffff and                                    ( f s loc )
   dup 8 rshift swap d# 24 lshift u32 fb-offset + u32                 ( f s hi lo )
   dup fb-offset u< if swap 1+ swap then
   0 D1VGA_CONTROL wreg
   0 EVERGREEN_GRPH_FLIP_CONTROL wreg
   swap dup EVERGREEN_GRPH_PRIMARY_SURFACE_ADDRESS_HIGH wreg
   EVERGREEN_GRPH_SECONDARY_SURFACE_ADDRESS_HIGH wreg                 ( f s lo )
   EVERGREEN_GRPH_SURFACE_ADDRESS_MASK and
   dup EVERGREEN_GRPH_PRIMARY_SURFACE_ADDRESS wreg
   EVERGREEN_GRPH_SECONDARY_SURFACE_ADDRESS wreg                      ( f s )
   swap EVERGREEN_GRPH_CONTROL wreg  EVERGREEN_GRPH_SWAP_CONTROL wreg
   \ 8 bits per colour goes through the LUT
   EVERGREEN_LUT_10BIT_BYPASS_EN EVERGREEN_GRPH_LUT_10BIT_BYPASS_CONTROL reg-clear
   0 EVERGREEN_GRPH_SURFACE_OFFSET_X wreg
   0 EVERGREEN_GRPH_SURFACE_OFFSET_Y wreg
   0 EVERGREEN_GRPH_X_START wreg
   0 EVERGREEN_GRPH_Y_START wreg
   m-hd EVERGREEN_GRPH_X_END wreg
   m-vd EVERGREEN_GRPH_Y_END wreg
   fb-pitch EVERGREEN_GRPH_PITCH wreg
   1 EVERGREEN_GRPH_ENABLE wreg
   m-vd EVERGREEN_DESKTOP_HEIGHT wreg
   0 EVERGREEN_VIEWPORT_START wreg
   m-hd d# 16 lshift m-vd 1+ -2 and or u32 EVERGREEN_VIEWPORT_SIZE wreg
   \ page flips anywhere in the vertical blank
   0 EVERGREEN_MASTER_UPDATE_MODE wreg ;

\ evergreen_line_buffer_adjust() with only CRTC 0 on: the whole line buffer.
\ evergreen_program_watermarks() is not ported; no priority marks.
: line-buffer ( -- )
   2 DC_LB_MEMORY_SPLIT wreg
   PRIORITY_OFF PRIORITY_A_CNT wreg
   PRIORITY_OFF PRIORITY_B_CNT wreg ;

\ one palette entry, 8 bits per colour (the LUT has 10)
: lut! ( r g b index -- )
   0 EVERGREEN_DC_LUT_RW_MODE wreg
   7 EVERGREEN_DC_LUT_WRITE_EN_MASK wreg
   EVERGREEN_DC_LUT_RW_INDEX wreg
   ff and 2 lshift  swap ff and d# 12 lshift or  swap ff and d# 22 lshift or
   EVERGREEN_DC_LUT_30_COLOR wreg ;

\ dce4_crtc_load_lut() with a grey ramp
: load-lut ( -- )
   0 EVERGREEN_DC_LUT_CONTROL wreg
   0 EVERGREEN_DC_LUT_BLACK_OFFSET_BLUE wreg
   0 EVERGREEN_DC_LUT_BLACK_OFFSET_GREEN wreg
   0 EVERGREEN_DC_LUT_BLACK_OFFSET_RED wreg
   ffff EVERGREEN_DC_LUT_WHITE_OFFSET_BLUE wreg
   ffff EVERGREEN_DC_LUT_WHITE_OFFSET_GREEN wreg
   ffff EVERGREEN_DC_LUT_WHITE_OFFSET_RED wreg
   0 EVERGREEN_DC_LUT_RW_MODE wreg
   7 EVERGREEN_DC_LUT_WRITE_EN_MASK wreg
   0 EVERGREEN_DC_LUT_RW_INDEX wreg
   100 0 do
      i 2 lshift dup d# 20 lshift over d# 10 lshift or or
      EVERGREEN_DC_LUT_30_COLOR wreg
   loop ;

\ ---- the encoder and transmitter ----
\ atombios_dig_encoder_setup(), DIG_ENCODER_CONTROL_PARAMETERS_V3
: dig-encoder ( action -- error? )
   COMMAND-DIGxEncoderControl 1 2 s" DIGxEncoderControl" need-rev if drop true exit then
   ps-clear
   m-clock d# 10 u/ dig3-clock ps-w!
   o-dig 4 lshift dig3-config ps-c!          \ acConfig.ucDigSel
   dig3-action ps-c!
   ATOM_ENCODER_MODE_DVI dig3-mode ps-c!
   lanes dig3-lanes ps-c!
   PANEL_8BIT_PER_COLOR dig3-bpc ps-c!
   COMMAND-DIGxEncoderControl exec ;

\ acConfig of DIG_TRANSMITTER_CONTROL_PARAMETERS_V3: bit 0 dual link,
\ bit 1 coherent, bit 2 link B, bit 3 encoder B, bits 4-5 the PLL (PPLL1
\ is 0), bits 6-7 the UNIPHY
: tx-config ( -- b )
   2  duallink? if 1 or then  o-linkb if 4 or then
   o-dig 1 and if 8 or then  o-txsel 6 lshift or ;

: transmitter ( action -- error? )
   COMMAND-UNIPHYTransmitterControl 1 3 s" UNIPHYTransmitterControl" need-rev
   if drop true exit then
   ps-clear
   m-clock duallink? if 2/ then d# 10 u/ tx3-clock ps-w!
   tx-config tx3-config ps-c!
   tx3-action ps-c!
   lanes tx3-lanes ps-c!
   COMMAND-UNIPHYTransmitterControl exec ;

\ ATOM_TRANSMITTER_ACTION_INIT: tell the BIOS which connector it serves
: transmitter-init ( -- error? )
   COMMAND-UNIPHYTransmitterControl 1 3 s" UNIPHYTransmitterControl" need-rev
   if true exit then
   ps-clear
   o-conn tx3-clock ps-w!                      \ usInitInfo
   2 o-linkb if 4 or then o-txsel 6 lshift or tx3-config ps-c!
   ATOM_TRANSMITTER_ACTION_INIT tx3-action ps-c!
   4 tx3-lanes ps-c!
   COMMAND-UNIPHYTransmitterControl exec ;

\ ---- once, before any mode is set ----
\ radeon_atom_encoder_init() and radeon_atom_disp_eng_pll_init(): every
\ transmitter learns its connector, and the display engine clock starts.
\ Without that clock the CRTC timing is wrong.
: display-init ( -- error? )
   out  #outs 0 ?do i select-output transmitter-init drop loop  to out
   DATA-FirmwareInfo data-start ?dup 0= if true exit then
   fwi-dispclk + cu32 ?dup 0= if d# 60000 then    ( dispclk, 10 kHz units )
   log( s" display engine clock " +s dup +dec s" 0 kHz" +s )log
   COMMAND-SetPixelClock 1 5 s" SetPixelClock" need-rev if drop true exit then
   ps-clear
   ATOM_CRTC_INVALID pc5-crtc ps-c!
   pc5-clock ps-w!
   ATOM_DCPLL pc5-ppll ps-c!
   COMMAND-SetPixelClock exec ;

\ ---- mode set ----
: .mode ( -- )
   m-hd +dec [char] x +c m-vd +dec s" , " +s m-clock +dec s"  kHz" +s ;

: mode-fits? ( -- f )
   m-clock single-link-max u> 0= if true exit then
   duallink? m-clock max-mode-clock u> 0= and ;

\ atombios_crtc_mode_set() and the encoder's mode set and DPMS on, for the
\ mode in m-* on the selected output. fb-bpp and fb-pitch must be set.
: modeset ( -- error? )
   o-dp if s" DisplayPort is not supported" rdn-log true exit then
   mode-fits? 0= if s" the mode's clock is too high for this output" rdn-log true exit then
   get-pll-info if true exit then
   adjust-pll ?dup 0= if true exit then to adj-clock
   select-crtc-source if true exit then
   \ CRTC prepare: lock, then off
   ATOM_ENABLE lock-crtc if true exit then
   crtc-on? if ATOM_ENABLE blank-crtc drop then
   false to crtc-on?
   ATOM_DISABLE memreq drop
   ATOM_DISABLE enable-crtc drop
   \ CRTC mode set
   adj-clock compute-pll to pl-post to pl-ref to pl-frac to pl-fb
   log( s" mode " +s .mode s" : PLL " +s adj-clock +dec s"  kHz, fb " +s
      pl-fb +dec [char] . +c pl-frac +dec s"  ref " +s pl-ref +dec
      s"  post " +s pl-post +dec )log
   disable-ss drop
   program-pll ?dup 0= if
      set-dtd ?dup 0= if
         set-base
         line-buffer
         overscan drop
         scaler drop
         \ encoder prepare: output off while the CRTC comes up
         ATOM_TRANSMITTER_ACTION_DISABLE transmitter drop
         \ CRTC commit
         ATOM_ENABLE enable-crtc drop
         ATOM_ENABLE memreq drop
         ATOM_DISABLE blank-crtc drop
         load-lut
         true to crtc-on?
         0
      then
   then
   ATOM_DISABLE lock-crtc drop
   ?dup if exit then
   \ encoder commit (DCE4: setup, then the transmitter on)
   ATOM_ENCODER_CMD_SETUP dig-encoder if true exit then
   ATOM_TRANSMITTER_ACTION_ENABLE transmitter ;

\ ---- choosing the output and the mode ----
\ the first detailed timing in the EDID that this output can drive
: edid-mode ( -- ok? )
   4 0 do
      edid edid-dtd + i /dtd * + dtd>mode if mode-fits? if true unloop exit then then
   loop false ;

\ The first output with a display that answers on DDC, else the first one
\ that is not DisplayPort; the display's preferred mode, else 1024x768.
: choose-output ( -- found? )
   #outs 0 ?do
      i select-output
      o-dp 0= if
         o-i2c i2c-bus if
            edid-read dup 0> if
               log( s" EDID found on " +s .output )log
               drop true unloop exit
            then
            log( s" no EDID on " +s .output s"  (error " +s dup +sdec [char] ) +c )log
            drop
         then
      then
   loop
   #outs 0 ?do i select-output o-dp 0= if unloop false exit then loop
   false ;

\ Everything from a cold card to a picture. The framebuffer is fb-bpp deep,
\ m-hd by m-vd, fb-pitch pixels a line, at fb-offset in the aperture.
: display-start ( -- error? )
   card-post if true exit then
   read-outputs
   #outs 0= if s" no DVI or HDMI output found" rdn-log true exit then
   0 select-output
   display-init if true exit then
   choose-output if edid-mode 0= if default-mode then else default-mode then
   o-dp if s" only DisplayPort outputs" rdn-log true exit then
   m-hd align-pitch to fb-pitch
   modeset ;

decimal

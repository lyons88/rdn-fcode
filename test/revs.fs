\ Lists the VBIOS table revisions the driver depends on:
\   gforth test/prelude.fs src/consts.fs src/atom.fs test/revs.fs
s" reference/vbios/visiontek6770.rom" load-rom atom-init drop
: .rev ( base -- ) dup 2 + cu8 .h ." ." 3 + cu8 .h ;
: crev ( idx "name" -- ) parse-name type ."  = " 2 * 4 + cmd-table + cu16 ?dup if .rev else ." none" then cr ;
: drev ( idx "name" -- ) parse-name type ."  = " data-start ?dup if .rev else ." none" then cr ;
COMMAND-ASIC_Init crev ASIC_Init
COMMAND-DIGxEncoderControl crev DIGxEncoderControl
COMMAND-UNIPHYTransmitterControl crev UNIPHYTransmitterControl
COMMAND-SetPixelClock crev SetPixelClock
COMMAND-AdjustDisplayPll crev AdjustDisplayPll
COMMAND-SelectCRTC_Source crev SelectCRTC_Source
COMMAND-EnableSpreadSpectrumOnPPLL crev EnableSpreadSpectrumOnPPLL
COMMAND-SetCRTC_UsingDTDTiming crev SetCRTC_UsingDTDTiming
COMMAND-EnableCRTC crev EnableCRTC
COMMAND-BlankCRTC crev BlankCRTC
COMMAND-EnableCRTCMemReq crev EnableCRTCMemReq
COMMAND-UpdateCRTC_DoubleBufferRegisters crev UpdateCRTC_DoubleBufferRegisters
COMMAND-SetCRTC_OverScan crev SetCRTC_OverScan
COMMAND-EnableScaler crev EnableScaler
d# 76 crev ProcessI2cChannelTransaction
d# 78 crev DPEncoderService
DATA-FirmwareInfo drev FirmwareInfo
DATA-Object_Header drev Object_Header
DATA-GPIO_I2C_Info drev GPIO_I2C_Info
DATA-GPIO_Pin_LUT drev GPIO_Pin_LUT
bye

| GM '0411 Kernel for PCMHammer
| 2018-2023 Antus / PCMHacking.net
| 2023-03-25 @21:00 Gampy <@pcmhacking.net>
| 2024-06-11 @21:00 Antus / PCMHacking.net Backport Gampy's akernel changes (shared without source), cleanup build system
| 2024-07-16 @19:00 Antus / PCMHacking.net Add P04_256k, BlackBox (98-02 4 connector)
| ===========================================================================
|
| C directives will only work if the source filename is .S, yup, capital S.
| That is the extension gnu has asscoaited the assembler with and it is case sensitive.
| When using gcc.exe with -x assembler option and anyother extension, the C directives will fail.
| We need to use gcc.exe for C directives, as.exe does not provide for them.
|

| Include Common elements
| These elements cannot be a sub of MainLoop
#include "Common-Assembly.h"

| Flash elements
#if defined P12
  .equ SIM_BASE,             0xFA30
  .equ SIM_20,               (SIM_BASE + 0x20) | Lock functions
#else
  .equ SIM_BASE,             0xFA00
#endif
.equ SIM_CSBARBT,            (SIM_BASE + 0x48) | (SIM_BASE + 0x48) (FA48)
.equ SIM_CSORBT,             (SIM_BASE + 0x4A) | (SIM_BASE + 0x4A) (FA4A)
.equ SIM_CSBAR0,             (SIM_BASE + 0x4C) | (SIM_BASE + 0x4C) (FA4C)
.equ SIM_CSOR0,              (SIM_BASE + 0x4E) | (SIM_BASE + 0x4E) (FA4E)

#if defined P04_256k
  .equ REG_FFC000,            0xC000
  .equ REG_FFC002,            0xC002
  .equ VPP_BIT2,              2
  .equ VPP_BIT4,              4
#elif defined P08
  .equ REG_FFF408,            0xF408
  .equ REG_FFF4C8,            0xF4C8
  .equ VPP_BIT,               2
  .equ HARDWARE_IO,           0xFA11
#elif defined BlackBox
  .equ PORTFDIR,              0xFA1D | Port F data direction reg, b4 = 1 for PF4 output
  .equ PORTFASN,              0xFA1F | Port F pin assignment, clear b4 for port F assignment
  .equ VPP_BIT,               4
  .equ HARDWARE_IO,           0xFA19 | Port F for flash, VPP bits for flash
#else
  .equ VPP_BIT,               0
  .equ HARDWARE_IO,           0xE2FA
#endif
|
.equ READ_ARRAY_CMD,         0xFFFF    | Intel
.equ ERASE_RESUME_CONFIRM,   0xD0D0    | Intel
.equ AMD_PROGRAM_CMD,        0xA0A0    | Amd
.equ SIGNATURE_CMD,          0x9090    | Both
.equ AMD_ERASE_SETUP,        0x8080    | AMD
.equ READ_STATUS_REGISTER,   0x7070    | Intel
.equ CLEAR_STATUS_REGISTER,  0x5050    | Intel
.equ INTEL_PROGRAM_CMD,      0x4040    | Intel
.equ AMD_ERASE_RESUME,       0x3030    | AMD
.equ INTEL_ERASE_CMD,        0x2020    | Intel
.equ AMD_CMD_ADDRESS_A,      0x0AAA    | AMD
.equ AMD_CMD_ADDRESS_B,      0x0554    | AMD
.equ AMD_CMD_UNLOCK_A,       0xAAAA    | AMD
.equ AMD_CMD_UNLOCK_B,       0x5555    | AMD
.equ AMD_READ_ARRAY_CMD,     0xF0F0    | AMD
|
|Supported flash chip manufactures
.equ INTEL_ID,               0x0089    | Manufacture ID
.equ AMD_ID,                 0x0001    | Manufacture ID
|
| Intel supported chips
.equ FLASH_ID_INTEL_28F400B,  0x4471   | 512k,  7 Sectors, 0,4000,6000,8000,20000,40000,60000
.equ FLASH_ID_INTEL_28F800B,  0x889D   | 1m,   11 Sectors, 0,4000,6000,8000,20000,40000,60000,80000,A0000,C0000,E0000
| AMD supported chips
.equ FLASH_ID_AMD_AM29F400BB, 0x22AB   | 512k, 11 Sectors, 0,4000,6000,8000,10000,20000,30000,40000,50000,60000,70000
.equ FLASH_ID_AMD_AM29F800BB, 0x2258   | 1m,   19 Sectors, 0,4000,6000,8000,10000,20000,30000,40000,50000,60000,70000,80000,90000,A0000,B0000,C0000,D0000,E0000,F0000
.equ FLASH_ID_AMD_AM29BL802C, 0x2281   | 1m,    9 Sectors, 0,4000,6000,8000,20000,40000,60000,80000,C0000
.equ FLASH_ID_AMD_AM29BL162C, 0x2203   | 2m,   11 Sectors, 0,4000,6000,8000,40000,80000,C0000,100000,140000,180000,1C0000

| CRC32
.equ POLYNOMIAL,         0x4C11DB7

| Modes supported shared in Common-Assembly.S
.equ KernelID_3D00,      0x3D00        | Return Kernel ID
.equ FlashID_3D01,       0x3D01        | Return Flash chip ID
.equ CRC_3D02,           0x3D02        | Return CRC for range
.equ OsID_3D03,          0x3D03        | Return OsID
.equ EraseSector_3D05,   0x3D05        | ProcessEraseSector
|.equ Halt_20             0x20          | Moved to Common-Assembly.s
|.equ Mode_34             0x34          | Moved to Common-Assembly.s
.equ Mode_35,            0x35          | Send data from PCM to Tool
|.equ Mode_36             0x36          | Moved to Common-Assembly.s

start:
    ori     #0x700, %sr                | Disable Interrupts

#if defined P08 | defined E54
    movea.w #0xB800, %sp               | Move the stack out the way P08/E54
#endif

    move.b  #0x03, (J1850_Command).l   | Flush frame except for completion code
    move.b  #0x00, (J1850_TX_FIFO).l   | Initiate transfer

Wait01:
    bsr.w   ResetWatchdog              | Scratch the dog
    bsr.w   WasteTime                  | Twiddle thumbs
    move.b  (J1850_Status).l, %d0      | Get the VPW status
    andi.b  #0xE0, %d0                 | Mask Receive FIFO Status Field (RFS) register 1110 0000
    cmpi.b  #0xE0, %d0                 | Check for "completion code only, at head of buffer" message 1110 0000
    bne.b   Wait01                     | Not ready? wait and retry
    move.b  (J1850_RX_FIFO).l, %d0     | Strip off the completion code

#if defined P12
    movea.l #Mode36Reply, %a0          | Reply Success to mode 3680, only P12 needs this.
    move.w  #6, %d0                    | It's 6 bytes long
    bsr.w   VPWSend                    | Send message
    bsr.w   WasteTime                  | Twiddle thumbs
#endif

MainLoop:
    move.b  #0, MessageBuffer + 3      | Clear last Mode, prevents repeating it
    bsr.w   ResetWatchdog              | Scratch the dog
    bsr.w   VPWReceive                 | Wait for and read next packet
    bsr.w   WasteTime                  | Twiddle thumbs
    moveq   #0, %d0                    | Start Clean
    move.b  MessageBuffer + 3, %d0     | Move Mode to Low byte of Word
    lsl.l   #8, %d0                    | Shift Mode to High byte of Word
    move.b  MessageBuffer + 4, %d0     | Move Submode to Low byte of Word

| Modes with Submodes handled here in MainLoop, i.e. two byte actions
    cmpi.w  #KernelID_3D00, %d0        | Is it mode 0x3D 00 Get Kernel ID (non-standard extension)
    beq.w   ProcessKernelID            | Process it
    cmpi.w  #FlashID_3D01, %d0         | Is it mode 0x3D 01 Get Flash Chip ID (non-standard extension)
    beq.w   ProcessFlashID             | Process it
    cmpi.w  #CRC_3D02, %d0             | Is it mode 0x3D 02 Get Flash Sector CRC (non-standard extension)
    beq.w   ProcessCRC                 | Process it
    cmpi.w  #OsID_3D03, %d0            | Is it mode 0x3D 03 Get Os ID (non-standard extension)
    beq.w   ProcessOsID                | Process it
    cmpi.w  #EraseSector_3D05, %d0     | Is it mode 0x3D 05 Erase Flash Sector (non-standard extension)
    beq.w   ProcessEraseSector         | Process it

    lsr.w   #8, %d0                    | Shift the Mode into the lower byte of the word

| Modes without Submodes, or Submodes that are ignored here in MainLoop, i.e. single byte actions
    cmpi.b  #Mode_34, %d0              | Is it mode 0x34 (Tool asking PCM, ok to send X bytes to address)
    beq.w   ProcessMode34              | Process it
    cmpi.b  #Mode_35, %d0              | Is it mode 0x35 (Tool asking PCM to send data from Flash)
    beq.w   ProcessMode35              | Process it
    cmpi.b  #Mode_36, %d0              | Is it mode 0x36 (Tool sending data to write, either to RAM or Flash)
    beq.l   ProcessMode36              | Process it
    cmpi.b  #Halt_20, %d0              | Is it mode 0x20 (return to normal comms)
    bne.s   MainLoop                   | No more options, next packet or reboot

| =============== S U B R O U T I N E =======================================
Reboot:
    movea.l #Mode60Reply, %a0          | Pointer to Mode 20 reply buffer
    move.w  #4, %d0                    | It's 4 bytes long
    bsr.w   VPWSend                    | Send it
    bsr.w   WasteTime                  | Twiddle thumbs
    reset                              | Reset External Devices

EndlessLoop:                           | Kills pcm, lets COPs expire
    bra.b   EndlessLoop

| =============== S U B R O U T I N E =======================================
ResetWatchdog:
    move.b  #0x55, (COP1).l            | Reset COP1
    move.b  #0xAA, (COP1).l            | Reset COP1
    eori.b  #0x80, (COP2).l            | Reset COP2 ... COP2 ^= 0x80
#if defined BlackBox
	move.b  #0x50, (COP3).l            | Vortec BlackBox only
	move.b  #0xA0, (COP3).l            |
#endif
    rts

| =============== S U B R O U T I N E =======================================
WasteTime:
    nop                                | Non Operation
    nop
    nop
    nop
    rts

| =============== S U B R O U T I N E =======================================
ProcessKernelID:
    movea.l #KernelIDReply, %a0        | Pointer to KernelIDReply buffer
#if defined P01
    move.b  #0x01, 8(%a0)              | P01, P59
#elif defined P04_256k
    move.b  #0x44, 8(%a0)              | P04 256k
#elif defined P04
    move.b  #0x04, 8(%a0)              | P04
#elif defined P08
    move.b  #0x08, 8(%a0)              | P08
#elif defined P10
    move.b  #0x10, 8(%a0)              | P10
#elif defined P12
    move.b  #0x0C, 8(%a0)              | P12
#elif defined E54
    move.b  #0x36, 8(%a0)              | E54
#elif defined BlackBox
    move.b  #0x88, 8(%a0)              | BlackBox
#endif
    move.w  #9, %d0                    | It's 9 bytes long
    bsr.w   VPWSend                    | Send it
    jmp     MainLoop                   | Return to MainLoop

| =============== S U B R O U T I N E =======================================
| Process Operating System ID
| Mode 3D03
|
| Gets the Operating System Number from flash and sends it
|
| Uses
|   a0 - OsIDReply buffer
|   a1 - OsID address buffer
|
| TODO: Have request supply address
ProcessOsID:
#if defined P04_256k
    movea.l #0x3FFFA, %a1              | Pointer to OsID location in Flash
#elif defined P04
    movea.l #0x7FFFA, %a1              | Pointer to OsID location in Flash
#elif defined P08
    movea.l #0x8000, %a1               | Pointer to OsID location in Flash
#elif defined P10
    movea.l #0x52E, %a1                | Pointer to OsID location in Flash
#elif defined P12
    movea.l #0x8004, %a1               | Pointer to OsID location in Flash
#elif defined E54 || defined BlackBox
    movea.l #0x20004, %a1              | Pointer to OsID location in Flash
#else
    movea.l #0x504, %a1                | Pointer to OsID location in Flash
#endif
    move.l  (%a1), %d1                 | Move OsID to register

    move.b  %d1, OsIDReply + 8         | Move OsID byte 4 to OsIDReply buffer byte 9
    lsr.l   #8, %d1                    | Shift OsID Right
    move.b  %d1, OsIDReply + 7         | Move OsID byte 3 to OsIDReply buffer byte 8
    lsr.l   #8, %d1                    | Shift OsID Right
    move.b  %d1, OsIDReply + 6         | Move OsID byte 2 to OsIDReply buffer byte 7
    lsr.l   #8, %d1                    | Shift OsID Right
    move.b  %d1, OsIDReply + 5         | Move OsID byte 1 to OsIDReply buffer byte 6

    movea.l #OsIDReply, %a0            | Pointer to OsIDReply buffer
    move.w  #9, %d0                    | It's 9 bytes long (6C F0 10 7D 03 00 BA 48 C2)
    bsr.w   VPWSend                    | Send it
    jmp     MainLoop                   | Return to MainLoop
| =============== S U B R O U T I N E =======================================
| Process CRC Request
| Mode 3D02
|
| Uses
|   MessageBuffer
|   a0 - Pointer to MessageBuffer
|   a1 - Scratch
|   a2 - Scratch
|   d0 - Scratch
|   d1 - Scratch
|   d2 - Scratch
|
ProcessCRC:
    moveq   #0, %d0                    | Start Clean
    movea.l #MessageBuffer, %a0        | Pointer to MessageBuffer buffer
    move.b  8(%a0), %d0                | First address byte
    lsl.l   #8, %d0                    | Logical Shift Left
    move.b  9(%a0), %d0                | Second address byte
    lsl.l   #8, %d0                    | Logical Shift Left
    move.b  10(%a0), %d0               | Third address byte
    movea.l %d0, %a1                   | Pointer to address buffer

    moveq   #0, %d0                    | Start Clean
    move.b  5(%a0), %d0                | First length byte
    lsl.l   #8, %d0                    | Logical Shift Left
    move.b  6(%a0), %d0                | Second length byte
    lsl.l   #8, %d0                    | Logical Shift Left
    move.b  7(%a0), %d0                | Third length byte
    movea.l %d0, %a2                   | Pointer to length buffer

    lea     (%a1, %a2.l), %a2          | for (int byte = 0; byte < nBytes; ++byte)
    moveq   #0x18, %d2                 | WIDTH-8 to register
    moveq   #0, %d0                    | unsigned int remainder = 0

ProcessCRCMainLoop:
    bsr.w   ResetWatchdog              | Scratch the dog
    move.b  (%a1)+, %d1                | message[byte] to register
    lsl.l   %d2, %d1                   | Shift left copy of message[byte] by (WIDTH-8)
    eor.l   %d1, %d0                   | xor in to remainder
    moveq   #8, %d1                    | for (unsigned char bit = 8; bit > 0; --bit)

ProcessCRCBitLoop:
    tst.l   %d0                        | if (remainder & TOPBIT)
    add.l   %d0, %d0                   | remainder = (remainder << 1); (optimised by tricky use of an add opcode)
    blt.b   ProcessCRCPolynomial       | Then jump to the polynomial part
    subq.b  #1, %d1                    | Next bit
    bne.s   ProcessCRCBitLoop          | Any bits left?

ProcessCRCMainLoopCheck:
    bsr.w   ResetWatchdog              | Scratch the dog
    cmpa.l  %a1, %a2                   | Is byte < nBytes?
    bne.s   ProcessCRCMainLoop         | If not, iterate main loop

| Process CRC Response
    move.b  #toolid, 1(%a0)            | To: Tool
    move.b  #pcmid, 2(%a0)             | From: PCM
    move.b  #0x7D, 3(%a0)              | 3D + 40 = Mode 3D, Success

    move.b  %d0, 14(%a0)               | Move CRC byte 4 to MessageBuffer byte 15
    lsr.l   #8, %d0                    | Shift CRC Right
    move.b  %d0, 13(%a0)               | Move CRC byte 3 to MessageBuffer byte 14
    lsr.l   #8, %d0                    | Shift CRC Right
    move.b  %d0, 12(%a0)               | Move CRC byte 2 to MessageBuffer byte 13
    lsr.l   #8, %d0                    | Shift CRC Right
    move.b  %d0, 11(%a0)               | Move CRC byte 1 to MessageBuffer byte 12

    move.w  #15, %d0                   | It's 15 bytes long (6C F0 10 7D 02 01 00 00 01 00 00 67 86 6E C2)
    bsr.w   VPWSend                    | Send it
    jmp     MainLoop                   | Return to MainLoop

ProcessCRCPolynomial:
    eori.l  #POLYNOMIAL, %d0           | ^ POLYNOMIAL;
    subq.b  #1, %d1                    | Next byte
    bne.b   ProcessCRCBitLoop          | Is there another bit in this byte?
    bra.b   ProcessCRCMainLoopCheck    | Next main loop

| =============== S U B R O U T I N E =======================================
|
| Process Flash ID Request
|
| NOTE: Once called, the Flash Chip ID remains at address FlashIDReply, for life (the Kernels life).
|
| Uses
|   FlashIDReply
|   a0 - Scratch 
|   a1 - AMD Command Address A
|   a2 - AMD Command Address B
|   d0 - Flash ID
|   d1 - Manufacture ID
|
ProcessFlashID:
#if defined P12
    move.w  #0xF322, (SIM_CSOR0).w     | 0xFA7E
#else
    move.w  #0x0007, (SIM_CSBAR0).w    | 0xFA4C Set CS registers
    move.w  #0x6820, (SIM_CSORBT).w    | 0xFA4A
    move.w  #0x7060, (SIM_CSOR0).w     | 0xFA4E
#endif
    movea.w #0, %a0                    | Pointer to address 0 (First word)
    move.w  #SIGNATURE_CMD, (%a0)      | Command Intel Read ID State (0x9090)
    move.w  (%a0), %d1                 | Move MFG ID to d1
    movea.w #2, %a0                    | Pointer to address 2 (Second word)
    move.w  (%a0), %d0                 | Move Flash ID to d0
    movea.w #0, %a0                    | Pointer to address 0 (First word)
    move.w  #READ_ARRAY_CMD, (%a0)     | Command Intel Read Array State (0xFFFF)

    cmp.w   #INTEL_ID, %d1             | Intel ID is 0x0089
    beq.s   ProcessFlashIDFound        | Jump found if Intel

    movea.w #AMD_CMD_ADDRESS_A, %a1    | Pointer to AMD Command Address A (0xAAA)
    movea.w #AMD_CMD_ADDRESS_B, %a2    | Pointer to AMD Command Address B (0x554)
    move.w  #AMD_CMD_UNLOCK_A, (%a1)   | Command AMD Unlock A  (0xAAAA)
    move.w  #AMD_CMD_UNLOCK_B, (%a2)   | Command AMD Unlock B  (0x5555)
    move.w  #SIGNATURE_CMD, (%a1)      | Command AMD Autoselect  (0x9090)
    move.w  (%a0), %d1                 | Move MFG ID to d1
    movea.w #2, %a0                    | Pointer to address 2 (Second word)
    move.w  (%a0), %d0                 | Move Flash ID to d0
    movea.w #0, %a0                    | Pointer to address 0 (First word)
    move.w  #AMD_READ_ARRAY_CMD, (%a0) | Command AMD Reset (0xF0F0)

    cmp.w   #AMD_ID, %d1               | Amd ID is 0x0001
    beq.s   ProcessFlashIDFound        | Jump found if AMD

    moveq   #0, %d0                    | Not found
    moveq   #0, %d1                    | Not found

ProcessFlashIDFound:
#if defined P12
    move.w  #0xA332, (SIM_CSOR0).w     | 0xFA7E
#else
    move.w  #0x1060, (SIM_CSOR0).w     | 0xFA4E
#endif
    move.b  %d1, FlashIDReply + 6      | Move MFG ID byte 2 to FlashIDReply buffer byte 7
    lsr.w   #8, %d1                    | Shift MFG ID Right
    move.b  %d1, FlashIDReply + 5      | Move MFG ID byte 1 to FlashIDReply buffer byte 6

    move.b  %d0, FlashIDReply + 8      | Move Flash ID byte 2 to FlashIDReply buffer byte 9
    lsr.w   #8, %d0                    | Shift Flash ID right
    move.b  %d0, FlashIDReply + 7      | Move Flash ID byte 1 to FlashIDReply buffer byte 8

    movea.l #FlashIDReply, %a0         | Pointer to FlashIDReply buffer
    move.w  #9, %d0                    | It's 9 bytes long
    bsr.w   VPWSend                    | Send it
    jmp     MainLoop

| =============== S U B R O U T I N E =======================================
|
| Process Erase Flash Sector
|
| WARNING!!! It requires that the application has previously requested the FlashID!
|
| Exit
|   d0 - Status 0=Ok, 1=Fail
|
| Uses
|   MessageBuffer
|   FlashIDReply
|   EraseSectorReply
|   a0 - Sector Address to Erase
|   d1 - Scratch
|
ProcessEraseSector:
    moveq   #0, %d1                    | Start Clean
    move.b  MessageBuffer + 5, %d1     | Move address byte 1 to register
    lsl.l   #8, %d1                    | Shift address left
    move.b  MessageBuffer + 6, %d1     | Move address byte 2 to register
    lsl.l   #8, %d1                    | Shift address left
    move.b  MessageBuffer + 7, %d1     | Move address byte 3 to register
    movea.l %d1, %a0                   | Pointer to flash sector address buffer

    move.b  FlashIDReply + 5, %d1      | Move Flash Chip Manufacture ID byte 1 to register
    lsl.w   #8, %d1                    | Shift Flash Chip Manufacture ID byte 1 to the high byte of the word
    move.b  FlashIDReply + 6, %d1      | Move Flash Chip Manufacture ID byte 2 to register

    moveq   #0, %d0                    | Set no error

    | Intel
    cmp.w #INTEL_ID, %d1               | 0x0089
    beq.s ProcessEraseFlashIntel       | Intel Flash Erase

    | AMD
    cmp.w   #AMD_ID, %d1               | 0x0001
    beq.s   ProcessEraseFlashAMD       | AMD Flash Erase

    moveq   #1, %d0                    | Set error
    bra.s   ProcessEraseFlashFinal     | Fail

ProcessEraseFlashIntel:
    bsr.w   IntelEraseSector           | Intel Flash Sector Erase
    bra.s   ProcessEraseFlashFinal     | Done

ProcessEraseFlashAMD:
    bsr.w   AMDEraseSector             | AMD Flash Sector Erase
    |bra.s   ProcessEraseFlashFinal     | Done - Waste, Keep as reminder

ProcessEraseFlashFinal:
    movea.l #EraseSectorReply, %a0     | Pointer to EraseSectorReply buffer
    move.b  %d0, 5(%a0)                | Move error value to EraseSectorReply buffer
    move.w  #7, %d0                    | PCMHammer returns 7 bytes, 6C F0 10 7D 05 00 00, last two are Status and Error?, why two??
    bsr.w   VPWSend                    | Send it!
    jmp     MainLoop

| =============== S U B R O U T I N E =======================================
|
| Process Write Flash Sector
|
| WARNING!!! It requires that the application has previously requested the FlashID!
|
| Entry
|   a0 - Source Address
|   a1 - Destination Address
|   d0 - Byte Count
|
| Exit
|   d0 - Status 0=Ok, 1=Fail
|
| Uses
|   FlashIDReply
|   d1 - Scratch
|
ProcessWriteSector:
    move.b  FlashIDReply + 5, %d1      | Move Flash Chip Manufacture ID byte 1 to register
    lsl.w   #8, %d1                    | Shift Flash Chip Manufacture ID byte 1 to the high byte of the word
    move.b  FlashIDReply + 6, %d1      | Move Flash Chip Manufacture ID byte 2 to register

    | Intel
    cmp.w   #INTEL_ID, %d1             | 0x0089
    beq.s   ProcessWriteFlashIntel     | Intel Flash Write

    | AMD
    cmp.w   #AMD_ID, %d1               | 0x0001
    beq.s   ProcessWriteFlashAMD       | AMD Flash Write

    moveq   #1, %d0                    | Set error
    bra.s   ProcessWriteFlashFinal

ProcessWriteFlashIntel:
    bsr.w   IntelWriteSector           | Intel Flash Write
    bra.s   ProcessWriteFlashFinal     | Done

ProcessWriteFlashAMD:
    bsr.w   AMDWriteSector             | AMD Flash Write
    |bra.s   ProcessWriteFlashFinal     | Done - Waste, Keep as reminder

ProcessWriteFlashFinal:
    rts

| =============== S U B R O U T I N E =======================================
|
| AMD Flash Unlock
| Tested on, 
|   AM29F400BB (512k)
|   AM29F800BB (1m)
|   AM29BL802C (1m)
|   AM29BL162C (2m)
|
| Entry
|   d1 - Mode 0=Erase, 1=Write, is only used for the P12 currently (AMD AM29BL chips)
|
AMDFlashUnlock:
#if defined P12
    andi.w  #0xFEFF, (SIM_20).w        | SIM_20 &= 0xFEFF;
    ori.w   #0x1000, (SIM_CSOR0).w     | SIM_CSOR0 |= 0x1000;
    andi.w  #0xFFF8, (SIM_CSBAR0).w    | SIM_CSBAR0 &= 0xFFF8;

    tst.b   %d1                        | Test if Zero
    bne.s   AMDFlashUnlockModeWrite    | Branch if non Zero

|AMDFlashUnlockModeErase
    ori.w   #0x0004, (SIM_CSBAR0).w    | Mode Zero - Erase
    bra.s   AMDFlashUnlockDone

AMDFlashUnlockModeWrite:
    ori.w   #0x0005, (SIM_CSBAR0).w    | Mode One - Write

AMDFlashUnlockDone:
#else
    move.w  #0x7060, (SIM_CSOR0).w     | 0xFA4E
#endif
    rts

| =============== S U B R O U T I N E =======================================
|
| AMD Flash Lock
| Tested on, 
|   AM29F400BB (512k)
|   AM29F800BB (1m)
|   AM29BL802C (1m)
|   AM29BL162C (2m)
|
AMDFlashLock:
#if defined P12
    andi.w  #0xEFFF, (SIM_CSOR0).w     | SIM_CSOR0 &= 0xEFFF;
    andi.w  #0xFEFF, (SIM_20).w        | SIM_20 &= 0xFEFF;
    ori.w   #0x0100, (SIM_20).w        | SIM_20 |= 0x0100;
#else
    move.w  #0x1060, (SIM_CSOR0).w     | 0xFA4E
#endif
    rts

| =============== S U B R O U T I N E =======================================
|
| AMD Flash Erase Sector by Address
| Tested on,
|   AM29F400BB (512k)
|   AM29F800BB (1m)
|   AM29BL802C (1m)
|   AM29BL162C (2m)
|
| Entry
|   a0 - Sector Address to Erase
|
| Exit
|   d0 - Status 0=Ok, 1=Fail
|
| Uses
|   a2 - AMD Command Address A
|   a3 - AMD Command Address B
|   d1 - Scratch
|   d2 - Scratch
|   d3 - Scratch
|
AMDEraseSector:
    bsr.w   ResetWatchdog              | Scratch the dog

    moveq   #0, %d1                    | Set Unlock Mode 0 = Erase
    bsr.w   AMDFlashUnlock             | d1 is 0=Erase or 1=Write

    lea     AMD_CMD_ADDRESS_A, %a2     | (0xAAA)
    lea     AMD_CMD_ADDRESS_B, %a3     | (0x554)

    move.w  #AMD_CMD_UNLOCK_A, (%a2)   | (0xAAAA, 0xAAA)
    move.w  #AMD_CMD_UNLOCK_B, (%a3)   | (0x5555, 0x554)
    move.w  #AMD_ERASE_SETUP, (%a2)    | (0x8080, 0xAAA)
    move.w  #AMD_CMD_UNLOCK_A, (%a2)   | (0xAAAA, 0xAAA)
    move.w  #AMD_CMD_UNLOCK_B, (%a3)   | (0x5555, 0x554)
    move.w  #AMD_ERASE_RESUME, (%a0)   | (0x3030, ProvidedAddress)

AMDEraseSectorNotReady:
    move.w  (%a0), %d2                 | Move value at address to register
    bsr.w   ResetWatchdog              | Scratch the dog
    move.w  (%a0), %d1                 | Move value at address to register

    andi.w  #0x40, %d2                 | Mask 0100 0000
    andi.w  #0x40, %d1                 | Mask 0100 0000
    cmp.w   %d2, %d1                   | If equal Success
    beq.s   AMDEraseSectorSuccess      | Jump Success

    move.w  (%a0), %d3                 | Move value at address to register
    btst    #5, %d3                    | Is bit 5 clear
    beq.s   AMDEraseSectorNotReady     | Yes, not ready so retry

    move.w  (%a0), %d2                 | Move value at address to register
    bsr.w   WasteTime                  | Twiddle thumbs
    move.w  (%a0), %d1                 | Move value at address to register
    andi.w  #0x40, %d2                 | Mask 0100 0000
    andi.w  #0x40, %d1                 | Mask 0100 0000
    cmp.w   %d2, %d1                   | If equal Success
    beq.s   AMDEraseSectorSuccess

    moveq   #1, %d0                    | Set error
    bra.s   AMDEraseSectorFinish

AMDEraseSectorSuccess:
    moveq   #0, %d0                    | Set success

AMDEraseSectorFinish:
    move.w  #AMD_READ_ARRAY_CMD, (%a0) | (0xF0F0) Return to Normal Read Array mode
    move.w  #AMD_READ_ARRAY_CMD, (%a0) | (0xF0F0) Return to Normal Read Array mode

    bsr.w   AMDFlashLock
    rts

| =============== S U B R O U T I N E =======================================
|
| AMD Flash Write Sector
| Tested on,
|   AM29F400BB (512k)
|   AM29F800BB (1m)
|   AM29BL802C (1m)
|   AM29BL162C (2m)
|
| Entry
|   a0 - Source Address
|   a1 - Destination Address
|   d0 - Byte count - must be divisible by 2
|
| Exit
|   d0 - Status 0=Ok, 1=Fail
|
| Uses
|   a2 - AMD Command Address A
|   a3 - AMD Command Address B
|   d1 - Scratch
|   d2 - Timeout index
|   d3 - Word to be written
|
AMDWriteSector:
    bsr.w   ResetWatchdog              | Scratch the dog

    moveq   #1, %d1                    | Set Unlock Mode 1 = Write
    bsr.w   AMDFlashUnlock             | d1 is 0=Erase, 1=Write

    lea     AMD_CMD_ADDRESS_A, %a2     | (0x0AAA) Pointer to the command address
    lea     AMD_CMD_ADDRESS_B, %a3     | (0x0554) Pointer to the command address

AMDWriteSectorLoop:
    move.w  #2346, %d2                 | FlashNotReady Loop Timeout index

    | We cannot write by bytes, we must write by word, So we make words out of bytes ...
    moveq   #0, %d3                    | Start clean
    move.b  (%a0)+, %d3                | First byte, increment source to next byte
    lsl.l   #8, %d3                    | Logical Shift Left
    move.b  (%a0)+, %d3                | Second byte to complete the Word, Increment source to next byte

    move.w  #AMD_CMD_UNLOCK_A, (%a2)   | (0xAAAA, 0x0AAA)
    move.w  #AMD_CMD_UNLOCK_B, (%a3)   | (0x5555, 0x0554)
    move.w  #AMD_PROGRAM_CMD, (%a2)    | (0xA0A0, 0x0AAA)

    move.w  %d3, (%a1)                 | Move word to destination ... Write it!

AMDWriteSectorNotReady:
    bsr.w   ResetWatchdog              | Scratch the dog
    move.w  (%a1), %d1                 | Read what was written
    cmp.w   %d3, %d1                   | Compare to what was written
    beq.s   AMDWriteSectorSuccess      | Jump for next byte if match
    dbeq    %d2, AMDWriteSectorNotReady | Retry reading back written value

    move.w  (%a1), %d1                 | One last chance
    cmp.w   %d1, %d3                   | Compare to whats written
    bne.s   AMDWriteSectorFail         | Branch fail if not equal

AMDWriteSectorSuccess:
    adda.w  #2, %a1                    | Increment address by 2 bytes (1 word)
    subq.w  #2, %d0                    | Decrement byte count by 2 (1 word)
    bne.s   AMDWriteSectorLoop         | Next word if not done

    move.w  #AMD_READ_ARRAY_CMD, (%a1) | 0xF0F0 Return to Normal Read Array mode
    move.w  #AMD_READ_ARRAY_CMD, (%a1) | 0xF0F0 Return to Normal Read Array mode

    moveq   #0, %d0                    | Set success
    bra.s   AMDWriteSectorFinal

AMDWriteSectorFail:
    moveq   #1, %d0                    | Set error

AMDWriteSectorFinal:
    bsr.w   AMDFlashLock
    rts

| =============== S U B R O U T I N E =======================================
|
| Intel Flash Unlock
| Tested on, 
|   AB28F400B (512k)
|   AB28F800B (1m)
|
| Uses
|   d1 - Scratch
|
IntelFlashUnlock:
#if defined P08
    move.w  #0x0F, (REG_FFF408).w      | Hardware Setup
    move.w  #0x05, (REG_FFF4C8).w      | Hardware Setup
#elif defined BlackBox
    move.b  #0xF8, (PORTFDIR).w        | Port F Direction
    move.b  #0x00, (PORTFASN).w        | Port F Assignment
#endif

    move.w  #0x0007, (SIM_CSBARBT).w   | $FFFFFA48
    move.w  #0x6820, (SIM_CSORBT).w    | $FFFFFA4A
    move.w  #0x0007, (SIM_CSBAR0).w    | $FFFFFA4C

    | Unlock
    move.w  #0x7060, (SIM_CSOR0).w     | $FFFFFA4E

    | Enable +12v Vpp
#if defined P04_256k
    bset    #VPP_BIT2, (REG_FFC002).w
    bset    #VPP_BIT4, (REG_FFC000).w
#elif defined P08
    bset    #VPP_BIT, (HARDWARE_IO).w  | Does not work on P01, needed for P08
#elif defined BlackBox
    bsr.w   WasteTime                  | Twiddle thumbs
    move.b  #0x18,(HARDWARE_IO).w      | Vortec Blackbox needs it done this way
#else
    move.w  (HARDWARE_IO).w, %d1       | Move Vpp Latch Address value to register
    bset    #VPP_BIT, %d1              | Set Vpp Latch Bit
    move.w  %d1, (HARDWARE_IO).w       | Move modified value back to Latch Address
#endif
    move.w  #32767, %d1                | 32768 loops
IntelFlashUnlockWait:                  | Allow Vpp ripple to settle
    bsr.w   ResetWatchdog              | Scratch the dog
    bsr.w   WasteTime                  | Twiddle thumbs
    dbf     %d1, IntelFlashUnlockWait  | If False Decrement and Branch
    nop                                | This nop is for P04 sizing/alignment
    rts

| =============== S U B R O U T I N E =======================================
|
| Intel Flash Lock
| Tested on,
|   AB28F400B (512k)
|   AB28F800B (1m)
|
| Uses
|   d1 - Scratch
|
IntelFlashLock:
    move.w  #0x0007, (SIM_CSBARBT).w   | $FFFFFA48
    move.w  #0x6820, (SIM_CSORBT).w    | $FFFFFA4A
    move.w  #0x0007, (SIM_CSBAR0).w    | $FFFFFA4C

    | Lock
    move.w  #0x1060, (SIM_CSOR0).w     | $FFFFFA4E

    | Disable +12v Vpp
#if defined P04_256k
    bclr    #VPP_BIT2, (REG_FFC002).w
    bclr    #VPP_BIT4, (REG_FFC000).w
#elif defined P08
    bclr    #VPP_BIT, (HARDWARE_IO).w  | Does not work on P01, needed for P08
#elif defined BlackBox
    move.b  #0x00,(HARDWARE_IO).w      | Vortec Blackbox needs it done this way
#else
    move.w  (HARDWARE_IO).w, %d1       | Move Vpp Latch Address value to register
    bclr    #VPP_BIT, %d1              | Set Vpp Latch Bit
    move.w  %d1, (HARDWARE_IO).w       | Move modified value back to Latch Address
#endif
    rts

| =============== S U B R O U T I N E =======================================
|
| Intel Flash Erase Sector by Address
| Tested on,
|   AB28F400B (512k)
|   AB28F800B (1m)
|
| Entry
|   a0 - Sector Address to Erase
|
| Exit
|   d0 - Status 0=Ok, 1=Fail
|
| Uses
|   d1 - Scratch
|   d2 - Loop Index
|
IntelEraseSector:
    bsr.w   IntelFlashUnlock

    move.w  #CLEAR_STATUS_REGISTER, (%a0) | 0x5050 Clear State Machine Status Register
    move.w  #INTEL_ERASE_CMD, (%a0)       | 0x2020 Set State Machine into Erase Mode
    move.w  #ERASE_RESUME_CONFIRM, (%a0)  | 0xD0D0 Confirm Command Erase - Start Erase
    move.w  #READ_STATUS_REGISTER, (%a0)  | 0x7070 Set State Machine into Read Status Mode, until commanded otherwise, see below

    move.l  #6553600, %d2              | Erase Loop Timeout index, run away prevention, leave big, allow each PCM to respond in it's own time

IntelEraseSectorNotReady:
    bsr.w   ResetWatchdog              | Scratch the dog
    move.w  (%a0), %d1                 | Move State Machine Status to register, it is still in Read Status Mode
    btst    #0x007, %d1                | Test Status - If bit 7 is set, it is Ready (done). 
    bne.s   IntelEraseSectorReady      | Status Success - Jump to IntelEraseSectorReady
    subq.l  #1, %d2                    | Decrement index
    bne.s   IntelEraseSectorNotReady   | Fail Status Check, Test again

    moveq   #2, %d0                    | Set Error Code 2 = Timeout error
    cmpi    #0, %d2                    | Did Timeout occure ?
    beq.s   IntelEraseSectorDone       | Yes, Done, Failure

IntelEraseSectorReady:
    moveq   #0, %d0                    | Set Success (if Status passes)
    andi.w  #0x0E8, %d1                | Mask 1110 1000
    cmp.w   #0x080, %d1                | Check status
    beq.s   IntelEraseSectorDone       | Pass ?, Done, Success
    moveq   #1, %d0                    | Set Failure

IntelEraseSectorDone:
    move.w    #READ_ARRAY_CMD, (%a0)   | 0xFFFF Take State Machine out of Read Status Mode
    move.w    #READ_ARRAY_CMD, (%a0)   | 0xFFFF Return to Normal Read Array mode

    bsr.w   IntelFlashLock
    rts

| =============== S U B R O U T I N E =======================================
|
| Intel Flash Write Sector
| Tested on,
|   AB28F400B (512k)
|   AB28F800B (1m)
|
| Entry
|   a0 - Source Address
|   a1 - Destination Address
|   d0 - Byte count - must be divisible by 2
|
| Exit
|   d0 - Status 0=Ok, 1=Fail
|
| Uses
|   d2 - Timeout index
|   d3 - Scratch
|
IntelWriteSector:
    bsr.w   IntelFlashUnlock

IntelWriteSectorLoop:
    move.w  #2346, %d2                 | FlashNotReady Loop Timeout index

    | We cannot write by bytes, we must write by word, So we make words out of bytes ...
    moveq   #0, %d3                    | Start clean
    move.b  (%a0)+, %d3                | First byte, and increment a0 by 1 byte
    lsl.l   #8, %d3                    | Logical Shift Left (shift byte left into upper word)
    move.b  (%a0)+, %d3                | Second byte to complete the Word, and increment register by 1 byte

    move.w  #CLEAR_STATUS_REGISTER, (%a1) | 0x5050 Clear Status register
    move.w  #INTEL_PROGRAM_CMD,(%a1)      | 0x4040 Command Program
    move.w  %d3, (%a1)                    | Move (Write) word to Flash
    move.w  #READ_STATUS_REGISTER, (%a1)+ | 0x7070 Read Status Register, and increment register by word (2 bytes)

IntelWriteSectorNotReady:
    bsr.w   ResetWatchdog              | Scratch the dog
    move.w  (%a1), %d3                 | Move Status to register
    btst    #0x007, %d3                | Test Status
    dbne    %d2, IntelWriteSectorNotReady | Status Not Ready, Retest

    move.w  #READ_ARRAY_CMD, (%a1)     | 0xFFFF Return to Normal Read Array mode
    move.w  #READ_ARRAY_CMD, (%a1)     | 0xFFFF Return to Normal Read Array mode

    andi.w  #0x098, %d3                | Mask 1001 1000
    cmp.w   #0x080, %d3                | Check status
    bne.s   IntelWriteSectorFail       | Fail Jump

    subq.w  #2, %d0                    | Decrement byte count by two (1 word)
    bne.s   IntelWriteSectorLoop       | Next word if not done

    moveq   #0, %d0                    | Set Success
    bra.s   IntelWriteSectorFinal      | All Done

IntelWriteSectorFail:
    moveq   #1, %d0                    | Set Failure

IntelWriteSectorFinal:
    bsr.w   IntelFlashLock
    rts

| =============== S U B R O U T I N E =======================================
ProcessMode34:
| TODO: Add rejections
    movea.l #Mode34Reply, %a0          | Pointer to Mode34Reply buffer
    move.w  #6, %d0                    | It's 6 bytes long
    bsr.w   VPWSend                    | Send it
    jmp     MainLoop

| =============== S U B R O U T I N E =======================================
|
| Process Mode35 request to send data
|
| Uses
|   MessageBuffer
|   a0 - Data start address
|   d0 - Data byte count (length)
|
ProcessMode35:
    moveq   #0, %d0                    | Start Clean
    or.b    MessageBuffer + 7, %d0     | First address byte
    lsl.l   #8, %d0                    | Logical Shift Left
    or.b    MessageBuffer + 8, %d0     | Second address byte
    lsl.l   #8, %d0                    | Logical Shift Left
    or.b    MessageBuffer + 9, %d0     | Third address byte
    movea.l %d0, %a0                   | Pointer to start address

    moveq   #0, %d0                    | Start Clean
    or.b    MessageBuffer + 5, %d0     | First length byte
    lsl.l   #8, %d0                    | Logical Shift Left
    or.b    MessageBuffer + 6, %d0     | Second length byte

    bsr.w   VPWSendBlock               | Send it
    jmp     MainLoop                   | Back to MainLoop
 
| =============== S U B R O U T I N E =======================================
| The packet is in the buffer when this function is called
| This function loads the target address, length, and checksum to registers
| Then it calculates the checksum of the received data and validates the block
| If OK it proceeds to copy the data, then it responds with success or failure
| 
| Uses
|   a0
|   a1
|   a2
|   a3
|   d0
|   d2
|   d3
|   d4
|
ProcessMode36:
    moveq   #0, %d0                    | Clear for Target Address
    move.b  MessageBuffer + 7, %d0     | Move First address byte to register
    lsl.l   #8, %d0                    | Shift byte left
    move.b  MessageBuffer + 8, %d0     | Move Second address byte to register
    lsl.l   #8, %d0                    | Shift bytes left
    move.b  MessageBuffer + 9, %d0     | Move Third address byte to register
    movea.l %d0, %a1                   | Pointer to target address
    movea.l %d0, %a2                   | Pointer to target address for execute jump

    moveq   #0, %d0                    | Start Clean
    move.b  MessageBuffer + 5, %d0     | Move First length byte to register
    lsl.w   #8, %d0                    | Shift byte left
    move.b  MessageBuffer + 6, %d0     | Move Second length byte to register

    lea     MessageBuffer + 10, %a0    | Pointer to start of data

| Process Mode36 Validate Sum          | a3 = buffer address, d2 = temp, d3 = counter, d4=sum
    moveq   #0, %d2                    | Init temporary register
    movea.l #MessageBuffer + 4, %a3    | Pointer to beginning of checksum range, Calculates from MessageBuffer[4] (0=6C 1=10 2=F0 3=36 4=<start>)
    moveq   #0, %d4                    | Init sum
    move.w  %d0, %d3                   | Payload length (Byte count)
    addq.w  #5, %d3                    | Add submode (1), address (3), length(2) = 6 bytes to sum length minus 1 for Zero Index

ProcessMode36NextSum:
    move.b  (%a3)+, %d2                | Move byte to register
    add.l   %d2, %d4                   | Add to sum
    dbf     %d3, ProcessMode36NextSum  | Loop until sum calculated

    moveq   #0, %d3                    | Clear for Payload sum
    move.b  (%a3)+, %d3                | First byte
    lsl.w   #8, %d3                    | Logical Shift Left
    move.b  (%a3)+, %d3                | Second byte
    cmp.w   %d3, %d4                   | Is the sum OK?
    beq.s   ProcessMode36Data          | Yes, Do it 

| Process Mode36 Response Fail
    move.b  #0x7F, Mode36Reply + 3     | 7F Failure
    move.b  MessageBuffer + 3, Mode36Reply + 4 | Mode
    move.b  MessageBuffer + 4, Mode36Reply + 5 | Submode
    movea.l #Mode36Reply, %a0          | Pointer to Mode36Reply buffer
    move.w  #6, %d0                    | Message is 6 bytes long
    bsr.w   VPWSend                    | Send it
    jmp     MainLoop                   | Return to main loop

ProcessMode36Data:
    | a0 - Source Address
    | a1 - Destination Address
    | d0 - Byte Count
    cmpi.b  #0x44, MessageBuffer + 4   | Test Write (0x44) ??
    beq.s   ProcessMode36ResponseOK    | Yes, branch to ProcessMode36ResponseOK
    bsr.w   ProcessWriteSector         | Selects Flash chip, writes received block of data

ProcessMode36ResponseOK:
    move.b  MessageBuffer + 4, %d4     | Move submode to register
    move.b  %d4, Mode36Reply + 4       | Move submode to response, and keep it for the execute test
    move.b  %d0, Mode36Reply + 5       | Status code 0=Ok, 1=Fail
    movea.l #Mode36Reply, %a0          | Pointer to Mode36Reply buffer
    move.w  #6, %d0                    | Message is 6 bytes long
    bsr.w   VPWSend                    | Send it
    andi.b  #0x80, %d4                 | Execute flag is bit 7 of submode
    cmpi.b  #0x80, %d4                 | Test for Execute
    bne.w   MainLoop                   | If not executing return to MainLoop
    jmp     (%a2)                      | Execute the payload

| =============== S U B R O U T I N E =======================================
WaitForTXFIFO:
    bsr.w   ResetWatchdog
    move.b  (J1850_Status).l, %d2      | Move status to register
    andi.b  #3, %d2                    | Mask the TMFS (Transmit FIFO Status) bits
    cmpi.b  #3, %d2                    | 3 = buffer full
    beq.s   WaitForTXFIFO              | Wait for the TX buffer to be non-full
    move.b  #4, (J1850_Command).l      | Load as transmit data to BTAD
    rts

| =============== S U B R O U T I N E =======================================
VPWSend:
    move.b  #0x14, (J1850_Command).l   | BTAD Byte type and destination field to 101 (000 101 00)
                                       | 101 = Load as first byte of transmit data
    subq.w  #2, %d0                    | Zero Index data length and last byte not counted

VPWSendNextByte:
    bsr.w   ResetWatchdog              | Scratch the dog
    move.b  (%a0)+, (J1850_TX_FIFO).l  | Write byte to TX FIFO
    bsr.w   WaitForTXFIFO              | Wait for space in TX FIFO
    dbf     %d0, VPWSendNextByte       | If False Decrement and Branch

    move.b  #0x0C, (J1850_Command).l   | 011 to BTAD - Load as last byte of transmit data
    move.b  (%a0)+, (J1850_TX_FIFO).l  | Drop the last byte in TX FIFO
    bsr.w   WasteTime                  | Twiddle thumbs
    move.b  #0x03, (J1850_Command).l   | Flush buffer
    move.b  #0x00, (J1850_TX_FIFO).l   | Completion byte

VPWSendWaitForFlush:
    bsr.w   ResetWatchdog              | Scratch the dog
    move.b  (J1850_Status).l, %d0      | Get status byte
    andi.b  #0xE0, %d0                 | Mask RFS 1110 0000
    cmpi.b  #0xE0, %d0                 | Empty except for completion byte status
    bne.s   VPWSendWaitForFlush        | Loop until true
    move.b  (J1850_RX_FIFO).l, %d0     | Read FIFO
    rts

| =============== S U B R O U T I N E =======================================
VPWReceive:
    movea.l #MessageBuffer, %a0        | Pointer to MessageBuffer buffer

VPWReceiveReadFrame:
    bsr.w   ResetWatchdog              | Scratch the dog
    move.b  (J1850_Status).l, %d0      | Get status byte
    andi.b  #0xE0, %d0                 | Mask RFS register 1110 0000
    tst.b   %d0                        | 000x xxxx = no data (or invalid)
    beq.s   VPWReceiveReadFrame        | Nothing, try again

    cmpi.b  #0x80, %d0                 | 100x xxxx = All codes *greater* than this mean completion code at buffer head
    bgt.s   VPWReceiveReadComplete     | Done, cleanup

| VPW Receive Read Byte                | Anything else, read a byte
    move.b  (J1850_RX_FIFO).l, (%a0)+  | Read from RX FIFO to input buffer, and move pointer to next byte
    bra.s   VPWReceiveReadFrame        | Next try

VPWReceiveReadComplete:
    move.b  (J1850_RX_FIFO).l, (%a0)+  | The completion code, so eat it
    movea.l #MessageBuffer, %a0        | Pointer to MessageBuffer buffer
    move.b  (%a0), %d0                 |
    andi.b  #0xFE, %d0                 | Mask 1111 1110
    cmpi.b  #0x6C, %d0                 | Priority 6C or 6D packet?
    bne.s   VPWReceiveReadFrame        | If not, abort and get next packet
    cmpi.b  #pcmid, 1(%a0)             | Check for device 0x10
    beq.s   VPWReceiveRTS              | If yes, return for processing
    cmpi.b  #0xFE, 1(%a0)              | Check for broadcast device id 0xFE
    bne.s   VPWReceiveReadFrame        | Not an FE, junk it, get next packet

VPWReceiveRTS:
    rts                                | Must have been an FE, return for processing

| =============== S U B R O U T I N E =======================================
|
| Sends requested number of bytes from supplied address
|
| Entry
|   a0 - Source Start Address
|   d0 - Number of bytes requested
|
| Uses
|   Mode35Reply
|   a1 - Mode35Reply header address
|   d1 - Scratch, Checksum
|   d2 - Scratch
|
VPWSendBlock:
    move.l  %a0, %d1                   | Move the requested start address to register
    move.b  %d1, Mode35Reply + 9       | Move address byte 3 to Mode35Reply buffer
    lsr.l   #8, %d1                    | Logical Shift Right 8 bits
    move.b  %d1, Mode35Reply + 8       | Move address byte 2 to Mode35Reply buffer
    lsr.l   #8, %d1                    | Logical Shift Right 8 bits
    move.b  %d1, Mode35Reply + 7       | Move address byte 1 to Mode35Reply buffer

    move.w  %d0, %d1                   | Move the requested length to register
    move.b  %d1, Mode35Reply + 6       | Move length byte 2 to Mode35Reply buffer
    lsr.w   #8, %d1                    | Logical Shift Right
    move.b  %d1, Mode35Reply + 5       | Move length byte 1 to Mode35Reply buffer

    subq.w  #1, %d0                    | Zero Index data length
    move.w  #9, %d1                    | Mode35Reply Header is 10 bytes long minus 1 for Zero index

    move.b  #0x14, (J1850_Command).l   | 0x14 to BTAD = Load as first byte of transmit data
    movea.l #Mode35Reply, %a1          | Pointer to Mode35Reply buffer

VPWSendBlockMode35ReplyHeader:
    bsr.w   ResetWatchdog              | Scratch the dog
    move.b  (%a1)+, (J1850_TX_FIFO).l  | Move Byte from Source to Destination
    dbf     %d1, VPWSendBlockMode35ReplyHeader | Send Mode35 Reply Header 10 bytes

    moveq   #0, %d1                    | Clear for checksum
    moveq   #0, %d2                    | Clear checksum scratch pad
    move.b  Mode35Reply + 4, %d2       | Get Submode byte
    add.l   %d2, %d1                   | Add Submode byte to checksum
    move.b  Mode35Reply + 5, %d2       | Get Length byte 1
    add.l   %d2, %d1                   | Add Length byte 1 to checksum
    move.b  Mode35Reply + 6, %d2       | Get Length byte 2
    add.l   %d2, %d1                   | Add Length byte 2 to checksum
    move.b  Mode35Reply + 7, %d2       | Get Address byte 1
    add.l   %d2, %d1                   | Add Address byte 1 to checksum
    move.b  Mode35Reply + 8, %d2       | Get Address byte 2
    add.l   %d2, %d1                   | Add Address byte 2 to checksum
    move.b  Mode35Reply + 9, %d2       | Get Address byte 3
    add.l   %d2, %d1                   | Add Address byte 3 to checksum
                                       | d1 contains the checksum of the packet so far
VPWSendBlockTryNextByte:
    bsr.w   WaitForTXFIFO              | Wait for space in the TX FIFO
    move.b  (%a0), (J1850_TX_FIFO).l   | Move a byte on to the TX FIFO
    move.b  (%a0)+, %d2                | Get same byte for checksum, increment Source pointer
    add.l   %d2, %d1                   | Add byte to checksum
    dbf     %d0, VPWSendBlockTryNextByte | Repeat loop N times sending requested data
    move.l  %d1, %d0                   | Move the checksum to register
    lsr.l   #8, %d0                    | Shift the checksum right by 8 bits

VPWSendBlockWaitForBuffer:
    bsr.w   ResetWatchdog              | Scratch the dog
    move.b  (J1850_Status).l, %d2      | Get Status
    andi.b  #3, %d2                    | Mask TMFS Transmit FIFO Status
    cmpi.b  #3, %d2                    | 3 = Buffer full
    beq.s   VPWSendBlockWaitForBuffer  | Wait for room in the TX FIFO
    move.b  #4, (J1850_Command).l      | Load as transmit data
    move.b  %d0, (J1850_TX_FIFO).l     | Move the first checksum byte to the TX FIFO

VPWSendBlockWaitForBuffer2:
    bsr.w   ResetWatchdog              | Scratch the dog
    move.b  (J1850_Status).l, %d2      | Get Status
    andi.b  #3, %d2                    | Mask TMFS
    cmpi.b  #3, %d2                    | 3=Buffer full
    beq.s   VPWSendBlockWaitForBuffer2 | Wait for TX buffer to have room
    move.b  #0xC, (J1850_Command).l    | BTAD ...0 11.. "Load as last byte of transmit data"
    move.b  %d1, (J1850_TX_FIFO).l     | Move the Second checksum byte  to the TX FIFO
    rts

| ---------------------------------------------------------------------------

.data
.ascii "(c)2024 pcmhacking.net" | An Antus / Gampy collaboration

| All AA bytes are padding for alignment

| Mode 35 Reply Header 10 bytes
Mode35Reply:    .byte  0x6D, toolid, pcmid, 0x36, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00

| Flash ID 9 bytes + 1 alignment
FlashIDReply:   .byte  0x6C, toolid, pcmid, 0x7D, 0x01, 0x00, 0x00, 0x00, 0x00, 0xAA

| Os ID Reply, 9 bytes + 1 alignment
OsIDReply:      .byte  0x6C, toolid, pcmid, 0x7D, 0x03, 0x00, 0x00, 0x00, 0x00, 0xAA

| Kernel ID Response, 9 bytes + 1 alignment
| 4 bytes, the first 12 bits are a static ID, next 12 bits are version, 4th byte = PCM ID
| P01/P59 = 01
| P04     = 04
| P08     = 08
| P10     = 0A
| P12     = 0C
| E54     = 36
KernelIDReply:  .byte  0x6C, toolid, pcmid, 0x7D, 0x00, 0x82, 0x40, 0x02, 0x00, 0xAA

| Mode34 Reply, 6 bytes - 6C F0 10 74 00 44, Default Success
Mode34Reply:    .byte  0x6C, toolid, pcmid, 0x74, 0x00, 0x44

| Mode36 Reply, 6 bytes - 6C F0 10 76 00 00
Mode36Reply:    .byte  0x6C, toolid, pcmid, 0x76, 0x00, 0x00

| Halt Kernel Reply, 4 bytes
Mode60Reply:    .byte  0x6C, toolid, pcmid, 0x60

| Erase Sector Reply, 7 bytes + 1 alignment
EraseSectorReply:   .byte  0x6C, toolid, pcmid, 0x7D, 0x05, 0x00, 0x00, 0xAA

| Global buffer, it's at the end and it's not transported, thus length is irrelevant!
  .globl   MessageBuffer
  .section .kerneldata, "aw", @progbits | Kernel data section, how it's excluded from transportation.
  .type    MessageBuffer, @object
  .size    MessageBuffer, 4128          | 4128 bytes is the J2534 J1850 spec, however size doesn't matter.
MessageBuffer:
.end


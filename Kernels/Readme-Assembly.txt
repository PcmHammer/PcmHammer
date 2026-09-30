Assembly Kernels for J1850 VPW PCMs (Motorola 68k) and GMLAN CAN PCMs (PowerPC).

Supported PCMs.

VPW (Motorola 68k):
P01
P04
P04_Early
P05 (VPW Variant)
P05b
P08
P10
P11
P12 (1m and 2m)
P59
E54
BlackBox (4 connector 1998-2002)

CAN (PowerPC):
E38               (MPC5xx-class,   -cpu505)
E39 / E39a        (MPC5566 e200z6, -cpu8540)
E92               (e200 Book E,    -cpu8540)

Note: the PowerPC kernels currently remain closed source and are shipped as blobs.
Source may be released in the future.

How to build the Assembly Kernels

To build an Assembly Kernel.
Build.cmd -x -aFF8000 -pP01
Will build the P01 Kernel for loading at address FF8000 and not copy it anywhere, Clean.cmd will remove it ...
The dash x tells the build system to build the assembly version.

To build a Loader and Kernel
Build.cmd -x -aFF9090 -lFF9890 -pP04
Will build the P04 Loader and Kernel.

If you want Build.cmd to copy the Kernel someplace
Build.cmd -x -c -tC:\Directory\Where\You\Want\It -aFF8000 -pP01

The PowerPC CAN kernels also need -cpu, and their source lives in PPC-CAN-Asm-<PCM>:
Build.cmd -x -a3FC434   -pE38 -cpu505
Build.cmd -x -a40007004 -pE39 -cpu8540
Build.cmd -x -a40007004 -pE92 -cpu8540

See Build.cmd -h for help and or other options ...

Load addresses
    -aFF8000 -pP01 (Includes P59)
    -aFF8000 -lFF9890 -pP04
    -aFF8000 -lFF9890 -pP04_Early
    -aFFC100 -pP05 (Includes P05b)
    -aFFABE0 -pP08
    -aFFB800 -pP10
    -aFFC100 -pP11
    -aFF2000 -pP12
    -aFF9100 -pE54
    -aFFC300 -pBlackBox
    -a3FC434   -pE38 -cpu505    (CAN, PowerPC)
    -a40007004 -pE39 -cpu8540   (CAN, PowerPC; serves E39 / E39a)
    -a40007004 -pE92 -cpu8540   (CAN, PowerPC)

Assembly kernel filelist
Kernel.S              The Kernel
Kernel.ld             Linker Script specific to the Assembly Kernel
Loader.S              The Loader
Loader.ld             Linker Script specific to the Assembly Loader
Common-Assembly.h     Common elements
Readme-Assembly.txt   Readme

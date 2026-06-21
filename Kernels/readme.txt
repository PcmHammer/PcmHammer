To build the Kernels you'll need the gcc-m68k toolchain for Windows available here: http://gnutoolchains.com/m68k-elf/

The gcc-m68k toolchain needs to be installed to the installation default location of C:\SysGCC\m68k-elf or you need to
point to the location of m68k tools with the -g command line parameter (See: Build.cmd -h), allowing it to add itself to
the PATH is your choice, Build.cmd does not require it.

The E38 PPC Kernel is built with the gcc powerpc-eabi toolchain (also from http://gnutoolchains.com/). It installs as a
sibling of the m68k tools at C:\SysGCC\powerpc-eabi. Build.cmd derives the powerpc bin directory from -g by swapping
m68k-elf for powerpc-eabi, so the same -g points at both toolchains.

Build.cmd encapsulates the options used to build the binaries on Windows.

See Build.cmd -h or BuildAll.cmd -h for help.
Typical single Kernel usage: Build.cmd -aFF8000 -pP01 -cpu68332

-cpu<value> is required, there is no default. Use -cpu68332 for the 68k VPW PCMs and -cpu505 for the E38 PowerPC CAN
kernel. The CPU selects the cross toolchain and assembler flags.

Build.cmd <- .cmd is important!

BuildAll.cmd wraps Build.cmd in a loop building all supported Kernels, all options in Build.cmd are available in BuildAll.cmd.

To build all supported Kernels simple run: BuildAll.cmd

BuildAll.cmd discovers a BuildKernel.cmd in each kernel source directory; each one owns the PCM list, base addresses and
-cpu value for its directory and calls Build.cmd once per kernel.

--

The 68k CPU Kernels can also be built on Unix/Linux using the gcc-m68k toolchain that can be built on most Unix/Linux
systems using: https://github.com/haarer/toolchain68k.

There are pre-built binaries for some distros available as gcc-m68k-linux-gnu. The E38 PowerPC kernel uses the
gcc-powerpc-linux-gnu toolchain.

If you do not use the default install location, PREFIX can be used to point to the location used.

You will need to move Kernel-*.bin and Loader-*.bin from the build directory to the PcmHammer directory.

The shipped kernels are the assembly implementation, built with makefile-assembly. Set srcdir to the kernel's source
directory (68k-VPW-Asm for most PCMs, 68k-VPW-Asm-P04 for P04, PPC-CAN-Asm-E38 for E38) and name=Loader to build a
loader. The default PREFIX is /opt/crosschain/bin/m68k-elf-; override PREFIX to match your toolchain location (the
examples below use the gcc-*-linux-gnu locations). The 68k kernels use the default ARCHFLAGS (-mcpu=68332); the E38
overrides ARCHFLAGS for the PowerPC core.

A C implementation also exists and is built with makefile (e.g. make pcm=P01 address=FF8000); it is not currently
shipped.

$ cd Kernels

$ make -f makefile-assembly PREFIX=/usr/bin/m68k-linux-gnu- pcm=P01 address=FF8000 srcdir=68k-VPW-Asm
$ make -f makefile-assembly pcm=P01 srcdir=68k-VPW-Asm clean

$ make -f makefile-assembly PREFIX=/usr/bin/m68k-linux-gnu- pcm=P04 address=FF8000 srcdir=68k-VPW-Asm-P04
$ make -f makefile-assembly PREFIX=/usr/bin/m68k-linux-gnu- pcm=P04 address=FF9890 name=Loader srcdir=68k-VPW-Asm-P04
$ make -f makefile-assembly pcm=P04 srcdir=68k-VPW-Asm-P04 clean

$ make -f makefile-assembly PREFIX=/usr/bin/m68k-linux-gnu- pcm=P04_Early address=FF8000 srcdir=68k-VPW-Asm
$ make -f makefile-assembly pcm=P04_Early srcdir=68k-VPW-Asm clean

$ make -f makefile-assembly PREFIX=/usr/bin/m68k-linux-gnu- pcm=P05 address=FFC100 srcdir=68k-VPW-Asm
$ make -f makefile-assembly pcm=P05 srcdir=68k-VPW-Asm clean

$ make -f makefile-assembly PREFIX=/usr/bin/m68k-linux-gnu- pcm=P08 address=FFABE0 srcdir=68k-VPW-Asm
$ make -f makefile-assembly pcm=P08 srcdir=68k-VPW-Asm clean

$ make -f makefile-assembly PREFIX=/usr/bin/m68k-linux-gnu- pcm=P10 address=FFB800 srcdir=68k-VPW-Asm
$ make -f makefile-assembly pcm=P10 srcdir=68k-VPW-Asm clean

$ make -f makefile-assembly PREFIX=/usr/bin/m68k-linux-gnu- pcm=P11 address=FFC000 srcdir=68k-VPW-Asm
$ make -f makefile-assembly pcm=P11 srcdir=68k-VPW-Asm clean

$ make -f makefile-assembly PREFIX=/usr/bin/m68k-linux-gnu- pcm=P12 address=FF2000 srcdir=68k-VPW-Asm
$ make -f makefile-assembly pcm=P12 srcdir=68k-VPW-Asm clean

$ make -f makefile-assembly PREFIX=/usr/bin/m68k-linux-gnu- pcm=E54 address=FF9100 srcdir=68k-VPW-Asm
$ make -f makefile-assembly pcm=E54 srcdir=68k-VPW-Asm clean

$ make -f makefile-assembly PREFIX=/usr/bin/m68k-linux-gnu- pcm=BlackBox address=FFC300 srcdir=68k-VPW-Asm
$ make -f makefile-assembly pcm=BlackBox srcdir=68k-VPW-Asm clean

The E38 PPC Kernel may be distributed binary only. When its source (PPC-CAN-Asm-E38) is not in the tree, the build ships
the committed Kernel-E38.bin as-is; when the source is present it is built from source as below. The link address is
3FC434, the run address (load address 3FC430 + 4): the boot loader lands the image at loadAddress+4, so linking there
keeps absolute references correct.

$ make -f makefile-assembly PREFIX=/usr/bin/powerpc-linux-gnu- pcm=E38 address=3FC434 srcdir=PPC-CAN-Asm-E38 ARCHFLAGS="-mregnames -mcpu=505 -mbig-endian -mstrict-align"
$ make -f makefile-assembly pcm=E38 srcdir=PPC-CAN-Asm-E38 clean

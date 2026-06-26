@setlocal
@echo off

goto beginning
* Create a non parsed area for header, notes and routines (call :label).
**********************************************************************
*
* Name         : Build.cmd
* Description  : Build PcmHammer's kernel with options, most specifically the kernel base address.
* Author       : Gampy <pcmhacking.net>
* Authored Date: 2026-11-16
* Revision Date: 2026-05-19 - Gampy <pcmhacking.net> Cleanup for publication.
* Revision Date: 2026-04-01 - Gampy <pcmhacking.net> Added -t<PCM Type>, added -r dump kernel RAM map.
* Revision Date: 2026-04-14 - Gampy <pcmhacking.net> Fixed ld map dump.
* Revision Date: 2026-04-03 - Gampy <pcmhacking.net> Merged P04, swapped -p & -t, removed -r, reworked
*                                                    for ease of adding new kernels, see NOTES:.
* Revision Date: 2023-03-25 - Gampy <pcmhacking.net> Added Assembly Kernel and Kernel Loader.
*                                                    Added -l Kernel Loader Address.
*                                                    Added -x Build Assembly Kernel and or Loader.
* Revision Date: 2026-05-30 - Antus <pcmhacking.net> Generate an epoch to embed in the kernel as version/build time stamp.
* Revision Date: 2026-06-01 - Antus <pcmhacking.net> Restructure: VPW-C, VPW-Asm, VPW-P04 subdirs; build/ for outputs.
* Revision Date: 2026-06-01 - Antus <pcmhacking.net> Verify .bin files: sha1, timestamp >= build start, OK/ERROR + exit.

*
* Authors disclaimer
*   It is what it is, you can do with it as you please. (with respect)
*
*   Just don't blame me if it teaches your computer to smoke!
*
*   -Enjoy
*
*
* NOTES:
* To add a new C Kernel,
*   1. Create the entry point C file named as "Kernel-<PCM>.c" in 68k-VPW-C\.
*      It must contain at least, the following:
*        int __attribute__((section(".kernelstart")))
*        KernelStart(void)
*        {
*          // Create main loop here.
*        }
*   2. Create a plain text "CFiles-<PCM>.list" in 68k-VPW-C\ for ancillary C files.
*      One dot c filename per line, no spaces, do not include Kernel-<PCM>.c.
*      Must contain at least main.c.
*   3. Add the new PCM to BuildAll.cmd.
*   4. Add the new PCM to .github/workflows/CheckBuild.yml (See note in CheckBuild.yml).
*
* To add a new Assembly Kernel (non-P04),
*   1. Add a new -p<PCM> entry in 68k-VPW-Asm\Kernel.S using an appropriate #ifdef block.
*   2. Add the new PCM to BuildAll.cmd.
*   3. Add the new PCM to .github/workflows/CheckBuild.yml.
*
* Source directories:
*   68k-VPW-C\       - C kernel source files (Kernel-P01.c, Kernel-P10.c, Kernel-P12.c, main.c, etc.)
*   68k-VPW-Asm\     - Assembly kernel source for all PCMs except P04 (Kernel.S, Loader.S, Common-Assembly.h)
*   68k-VPW-Asm-P04\ - P04-specific assembly kernel (Kernel.S from crowbar, plus Loader.S, Kernel.ld, Loader.ld)
*   build\           - All binary artifacts (.bin, .elf, .map, .disassembly)
*
**********************************************************************
* Here we'll collect the routines (call :label / goto label).
*
**
* Help message
**
:Usage
  echo.
  echo   %0 -a^<address^> -c -d -g^<path^> -m -p^<pcm^> -t^<path^> -x
  echo.
  echo     -a^<address^>
  echo       Set base address for the kernel. (no space, in hex, no 0x)
  echo       Value: 0x%BASE_ADDRESS%
  echo.
  echo     -c
  echo       Set flag to copy Kernel-^<PCM^>.bin to PcmHammer's build directory or not.
  if defined COPY_BIN (
    echo       Value: True
  ) else (
    echo       Value: False
  )
  echo.
  echo     -d
  echo       Set flag to dump Kernel-^<PCM^>.elf ^> Kernel-^<PCM^>.disassembly or not.
  if defined DUMP_ELF (
    echo       Value: True
  ) else (
    echo       Value: False
  )
  echo.
  echo     -g^<path^>
  echo       Set path to GNU m68k bin directory. (no space)
  echo       Value: %GCC_LOCATION%
  echo.
  echo     -cpu^<value^>
  echo       Set the target CPU, given as the gcc -mcpu value (no space). Required, no default.
  echo       68332 = Motorola 68k (m68k-elf, all VPW PCMs); 505 = PowerPC MPC5xx (powerpc-eabi, E38 CAN).
  echo       Value: %CPU%
  echo.
  echo     -l^<address^>
  echo       Set base address for the Loader. (no space, in hex, no 0x)
  echo       Value: 0x%LOADER_ADDRESS%
  echo.
  echo     -m
  echo       Set flag to dump Kernel-^<PCM^>.map or not.
  if defined DUMP_MAP (
    echo       Value: True
  ) else (
    echo       Value: False
  )
  echo.
  echo     -p^<pcm^>
  echo       Set the PCM (P01 (P01 includes P59), P04_Early, P04, P05, P08, P10, P11, P12, E54, BlackBox, Micro, Read, Test). (no space)
  echo       Value: %PCM%
  echo.
  echo     -t^<path^>
  echo       Set target ^<path^> where to copy Kernel-^<PCM^>.bin. (no space)
  echo       Value: %BIN_LOCATION%
  echo.
  echo     -x
  echo       Set flag to build an Assembly Kernel (and or Loader) versus C Kernel.
  if defined ASSEMBLY_KERNEL (
    echo       Value: True
  ) else (
    echo       Value: False
  )
  echo.
  echo     /h
  echo     -h
  echo     --help
  echo       Display the Help Menu
  echo       TIP: Following your arguments with the help argument (as the last argument), it will show your values in help.
  echo.
  echo.
  echo     Examples:
  echo     %0
  echo     %0 -a%BASE_ADDRESS% -p%PCM%
  echo     %0 -a%BASE_ADDRESS% -p%PCM% -g%GCC_LOCATION% -t%BIN_LOCATION%
  echo.
  echo     P01 is default, thus all others require at least example 2
  echo.
  goto :EOF
*
*
**
* Removes trailing slash if one exists.
**
:Detrailslash in out
  set A=%~1
  if %A:~-1%==\ (
    set %2=%A:~0,-1%
  ) else (
    set %2=%A%
  )
  goto :EOF
*
*
**
* Verifies a freshly built .bin: must exist and have mtime >= BUILD_EPOCH.
* Prints:  <file>  sha1:<hash>  OK    or    ERROR: <reason> and returns exit code 1.
* Caller is responsible for (popd ^& exit /b 1) if called inside a pushd block.
**
:VerifyBin
set "_VF=%~1"
if not exist "%_VF%" (
  echo ERROR: %_VF% was not created.
  set "_VF="
  exit /b 1
)
powershell -NoProfile -Command "& { $f = Get-Item '%_VF%'; $h = (Get-FileHash '%_VF%' -Algorithm SHA1).Hash.ToLower(); $t = [long][math]::Floor(($f.LastWriteTimeUtc - [DateTime]::new(1970,1,1,0,0,0,[System.DateTimeKind]::Utc)).TotalSeconds); if ($t -ge %BUILD_EPOCH%) { Write-Host ('%_VF%  sha1:' + $h + '  OK') } else { Write-Host ('ERROR: %_VF% is stale (mtime=' + $t + ' < start=%BUILD_EPOCH%)'); exit 1 } }"
if %errorlevel% neq 0 (set "_VF=" & exit /b 1)
set "_VF="
goto :EOF
*
*
*************************************** Beginning
* Let us get to it!
:beginning

rem * Set option defaults here ...

rem * -a Set default base address for the kernel.
set BASE_ADDRESS=FF8000

rem * -c Set default to copy Kernel-<PCM>.bin to PcmHammer's build directory.
set COPY_BIN=True

rem * -d Set default to not dump Kernel-<PCM>.elf > Kernel-<PCM>.disassembly
set DUMP_ELF=

rem * -g Set default path to m68k bin directory.
set GCC_LOCATION=C:\SysGCC\m68k-elf\bin\

rem * -l Set to build the Loader kernel (Non defined is default, do not modify).
set LOADER_ADDRESS=

rem * -m Set default to not dump memory map Kernel-<PCM>.map.
set DUMP_MAP=

rem * -p Set default PCM.
set PCM=P01

rem * -t Set default target <path> where to copy Kernel-<PCM>.bin.
set BIN_LOCATION=..\Apps\UI\WindowsForms\PcmHammer\bin\Debug\

rem * -x Set flag to build an Assembly Kernel (and or Loader) versus C Kernel.
set ASSEMBLY_KERNEL=

rem * -cpu Target CPU, given as the actual gcc -mcpu value so the same value is used
rem *      end to end (command line -> -mcpu flag). Required: there is no default, so
rem *      every build states its CPU explicitly. The CPU selects the cross toolchain,
rem *      assembler flags and (for non-68k) source dir, in the CPU block below.
rem *      Supported CPUs and the PCMs that use them:
rem *        68332 - Motorola 68k (MC68332), m68k-elf toolchain.
rem *                P01, P04, P04_Early, P05, P08, P10, P11, P12, E54, BlackBox.
rem *        505   - PowerPC. This is the gcc -mcpu name for the classic 32-bit
rem *                PowerPC 5xx core family, NOT a chip part number; gcc has no
rem *                555/561/565 option, so 505 is what targets the E38's MPC56x.
rem *                powerpc-eabi toolchain. Used by: E38 (CAN).
set CPU=


rem * Handle command line options.
(
  setlocal enabledelayedexpansion
  for %%A in (%*) do (
    set VAR=%%A
    if /i "!VAR:~0,2!" == "-a" set "BASE_ADDRESS=!VAR:~2!"
    if /i "!VAR!" == "-c"      set COPY_BIN=
    if /i "!VAR!" == "-d"      set DUMP_ELF=True
    if /i "!VAR:~0,2!" == "-g" set "GCC_LOCATION=!VAR:~2!"
    if /i "!VAR:~0,4!" == "-cpu" set "CPU=!VAR:~4!"
    if /i "!VAR:~0,2!" == "-k" set "KVARIANT=!VAR:~2!"
    if /i "!VAR:~0,2!" == "-l" set "LOADER_ADDRESS=!VAR:~2!"
    if /i "!VAR!" == "-m"      set DUMP_MAP=True
    if /i "!VAR:~0,2!" == "-p" set "PCM=!VAR:~2!"
    if /i "!VAR:~0,2!" == "-t" set "BIN_LOCATION=!VAR:~2!"
    if /i "!VAR!" == "-x"      set ASSEMBLY_KERNEL=True
    if /i "!VAR!" == "/h"      goto Usage
    if /i "!VAR!" == "-h"      goto Usage
    if /i "!VAR!" == "--help"  goto Usage
  )
  setlocal disabledelayedexpansion
)


rem * Ensure no trailing slash on paths.
call :Detrailslash "%GCC_LOCATION%" GCC_LOCATION
call :Detrailslash "%BIN_LOCATION%" BIN_LOCATION

rem * Determine source directory based on PCM type and kernel type.
rem *   P04 assembly uses 68k-VPW-Asm-P04 (crowbar-derived kernel with AMD flash support).
rem *   All other assembly kernels use 68k-VPW-Asm.
rem *   C kernels use 68k-VPW-C.
set "SOURCE_DIR=68k-VPW-Asm"
if not defined ASSEMBLY_KERNEL set "SOURCE_DIR=68k-VPW-C"
if /i "%PCM%"=="P04" if defined ASSEMBLY_KERNEL set "SOURCE_DIR=68k-VPW-Asm-P04"
rem *   P05c is a 68k (68332) part but CAN, not VPW: its own assembly kernel directory.
if /i "%PCM%"=="P05c" if defined ASSEMBLY_KERNEL set "SOURCE_DIR=68k-CAN-Asm-P05c"

rem * P05c default link address. The boot programming handler only launches at the EXACT factory
rem * address 0xFF61AE (2-byte aligned), which the linker (4-byte .text alignment) cannot target.
rem * So link at 0xFF61B0 and let PostBuild.cmd prepend a 2-byte NOP; PcmHammer loads the image at
rem * 0xFF61AE (KernelBaseAddress), the NOP runs, execution falls into the kernel at 0xFF61B0.
rem * Replaces the global FF8000 default for P05c (pass -aXXXX to override).
if /i "%PCM%"=="P05c" if /i "%BASE_ADDRESS%"=="FF8000" set BASE_ADDRESS=FF61B0

rem * Target CPU selection. Maps the required -cpu value to the cross toolchain,
rem * assembler flags and (for non-68k) source dir. 68k uses the m68k toolchain and the
rem * source dirs chosen above; PowerPC kernels live in their own PPC-CAN-Asm-<PCM> dir,
rem * and the PowerPC tools install as a sibling of the m68k tools under SysGCC, so derive
rem * their bin dir from GCC_LOCATION (m68k-elf -> powerpc-eabi) rather than hardcoding a
rem * path, which keeps -g working. The -mcpu flag reuses %CPU% so the value is the same
rem * end to end. Add new CPU families here.
if /i "%CPU%"=="68332" (
  set "TOOL_PREFIX=m68k-elf-"
  set "ASM_CC_FLAGS=-fomit-frame-pointer -std=gnu99 -mcpu=%CPU% -O0"
) else if /i "%CPU%"=="505" (
  set ASSEMBLY_KERNEL=True
  set "SOURCE_DIR=PPC-CAN-Asm-%PCM%"
  set "GCC_LOCATION=%GCC_LOCATION:m68k-elf=powerpc-eabi%"
  set "TOOL_PREFIX=powerpc-eabi-"
  set "ASM_CC_FLAGS=-mregnames -mcpu=%CPU% -mbig-endian -mstrict-align"
) else (
  echo ERROR: -cpu^<value^> is required. Supported: 68332 ^(m68k VPW PCMs^), 505 ^(PowerPC E38^).
  exit /b 1
)

rem * Ensure build directory exists.
if not exist build mkdir build

rem * Setup linker map dumps (point into build directory).
if defined DUMP_MAP (
  set "DUMPMAP=-Map ..\build\Kernel-%PCM%.map"
  set "LDUMPMAP=-Map ..\build\Loader-%PCM%.map"
)

rem * Create PCM specific object file list from CFiles-<PCM>.list (C kernels only).
(
  setlocal enabledelayedexpansion
  if not defined ASSEMBLY_KERNEL (
    if exist "%SOURCE_DIR%\CFiles-%PCM%.list" (
      for /f "usebackq" %%A in ("%SOURCE_DIR%\CFiles-%PCM%.list") do (
        set "OLIST=!OLIST! %%~nA.o"
      )
    )
  )
  setlocal disabledelayedexpansion
)

rem *** Build timestamp as Unix epoch (seconds since 1970-01-01 UTC)
for /f %%a in ('powershell "[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()"') do set BUILD_EPOCH=%%a
set BUILD_DEFS=-DBUILD_EPOCH=%BUILD_EPOCH%

rem *** Assembly kernel source/output selection. Optional -k<variant> builds an
rem *** alternate source Kernel-<variant>.S into Kernel-<PCM>-<variant>.bin, so one PCM
rem *** directory can produce several kernels (e.g. P05c read vs write). Default (no -k):
rem *** Kernel.S -> Kernel-<PCM>.bin. These must be set OUTSIDE the build if/else block
rem *** below (cmd expands %vars% per parenthesized block when the block is entered).
set "KSRC=Kernel.S"
set "KOBJ=Kernel.o"
set "KOUT=Kernel-%PCM%"
if defined KVARIANT (
  set "KSRC=Kernel-%KVARIANT%.S"
  set "KOBJ=Kernel-%KVARIANT%.o"
  set "KOUT=Kernel-%PCM%-%KVARIANT%"
)

rem *** Build from source subdirectory; binary outputs go to ..\build\
pushd "%SOURCE_DIR%"

if not defined ASSEMBLY_KERNEL (
  rem ***
  rem *** C Kernel
  rem ***
  echo Building Kernel-%PCM%.bin [C, %SOURCE_DIR%]...
  "%GCC_LOCATION%\m68k-elf-gcc.exe" -c -D=%PCM% %BUILD_DEFS% -fomit-frame-pointer -std=gnu99 -mcpu=68332 -O0 Kernel-%PCM%.c @CFiles-%PCM%.list
  if %errorlevel% neq 0 (popd & goto :EOF)

  rem * Create PCM specific Linker Script (.ld) in current (source) directory.
  call "%~dp0CreateCKernelPCMSpecificLinkerScript.cmd" %PCM%

  "%GCC_LOCATION%\m68k-elf-ld.exe" --section-start .kernel_code=0x%BASE_ADDRESS% -T LinkerScript.tmp %DUMPMAP% -o ..\build\Kernel-%PCM%.elf Kernel-%PCM%.o %OLIST%
  if %errorlevel% neq 0 (del /f LinkerScript.tmp & popd & goto :EOF)
  del /f LinkerScript.tmp

  "%GCC_LOCATION%\m68k-elf-objcopy.exe" -O binary --only-section=.kernel_code --only-section=.rodata ..\build\Kernel-%PCM%.elf ..\build\Kernel-%PCM%.bin
  if %errorlevel% neq 0 (popd & goto :EOF)
  call :VerifyBin "..\build\Kernel-%PCM%.bin"
  if %errorlevel% neq 0 (popd & exit /b 1)

  if defined DUMP_ELF (
    "%GCC_LOCATION%\m68k-elf-objdump.exe" -d -S ..\build\Kernel-%PCM%.elf > ..\build\Kernel-%PCM%.disassembly
    if %errorlevel% neq 0 (popd & goto :EOF)
  )

) else (
  rem ***
  rem *** Assembly Kernel
  rem ***
  echo Building %KOUT%.bin [Assembly, %SOURCE_DIR%]...
  "%GCC_LOCATION%\%TOOL_PREFIX%gcc.exe" -c -D=%PCM% %BUILD_DEFS% %ASM_CC_FLAGS% %KSRC%
  if %errorlevel% neq 0 (popd & goto :EOF)

  "%GCC_LOCATION%\%TOOL_PREFIX%ld.exe" --section-start .text=0x%BASE_ADDRESS% -T Kernel.ld %DUMPMAP% -o ..\build\%KOUT%.elf %KOBJ%
  if %errorlevel% neq 0 (popd & goto :EOF)

  "%GCC_LOCATION%\%TOOL_PREFIX%objcopy.exe" -O binary --only-section=.text --only-section=.data ..\build\%KOUT%.elf ..\build\%KOUT%.bin
  if %errorlevel% neq 0 (popd & goto :EOF)

  rem *** Optional per-kernel post-build step. If the source directory provides a
  rem *** PostBuild.cmd, run it on the finished .bin before verification so the
  rem *** verified artifact is the final one, and so any kernel-specific
  rem *** post-processing stays within that kernel's own directory.
  if exist PostBuild.cmd (
    call ".\PostBuild.cmd" "..\build\%KOUT%.elf" "..\build\%KOUT%.bin" "%BASE_ADDRESS%" "%GCC_LOCATION%" "%TOOL_PREFIX%"
    if errorlevel 1 (popd & exit /b 1)
  )

  call :VerifyBin "..\build\%KOUT%.bin"
  if %errorlevel% neq 0 (popd & exit /b 1)

  if defined DUMP_ELF (
    "%GCC_LOCATION%\%TOOL_PREFIX%objdump.exe" -d -S ..\build\%KOUT%.elf > ..\build\%KOUT%.disassembly
    if %errorlevel% neq 0 (popd & goto :EOF)
  )

  rem *** Handle the Kernel Loader
  if defined LOADER_ADDRESS (
    echo Building Loader-%PCM%.bin [Assembly, %SOURCE_DIR%]...
    "%GCC_LOCATION%\m68k-elf-gcc.exe" -c -D=%PCM% %BUILD_DEFS% -fomit-frame-pointer -std=gnu99 -mcpu=68332 -O0 Loader.S
    if %errorlevel% neq 0 (popd & goto :EOF)

    "%GCC_LOCATION%\m68k-elf-ld.exe" --section-start .text=0x%LOADER_ADDRESS% -T Loader.ld %LDUMPMAP% -o ..\build\Loader-%PCM%.elf Loader.o
    if %errorlevel% neq 0 (popd & goto :EOF)

    "%GCC_LOCATION%\m68k-elf-objcopy.exe" -O binary --only-section=.text --only-section=.data ..\build\Loader-%PCM%.elf ..\build\Loader-%PCM%.bin
    if %errorlevel% neq 0 (popd & goto :EOF)
    call :VerifyBin "..\build\Loader-%PCM%.bin"
    if %errorlevel% neq 0 (popd & exit /b 1)

    if defined DUMP_ELF (
      "%GCC_LOCATION%\m68k-elf-objdump.exe" -d -S ..\build\Loader-%PCM%.elf > ..\build\Loader-%PCM%.disassembly
      if %errorlevel% neq 0 (popd & goto :EOF)
    )
  )
)

popd

rem *** Install: copy binaries from build\ to BIN_LOCATION if requested.
if defined COPY_BIN (
  if exist "build\%KOUT%.bin" (
    echo Copying %KOUT%.bin -^> %BIN_LOCATION%\%KOUT%.bin
    copy build\%KOUT%.bin "%BIN_LOCATION%" 1>nul
  )
  if defined LOADER_ADDRESS (
    if exist "build\Loader-%PCM%.bin" (
      echo Copying Loader-%PCM%.bin -^> %BIN_LOCATION%\Loader-%PCM%.bin
      copy build\Loader-%PCM%.bin "%BIN_LOCATION%" 1>nul
    )
  )
)

@echo off
echo Cleaning build artifacts...

rem Clean all binary artifacts from build\
if exist build (
  pushd build
  for %%A in (
    *.o
    *.disassembly
    *.out
    *.ram
    *.bin
    *.elf
    *.log
    *.exe
    *.map
    *.tmp
    ) do if exist %%A echo   Deleting build\%%A & del "%%A"
  popd
)

rem Clean intermediate object files from every kernel source subdirectory.
for /d %%D in (*) do (
  pushd %%D
  for %%A in (*.o *.tmp) do if exist %%A echo   Deleting %%D\%%A & del "%%A"
  popd
)

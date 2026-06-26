PCM Hammer supports reading and writing the Operating System (OS) and Calibration of General Motors P01 and P59 Powertrain Control Modules (PCMs). The 12200411 is best known variant, but there are several and we aim to support all of them.

PCM Logger supports logging from the same PCMs. 

VPW Explorer is intended for developers rather than for end users. It's basically just a sandbox for testing new ideas.

PcmLibrary contains core logic for the applications. While the applications currently only run on Windows, this probably should work on any operating system that has a .Net Core implementation (Mac, Linux, Android).

PcmLibraryWindowsForms contains Windows-specific functionality like serial ports and J2534 support. 

## Running the unit tests

The Tests project (`Apps/Tests/Tests.csproj`) is a legacy non-SDK .NET Framework 4.8 project using MSTest v1. `dotnet test` does not run this project format - it exits 0 without executing anything. Build with MSBuild and run the resulting assembly with `vstest.console.exe` instead:

```
msbuild Apps\Tests\Tests.csproj /p:Configuration=Debug
vstest.console.exe Apps\Tests\bin\Debug\Tests.dll
```

Run a subset with a filter:

```
vstest.console.exe Apps\Tests\bin\Debug\Tests.dll /TestCaseFilter:"FullyQualifiedName~CanKernelBootPolicyTests"
```

`msbuild` and `vstest.console.exe` ship with Visual Studio; find them via `vswhere` (for example under `<VS install>\MSBuild\Current\Bin` and `<VS install>\Common7\IDE\Extensions\TestPlatform`). The project uses an explicit `<Compile Include>` list, so a new test file must be added to `Tests.csproj` to be compiled.

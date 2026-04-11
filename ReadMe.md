## Overview

PCM Hammer and tools support reading, writing, and data logging with General Motors P01, P04, P05 (VPW), P08, P10, P11, P12, P59, 4 connector 98-02 Black Box and E54 Powertrain Control Modules (PCMs).

```
+---------+--------+-------+---------+----------+---------+-------+--------+-------------+---------+
|         |        |       | Clone   | Segment  | Recovery| Slave |        |             |         |
|PCM Type | Logging| Read  | Write   | Write    | Write   | Write | Loader | Boot Sector | Kernel  |
+---------+--------+-------+---------+----------+---------+-------+--------+-------------+---------+
|Black    |        |       |         |          |         |       |        |             |         |
|Box 4    |        |       |         |          |         |       |        |             |         |
|Connector| Yes    | Yes   | Yes     | N/A      | ?       | N/A   | N/A    | Yes         | Assembly|
|P01      | Yes    | Yes   | Yes     | Yes      | Yes     | N/A   | N/A    | Yes         | C       |
|P04      | Yes    | Yes   | Yes     | N/A      | N/A     | N/A   | Yes    | Yes         | Assembly|
|P05(VPW) | Yes    | Yes   | Yes     | N/A      | ?       | N/A   | N/A    | No          | Assembly|
|P08      | Yes    | Yes   | Yes     | Yes      | No      | N/A   | N/A    | Yes         | Assembly|
|P10      | Yes    | Yes   | Yes     | Yes      | Yes     | No    | N/A    | Yes         | C       |
|P11      | Yes    | Yes   | Yes     | Yes      | ?       | No    | N/A    | Yes         | Assembly|
|P12      | Yes    | Yes   | Yes     | Yes      | Yes     | No    | N/A    | No          | C       |
|P59      | Yes    | Yes   | Yes     | Yes      | Yes     | N/A   | N/A    | Yes         | C       |
|E54      | Yes    | Yes   | Yes     | Yes      | No      | N/A   | N/A    | Yes         | Assembly|
+---------+--------+-------+---------+----------+---------+-------+--------+-------------+---------+
```

Yes means an operation is supported by the PCM, and tested in PCM Hammer.
No means an operation is possible in the PCM, or not supported.
N/A means the operation is not applicable (eg PCM does not have a slave cpu).

The C kernels are the original and trusted kernel.
The Assembly kernels are the smaller new generation of kernel.
The PCM Hammer team recommends to flash PCMs on the bench and with a good power supply of around 12.5v to 14v. 
The required current is around 1 amp. Phone chargers and other cheap switch mode power supplies often cause
flash failure due to poor quality power.
Quality lab type power supplies, or charged car batteries with good floating voltages are recommended.
There are mutiple revisions of P05 PCM. PCMHammer only works with VPW capable units (P05a, P05b) and does not support CAN (P05c).

## Installation

Go here: https://github.com/PcmHammer/PcmHammer/releases

The most recent release will be at the top of that page.

Click "Assets" (below the description of the release) and download the .zip file.   

Extract the contents of the zip file, and run PcmHammer.exe or PcmLogger.exe.

If you get an error about not having the right version of .NET, you need to download and install the .NET 4.6.2 runtime. You can get it [here](https://dotnet.microsoft.com/en-us/download/dotnet-framework/thank-you/net462-web-installer).

## Building

You will need Visual Studio 2019 or later, and the .NET 4.6.2 SDK.

You can get the .NET 4.6.2 SDK [here](https://go.microsoft.com/fwlink/?linkid=2099466)

## Links

[The GM section of pcmhacking.net](https://pcmhacking.net/forums/viewforum.php?f=42)

[Announcements on Facebook](https://www.facebook.com/PcmHammer)

[A shortcut to the project's GitHub page](http://pcmhammer.org/)

[Universal Patcher](https://universalpatcher.net/)

## What do I need to edit bin files?

Most people use tunerpro. To do so you need to know the operating system id of your computer (OSID) and use that to find a matching XDF file which tells tunerpro what is in the XDF and how to edit it. XDFs can be found on pcmhacking.net and other places. Also check out [Universal Patcher](https://universalpatcher.net/) which does a lot more than just patching!

## Where did the Arduino stuff go?

[Here.](https://github.com/LegacyNsfw/ArduinoVpw)

## Overview

These tools currently support reading, writing, and data logging with General Motors P01, P04, P08, P10, P12, P59, 4 connector 98-02 Black Box and E54 Powertrain Control Modules (PCMs). 

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

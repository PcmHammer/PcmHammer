Uno is a UI library that paves the way to Android and iOS versions of PCM Hammer, but there is still a lot
of work to be done. This document outlines the current state of the Uno UI and what needs to be done to,
first, get it working, and second, make it a viable alternative to the existing Windows Forms UI.

## To get it working on Windows

* Revise the Settings page / model to read and write using PcmLibraryWindowsApi's Settings class.
* Get the connection lifecycle working - connect at startup, poll, reconnect when settings change.
* Get the "Get PCM Info" page working.
* Implement flash read 
* Implement flash write 
* Implement data logging 
* TroubleshootingLogger should store logs in memory (probably in a circular buffer) and make them available
in the UI. 

## To get it working cross-platform

* DeviceFactory.CreateDeviceFromConfigurationSettings loads and stores the user's COM port, device type, and
J2534 device name using Windows APIs. To make this work cross-platform, we'll need to use Uno's settings API,
store those values in an object, and pass that object into CreateDeviceFromConfigurationSettings - which
should probably be renamed to CreateDevice at that point.
See https://platform.uno/docs/articles/features/settings.html for more information.
* Moving settings into Uno's work will require corresponding changes in the Windows Forms UI.
* The Uno app's settings page will need to be revised to use Uno's storage as well


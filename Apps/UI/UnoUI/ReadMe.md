Uno is a UI library that paves the way to Android and iOS versions of PCM Hammer, but there is still a lot
of work to be done. This document outlines the current state of the Uno UI and what needs to be done to,
first, get it working, and second, make it a viable alternative to the existing Windows Forms UI.

## To get it working on Windows

* done - Revise the Settings page / model to read and write using PcmLibraryWindowsApi's Settings class.
* done - Get the connection lifecycle working - connect at startup, poll, reconnect when settings change.
* done - Get the "Get PCM Info" page working.
* done - Implement flash read 
* in progress - Implement flash write 
* Implement data logging 
* TroubleshootingLogger should store logs in memory (maybe in a circular buffer?) and make them available in the UI.

All of the above are easier said than done. This will take some time.

## Other stuff to do:

* Implement "Change VIN"
* Implement "message of the day" (so we can alert people to new versions)
* Implement the help page
* Use Windows Management API to get device driver names for serial ports
* Add "verify flash" button on "other" page

## Future ideas:

* DTC reader
* Crank relearn 
 
## Surprises

* ApplicationData.Current.LocalSettings is used to store configuration settings.
This doesn't work for "unpackaged" Windows apps. because it requires an app-data folder, which is only supported for packaged apps.
* You can't have "async void" methods in a Model class, due to a bug in the Uno Platform code generator. The AsyncLogger class works around this.
* You can't have two Model classes share a XAML file. Hence the duplication between ReadModel/WriteModel and ReadPage/WritePage.

## To get it working cross-platform

* DeviceFactory.CreateDeviceFromConfigurationSettings loads and stores the user's COM port, device type, and
J2534 device name using Windows APIs. To make this work cross-platform, we'll need to use Uno's settings API,
store those values in an object, and pass that object into CreateDeviceFromConfigurationSettings - which
should probably be renamed to CreateDevice at that point.
See https://platform.uno/docs/articles/features/settings.html for more information.
* Moving settings into Uno's work will require corresponding changes in the Windows Forms UI.
* The Uno app's settings page will need to be revised to use Uno's storage as well


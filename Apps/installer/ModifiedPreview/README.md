# Modified-preview setup proposal

This optional personal installer accompanies the integrated WPF workspace proposal. It does not replace upstream release tooling. It verifies payload hashes, installs per user, offers desktop and Start menu shortcuts, and optionally starts the app. The target and shortcut names contain **Modified Preview**, so an upstream installation is not replaced.

Close the app before updating. Once a payload has been prepared, run `Install.cmd`, choose the shortcut/launch options and click Install. `Install.ps1 -DestinationDirectory <folder> -NoShortcuts` supports isolated installation verification.

To prepare a fresh payload, use the installed .NET 10 SDK and:

```powershell
./Apps/installer/ModifiedPreview/Package.ps1 -OutputDirectory <new-output-folder>
```

The script uses the existing upstream kernel/resource build outputs. Having no programming kernels does not prevent opening the UI but does prevent relevant vehicle operations; build the upstream resources before expecting vehicle support. No compiled program, borrowed BIN, research definition or new ECU-side component is included by this proposal.

The repository's original LICENSE and THIRD-PARTY-NOTICES govern distribution. A personal build containing restricted PCM-side components is not automatically authorized for public redistribution. Maintainers decide whether and how to incorporate this installer into an official release.

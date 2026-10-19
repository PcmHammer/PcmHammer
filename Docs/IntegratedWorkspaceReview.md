# Integrated WPF tuning and logging overhaul

Proposed by FoggEyez, October 18, 2026. Base: `develop` at `ac45d15958cfd8591bf0c47ceae9040394e25e2b`.

This is one proposal covering the complete host-application overhaul. Maintainers can review the sections independently and decide which portions fit PCM Hammer. It retains the PCM Hammer name, upstream PCM icon, GPLv3, original credits and third-party notices. It is labeled an unofficial modified preview, not an official release.

## Existing capabilities reused

PCM identification, read/write protocols, PHZ documents, parameter definitions and logging infrastructure already existed upstream. The standalone PCM Logger already supplied logging and dashboard/monitor functionality. The existing WPF front end and other front ends are retained. This contribution adds an integrated editor/data workspace and its supporting checks; it does not claim to introduce those upstream foundations.

## Integrated live logging and charts

- Adds a Data & charts workspace to WPF, using PcmLibrary parameter sources and recording infrastructure.
- Adds selectable channels and stock-baseline, cam idle/startup and misfire profiles. The preview limits recording to twelve selected channels.
- Integrated vehicle logging supports CAN 500k. VPW logging is not added to the WPF workspace; upstream PCM Logger remains separate.
- Adds CSV recording, source/time labels, cancellation, elapsed time, saved-recording review and visibly labeled demo recordings.
- Passes cancellation through CAN PID requests and rejects short data replies rather than manufacturing zero-filled readings.
- Keeps vehicle programming and logging mutually exclusive.
- Principal files: `Logging/LiveDataRecording.cs`, `RecordingCsvReader.cs`, `TuningLogProfiles.cs`, `GmlanPollingLogger.cs`, `Logger.cs`, `Vehicle.Logging.cs`; WPF `DataLoggingViewModel.cs`, `DataLoggingView.xaml` and `TelemetryChart.cs`.

## Gauges and speed correction

- Adds original vector speedometer/tachometer controls to the WPF workspace. They consume existing recorded samples without extra polling.
- Provides mph/km/h display switching and clears missing, invalid or stale samples after three seconds during recording. Stopped readings are labeled as last received; opening a saved recording clears live readings.
- Corrects the shipped vehicle-speed mph expression to `x / 1.609344`. At 100 km/h the old expression returns 60.934 mph; the corrected result is approximately 62.137119 mph.
- Adds a regression test that loads the shipped definition and checks km/h and mph through LogRowParser at 0, 100 and 255 km/h.
- The amber tachometer sector is decorative, not an ECU rev limit or a verified safe engine range.
- Principal files: `InstrumentGauge.cs`, `DataLoggingViewModel.Gauges.cs`, `Parameters.Standard.xml` and `LoggingTests.cs`.

## Calibration workspace and table editing

- Adds category navigation, table search, numeric tables and larger 3D/2D graph views of the same data. Graph-only, table-only and combined layouts are available.
- Adds extended cell selection, row/column/all selection and clipboard copy/paste.
- Provides fill, additive offset, multiply, divide, percentage adjustment, row/column/surface interpolation, smoothing and restoration of original values.
- Rejects non-finite values, division by zero, invalid axis ordering and unsuitable interpolation selections.
- Edits an in-memory copy with undo/redo and a compact change journal. A failed selection transform does not partially apply.
- Separates committing typed cell values, applying edits to the working tune and exporting a new BIN. Pending edits participate in operation/save guards.
- Binds a read/loaded tune into the editor workflow where supported. Reading a file does not imply that all its calibration tables are defined.
- Principal files: `Calibration/CalibrationEditor.cs`, `TableSelectionTools.cs`, WPF `TuneEditorViewModel*.cs`, `TuneEditorView.xaml`, its code-behind and `CalibrationSurface.cs`.

## Definition handling and generated examples

- Adds external BIN/XDF loading for complete 2 MiB E38 images with an exact OSID match. A nearby OS definition is not substituted.
- Handles selected integer/float storage types, linear equations and bounded contiguous layouts. Rejects unsupported expressions/layouts, overlaps and out-of-calibration addresses; it does not implement every XDF/TunerPro feature or plugin.
- Imported XDFs are practice-only: editing is isolated from the working vehicle tune, and vehicle Apply/BIN export stay blocked.
- Native definitions require both exact OSID and an OS-byte fingerprint. This is a structural match, not proof that the definition's table meanings or limits are safe.
- Adds definition identity/count display, empty-category guidance and a coverage report. Opening a definition replaces the current list.
- Public examples are generated, explicitly labeled illustrations. No borrowed vehicle BIN, borrowed reference curve or OS 12614088 research definition is added to this branch. External import functionality is retained.
- Interface tests generate bounded test mappings against the upstream repository's existing test fixture. These mappings do not identify real vehicle settings.
- No verified VATS/immobilizer patch, complete AFM/DOD workflow, full torque-model definition or transmission editor is supplied.
- Principal files: `Calibration/XdfPracticeReader.cs`, `TuneEditorViewModel.Xdf.cs`, `.Navigation.cs` and generated-demo methods in `TuneEditorViewModel.cs`.

## Programming-validation changes

- Correlates boot-loader responses with the request service before progressing, and does not accept response-pending as a completed acknowledgment.
- Checks complete slave-module sets, valid headers and matching ASCII/binary part-number fields before programming.
- For E38 slave updates, compares bytes with the embedded factory baseline. A matching part-number string alone is insufficient. Baseline arrays are copied before exposing them to callers.
- Compares forced controller selections with detected file type unless the existing explicit cross-flash setting permits otherwise.
- These are host-side validation changes. Main calibration validation is separate from slave-baseline checks; this does not categorically reject custom main tuning files.
- Principal files: `CanBootLoaderWriter.cs`, `CanCommands.cs`, `FlashModuleBuilder.cs`, `Package/SlaveLibrary.cs`, `Package/SlaveWriteValidation.cs`, `Misc/CanWriteTargetPolicy.cs` and `WriteManager.cs`.

## Layout, connection controls and setup

- Combines application menus and workspace navigation in a compact row and removes duplicate tab headers.
- Hides the operations sidebar in data/tuning workspaces while retaining operation commands through menus and connection controls.
- Collapses notes/history to a compact expander and verifies the minimum supported window size.
- Adds footer adapter status and a connection check. Merely selecting an adapter reports an unverified selection rather than a working ECU connection.
- Uses adapter protocol capabilities when available.
- Uses the original PCM icon, retains project notices and adds a compact FoggEyez contribution credit.
- Includes the optional `Apps/installer/ModifiedPreview` setup scripts: payload verification, per-user installation, desktop/Start menu shortcuts and optional launch. These are offered for review rather than replacing upstream release scripts.

## Validation and limits

See `IntegratedWorkspaceValidation.md` for the actual tests run on this branch. No physical ECU or adapter was used. Software checks do not establish read/write compatibility, recovery behavior, table semantics or safe engine settings.

The source baseline includes historical upstream changes; source ancestry alone does not guarantee every old behavior or package every front end. This PR does not remove upstream controller data, kernels, original tests, Uno or CLI source. It adds no new or modified ECU-side files and publishes no installer binaries.

The following issues are **not claimed fixed**:

- [#94: E39 upload failure at block 13](https://github.com/PcmHammer/PcmHammer/issues/94). Not reproduced or validated on affected hardware.
- [#87: E92 boot-loader compressed/uncompressed write behavior](https://github.com/PcmHammer/PcmHammer/issues/87). Existing development source has write paths, but their presence and our response checks do not prove successful programming or resolve the reported hang.

## Maintainer review areas

Please assess the whole proposal and identify any parts you would adopt, reshape or omit. In particular: communication/operation ownership, cancellation/threading, definition trust and editing restrictions, clipboard validation, display scaling/layout, test portability, and how these changes should fit the existing front ends and release process.

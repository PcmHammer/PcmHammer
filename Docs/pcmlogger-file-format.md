# PcmLogger dashboard package (`.plz`)

A `.plz` holds everything needed to display a logging session: which PIDs to poll, and the
dashboards, monitors and histograms that show them. It is the logger's counterpart to `.phz`, and
deliberately the same shape so the two are learned once.

## Container

A ZIP containing:

| Entry | Purpose |
|---|---|
| `manifest.json` | Everything structural. UTF-8 JSON. |
| `readme.txt` | Plain text for anyone who opens the file with an archiver. |

A ZIP rather than a bare JSON file because the format is expected to grow — a dashboard background
image, a captured log to replay with, exported histogram data. Those want to be files beside the
manifest, and retrofitting a container later would leave two formats to read.

**There are no checksums**, unlike `.phz`. That format protects flash images, where a corrupt byte
can brick a PCM. The worst a damaged dashboard can do is draw badly, and a checksum that refused to
open it would obstruct more than it protected.

## Manifest

```json
{
  "FormatVersion": 1,
  "Description": "LS1 dash imported from AVT LS1 SAE & GM_ENH V1.6",
  "Created": "2026-10-02T09:15:00Z",
  "CreatedBy": "PcmLogger 1.2.3",
  "Source":        { "Kind": "adx", "Name": "AVT LS1 ....adx", "Guid": "e9efde9f-..." },
  "Communications":{ "Protocol": "VPW", "DeviceCategory": "J2534",
                     "DeviceId": "Tactrix OpenPort 2.0", "Osid": 12587603, "FourXReadWrite": false },
  "Pids":       [ ... ],
  "Dashboards": [ ... ],
  "Monitors":   [ ... ],
  "Histograms": [ ... ]
}
```

`FormatVersion` is checked on load. A file from a **newer** build is refused rather than opened,
because reading it would silently drop sections this build cannot represent and then discard them on
the next save.

`Source` records where the package came from. An ADX import keeps the source name and GUID so a
dashboard can be traced back to the file it was drawn from. `Kind` is `"builtin"` for the dashboard a
fresh install opens with, which is built in code rather than read from a file — see
`Docs/logger-dash-and-monitors.md`. It is an ordinary package in every other way, and Save As turns
it into a file like any other.

`Communications` describes the setup the package was built against, in the same spirit as the vehicle
block in a `.phz`. It is informational — opening a package does not reconfigure the interface.

### `Pids`

What the logger should poll. Everything displayed references one of these by `Id`.

```json
{
  "Id": "EngineSpeed",
  "Units": "RPM",
  "Name": "Engine Speed",
  "Zoom": false,
  "Definition": null
}
```

- **`Id`** joins to the app's parameter database.
- **`Units`** selects which conversion. A parameter often has several — `TPSensor` is Volts or
  percent — and a gauge drawn in kPa must not be fed volts, so the choice travels with the package.
- **`Zoom`** mirrors the existing `.LogProfile` flag, so a package round-trips a profile fully.
- **`Definition`** is normally null. Parameters are referenced, not copied, because the database can
  be corrected and extended over time; a package that embedded every definition would freeze
  whatever was believed when it was written. This matters in practice: a widely circulated ADX has
  PID 1155 labelled as engine RPM where the authoritative P01/P59 list has it as the fuel level
  sensor. `Definition` is the escape hatch for a parameter the database genuinely lacks:

```json
"Definition": { "Pid": "1155", "StorageType": "uint16", "BitMapped": false,
                "BitIndex": -1, "Expression": "x", "Format": "0" }
```

### `Dashboards`

```json
{
  "Id": "dash1", "Title": "GM AND SAE DASH", "Visible": true, "Order": 0,
  "Gauges": [
    { "Kind": "Round", "PidId": "EngineSpeed", "Title": "Engine RPM", "Units": "RPM",
      "Left": 0.0, "Top": 0.0, "Width": 0.27, "Height": 0.35,
      "RangeLow": 0, "RangeHigh": 8000,
      "HasAlarms": true, "AlarmLow": 0, "AlarmHigh": 6000,
      "NormalColor": 255, "AlarmColor": 16711680,
      "Digits": 0, "ArcDegrees": 300 }
  ]
}
```

- **`Visible` / `Order`** carry which dashboards are shown and in what sequence. Only one is shown
  at a time today, but the plumbing is here so adding more is not a format change. `Visible` is
  written as the user switches dashboards, so reopening a package comes back to the one they left
  on — and because the dashboard on screen decides what is polled, it also records what the package
  logs by default.
- **Bounds are fractions of the dashboard area, 0 to 1**, never pixels, so a layout keeps its
  proportions at any window size.
- **`Kind`** is `Round`, `Text`, `Indicator` or `Bar`.
- **`PidId`** is null when the gauge is unbound. An unbound gauge draws its face and no value. That
  is a normal state straight after an import, not an error.
- **Colours** are `0xRRGGBB` integers.

### `Monitors`

```json
{
  "Id": "monitor1", "Title": "Monitor1", "Visible": true, "Order": 0,
  "TimeSpanSeconds": 20,
  "Series": [
    { "PidId": "EngineSpeed", "Title": "Engine RPM", "Units": "RPM",
      "Color": 255, "RangeLow": 0, "RangeHigh": 8000, "Visible": true }
  ]
}
```

`Visible` on a series is the tick state behind the monitor's right-click menu, so which traces are
shown is part of the saved dashboard rather than a transient UI state. It also decides what is
polled: a hidden monitor, or an unticked trace within a shown one, does not select its parameter.

### `Histograms`

Reserved. Nothing reads this yet; it is in the format from the start because adding a section later
means every file written before it is a different shape, and an empty list costs nothing.

```json
{
  "Id": "hist1", "Title": "VE Learn", "Visible": false, "Order": 0,
  "XPidId": "MAPSensor",   "XBins": [20, 40, 60, 80, 100],
  "YPidId": "EngineSpeed", "YBins": [800, 1200, 1600, 2000],
  "CellPidId": "LongTermFTBank1",
  "Aggregation": "Average", "MinimumSamples": 3
}
```

`MinimumSamples` exists because a cell with one hit is noise, and a histogram that draws it the same
as a cell with fifty invites tuning against nothing.

## Importing from TunerPro

An ADX import reads only the **presentation** half of the file: dashboards, monitors, and the display
properties of the parameters they reference (title, units, range, alarms, digits).

It does **not** read acquisition — packet offsets, send commands, listen-packet framing. Those
describe how some other interface read a packet, and are meaningless here. That omission is what
lets any ADX be imported whatever interface it was written for, and it is also why CAN is not a
problem: the ADX format cannot describe CAN at all, having no concept of an identifier or of
multi-frame reassembly.

Imported gauges arrive **unbound** and are then matched to parameters by name — never by the PIDs in
the source file. See `Docs/logger-dash-and-monitors.md` for why that distinction matters.

package.phz
├── manifest.json                 ← the descriptor (the only thing the app parses)
├── 1/main.bin
├── 1/12625892.bin
├── 1/12629150.bin
└── 2/main.bin

example package.phz/manifest.json:

{
  "format": "pcmhammer/package",
  "formatVersion": 1,
  "generator": "PcmHammer 0.9.x",
  "created": "2026-07-29T10:00:00Z",
  "vehicle": { "description": "2009 Corvette", "vin": "1G1YY26W895000000" },
  "notes": "optional free text",

  "controllers": [
    {
      "id": 1,                            // stable key within the package
      "type": "PCM",                      // for future use
      "moduleType": "E38",                // PcmType / OSIDInfo
      "busId": "0x7E0",                   // for future use
      "images": [
        { "target": "main",              "file": "main.bin",
          "size": 2097152, "sha256": "…", "osid": 12628990 },
        { "target": "slave-os",          "file": "12625892.bin",
          "size": 26752,   "sha256": "…", "partNumber": 12625892 },
        { "target": "slave-calibration", "file": "12629150.bin",
          "size": 1536,    "sha256": "…", "partNumber": 12629150 }
      ]
    },
    {
      "id": 2,
      "type": "TCM",
      "moduleType": "T42",               // future target / example
      "busId": "0x7E1",
      "images": [
        { "target": "main", "file": "main.bin", "size": 1048576, "sha256": "…" }
      ]
    }
  ]
}
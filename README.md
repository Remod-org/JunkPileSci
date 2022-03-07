# JunkPileSci
As of late 2021 or early 2022, the junkpile scientists disappeared from Rust.  This is just a simple attempt to bring them back.  Ultimately, Facepunch will probably bring them back, at which time this plugin will become pointless.

We spawn a junk pile scientist at spawned junk piles based on a configured percentage.  Efforts have been made to ensure they are removed along with their associated junk pile or if they, for some reason, roam too far from it.

## Configuration
```json
{
  "Minimum spawn distance between scientists": 75.0,
  "Default scientist health": 120.0,
  "Hostile": false,
  "Allow in safe zone": false,
  "Roam range": 10.0,
  "Target lost range": 75.0,
  "Listen range": 30.0,
  "Memory duration": 30.0,
  "Spawn percentage vs junk pile spawns": 50,
  "debug": false,
  "Version": {
    "Major": 1,
    "Minor": 0,
    "Patch": 1
  }
}
```

Generally speaking, you will probably want to leave the defaults as-is until or unless you are comfortable with what changing these will do.


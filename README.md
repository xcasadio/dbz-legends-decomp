# Dragon Ball Z: Legends (PS1) Decompilation

A work-in-progress decompilation of **Dragon Ball Z: Legends** (SLPS-00355) for the Sony PlayStation.

## About

This project aims to reverse-engineer and decompile the PlayStation game "Dragon Ball Z: Legends" (1996, Bandai) into readable C code that compiles back to a byte-for-byte match of the original game.

**Game Information:**
- **Title:** Dragon Ball Z: Legends (ドラゴンボールZ 偉大なる孫悟空伝説)
- **Platform:** Sony PlayStation
- **Release:** 1996 (Japan)
- **Publisher:** Bandai
- **Developer:** Bandai / Tose

## Set-up

### Prerequisites

Clone the repository:

```shell
git clone git@github.com:YOUR_USERNAME/dbz-legends.git --recursive
cd dbz-legends
```

### Game Files

Place the required game disc files:

```shell
disks/dbz-legends.bin
disks/dbz-legends.cue
```

Or copy the extracted game files to `disks/jp/`:

```
disks/jp/
├── SLPS_003.55      # Main executable
├── GAME.EXE         # Game overlay
├── TITLE.EXE        # Title screen overlay
├── SELECT.EXE       # Select screen overlay
├── VS.EXE           # VS mode overlay
├── SP.EXE           # Special mode overlay
├── DEMO.EXE         # Demo overlay
├── MOVIE.EXE        # Movie player overlay
├── ENDING.EXE       # Ending overlay
├── AT1/             # Attack data 1
├── AT2/             # Attack data 2
├── CHR_DATA/        # Character data
├── CH_BIN1/         # Character binary 1
├── CH_BIN2/         # Character binary 2
├── CH_BIN3/         # Character binary 3
├── MOVIE/           # FMV files
├── SOUND/           # Sound data
├── STG/             # Stage data
└── SUB/             # Subtitle/demo data
```

## Building

TODO

## Contributing

Contributions are welcome! If you'd like to help decompile this game:

1. Fork the repository
3. Decompile and test your changes
4. Submit a pull request

## Progress

| Overlay        | Functions | Matching | Progress |
|----------------|-----------|----------|----------|
| SLPS_003.55    | ?         | ?        | 0%       |
| GAME.EXE       | ?         | ?        | 0%       |
| TITLE.EXE      | ?         | ?        | 0%       |
| SELECT.EXE     | ?         | ?        | 0%       |
| VS.EXE         | ?         | ?        | 0%       |
| SP.EXE         | ?         | ?        | 0%       |
| DEMO.EXE       | ?         | ?        | 0%       |
| MOVIE.EXE      | ?         | ?        | 0%       |
| ENDING.EXE     | ?         | ?        | 0%       |

## Resources

- [PSX Dev Wiki](https://psx-spx.consoledev.net/)
- [decomp.me](https://decomp.me/) - Collaborative decompilation
- [Ghidra](https://ghidra-sre.org/) - Reverse engineering tool
- [no$psx](https://problemkaputt.de/psx.htm) - PSX debugger/emulator

## Legal

This project does not include any copyrighted game assets. You must provide your own legally obtained copy of the game.

## License

This decompilation project is released for educational purposes only.

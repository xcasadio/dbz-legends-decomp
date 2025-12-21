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

Install the necessary dependencies:

```shell
make requirements

# Debian/Ubuntu
sudo add-apt-repository ppa:longsleep/golang-backports
sudo apt update
sudo apt install golang-go ninja-build binutils-mipsel-linux-gnu gcc-mipsel-linux-gnu

# Arch Linux
sudo pacman -S go ninja
yay mipsel-linux-gnu-binutils mipsel-linux-gnu-gcc

# Windows (MSYS2)
pacman -S mingw-w64-x86_64-go ninja
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

```shell
# Build the project
make build

# Clean generated files
make clean

# Format source code
make format

# Check matching status
make report
```

## Tooling

- `make build`: Build project and compare against original
- `make clean`: Remove generated files
- `make format`: Format the codebase with clang-format
- `make extract`: Extract game files from disc image
- `./mako.sh rank <overlay>`: Find remaining functions to decompile
- `./mako.sh dec <function_name>`: Decompile a function
- `./mako.sh symbols add <path> <name> <offset>`: Add or rename a symbol

## Project Structure

```
dbz-legends/
├── asm/              # Generated assembly files
├── assets/           # Extracted game assets
├── build/            # Build output
├── config/           # Configuration files
│   ├── jp.yaml       # Main project config (Japan version)
│   └── symbols.*.txt # Symbol definitions
├── disks/            # Game disc images/files
├── include/          # Header files
│   ├── common.h      # Common types and macros
│   ├── game.h        # Game-specific types
│   └── psxsdk/       # PSX SDK headers
├── src/              # Decompiled source code
│   ├── main/         # Main executable (SLPS_003.55)
│   ├── game/         # GAME.EXE overlay
│   ├── title/        # TITLE.EXE overlay
│   ├── select/       # SELECT.EXE overlay
│   ├── vs/           # VS.EXE overlay
│   ├── sp/           # SP.EXE overlay
│   ├── demo/         # DEMO.EXE overlay
│   ├── movie/        # MOVIE.EXE overlay
│   └── ending/       # ENDING.EXE overlay
└── tools/            # Development tools
```

## Contributing

Contributions are welcome! If you'd like to help decompile this game:

1. Fork the repository
2. Pick a function to decompile (use `./mako.sh rank` to find easy ones)
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

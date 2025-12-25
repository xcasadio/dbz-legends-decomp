#!/bin/bash
# PSX Compiler wrapper script
# Uses cc1-psx-26 (GCC 2.6 for PSX) for accurate decomp matching

set -e

CC1_PSX="/usr/local/bin/cc1-psx-26"
CPP="mips-linux-gnu-cpp"
AS="mips-linux-gnu-as"
OBJCOPY="mips-linux-gnu-objcopy"

# Default flags matching original PSX SDK
CC1_FLAGS="-O2 -G0 -quiet -mcpu=3000 -mgas -msoft-float"
CPP_FLAGS="-Iinclude -Iinclude/psxsdk -undef -D__GNUC__=2 -D__OPTIMIZE__ -D_MIPS_SZLONG=32 -D_MIPS_SZINT=32 -D_MIPS_SZPTR=32 -D__mips__ -D__mips -DPSX"
AS_FLAGS="-march=r3000 -mabi=32 -Iinclude -no-pad-sections"

usage() {
    echo "Usage: $0 [options] <input.c> -o <output.o>"
    echo ""
    echo "Options:"
    echo "  -S          Output assembly instead of object file"
    echo "  -E          Preprocess only"
    echo "  -c          Compile to object file (default)"
    echo "  -O<n>       Optimization level (0,1,2,3)"
    echo "  -I<dir>     Add include directory"
    echo "  -D<macro>   Define macro"
    echo "  -v          Verbose output"
    exit 1
}

VERBOSE=0
OUTPUT_ASM=0
PREPROCESS_ONLY=0
INPUT=""
OUTPUT=""
EXTRA_CPP_FLAGS=""
EXTRA_CC1_FLAGS=""

while [[ $# -gt 0 ]]; do
    case $1 in
        -S)
            OUTPUT_ASM=1
            shift
            ;;
        -E)
            PREPROCESS_ONLY=1
            shift
            ;;
        -c)
            shift
            ;;
        -o)
            OUTPUT="$2"
            shift 2
            ;;
        -O*)
            EXTRA_CC1_FLAGS="$EXTRA_CC1_FLAGS $1"
            shift
            ;;
        -I*)
            EXTRA_CPP_FLAGS="$EXTRA_CPP_FLAGS $1"
            shift
            ;;
        -D*)
            EXTRA_CPP_FLAGS="$EXTRA_CPP_FLAGS $1"
            shift
            ;;
        -v)
            VERBOSE=1
            shift
            ;;
        -*)
            echo "Unknown option: $1"
            usage
            ;;
        *)
            if [[ -z "$INPUT" ]]; then
                INPUT="$1"
            fi
            shift
            ;;
    esac
done

if [[ -z "$INPUT" ]]; then
    echo "Error: No input file specified"
    usage
fi

if [[ -z "$OUTPUT" ]]; then
    OUTPUT="${INPUT%.c}.o"
fi

# Temporary files
BASENAME=$(basename "$INPUT" .c)
TMP_I="/tmp/${BASENAME}.i"
TMP_S="/tmp/${BASENAME}.s"

# Step 1: Preprocess
if [[ $VERBOSE -eq 1 ]]; then
    echo "$CPP $CPP_FLAGS $EXTRA_CPP_FLAGS $INPUT -o $TMP_I"
fi
$CPP $CPP_FLAGS $EXTRA_CPP_FLAGS "$INPUT" -o "$TMP_I"

if [[ $PREPROCESS_ONLY -eq 1 ]]; then
    cat "$TMP_I"
    rm -f "$TMP_I"
    exit 0
fi

# Step 2: Compile with cc1-psx
if [[ $VERBOSE -eq 1 ]]; then
    echo "$CC1_PSX $CC1_FLAGS $EXTRA_CC1_FLAGS $TMP_I -o $TMP_S"
fi
$CC1_PSX $CC1_FLAGS $EXTRA_CC1_FLAGS "$TMP_I" -o "$TMP_S"

if [[ $OUTPUT_ASM -eq 1 ]]; then
    cat "$TMP_S"
    rm -f "$TMP_I" "$TMP_S"
    exit 0
fi

# Step 3: Assemble
if [[ $VERBOSE -eq 1 ]]; then
    echo "$AS $AS_FLAGS $TMP_S -o $OUTPUT"
fi
$AS $AS_FLAGS "$TMP_S" -o "$OUTPUT"

# Cleanup
rm -f "$TMP_I" "$TMP_S"

echo "Compiled: $INPUT -> $OUTPUT"

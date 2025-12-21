# DBZ Legends Decompilation Makefile
# Supports building with Docker on Windows

#---------------------------------------------------------------------------
# Configuration
#---------------------------------------------------------------------------

VERSION ?= jp
OVERLAY ?= main
BUILD_DIR := build/$(VERSION)
ASM_DIR := asm/$(VERSION)
SRC_DIR := src
DATA_DIR := data

# Docker configuration
DOCKER_IMAGE := dbz-legends-build
DOCKER_RUN := docker run --rm -v "$(CURDIR):/project" -w /project $(DOCKER_IMAGE)

# Cross-compiler tools (run in Docker)
CC1_PSX := /usr/local/bin/cc1-psx-26
CPP := mips-linux-gnu-cpp
AS := mips-linux-gnu-as
LD := mips-linux-gnu-ld
OBJCOPY := mips-linux-gnu-objcopy
OBJDUMP := mips-linux-gnu-objdump

# Compiler flags for PSX (GCC 2.6)
CC1_FLAGS := -O2 -G0 -quiet -mcpu=3000 -mgas -msoft-float
CPP_FLAGS := -Iinclude -Iinclude/psxsdk -undef -D__GNUC__=2 -D__OPTIMIZE__ -DPSX
AS_FLAGS := -march=r3000 -mabi=32 -Iinclude -no-pad-sections

# Overlay-specific settings
VRAM_START_main := 0x80020000
VRAM_START_game := 0x80020000
VRAM_START_title := 0x80020000
VRAM_START_select := 0x80020000
VRAM_START_vs := 0x80020000
VRAM_START_sp := 0x80020000
VRAM_START_demo := 0x80020000
VRAM_START_movie := 0x80020000
VRAM_START_ending := 0x80010000

VRAM_START := $(VRAM_START_$(OVERLAY))

# Source files
SRC_C := $(wildcard $(SRC_DIR)/$(OVERLAY)/*.c)
SRC_S := $(wildcard $(ASM_DIR)/$(OVERLAY)/*.s)
OBJS := $(patsubst $(SRC_DIR)/%.c,$(BUILD_DIR)/%.o,$(SRC_C))
OBJS += $(patsubst $(ASM_DIR)/%.s,$(BUILD_DIR)/asm/%.o,$(SRC_S))

#---------------------------------------------------------------------------
# Main targets
#---------------------------------------------------------------------------

.PHONY: all build clean setup help
.PHONY: docker-build docker-shell docker-image docker-asm
.PHONY: diff context extract progress

all: build

# Build overlay
build: setup
	@echo "Building overlay: $(OVERLAY)"
	@echo "Sources: $(SRC_C)"

# Setup directories
setup:
	@mkdir -p $(BUILD_DIR)/$(OVERLAY)
	@mkdir -p $(BUILD_DIR)/asm/$(OVERLAY)
	@mkdir -p $(ASM_DIR)/$(OVERLAY)
	@mkdir -p $(SRC_DIR)/$(OVERLAY)

# Clean build artifacts
clean:
	rm -rf build

# Full clean
distclean: clean
	rm -rf asm expected

#---------------------------------------------------------------------------
# Docker targets
#---------------------------------------------------------------------------

# Build Docker image
docker-image:
	docker build -t $(DOCKER_IMAGE) .

# Run shell in Docker
docker-shell:
	docker run --rm -it -v "$(CURDIR):/project" -w /project $(DOCKER_IMAGE) /bin/bash

# Compile a single C file and show assembly
docker-asm:
ifndef FILE
	$(error FILE is not set. Usage: make docker-asm OVERLAY=main FILE=cd)
endif
	$(DOCKER_RUN) /bin/bash -c " \
		$(CPP) $(CPP_FLAGS) $(SRC_DIR)/$(OVERLAY)/$(FILE).c -o /tmp/$(FILE).i && \
		$(CC1_PSX) $(CC1_FLAGS) /tmp/$(FILE).i -o -"

# Compile to object file
docker-compile:
ifndef FILE
	$(error FILE is not set. Usage: make docker-compile OVERLAY=main FILE=cd)
endif
	@mkdir -p $(BUILD_DIR)/$(OVERLAY)
	$(DOCKER_RUN) /bin/bash -c " \
		$(CPP) $(CPP_FLAGS) $(SRC_DIR)/$(OVERLAY)/$(FILE).c -o /tmp/$(FILE).i && \
		$(CC1_PSX) $(CC1_FLAGS) /tmp/$(FILE).i -o /tmp/$(FILE).s && \
		$(AS) $(AS_FLAGS) /tmp/$(FILE).s -o $(BUILD_DIR)/$(OVERLAY)/$(FILE).o"
	@echo "Compiled: $(BUILD_DIR)/$(OVERLAY)/$(FILE).o"

#---------------------------------------------------------------------------
# Diff and analysis tools
#---------------------------------------------------------------------------

# Run asm-differ
diff:
ifndef FUNC
	$(error FUNC is not set. Usage: make diff OVERLAY=main FUNC=CdSeekAndRead)
endif
	$(DOCKER_RUN) python3 tools/asm-differ/diff.py -mwo --overlay $(OVERLAY) $(FUNC)

# Extract original assembly for a function
extract:
ifndef START
	$(error START is not set. Usage: make extract OVERLAY=main START=0x80021574 END=0x800215c0)
endif
ifndef END
	$(error END is not set. Usage: make extract OVERLAY=main START=0x80021574 END=0x800215c0)
endif
	$(DOCKER_RUN) python3 tools/extract_asm.py $(DATA_DIR)/SLPS_003.55 $(START) $(END) --overlay $(OVERLAY)

# Generate context for decomp.me
context:
	$(DOCKER_RUN) python3 tools/m2ctx.py $(OVERLAY)

# Decompile with m2c
m2c:
ifndef ASM_FILE
	$(error ASM_FILE is not set. Usage: make m2c ASM_FILE=asm/jp/main/cd.s)
endif
	$(DOCKER_RUN) python3 tools/m2c/m2c.py --target mipsel-gcc-c $(ASM_FILE)

#---------------------------------------------------------------------------
# Help
#---------------------------------------------------------------------------

help:
	@echo "DBZ Legends Decompilation - Makefile targets"
	@echo ""
	@echo "Build targets:"
	@echo "  make build OVERLAY=main     - Build an overlay"
	@echo "  make setup OVERLAY=main     - Setup directories for overlay"
	@echo "  make clean                  - Remove build artifacts"
	@echo ""
	@echo "Docker targets:"
	@echo "  make docker-image           - Build Docker image"
	@echo "  make docker-shell           - Open shell in Docker"
	@echo "  make docker-asm OVERLAY=main FILE=cd  - Compile and show ASM"
	@echo "  make docker-compile OVERLAY=main FILE=cd - Compile to .o"
	@echo ""
	@echo "Analysis targets:"
	@echo "  make diff OVERLAY=main FUNC=FuncName  - Diff a function"
	@echo "  make extract OVERLAY=main START=0x80021574 END=0x800215c0"
	@echo "  make context OVERLAY=main   - Generate decomp.me context"
	@echo "  make m2c ASM_FILE=asm/jp/main/cd.s - Decompile with m2c"
	@echo ""
	@echo "Overlays: main, game, title, select, vs, sp, demo, movie, ending"

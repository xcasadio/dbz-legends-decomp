# DBZ Legends Decompilation Makefile

# Overlay list (Japan version)
OVL_JP += SLPS_003.55
OVL_JP += GAME.EXE
OVL_JP += TITLE.EXE
OVL_JP += SELECT.EXE
OVL_JP += VS.EXE
OVL_JP += SP.EXE
OVL_JP += DEMO.EXE
OVL_JP += MOVIE.EXE
OVL_JP += ENDING.EXE

VERSION ?= jp
BUILD_DIR := build/$(VERSION)
ASM_DIR := asm/$(VERSION)
SRC_DIR := src
DISK_DIR := disks/$(VERSION)

# Tools
CC := mipsel-linux-gnu-gcc
AS := mipsel-linux-gnu-as
LD := mipsel-linux-gnu-ld
OBJCOPY := mipsel-linux-gnu-objcopy
OBJDUMP := mipsel-linux-gnu-objdump

# Compiler flags
CFLAGS := -O2 -G0 -mips1 -mabi=32 -mno-abicalls -fno-pic
CFLAGS += -Wall -Wextra -Wno-unused-parameter
CFLAGS += -Iinclude -Iinclude/psxsdk
CFLAGS += -DUSE_INCLUDE_ASM

ASFLAGS := -march=mips1 -mabi=32 -Iinclude

.PHONY: all
all: extract build

.PHONY: build
build: bin/cc1-psx-26 bin/cc1-psx-272
	@./mako.sh build

.PHONY: extract
extract: disks/$(VERSION)
	@echo "Game files extracted to disks/$(VERSION)"

.PHONY: disks
disks: disks/$(VERSION)

# Extract from BIN/CUE if available
disks/%.iso:
	bchunk "disks/$*.bin" "disks/$*.cue" "$@"
	mv "disks/$*.iso01.iso" "$@"

disks/jp: 
	@if [ -f "disks/dbz-legends.bin" ]; then \
		bchunk "disks/dbz-legends.bin" "disks/dbz-legends.cue" "disks/dbz-legends.iso"; \
		mv "disks/dbz-legends.iso01.iso" "disks/dbz-legends.iso"; \
		7z x "disks/dbz-legends.iso" -o$@; \
	elif [ -d "data" ]; then \
		mkdir -p $@; \
		cp -r data/* $@/; \
	else \
		echo "Error: No game files found. Place disks/dbz-legends.bin+cue or copy files to data/"; \
		exit 1; \
	fi

.PHONY: clean
clean:
	@./mako.sh clean
	rm -rf $(BUILD_DIR)
	rm -rf $(ASM_DIR)

.PHONY: format
format:
	@./mako.sh format

.PHONY: rebuild
rebuild:
	@./mako.sh clean
	@./mako.sh build

.PHONY: report
report: build
	@./mako.sh report $(VERSION) build/report.json

.PHONY: requirements
requirements:
	python3 -m venv .venv
	.venv/bin/pip3 install -r requirements.txt

.PHONY: setup
setup: requirements
	@mkdir -p $(BUILD_DIR)
	@mkdir -p $(ASM_DIR)
	@mkdir -p config
	@mkdir -p src/main src/game src/title src/select src/vs src/sp src/demo src/movie src/ending
	@echo "Setup complete!"

# Object file rules
$(BUILD_DIR)/%.o: $(SRC_DIR)/%.c
	@mkdir -p $(dir $@)
	$(CC) $(CFLAGS) -c $< -o $@

$(BUILD_DIR)/%.o: $(ASM_DIR)/%.s
	@mkdir -p $(dir $@)
	$(AS) $(ASFLAGS) $< -o $@

# Download PSX GCC compiler
bin/cc1-psx-%: bin/cc1-psx-%.gz
	sha256sum --check $<.sha256
	gzip -kcd $< > $@
	touch $@
	chmod +x $@

bin/cc1-psx-%.gz: bin/cc1-psx-%.gz.sha256
	wget -O $@ https://github.com/Xeeynamo/ff7-decomp/releases/download/init/cc1-psx-$*.gz

# Context generation for decomp.me
.PHONY: context
context:
	@./tools/m2ctx.py $(OVERLAY)

# Diff tool
.PHONY: diff
diff:
	@python3 tools/asm-differ/diff.py -mwo $(FUNC)

.PHONY: help
help:
	@echo "DBZ Legends Decompilation - Available targets:"
	@echo ""
	@echo "  make build      - Build the project"
	@echo "  make clean      - Remove build artifacts"
	@echo "  make extract    - Extract game files from disc"
	@echo "  make format     - Format source code"
	@echo "  make report     - Generate matching report"
	@echo "  make setup      - Initial project setup"
	@echo "  make requirements - Install Python dependencies"
	@echo "  make context OVERLAY=main - Generate decomp.me context"
	@echo "  make diff FUNC=FuncName - Diff a function"
	@echo ""

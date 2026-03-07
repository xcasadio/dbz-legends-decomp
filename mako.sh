#!/usr/bin/env sh
# DBZ Legends - Build system wrapper
# Runs the Go-based build tool

# Explicitly set the path to the Go binary
GO_CMD="/usr/bin/go"

# Check if Go is available
if [ ! -x "$GO_CMD" ]; then
    echo "Error: Go is not installed or not executable at $GO_CMD. Please install Go first."
    exit 1
fi

# Run the builder
$GO_CMD run ./tools/builder "$@"

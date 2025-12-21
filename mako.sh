#!/usr/bin/env sh
# DBZ Legends - Build system wrapper
# Runs the Go-based build tool

# Check if Go is available
if ! command -v go &> /dev/null; then
    echo "Error: Go is not installed. Please install Go first."
    exit 1
fi

# Run the builder
go run ./tools/builder "$@"

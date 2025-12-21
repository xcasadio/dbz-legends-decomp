package main

import (
	"fmt"
	"os"
)

// DBZ Legends Build Tool
// This is a placeholder for the build system
// Implement build, clean, format, report commands as needed

func main() {
	if len(os.Args) < 2 {
		printUsage()
		os.Exit(1)
	}

	command := os.Args[1]

	switch command {
	case "build":
		build()
	case "clean":
		clean()
	case "format":
		formatCode()
	case "report":
		report()
	case "rank":
		rank()
	case "dec":
		decompile()
	case "symbols":
		symbols()
	default:
		fmt.Printf("Unknown command: %s\n", command)
		printUsage()
		os.Exit(1)
	}
}

func printUsage() {
	fmt.Println("DBZ Legends Build Tool")
	fmt.Println("")
	fmt.Println("Usage: mako.sh <command> [args]")
	fmt.Println("")
	fmt.Println("Commands:")
	fmt.Println("  build           Build the project")
	fmt.Println("  clean           Remove build artifacts")
	fmt.Println("  format          Format source code")
	fmt.Println("  report [ver]    Generate matching report")
	fmt.Println("  rank <overlay>  Rank functions by difficulty")
	fmt.Println("  dec <func>      Decompile a function")
	fmt.Println("  symbols <cmd>   Symbol management")
}

func build() {
	fmt.Println("Building DBZ Legends...")
	// TODO: Implement build logic
	// - Read config/jp.yaml
	// - Generate ninja build file
	// - Run ninja
	fmt.Println("Build not yet implemented. Please implement in tools/builder/main.go")
}

func clean() {
	fmt.Println("Cleaning build artifacts...")
	// TODO: Implement clean logic
	os.RemoveAll("build")
	os.RemoveAll("asm")
	os.Remove("build.ninja")
	os.Remove(".ninja_log")
	os.Remove(".ninja_deps")
	fmt.Println("Clean complete.")
}

func formatCode() {
	fmt.Println("Formatting source code...")
	// TODO: Run clang-format on src/ and include/
	fmt.Println("Format not yet implemented.")
}

func report() {
	fmt.Println("Generating matching report...")
	// TODO: Implement report generation
	fmt.Println("Report not yet implemented.")
}

func rank() {
	fmt.Println("Ranking functions by difficulty...")
	// TODO: Implement function ranking
	fmt.Println("Rank not yet implemented.")
}

func decompile() {
	fmt.Println("Decompiling function...")
	// TODO: Implement decompilation helper
	fmt.Println("Decompile not yet implemented.")
}

func symbols() {
	fmt.Println("Symbol management...")
	// TODO: Implement symbol management
	fmt.Println("Symbols not yet implemented.")
}

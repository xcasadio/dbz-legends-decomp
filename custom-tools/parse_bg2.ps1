# Parse INT_ARRAY_80087d94 directly from PCSX-Redux memory
# Read the full array (6 + 80*3 = 246 int32s = 984 bytes)

# Hex dump from PCSX-Redux memory read at 0x80087d94, 1000 bytes:
$hexLines = @(
"01000000 00005400 6c007f00 2e005800 28000000 01000000 10001000"
"a8fdffff 9cffffff a8fdffff f8f8ffff 38ffffff 70feffff e0fcffff 70feffff"
"50fbffff c0f9ffff a8fdffff 18fcffff d8f5ffff 9cffffff e0fcffff 88faffff"
"d4feffff 88faffff 70feffff a8fdffff f8f8ffff 28f1ffff 44fdffff 70feffff"
"b8f2ffff 0cfeffff 18fcffff 70feffff 9cffffff b8f2ffff 18fcffff 7cfcffff"
"d8f5ffff 50fbffff 0cfeffff f0f1ffff f8f8ffff 38ffffff a0f6ffff f8f8ffff"
"a8fdffff 80f3ffff 68f7ffff 7cfcffff f8f8ffff 48f4ffff 70feffff c0f9ffff"
"f0f1ffff d4feffff 68f7ffff 10f5ffff e0fcffff 10f5ffff 10f5ffff 70feffff"
"f0f1ffff 28f1ffff 38ffffff f0f1ffff a8fdffff 9cffffff"
"58020000 f8f8ffff 38ffffff 90010000 e0fcffff 70feffff b0040000 c0f9ffff"
"a8fdffff e8030000 d8f5ffff 9cffffff 20030000 88faffff d4feffff 78050000"
"70feffff a8fdffff 08070000 28f1ffff 44fdffff 90010000 b8f2ffff 0cfeffff"
"e8030000 70feffff 9cffffff 480d0000 18fcffff 7cfcffff 280a0000 50fbffff"
"0cfeffff 100e0000 f8f8ffff 38ffffff 60090000 f8f8ffff a8fdffff 800c0000"
"68f7ffff 7cfcffff 08070000 48f4ffff 70feffff 40060000 f0f1ffff d4feffff"
"98080000 10f5ffff e0fcffff f00a0000 10f5ffff 70feffff 100e0000 28f1ffff"
"38ffffff 100e0000"
"58020000 9cffffff a8fdffff 08070000 38ffffff 70feffff 20030000 70feffff"
"50fbffff 40060000 a8fdffff 18fcffff 280a0000 9cffffff e0fcffff 78050000"
"d4feffff 88faffff 90010000 a8fdffff f8f8ffff d80e0000 44fdffff 70feffff"
"480d0000 0cfeffff 18fcffff 90010000 9cffffff b8f2ffff e8030000 7cfcffff"
"d8f5ffff b0040000 0cfeffff f0f1ffff 08070000 38ffffff a0f6ffff 08070000"
"a8fdffff 80f3ffff 98080000 7cfcffff f8f8ffff b80b0000 70feffff c0f9ffff"
"100e0000 d4feffff 68f7ffff f00a0000 e0fcffff 10f5ffff f00a0000 70feffff"
"f0f1ffff d80e0000 38ffffff f0f1ffff"
"58020000 9cffffff 58020000 08070000 38ffffff 90010000 20030000 70feffff"
"b0040000 40060000 a8fdffff e8030000 280a0000 9cffffff 20030000 78050000"
"d4feffff 78050000 90010000 a8fdffff 08070000 d80e0000 44fdffff 90010000"
"480d0000 0cfeffff e8030000 90010000 9cffffff 480d0000 e8030000 7cfcffff"
"280a0000 b0040000 0cfeffff 100e0000 08070000 38ffffff 60090000 08070000"
"a8fdffff 800c0000 98080000 7cfcffff 08070000 b80b0000 70feffff 40060000"
"100e0000 d4feffff 98080000 f00a0000 e0fcffff f00a0000 f00a0000 70feffff"
"100e0000 d80e0000 38ffffff 100e0000"
)

# Actually let me just read from memory directly via the raw hex dump
# Instead, parse from raw bytes at the known address

# Read line by line from the actual hex output we got
# Let me construct the byte array from the original dump manually

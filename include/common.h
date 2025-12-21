#ifndef COMMON_H
#define COMMON_H

/**
 * DBZ Legends - Common types and macros
 * 
 * This file contains common type definitions and macros used throughout
 * the decompilation project.
 */

#ifndef NULL
#define NULL 0
#endif

/* Assembly include macro support */
#ifdef SKIP_ASM
#undef USE_INCLUDE_ASM
#endif

#ifdef USE_INCLUDE_ASM
__asm__(".include \"macro.inc\"\n");
#define INCLUDE_ASM(FOLDER, NAME)                                              \
    void __maspsx_include_asm_hack_##NAME() {                                  \
        __asm__(".text # maspsx-keep \n"                                       \
                "\t.align\t2 # maspsx-keep\n"                                  \
                "\t.set noreorder # maspsx-keep\n"                             \
                "\t.set noat # maspsx-keep\n"                                  \
                ".include \"" FOLDER "/" #NAME ".s\" # maspsx-keep\n"          \
                "\t.set reorder # maspsx-keep\n"                               \
                "\t.set at # maspsx-keep\n");                                  \
    }
#else
#define INCLUDE_ASM(FOLDER, NAME)
#endif

/* Standard integer types */
typedef signed char s8;
typedef unsigned char u8;
typedef signed short s16;
typedef unsigned short u16;
typedef signed int s32;
typedef unsigned int u32;
typedef signed long long s64;
typedef unsigned long long u64;

/* Fixed-point types (common in PSX games) */
typedef s16 fixed16;    /* 4.12 fixed point */
typedef s32 fixed32;    /* 20.12 fixed point */

/* Pointer types */
typedef u8 unk_data;
typedef void* unk_ptr;
typedef u32 uintptr;

/* Boolean type */
typedef s32 bool32;
#define TRUE 1
#define FALSE 0

/* Utility macros */
#define LEN(x) ((s32)(sizeof(x) / sizeof(*(x))))
#define MIN(a, b) ((a) < (b) ? (a) : (b))
#define MAX(a, b) ((a) > (b) ? (a) : (b))
#define ABS(x) ((x) < 0 ? -(x) : (x))
#define CLAMP(x, min, max) (MIN(MAX(x, min), max))

/* Alignment macros */
#define ALIGN4(x) (((x) + 3) & ~3)
#define ALIGN8(x) (((x) + 7) & ~7)
#define ALIGN16(x) (((x) + 15) & ~15)

/* Bit manipulation */
#define BIT(n) (1 << (n))
#define BITS(x, start, len) (((x) >> (start)) & ((1 << (len)) - 1))

/* Memory addresses */
#define RAM_START 0x80000000
#define RAM_SIZE  0x00200000  /* 2MB */
#define RAM_END   (RAM_START + RAM_SIZE)

/* Hardware registers base addresses */
#define HW_REGS   0x1F800000

/* Unused parameter marker */
#define UNUSED(x) (void)(x)

/* Force inline (compiler-specific) */
#define INLINE static inline

/* Structure packing */
#define PACKED __attribute__((packed))

#endif /* COMMON_H */

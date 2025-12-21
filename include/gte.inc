.ifndef .L_GTE_INC
.L_GTE_INC:

# GTE (Geometry Transformation Engine) instruction macros
# These are coprocessor 2 (CP2) instructions for 3D math operations

# Perspective transformation (single vertex)
.macro RTPS
    .word 0x4A180001
.endm

# Perspective transformation (three vertices)
.macro RTPT
    .word 0x4A280030
.endm

# Depth cueing (single)
.macro DPCL
    .word 0x4A680029
.endm

# Depth cueing (single, using IR0)
.macro DPCS
    .word 0x4A780010
.endm

# Depth cueing (three)
.macro DPCT
    .word 0x4AF8002A
.endm

# Interpolation
.macro INTPL
    .word 0x4A980011
.endm

# Normal color (single, no saturation)
.macro NCS
    .word 0x4AC8041E
.endm

# Normal color (three, no saturation)
.macro NCT
    .word 0x4AD80420
.endm

# Normal color depth cue (single)
.macro NCDS
    .word 0x4AE80413
.endm

# Normal color depth cue (three)
.macro NCDT
    .word 0x4AF80416
.endm

# Normal color color (single)
.macro NCCS
    .word 0x4B08041B
.endm

# Normal color color (three)
.macro NCCT
    .word 0x4B18043F
.endm

# Color depth cue (single)
.macro CDP
    .word 0x4B280414
.endm

# Color color (single)
.macro CC
    .word 0x4B38041C
.endm

# Normal clipping
.macro NCLIP
    .word 0x4B400006
.endm

# Average of Z values (3)
.macro AVSZ3
    .word 0x4B58002D
.endm

# Average of Z values (4)
.macro AVSZ4
    .word 0x4B68002E
.endm

# Outer product
.macro OP
    .word 0x4B70000C
.endm

# General purpose interpolation
.macro GPF
    .word 0x4B90003D
.endm

# General purpose interpolation (shift 12)
.macro GPL
    .word 0x4BA0003E
.endm

# Matrix vector multiply (with vector add)
.macro MVMVA
    .word 0x4A400012
.endm

# Square of vector
.macro SQR
    .word 0x4AA00428
.endm

.endif

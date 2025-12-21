.ifndef .L_MACRO_INC
.L_MACRO_INC:

# Global label macro
.macro glabel label
    .global \label
    \label:
.endm

# Jump label macro
.macro jlabel label
    glabel \label
.endm

# Local label (not exported)
.macro llabel label
    \label:
.endm

# Define macro (empty, for compatibility)
.macro .def #
.endm

# Include GTE instruction macros
.include "gte.inc"

.endif

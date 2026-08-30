using System;

namespace DbzLegendsRemaster;

// JUSTIFICATION: PSX hardware adaptation only
// RELATION: models the observable contract of the BIOS LoadExec vector, A0(0x51). On hardware the
// call replaces the resident executable and transfers control permanently: it never returns to its
// caller, which is why every original call site is followed by unreachable code. The desktop
// adapter runs the incoming overlay's main on the same thread, so it needs an explicit way to not
// return once that main gives control back. Caught by Game1's runtime-thread wrapper.
internal sealed class LoadExecTransferException : Exception
{
}

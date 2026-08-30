using System;
using DbzLegendsRemaster.TITLE_EXE;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: bench for the title task UpdateTitleScreen @ 0x80021E28.
//
// It is checked against the console rather than against itself. PCSX-Redux was stopped at the task
// context's first AddPrim (0x80022524) on the real title screen, and the 112 bytes at 0x80017CB4
// were read out. Those bytes are the expectations below: two textured quads and the scratch slot,
// exactly as the hardware had them on the frame state 0 handed over to state 1.
//
// One byte of the capture is deliberately not compared. p[1].x3 is stored in the delay slot of the
// jal at 0x80022528, so the console dump was taken one instruction before that write landed and
// reads 0 where the code plainly stores 0x280. The port is checked against the code there.
internal static class TitleScreenTaskValidation
{
    private static int s_failures;

    internal static int Run()
    {
        s_failures = 0;

        PsxSdkBridges.Install();
        PsxSdkBridges.ActivateTitleExe();

        FrameBaton.ResetHeadless(1);
        try
        {
            new TITLE_EXE_exe().Main();
        }
        catch (GameShutdownException)
        {
        }
        catch (Exception exception)
        {
            Console.WriteLine($"  ECHEC: main a leve {exception.GetType().Name}: {exception.Message}");
            Console.WriteLine("TITLE-TASK: echec");
            return 1;
        }

        int task = TaskSystem.g_TaskListHead[6];
        Check(task != 0, $"une tache existe en liste 6, lu 0x{task:X}");
        if (task == 0)
        {
            Console.WriteLine("TITLE-TASK: echec");
            return 1;
        }

        int contextAddress = PsxRam.ReadI32(task + 8);
        Check(LibGpu.RamResolve(contextAddress, out byte[] buffer, out int offset),
            $"le contexte 0x{contextAddress:X} se resout en memoire");
        if (buffer == null)
        {
            Console.WriteLine("TITLE-TASK: echec");
            return 1;
        }

        var p = new POLY_FT4Ref(buffer, offset);
        var p1 = p[1];
        var p2 = p[2];

        // Drive the task until state 0 has handed over. main's single headless frame may or may not
        // have swept list 6 already, so the bench makes the point deterministic.
        for (int guard = 0; guard < 4 && p2.ReadHalf(0) == 0; guard++)
        {
            TaskSystem.ExecuteTaskList(6);
        }

        Check(p2.ReadHalf(0) == 1, $"l'etat passe a 1 apres l'initialisation, lu {p2.ReadHalf(0)}");

        // --- l'etat de travail, contre la capture console 80 02 80 02 40 01 ---
        Check(p2.ReadHalf(4) == 0x0280, $"r0|g0 = 0x0280, lu 0x{(ushort)p2.ReadHalf(4):X4}");
        Check(p2.ReadHalf(6) == 0x0280, $"b0|code = 0x0280, lu 0x{(ushort)p2.ReadHalf(6):X4}");
        Check(p2.x0 == 0x140, $"x0 = 0x140, lu 0x{p2.x0:X}");
        Check(p2.u2 == 0, $"le fondu part de 0, lu 0x{p2.u2:X}");
        Check(p2.ReadHalf(2) == 0, $"le compteur de frames part de 0, lu {p2.ReadHalf(2)}");

        // --- les deux bandes, contre la capture ---
        CheckQuad(p, "p[0]", 9, 0x2e, 0x46);
        CheckQuad(p1, "p[1]", 9, 0x2e, 0x46);

        Check(p.clut == p1.clut, $"les deux bandes partagent la CLUT, lu 0x{p.clut:X4} / 0x{p1.clut:X4}");
        Console.WriteLine($"  CLUT de GetClut(0x180, 0xfe): 0x{p.clut:X4}");

        // Geometry: x0 = -p[2].x0, x1 = 0x140 - p[2].x0, bande haute y 0 a 0x58.
        Check(p.x0 == -0x140, $"p[0].x0 = -320, lu {p.x0}");
        Check(p.y0 == 0, $"p[0].y0 = 0, lu {p.y0}");
        Check(p.x1 == 0, $"p[0].x1 = 0, lu {p.x1}");
        Check(p.x2 == -0x140, $"p[0].x2 = -320, lu {p.x2}");
        Check(p.y2 == 0x58, $"p[0].y2 = 0x58, lu 0x{p.y2:X}");
        Check(p.x3 == 0, $"p[0].x3 = 0, lu {p.x3}");
        Check(p.y3 == 0x58, $"p[0].y3 = 0x58, lu 0x{p.y3:X}");

        // Bande basse, y 0xbc a 0xf0, decalee dans l'autre sens.
        Check(p1.x0 == 0x140, $"p[1].x0 = 320, lu {p1.x0}");
        Check(p1.y0 == 0xbc, $"p[1].y0 = 0xbc, lu 0x{p1.y0:X}");
        Check(p1.x1 == 0x280, $"p[1].x1 = 640, lu {p1.x1}");
        Check(p1.x2 == 0x140, $"p[1].x2 = 320, lu {p1.x2}");
        Check(p1.y2 == 0xf0, $"p[1].y2 = 0xf0, lu 0x{p1.y2:X}");
        Check(p1.x3 == 0x280, $"p[1].x3 = 640, lu {p1.x3}");
        Check(p1.y3 == 0xf0, $"p[1].y3 = 0xf0, lu 0x{p1.y3:X}");

        // --- la soumission ---
        // Bucket 0 is where everything lands on this frame: the two bands are AddPrim'd there
        // directly, and DrawSpriteGroup returns 0x800 - OTZ, which the console measured as bucket 0
        // too (a0 was 0x800A6830 exactly at the caller's first AddPrim).
        //
        // The bucket is a stack, so the head is whatever was added LAST. The title task adds the
        // two bands in the middle of its tail and then calls the sprite renderer twice more, so
        // the bands sit further down the chain, not at the head. What has to hold is their ORDER:
        // p is added before p_00, so p_00 links to p.
        uint bucket0 = ReadWord(FrameLoop.OT_800a6830, 0);
        int contextLow = contextAddress & 0x00ffffff;
        Check((bucket0 & 0x00ffffff) != 0x00ffffff && bucket0 != 0,
            $"la case 0 porte une primitive, lu 0x{bucket0:X8}");

        int bandTop = (contextLow + POLY_FT4Ref.Size) & 0x00ffffff;
        int posP1 = -1;
        int posP0 = -1;
        int chainLength = 0;
        uint link = bucket0 & 0x00ffffff;
        while (link != 0x00ffffff && link != 0 && chainLength < 4096)
        {
            if ((int)link == bandTop && posP1 < 0)
            {
                posP1 = chainLength;
            }

            if ((int)link == contextLow && posP0 < 0)
            {
                posP0 = chainLength;
            }

            if (!LibGpu.RamResolveLink(link, out byte[] nodeBuf, out int nodeOff))
            {
                break;
            }

            link = ReadWord(nodeBuf, nodeOff) & 0x00ffffff;
            chainLength++;
        }

        Check(posP1 >= 0, $"p[1] est dans la chaine de la case 0, position {posP1}");
        Check(posP0 >= 0, $"p[0] est dans la chaine de la case 0, position {posP0}");
        Check(posP1 >= 0 && posP0 == posP1 + 1,
            $"p[1] precede immediatement p[0], positions {posP1} et {posP0}");
        Check((p1.tag & 0x00ffffff) == (uint)contextLow,
            $"p[1] chaine vers p[0], attendu 0x{contextLow:X6}, lu 0x{p1.tag & 0x00ffffff:X6}");
        Check((p.tag >> 24) == 9 && (p1.tag >> 24) == 9,
            $"les deux gardent leur longueur 9, lu {p.tag >> 24} / {p1.tag >> 24}");

        // The sprite renderer runs after the two bands, so anything ahead of them in the chain is
        // its work. Zero would mean DrawSpriteGroup emitted nothing.
        Check(posP1 > 0, $"le renderer de sprites a empile {posP1} primitive(s) devant les bandes");

        Console.WriteLine(
            $"  case 0: {chainLength} primitives, p[1] en position {posP1}, p[0] en {posP0}");

        // --- la machine a etats, etat 1: le fondu monte de 8 par frame jusqu'a 0x80 ---
        int frames = 0;
        while (p2.ReadHalf(0) == 1 && frames < 32)
        {
            TaskSystem.ExecuteTaskList(6);
            frames++;
        }

        Check(frames == 16, $"le fondu prend 16 frames pour atteindre 0x80, compte {frames}");
        Check(p2.u2 == 0x80, $"le fondu culmine a 0x80, lu 0x{p2.u2:X}");
        Check(p2.ReadHalf(0) == 2, $"l'etat passe a 2, lu {p2.ReadHalf(0)}");

        // --- etat 2: la bande glisse de 0x50 par frame et l'offset descend de 0xa0 ---
        short xBefore = p2.x0;
        TaskSystem.ExecuteTaskList(6);
        Check(p2.x0 == (short)(xBefore - 0x50),
            $"x0 recule de 0x50 par frame, {xBefore} -> {p2.x0}");
        Check(p.x0 == (short)-p2.x0, $"p[0].x0 suit -x0, lu {p.x0} pour x0 {p2.x0}");

        // --- la preuve de bout en bout: la chaine atteint-elle la VRAM ---
        // Rien n'est asserti sur CE QUE ca dessine - les couleurs et les UV sont deja verifies
        // champ par champ plus haut. La question ici est la seule qui restait ouverte: la
        // soumission traverse-t-elle jusqu'au framebuffer. C'est exactement ce qui echouait avant,
        // et en silence: le rasteriseur resolvait ses liens par le seul miroir 0x80000000 alors que
        // TITLE.EXE arme son tas a 0x00010000, donc chaque primitive etait jetee sans un mot.
        ushort[] before = new ushort[LibGpu.Vram.Length];
        Array.Copy(LibGpu.Vram, before, LibGpu.Vram.Length);

        LibGpu.DrawOTag(unchecked((int)0x800A6830));

        int changed = 0;
        for (int i = 0; i < LibGpu.Vram.Length; i++)
        {
            if (LibGpu.Vram[i] != before[i])
            {
                changed++;
            }
        }

        Check(changed > 0, $"la soumission ecrit dans la VRAM, {changed} cellules changees");
        Console.WriteLine($"  VRAM: {changed} cellules changees par la soumission");

        // --- et que les sprites arrivent OPAQUES ---
        // Toutes les primitives de l'ecran titre portent le code 0x2E, semi-transparent, mais leurs
        // CLUT ne disent pas la meme chose: les deux bandes echantillonnent le texel 0xFFFF que la
        // tache titre televerse elle-meme, bit 15 pose, tandis que les 255 et 146 entrees non nulles
        // des CLUT du logo et de l'artwork ont toutes le bit 15 a zero. Sur le materiel une
        // primitive TEXTUREE ne mélange que les texels qui portent ce bit; les autres sont opaques.
        //
        // Melanger tout le monde divisait l'image par deux, et c'est invisible sans regarder l'ecran.
        // L'invariant qui separe les deux cas sans seuil arbitraire: contre un fond noir, aucun mode
        // de melange ne peut produire un canal a 31. abr=0 rend f/2, abr=3 rend f/4, abr=2 soustrait.
        // Seul un texel dessine opaque atteint le maximum.
        int maxChannel = 0;
        int saturated = 0;
        for (int y = 0; y < 240; y++)
        {
            for (int x = 0; x < 320; x++)
            {
                ushort v = LibGpu.Vram[(y * 1024) + x];
                int r5 = v & 0x1f;
                int g5 = (v >> 5) & 0x1f;
                int b5 = (v >> 10) & 0x1f;
                if (r5 > maxChannel) { maxChannel = r5; }
                if (g5 > maxChannel) { maxChannel = g5; }
                if (b5 > maxChannel) { maxChannel = b5; }
                if (r5 == 31 || g5 == 31 || b5 == 31) { saturated++; }
            }
        }

        Check(maxChannel == 31,
            $"un texel au moins arrive opaque, canal maximum {maxChannel} sur 31");

        // Un seul pixel a 31 pourrait etre un hasard de melange; des milliers ne le peuvent pas.
        Check(saturated > 1000,
            $"les texts satures arrivent en nombre, {saturated} pixels a 31");
        Console.WriteLine($"  canal maximum: {maxChannel} / 31, {saturated} pixels satures");

        Console.WriteLine(s_failures == 0
            ? "TITLE-TASK: toutes les verifications passent"
            : $"TITLE-TASK: {s_failures} echec(s)");
        return s_failures == 0 ? 0 : 1;
    }

    private static void CheckQuad(POLY_FT4Ref q, string label, uint length, byte code, ushort tpage)
    {
        Check((q.tag >> 24) == length, $"{label}: longueur {length}, lu {q.tag >> 24}");
        Check(q.code == code, $"{label}: code 0x{code:X2}, lu 0x{q.code:X2}");
        Check(q.tpage == tpage, $"{label}: tpage 0x{tpage:X2}, lu 0x{q.tpage:X2}");
        Check(q.r0 == 0x60 && q.g0 == 0x60 && q.b0 == 0x60,
            $"{label}: gris 0x60, lu {q.r0:X2}/{q.g0:X2}/{q.b0:X2}");
        Check(q.u0 == 0 && q.u1 == 0 && q.u2 == 0 && q.u3 == 0, $"{label}: tous les u a 0");
        Check(q.v0 == 0xff && q.v1 == 0xff && q.v2 == 0xff && q.v3 == 0xff,
            $"{label}: tous les v a 0xff");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition)
        {
            s_failures++;
            Console.WriteLine($"  ECHEC: {label}");
        }
    }

    private static uint ReadWord(byte[] b, int o) =>
        (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
}

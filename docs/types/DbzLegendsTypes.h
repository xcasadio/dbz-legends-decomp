/* DbzLegendsTypes -- les enregistrements de VS.EXE, derives de data/VS.EXE.
 *
 * LA SOURCE DE VERITE EST L ARCHIVE GHIDRA DbzLegendsTypes (.gdt). Ce fichier en est
 * le MIROIR TEXTE, tenu a jour parce qu une archive .gdt est binaire et qu un git diff
 * dessus ne dit rien ; ici, un champ qui change se voit.
 *
 * GENERE pour les structures derivees (struct_fields.py --emit-c), ECRIT A LA MAIN pour
 * les syntheses que l outil ne fait pas (les tableaux du contexte, le noeud lu chez
 * CreateTask). Voir docs/tasks/VS_EXE_STRUCTURES.md pour la methode et les controles.
 *
 * Un type deja source depuis l archive se met a jour par parse-c-structure sous son
 * nom ; un type nouveau ne peut etre cree que dans le programme, puis deplace dans
 * l archive a la main.
 */

/* L EN-TETE DE LECTEUR DE FLUX D ANIMATION, partage par tout espace de travail qui joue
 * un flux. Prouve par ses deux fonctions et par qui les appelle :
 *
 *   BindAnimStream (0x80053970) : frame = 0, field_6 = 0, streamPtr = table[index]
 *       (+ streamBase quand l entree est relative, c est-a-dire < 0x80000000)
 *   StepAnimStream (0x800539D0) : frame += 1, puis parcourt le flux depuis streamPtr par
 *       entrees [debut, fin, longueur | opcode] et distribue chaque opcode dont la fenetre
 *       contient frame via PTR_LAB_80083C10 -- LA TABLE DE 51 OPCODES que check_vs_dispatch.py
 *       verifie. Au terminateur 0xFFFF : si fin <= frame, frame = 0 (boucle) ; si field_6 == 0,
 *       field_6 = 1.
 *
 * ET LES DEUX RECOIVENT DES COMBATTANTS AUSSI BIEN QUE DES EVENEMENTS : ActivateFighterInSlot
 * fait `BindAnimStream(fighter, *(fighter->characterBank + 0x38), 0); StepAnimStream(fighter)`,
 * et l etape 9.5 d UpdateFighter (FUN_80047688) rappelle StepAnimStream(fighter) chaque frame.
 * C est pourquoi FighterRecord et AttackEventRecord commencent par la meme structure -- et
 * pourquoi le +0x04 du combattant, que le portage appelait animFrameCounter, EST la frame.
 *
 * +0x0C EN FAIT PARTIE, contrairement a ce que j avais d abord ecrit : CreateFighterTask
 * (0x800512CC) y range &+0x10 comme CreateAttackEventTask y range &+0x80. Ce ne sont pas
 * les lecteurs de flux qui l ecrivent, ce sont les createurs. */
struct AnimStreamHeader {
    int      streamBase;            /* +0x00 : ajoute aux pointeurs de flux relatifs */
    ushort   frame;                 /* +0x04 : incremente par StepAnimStream, 0 = flux boucle/fini */
    ushort   field_6;               /* +0x06 : 0 a la liaison, 1 apres le premier terminateur */
    ushort   *streamPtr;            /* +0x08 : le curseur dans le flux */
    void     **vmBindings;          /* +0x0C : la table des variables de VM -- +0x10 chez le
                                       combattant (28 entrees), +0x80 chez l evenement (10) ;
                                       les deux createurs l ecrivent, d ou sa place ici */
};

/* LE COMBATTANT, lu chez ses DEUX createurs : CreateFighterTask (0x800512CC, ex-
 * FUN_800512cc) alloue `CreateTask(UpdateFighter, 0, 10, 0x240, 1, ...)` -- 0x240 EST LA
 * TAILLE -- et remplit la table de liaisons, les bornes, les noeuds ; ActivateFighterInSlot
 * (0x80027340) le relie ensuite a son personnage. Puis UpdateFighter (0x80050AE4, la tache)
 * borne la position contre posMin/posMax a chaque frame, et reecrit opponentTaskNode avec
 * le resultat de FUN_8004fa8c (repli sur soi-meme) -- c est ce qui distingue +0xAC de
 * +0x104, que le createur remplit tous deux avec le noeud propre. */
struct FighterRecord {
    struct AnimStreamHeader anim;   /* +0x00..+0x0F ; vmBindings -> &binding_10 */
    void     *binding_10;           /* +0x10..+0x7C : 28 variables de VM, chacune un pointeur */
    void     *binding_14;           /*   dans l enregistrement, posees par CreateFighterTask : */
    SVECTOR  *opponentPos;          /*   +0x18 = &pos de l adversaire (UpdateFighter, chaque frame) */
    void     *binding_1c;           /*   -> +0x80 bankEntry5      */
    void     *binding_20;           /*   -> +0xD0                 */
    void     *binding_24;           /*   -> +0xB0 posMin          */
    void     *binding_28;           /*   -> +0xB8 posMax          */
    void     *binding_2c;           /*   -> +0x134 flagsB         */
    void     *binding_30;           /*   -> +0x16B prevStateOpcode      */
    void     *binding_34;           /*   -> l enregistrement lui-meme */
    void     *binding_38;           /*   -> +0xF4                 */
    void     *binding_3c;           /*   -> +0x124                */
    void     *binding_40;           /*   -> +0x12C                */
    void     *binding_44;           /*   -> +0xE0                 */
    void     *binding_48;           /*   -> +0xC0                 */
    void     *binding_4c;           /*   -> +0x162                */
    ushort   *slotRecordPtr;        /* +0x50 = &ctx->slotRecord[slot][2] (ActivateFighterInSlot) */
    void     *binding_54;           /*   -> +0x16A stateOpcode    */
    void     *binding_58;           /*   -> +0x226                */
    void     *binding_5c;           /*   -> +0x176                */
    void     *opponentListLink;     /* +0x60 = &listNext de l adversaire (UpdateFighter) */
    struct TaskNode *binding_64;    /*   -> le noeud propre       */
    void     *binding_68;           /*   -> +0x160 characterIndex */
    void     *characterRow;         /* +0x6C = 0x80082ED4 + (characterId - 1) * 8 */
    void     *binding_70;           /*   -> +0x138 flagsA         */
    void     *binding_74;           /*   -> +0xC8                 */
    void     *binding_78;           /*   -> +0x228                */
    void     *binding_7c;           /*   -> +0x224                */
    void     *bankEntry5;           /* +0x80..+0x90 : entrees 5, 0, 6, 7, 12 de characterBank */
    void     *bankEntry0;
    void     *bankEntry6;
    void     *bankEntry7;
    void     *bankEntry12;
    byte     *frameImageBlob;       /* +0x94 : source LZSS de la frame, 1er argument de DecompressAndLoadImage (UploadFighterTexture) */
    int      *spriteGroup;          /* +0x98 : 1er argument (record) de DrawSpriteGroup, lu par DrawFighterSprite */
    undefined1 pad_9c[8];
    int      field_a4;
    byte     field_a8;
    undefined1 pad_a9[1];
    byte     field_aa;
    undefined1 pad_ab[1];
    struct TaskNode *opponentTaskNode;  /* +0xAC : FUN_8004fa8c(fighter) ou soi-meme, chaque frame */
    SVECTOR  posMin;                /* +0xB0 : (-480, -768, -480), ou (-20000, -3000, -20000) en mode 1 */
    SVECTOR  posMax;                /* +0xB8 : ( 480,  120,  480), ou ( 20000,   120,  20000) en mode 1 */
    undefined1 pad_c0[8];           /* +0xC0/+0xC4 recoivent field_50/field_54 de l evenement au coup */
    undefined2 field_c8;
    undefined2 field_ca;
    undefined2 field_cc;
    undefined1 pad_ce[14];
    uint     pendingHitRequest0;    /* +0xDC : dernier mot du bloc VM de 16 octets a +0xD0 (binding_20) ; octet 1 = sorte du coup, 0x80 = cible alternative */
    undefined1 pad_e0[12];
    uint     pendingHitRequest1;    /* +0xEC : idem pour le bloc a +0xE0 (binding_44) ; seule UpdateOutOfPlayFighter livre l indice 1 */
    struct BattleContext *battleContext;   /* +0xF0, le 1er argument de CreateFighterTask */
    void     *hitTargetLink;        /* +0xF4 : pointe sur le listNext (+0xF8) d un autre combattant (FUN_80055dfc le compare a opponent->listNext) ; AUCUN ecrivain dans VS.EXE */
    void     *listNext;             /* +0xF8/+0xFC : maillon de la liste ancree a DAT_80083CB4 */
    void     *listPrev;
    struct TaskNode *hitAttackerTaskNode; /* +0x100 : g_CurrentTask tampon par DeliverPendingHitEvent sur la cible ; aucun lecteur trouve */
    struct TaskNode *ownTaskNode;   /* +0x104 : le noeud propre, jamais reecrit */
    short    *hitHullTable;         /* +0x108 : charge du noeud de proximite = 0x80101BA4, coque statique (6 normales, 24 coins) lue par FUN_80045130 */
    short    cellX;                 /* +0x10C/+0x10E : cle de grille = pos.x >> 9, pos.z >> 9 (FUN_80045814), phase large de FUN_80045130 */
    short    cellZ;
    ushort   hitStamp;              /* +0x110 : recoit le demi-mot de requete du coup livre (DeliverPendingHitEvent) et le tampon 0x40 de FUN_80045130 */
    undefined1 pad_112[2];
    SVECTOR  pos;                   /* +0x114 : bornee par posMin/posMax dans UpdateFighter ;
                                       vy <= 0 toujours, et >= posMin.vy quand stateOpcode == 0 ;
                                       passee a DistanceBetweenPositions, ComputeYawPitchToTarget */
    SVECTOR  rot;                   /* +0x11C : variable VM n.1 (binding_14). Le rasoir est le
                                       couple de resolveurs de la VM d animation : FUN_8003f228
                                       rend fighter + 0x114 pour un vecteur de POSITION, et
                                       FUN_8003f2b0 rend fighter + 0x11C pour un vecteur de
                                       ROTATION, sur les memes pointeurs de creneau ; rotate_set
                                       (FUN_8003c3c0) passe ce dernier a FUN_80047550, qui lit
                                       +2 et +4 comme angles de RotMatrix. Mon refus precedent
                                       ne regardait que l aura, qui recoit &rot sans le lire. */
    uint     field_124;             /* +0x124..+0x130 : quatre mots, le descripteur d attaque du portage */
    uint     field_128;
    uint     field_12c;
    uint     field_130;
    uint     flagsB;                /* +0x134 : masques partout ; octet bas = code de vue (FUN_80055c6c) */
    uint     flagsA;                /* +0x138 : masques partout, voir la table des bits ci-dessous */
    int      lastDrawDepthKey;      /* +0x13C : retour de DrawSpriteGroup (0x800 - z moyen, -1 ecarte, 0 pool plein) */
    int      otBias;                /* +0x140 : 10e argument (otBias) de DrawSpriteGroup */
    void     *characterData;        /* +0x144 = ctx->characterData[slot] */
    void     *characterBank;        /* +0x148 = characterData + *characterData */
    byte     *uploadedImageBlob;    /* +0x14C : dernier frameImageBlob envoye en VRAM (UploadFighterTexture) */
    byte     tintR;                 /* +0x150..+0x152 : les r,g,b types de DrawSpriteGroup ; 0x80 neutre, 0xFF vif */
    byte     tintG;
    byte     tintB;
    undefined1 pad_153[1];
    ushort   field_154;
    ushort   texVramX;              /* +0x156/+0x158 : arithmetique GetTPage dans DrawFighterSprite, x/y de LoadImage */
    ushort   texVramY;
    ushort   clutId;                /* +0x15A */
    undefined2 field_15c;
    short    field_15e;             /* lu signe (lh) : compteur de frames a role multiple, non nomme */
    short    characterIndex;        /* +0x160, le 2e argument de CreateFighterTask */
    ushort   field_162;
    ushort   field_164;
    ushort   field_166;
    ushort   field_168;
    byte     stateOpcode;           /* +0x16A */
    byte     prevStateOpcode;       /* +0x16B : FighterSetState y archive stateOpcode avant d ecrire le nouveau */
    undefined1 pad_16c[1];
    undefined1 field_16d;
    undefined1 pad_16e[2];
    undefined1 field_170;
    undefined1 field_171;
    undefined1 pad_172[1];
    byte     slotIndex;             /* +0x173, le 3e argument de CreateFighterTask */
    byte     field_174;
    undefined1 pad_175[1];
    ushort   field_176;
    undefined1 pad_178[8];
    uint     padStateHistory[20];   /* +0x180 : les anneaux de FighterInput.cs, passes aux dix */
    uint     padEdgeHistory[20];    /* +0x1D0 :   decodeurs 0x80047E18..0x8004C300 ; pavent 0x220 */
    int      repeatedFaceButton;    /* +0x220 */
    char     state21Frames;         /* +0x224 : compteur de l etat 0x21, +1 par frame borne a 40 (FUN_8004bf50), lu signe */
    byte     field_225;
    byte     field_226;
    char     effectSlotCount;       /* +0x227 : entrees vivantes de la table d effets 0x8008D610 (30 x 0x24), plafond 5 ; +1 allocation, -1 retrait, 0 par FUN_80026a28 */
    byte     state1cFrames;         /* +0x228 : compte a rebours de l etat 0x1C, 5 ou 15 selon flagsA & 0x80000 (FUN_8004aa9c), -1 par frame (FUN_8004ad80) */
    char     comboClearDelay;       /* +0x229 : 20 tant que flagsA & 0x40, sinon decremente ; < 0 => comboCount = 0 */
    short    opponentLockFrames;    /* +0x22A : SelectOpponentTask le pose a 0x3C au changement de cible, le decremente, le remet a 0 sous flagsA 0x30000000 */
    byte     inputFlags;            /* +0x22C */
    byte     archetypeIndex;        /* +0x22D = ctx->slotDisplay[slot][0xC] */
    undefined1 pad_22e[3];
    byte     field_231;
    byte     field_232;
    byte     field_233;
    ushort   field_234;
    ushort   field_236;
    ushort   field_238;
    undefined1 pad_23a[6];          /* jusqu a 0x240, la taille passee a CreateTask */
};

/* LES BITS DE flagsA (+0x138), tels que le passage UpdateFighter les a etablis, ecrivains a l appui.
 * Le degre est celui des refuteurs : [D] decisif, [F] fort, [?] non etabli.
 *
 *   0x80000000 [D] gel par le gestionnaire de combat (RunBattleManagerFrame) : la frame se reduit a FUN_8005070c
 *   0x40000000 [D] orientation : pose par UpdateFighterFacingFlag quand l adversaire est a droite en espace vue
 *   0x20000000 [D] creneau actif de l equipe B cette frame   } miroir de actingSlotTeamA/B, ecrit par
 *   0x10000000 [D] creneau actif de l equipe A cette frame   } UpdateFighter seul, sous ctx->flags & 0x100000
 *   0x08000000 [D] ne pas dessiner : le quintet SetFighterTint..DriveFighterAura est saute
 *   0x04000000 [D] KO / hors jeu : pose par RunBattleManagerFrame (status & 0x200, health == 0) ; les masques
 *                  `flagsA &= 0x0E000000` le CONSERVENT (ils gardent 25/26/27), c est ce qui le rend collant
 *   0x02000000 [F] commande figee : command = 0, SelectFighterCommand non appele (ActivateFighterInSlot, gestionnaire)
 *   0x00100000 [?] jamais pose dans VS.EXE, seulement efface
 *   0x00040000 [D] teinte vive : SetFighterTint ecrit 0xFF au lieu de 0x80
 *   0x00007F00 [F] un etat de reaction est en cours -> DispatchFighterReactionState
 *   0x000200FF [F] une action de base est en cours -> DispatchFighterActionState
 *   0x00027FFF [F] masque « occupe » de SelectOpponentTask : la cible est gardee
 *   0x00000040 [F] enchainement : +1 comboCount par coup porte, comboClearDelay maintenu a 20
 *
 * LES BITS DE flagsB (+0x134) :
 *   0x80000000 [F] un coup a ete leve pendant le pas d animation ; efface en tete de StepFighterAnimAndProximity,
 *                  teste juste apres ; l ecrivain passe par binding_2c (deplacement 0), pas par +0x134
 *   0x20000000 [D] le coup en attente a deja ete livre (DeliverPendingHitEvent, seul poseur)
 *   0x04000000 [D] tenu en attente : UpdateHeldFighter ; SetFighterTint ne touche plus la teinte ;
 *                  relache avec le bit 25 par RunBattleRound (0xF9FFFFFF) en fin de manche
 *   0x02000000 [D] demande de passage au corps reduit, consommee par FUN_800501b8 / UpdateOutOfPlayFighter
 *   0x000000C0 [D] bits 6/7 -> drapeaux 0x4000/0x8000 de DrawSpriteGroup (retournement vertical / horizontal)
 *   0x000000BF [D] octet bas = code de vue compacte : bits 0..4 index de vue, bit 7 miroir (FUN_80055c6c)
 *
 * ctx->flags (+0x10) 0x100000 [F] autorise le marquage des creneaux actifs ; transition 22 -> 20 -> 21 a 0x80056F74.
 *
 * LE NOEUD DE PROXIMITE. +0xF8..+0x11B est aussi lu comme un noeud de liste intrusive (tete 0x80083CB4) par
 * FUN_80045998 / FUN_80045a38 / FUN_80045814 / FUN_80045130 : +0 listNext, +4 listPrev, +8 hitAttackerTaskNode,
 * +0xC ownTaskNode, +0x10 hitHullTable, +0x14 cellX, +0x16 cellZ, +0x18 hitStamp, +0x1C pos.vx, +0x20 pos.vz.
 * Il chevauche des champs deja nommes : la disposition reste plate, seuls cellX/cellZ ont quitte le padding.
 */

/* L ENREGISTREMENT D EVENEMENT D ATTAQUE, lu chez son createur.
 *
 * CreateAttackEventTask @ 0x80043598 fait `CreateTask(UpdateAttackEventTask, 0, 0xb,
 * 0xC0, 0, ...)` puis travaille sur `*(noeud + 8)`, c est a dire l espace de travail
 * du noeud. DEUX CHOSES EN DECOULENT, et aucune n est une supposition :
 *
 *   - LA TAILLE EST 0xC0, donnee par l appel lui-meme. La carte derivee par
 *     struct_fields.py depuis FUN_8004EE48 s arretait a 0xBD ; les deux concordent,
 *     et c est la premiere confirmation independante d une taille dans ce fichier.
 *   - `*(travail + 0x3C) = DAT_8008d16c`, et DAT_8008d16c est g_CurrentTask. DONC
 *     +0x3C EST UN NOEUD DE TACHE.
 *
 * UNE CORRECTION DE MA CORRECTION. J avais d abord nomme +0x3C et +0x70
 * attackerTaskNode / targetTaskNode, puis je les avais renommes en ...Context en
 * raisonnant que `*(cible + 8) = attaquant` ecrirait par-dessus le champ contexte du
 * scheduler. Le raisonnement etait joli et la ligne 20 ci-dessus le refute : le
 * champ recoit g_CurrentTask, un noeud. Les noms d origine sont retablis.
 *
 * LA CHAINE IMBRIQUEE, telle que FUN_8004EE48 la parcourt :
 *
 *     attackerTaskNode -> context -> slotIndex
 *     ((FighterRecord *)attackerTaskNode->context)->slotIndex     (+8 puis +0x173)
 *
 * une structure qui en contient une autre, et c est le +8 du noeud -- le contexte que
 * CreateTask y range -- qui fait le pont.
 *
 * L ASYMETRIE RESTE OUVERTE, et n est pas lissee ici : le chemin ATTAQUANT passe par
 * `+8` (le contexte du noeud), le chemin CIBLE par `+0x0C` PUIS `+8`
 * (`*(*(target + 0x0C) + 8)`). Or +0x0C d un noeud est le param_5 de CreateTask, qui
 * vaut 0 ou 1 aux sites d appel connus. Soit +0x70 ne pointe pas sur un noeud, soit
 * son +0x0C est reecrit apres coup. A mesurer, pas a trancher.
 *
 * +0x80..+0xA8 EST UNE TABLE DE LIAISONS, mais pas un tableau : CreateAttackEventTask
 * y range DIX pointeurs vers les champs de l enregistrement lui-meme et SAUTE +0x98.
 * J avais d abord ecrit `bindings[11]` ; l union des trois vues (createur, consommateur,
 * tache propre) a montre le trou. +0x0C pointe sur cette table, et le dernier appel,
 * `FUN_80053970(travail, &PTR_DAT_800217F0, drapeaux)`, la relie a la VM d animation.
 */
struct AttackEventRecord {
    struct AnimStreamHeader anim;   /* +0x00..+0x0B : BindAnimStream(record, g_AttackEffectStreams, streamIndex) */
    void     *vmBindings;           /* +0x0C -> &binding_80 */
    undefined4 field_10;            /* +0x10 : la premiere variable liee (binding_80) */
    undefined1 pad_14[20];
    void     *spriteGroup;          /* +0x28 : premier argument de DrawSpriteGroup */
    undefined1 pad_2c[16];
    struct TaskNode *attackerTaskNode;  /* +0x3C = g_CurrentTask a la creation */
    SVECTOR  pos;                   /* +0x40 : recopie du 1er argument de CreateAttackEventTask ;
                                       x et z moins la camera (DAT_1f8000b4/bc) vont a DrawSpriteGroup */
    SVECTOR  rot;                   /* +0x48 : recopie du 2e argument ; vy et vz sont RE-TIRES au
                                       hasard de +/-0x600 et masques & 0xFFF apres un coup -- des
                                       angles PSX sur 12 bits ; passes a RotateVectorByAngles */
    uint     field_50;              /* +0x50/+0x54 : une paire recopiee chez la cible (+0xC0/+0xC4 */
    uint     field_54;              /*   du combattant, +0x2C/+0x30 de son espace de travail)     */
    uint     field_58;              /* +0x58/+0x5C : l autre paire, vers +0x34/+0x38 de la cible  */
    uint     field_5c;
    int      velocity[3];           /* +0x60 : RotateVectorByAngles((speed,0,0), &rot, velocity) --
                                       12 octets, le 4e long d un VECTOR serait eventType */
    uint     eventType;             /* +0x6C, >>8 == 0x80 : la forme cible alternative */
    struct TaskNode *targetTaskNode;    /* +0x70, rempli par la VM via binding_9c */
    int      drawDepth;             /* +0x74 : l argument de profondeur de DrawSpriteGroup */
    int      eventFlags;            /* +0x78 : 0x4000000 a la creation, |= 2 chaque frame, bit 31 = coup a resoudre */
    short    speed;                 /* +0x7C : le 3e argument de CreateAttackEventTask ; c est le vx tourne */
    byte     spriteCell;            /* +0x7E = LookupSpriteCell(rot.vz, rot.vy) : 6 bits de cellule, 2 de miroir */
    undefined1 pad_7f[1];
    void     *binding_80;           /* +0x80..+0xA8 : dix pointeurs DANS l enregistrement -- +0x10, */
    void     *binding_84;           /*   +0x40, +0x48, +0x60, +0x78, +0x7E, puis +0x70, +0x50,  */
    void     *binding_88;           /*   +0x58, +0x7C -- les variables que la VM d animation lit  */
    void     *binding_8c;           /*   et ecrit. +0x98 N EST PAS ECRIT : pas un tableau de onze. */
    void     *binding_90;
    void     *binding_94;
    undefined1 pad_98[4];
    void     *binding_9c;
    void     *binding_a0;
    void     *binding_a4;
    void     *binding_a8;
    undefined1 pad_ac[16];
    byte     field_bc;              /* +0xBC : == 3 teste avec les bits 0x6000000 de flagsB de la cible */
    undefined1 pad_bd[3];
};

/* LE NOEUD DU SCHEDULER, lu chez son proprietaire : FUN_80053330 @ 0x80053330,
 * le CreateTask de VS.EXE (42 appelants). Ghidra type son pointeur en
 * `undefined2 *`, DONC LES DECALAGES QU IL IMPRIME SONT EN DEMI-MOTS -- c est le
 * piege de cette fonction, et tout se remet en place une fois double :
 *
 *     *puVar2          = id            -> +0x00, demi-mot
 *     puVar2[1]        = 0             -> +0x02, demi-mot
 *     puVar2 + 2       = param_1       -> +0x04, le POINT D ENTREE
 *     puVar2 + 4       = puVar2 + 0xc  -> +0x08, le CONTEXTE, qui pointe sur +0x18
 *     puVar2 + 6       = param_5       -> +0x0C
 *     puVar2 + 8       = ...           -> +0x10, chainage
 *     puVar2 + 10      = ...           -> +0x14, chainage
 *     l espace de travail commence a puVar2 + 0xc -> +0x18
 *
 * ET L ALLOCATION LE CONFIRME : `FUN_80062f94(taille + ... + 0x18)`. L en-tete fait
 * 0x18 octets et l espace de travail suit EN LIGNE, ce qui est aussi pourquoi le
 * contexte pointe sur +0x18 quand la taille est non nulle, et vaut 0 sinon.
 *
 * LE SENS DU CHAINAGE, tire de l insertion en queue (lignes 60-61) : la queue recoit
 * `queue->+0x14 = nouveau` et le nouveau recoit `nouveau->+0x10 = queue`. Donc +0x14
 * va vers la queue et +0x10 vers la tete.
 */
struct TaskNode {
    ushort   id;                    /* +0x00 */
    ushort   field_2;               /* +0x02 : bits 0-1 = etat (0 tourne, 1 a supprimer), bit 1 = protege */
    void     *entry;                /* +0x04 : appele sans argument par ExecuteTaskList */
    void     *context;              /* +0x08 -> &workspace, ou 0 si taille nulle */
    int      param5;                /* +0x0C : COMPTEUR DE FRAMES, lu chez ExecuteTaskList --
                                       > 0  decremente chaque frame sans tourner (delai)
                                       = 0  tourne a chaque frame
                                       < 0  tourne, puis +1 ; a -1 le noeud est libere */
    struct TaskNode *prev;          /* +0x10, vers la tete (g_TaskListHead) */
    struct TaskNode *next;          /* +0x14, vers la queue ; c est le sens de parcours */
    /* +0x18 : l espace de travail, en ligne, de la taille demandee a CreateTask */
};

/* LES GLOBAUX DU SCHEDULER, types chez leurs ecrivains. g_CurrentTask a 96 references,
 * mais seuls ExecuteTaskList, DeleteTask et DeleteTaskList l ECRIVENT, et le balayage du
 * programme entier a travers sa valeur ne touche que +2, +4, +0xC, +0x10, +0x14 : rien hors
 * de l en-tete de 0x18. Les trois tables ont 21 entrees : 0x80083B90 - 0x80083B3C = 0x54. */
extern struct TaskNode *g_CurrentTask;          /* 0x8008D16C */
extern ushort           g_CurrentTaskListIndex; /* 0x8008D170 */
extern struct TaskNode *g_TaskListHead[21];     /* 0x80083B3C, le noeud dont prev == 0 */
extern struct TaskNode *g_TaskListTail[21];     /* 0x80083B90, le noeud dont next == 0 */
extern short            g_TaskListCount[21];    /* 0x80083BE4 */
extern ushort          *g_AttackEffectStreams[20]; /* 0x800217F0 : indexee par le 4e argument de CreateAttackEventTask */

/* LA LIGNE DE CRENEAU, une par combattant, douze dans le contexte a 0x15B0, foulee 0x14.
 * Chaque colonne est nommee par la fonction qui lui donne un role :
 *   status            +0x00 : bit 0x200 = creneau hors jeu ; RunBattleManagerFrame pose alors
 *                             flagsA |= 0x4000000 sur son combattant
 *   health            +0x02 : lu signe, borne a [0, 0x640] ; a zero, l etat 0x22 (KO) est pose
 *   kiGauge           +0x04 : lu signe, plafond 16000, seuil 400 (DispatchFighterNeutralCommand)
 *   gaugeContribution +0x08 : seul ecrivain AddSlotGaugeContribution ; RunBattleRound la somme
 *   comboCount        +0x0A : +1 par coup porte (FUN_8004d694/FUN_8004d9f4), borne [0, 99],
 *                             remis a zero par UpdateFighterComboTimer
 *   targetSlotIndex   +0x10 : indice du creneau vise, index de fighterSlot[] (SelectOpponentTask)
 */
struct SlotRecord {
    ushort   status;
    short    health;
    short    kiGauge;
    undefined2 field_6;
    short    gaugeContribution;
    short    comboCount;
    undefined2 field_c;
    undefined2 field_e;
    short    targetSlotIndex;
    undefined2 field_12;
};


/* LE CONTEXTE DE COMBAT, ET IL PAVE DE BOUT EN BOUT.
 *
 * struct_fields.py ne rapporte que des decalages ; ce sont les APPARIEMENTS qui
 * prouvent les tableaux. Dans RunFighterSubstitution la boucle equipe A part de `ctx`
 * et celle d equipe B de `ctx + 0x78`, avec le meme decalage de base, donc chaque
 * tableau se montre deux fois a exactement six foulees d ecart :
 *
 *     0x1520 et 0x1538   ->  6 * 4      = 0x18
 *     0x1550 et 0x1580   ->  6 * 8      = 0x30
 *     0x15B0 et 0x1628   ->  6 * 0x14   = 0x78
 *
 * Et les quatre tableaux se succedent sans trou ni recouvrement :
 *
 *     0x0020 + 12 * 0x1C0 = 0x1520      (la foulee de BuildSlotDigitQuads)
 *     0x1520 + 12 * 4     = 0x1550
 *     0x1550 + 12 * 8     = 0x15B0
 *     0x15B0 + 12 * 0x14  = 0x16A0
 *
 * Douze creneaux, six par equipe, quatre tables paralleles. Un decalage isole ne dit
 * rien de tout cela ; quatre pavages exacts qui s enchainent, si.
 *
 * UNE ERREUR ATTRAPEE ICI, ET ELLE VAUT D ETRE CONNUE. J avais place roundRequest a
 * 0x16B0, parce que la decompilation de RunBattleManagerFrame lit
 * `*(uint *)(ctx + 0x16b0)`. C EST LE MEME PIEGE DES DEMI-MOTS QUE DANS CreateTask :
 * Ghidra type ce local en `undefined2 *`, et 0x16B0 * 2 = 0x2D60. L instruction
 * tranche sans appel -- 0x8005BFF4 est `addu $a0,$s2,$zero`, donc le `ctx` du
 * callee EST $s2, et le chargement voisin est `lw $a1,0x2d60($s2)`. Le portage avait
 * raison avec CtxRoundRequest = 0x2D60. La pretendue collision entre le tableau a
 * 0x16A0 et roundRequest, que j avais signalee, n existe pas : elle venait de la
 * meme mauvaise lecture.
 *
 * MORALE, deux fois payee en une session : un decalage imprime par le decompilateur
 * n a de sens qu avec l unite du pointeur qui le porte. C est exactement ce que les
 * types suppriment.
 */
struct BattleContext {
    undefined1 pad_0[16];
    uint     flags;                     /* +0x10 : champ de bits (0x100000 ouvre le marquage des creneaux actifs) */
    short    actingSlotTeamA;           /* 0..5   */
    short    actingSlotTeamB;           /* 6..11  */
    undefined1 pad_18[8];
    undefined1 slotDisplay[12][448];    /* 0x0020, foulee 0x1C0 : BuildSlotDigitQuads */
    struct TaskNode *fighterSlot[12];   /* 0x1520, un noeud par creneau ; ->context = FighterRecord */
    ushort   slotPose[12][4];           /* 0x1550, foulee 8, trois demi-mots utilises */
    struct SlotRecord slotRecord[12];   /* 0x15B0, foulee 0x14 */
    void     *characterData[12];        /* 0x16A0 : ActivateFighterInSlot y lit fighter->characterData */
    undefined1 pad_16d0[5776];
    uint     roundRequest;              /* 0x2D60 */
};

/* L AURA DU COMBATTANT, lue a son rendu.
 *
 * Six enregistrements de 0x1E58 octets a 0x8008DA48 (g_FighterAuras), remis a zero par
 * FUN_80034D98 (memset 0xB610 = 6 * 0x1E58). Le createur, AllocFighterAura @ 0x8003478C,
 * n ecrit que la cle : characterId et slotIndex, deux demi-mots que UpdateFighterAura
 * relit en un seul mot `characterId | slotIndex << 16`. BuildFighterAuraPrimitives
 * @ 0x80034818 remplit le reste a partir de trois gabarits (PTR_DAT_800811B4 :
 * 8, 40 et 40 quads), et UpdateFighterAura @ 0x800340A8 le pilote a chaque frame.
 *
 * C EST LE RENDU QUI NOMME LA DISPOSITION. RenderFighterAuraPass @ 0x80033C64 fait
 *
 *     RotMatrix(rec + pass*8 + 0x14)               -> SVECTOR rot[3]
 *     translation depuis rec + pass*8 + 0x2C       -> SVECTOR pos[3]   (moins la camera)
 *     ScaleMatrix(rec + pass*0x10 + 0x44)          -> VECTOR  scale[3]
 *     RotAverage4 sur rec + i*0x18 + 0x14C4        -> short   vertices[100][4][3]
 *     xy ecrits dans rec + i*0x34 + 0x74           -> POLY_GT4 prims[100]
 *
 * et 0x74 + 100 * 0x34 = 0x14C4 exactement, 0x14C4 + 100 * 0x18 = 0x1E24 exactement.
 * Les couleurs que SetFighterAuraPassColor ecrit a +0x78/+0x84/+0x90/+0x9C de chaque
 * primitive sont r0..r3 d un POLY_GT4 (foulee 0xC), ce qui ferme le type des paquets.
 *
 * LA MACHINE. phase (+0x04) : 0 repos, 1 debut, 2 en cours, 3 stabilisation, 0xFF fondu.
 * activePass (+0x10) : 1 = aura de deplacement (gabarit 1, un ellipsoide, plus les
 * trainees de 1000 unites du gabarit 0), orientee selon le deplacement du combattant --
 * ratan2 de (pos - lastFighterPos) dans motionPitch/motionYaw, copies dans rot[1].vx/vy,
 * rot[1].vz tournant de -0xA0 par frame ; 2 = aura dressee (gabarit 2), rot[2].vx = -0x400,
 * dont l echelle "eclate" : scaleFactor += scaleVelocity, scaleVelocity -= 300 par frame,
 * chaque composante bloquee a 0x1000, phase 2 quand les trois y sont. Les etats 2..10,
 * 0x1C et 0x2A choisissent la passe 1, tous les autres la passe 2. timer (+0x0C) vaut 10
 * au fondu et 3 au debut, decremente par les fonctions de phase. scale[pass] part des
 * tables par personnage ci-dessous ; scale[0].vz croit de 500 par frame jusqu a
 * scale[1].vz, c est l etirement des trainees.
 *
 * QUATRE TABLES PAR PERSONNAGE, fermees par pavage : 0x800811C0 + 39*4 = 0x8008125C,
 * + 39*4 = 0x800812F8, + 39*16 = 0x80081568, + 39*16 = 0x800817D8, sans trou. Le
 * decalage en y est soustrait a pos[pass].vy, le VECTOR va dans scale[pass].
 *
 * fighterRot (&fighter->rot) est passe aux six fonctions de phase et lu par aucune
 * (struct_fields.py sur $a1 : carte vide). SettleUprightAura recoit un quatrieme
 * argument, l etat, charge dans $a3 par le slot de delai de 0x800346EC, et ne le lit pas
 * non plus. field_1e50 est lu comme un mot et jamais nomme : son role n est pas vu.
 */
struct FighterAuraRecord {
    ushort   characterId;           /* +0x00 : id du noeud de tache, i.e. du personnage */
    ushort   slotIndex;             /* +0x02 */
    byte     phase;                 /* +0x04 */
    byte     primCount;             /* +0x05 : curseur de primitives des trois passes */
    byte     passFirst[3];          /* +0x06 : premiere primitive de la passe 0/1/2 */
    byte     passCount[3];          /* +0x09 : nombre de primitives de la passe 0/1/2 */
    int      timer;                 /* +0x0C : frames restantes */
    int      activePass;            /* +0x10 : 1 ou 2 */
    SVECTOR  rot[3];                /* +0x14 : une par passe, RotMatrix */
    SVECTOR  pos[3];                /* +0x2C : une par passe, translation */
    VECTOR   scale[3];              /* +0x44 : une par passe, ScaleMatrix */
    POLY_GT4 prims[100];            /* +0x74 : paquets GPU, code 0x3C ou 0x34 */
    short    vertices[100][4][3];   /* +0x14C4 : les quatre coins de chaque quad */
    VECTOR   scaleVelocity;         /* +0x1E24 */
    VECTOR   scaleFactor;           /* +0x1E34 : virgule fixe 12 bits, 0x1000 = 1.0 */
    SVECTOR  lastFighterPos;        /* +0x1E44 */
    short    motionPitch;           /* +0x1E4C : ratan2(-dy, dz) */
    short    motionYaw;             /* +0x1E4E : ratan2(dx, sqrt(dz*dz + dy*dy)) */
    uint     field_1e50;
    int      lastStateOpcode;       /* +0x1E54 : l etat de la frame precedente */
};

extern struct FighterAuraRecord g_FighterAuras[6];   /* 0x8008DA48 */
extern int    g_MotionAuraYOffset[39];               /* 0x800811C0 */
extern int    g_UprightAuraYOffset[39];              /* 0x8008125C */
extern VECTOR g_MotionAuraScale[39];                 /* 0x800812F8 */
extern VECTOR g_UprightAuraScale[39];                /* 0x80081568 */

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
    void     *binding_30;           /*   -> +0x16B moveClass      */
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
    undefined4 field_94;
    int      field_98;
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
    undefined4 field_dc;
    undefined1 pad_e0[12];
    undefined4 field_ec;
    struct BattleContext *battleContext;   /* +0xF0, le 1er argument de CreateFighterTask */
    int      field_f4;
    void     *listNext;             /* +0xF8/+0xFC : maillon de la liste ancree a DAT_80083CB4 */
    void     *listPrev;
    undefined1 pad_100[4];
    struct TaskNode *ownTaskNode;   /* +0x104 : le noeud propre, jamais reecrit */
    void     *listPayload;          /* +0x108 : 3e argument de FUN_80045998 */
    undefined1 pad_10c[4];
    ushort   field_110;
    undefined1 pad_112[2];
    SVECTOR  pos;                   /* +0x114 : bornee par posMin/posMax dans UpdateFighter ;
                                       vy <= 0 toujours, et >= posMin.vy quand stateOpcode == 0 ;
                                       passee a DistanceBetweenPositions, ComputeYawPitchToTarget */
    ushort   field_11c;             /* +0x11C..+0x120 : trois demi-mots remis a zero a la creation, */
    ushort   field_11e;             /*   variable VM n.1, passes a FUN_800340a8 par adresse. PAS   */
    ushort   field_120;             /*   nommes rot : cette fonction ne les donne pas a RotMatrix  */
    undefined1 pad_122[2];
    uint     field_124;             /* +0x124..+0x130 : quatre mots, le descripteur d attaque du portage */
    uint     field_128;
    uint     field_12c;
    uint     field_130;
    int      flagsB;
    int      flagsA;
    undefined4 field_13c;
    int      field_140;
    void     *characterData;        /* +0x144 = ctx->characterData[slot] */
    void     *characterBank;        /* +0x148 = characterData + *characterData */
    undefined1 pad_14c[4];
    byte     field_150;
    byte     field_151;
    byte     field_152;
    undefined1 pad_153[1];
    ushort   field_154;
    ushort   field_156;
    ushort   field_158;
    ushort   clutId;                /* +0x15A */
    undefined2 field_15c;
    ushort   field_15e;
    short    characterIndex;        /* +0x160, le 2e argument de CreateFighterTask */
    ushort   field_162;
    ushort   field_164;
    ushort   field_166;
    ushort   field_168;
    byte     stateOpcode;           /* +0x16A */
    byte     moveClass;             /* +0x16B */
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
    byte     field_224;
    byte     field_225;
    byte     field_226;
    undefined1 pad_227[1];
    byte     field_228;
    byte     field_229;
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
    int      field_10;                  /* teste par & 0x100000 en phase 8 (pilotage pad) */
    short    actingSlotTeamA;           /* 0..5   */
    short    actingSlotTeamB;           /* 6..11  */
    undefined1 pad_18[8];
    undefined1 slotDisplay[12][448];    /* 0x0020, foulee 0x1C0 : BuildSlotDigitQuads */
    struct TaskNode *fighterSlot[12];   /* 0x1520, un noeud par creneau ; ->context = FighterRecord */
    ushort   slotPose[12][4];           /* 0x1550, foulee 8, trois demi-mots utilises */
    ushort   slotRecord[12][10];        /* 0x15B0, foulee 0x14, le +0 porte les drapeaux */
    void     *characterData[12];        /* 0x16A0 : ActivateFighterInSlot y lit fighter->characterData */
    undefined1 pad_16d0[5776];
    uint     roundRequest;              /* 0x2D60 */
};

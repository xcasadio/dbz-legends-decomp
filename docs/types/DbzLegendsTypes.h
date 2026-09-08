/* DbzLegendsTypes -- les enregistrements de VS.EXE, derives de data/VS.EXE.
 *
 * GENERE. Ne pas editer a la main : regenerer avec custom-tools/scripts/struct_fields.py
 * (voir docs/tasks/VS_EXE_STRUCTURES.md pour la methode et les controles).
 *
 * A IMPORTER DANS L ARCHIVE DbzLegendsTypes, pas dans le programme :
 *   Ghidra : Window > Data Type Manager > (icone archive) > New File Archive...
 *            nommer DbzLegendsTypes, puis clic droit dessus > Parse C Source...
 *            et donner ce fichier.
 */

struct FighterRecord {
    int      field_0;
    ushort   animFrameCounter;
    ushort   field_6;
    undefined1 pad_8[16];
    undefined4 field_18;  /* seulement ecrit: le signe n est pas etabli */
    undefined1 pad_1c[68];
    undefined4 field_60;  /* seulement ecrit: le signe n est pas etabli */
    undefined1 pad_64[28];
    int      field_80;
    int      field_84;
    undefined1 pad_88[4];
    int      field_8c;
    undefined1 pad_90[4];
    undefined4 field_94;  /* seulement ecrit: le signe n est pas etabli */
    int      field_98;
    undefined1 pad_9c[8];
    int      field_a4;
    byte     field_a8;
    undefined1 pad_a9[1];
    byte     field_aa;
    undefined1 pad_ab[1];
    int      currentTaskNode;
    undefined1 pad_b0[2];
    ushort   field_b2;  /* lu en signe ET en non signe */
    undefined1 pad_b4[20];
    undefined2 field_c8;  /* seulement ecrit: le signe n est pas etabli */
    undefined2 field_ca;  /* seulement ecrit: le signe n est pas etabli */
    undefined2 field_cc;  /* seulement ecrit: le signe n est pas etabli */
    undefined1 pad_ce[14];
    undefined4 field_dc;  /* seulement ecrit: le signe n est pas etabli */
    undefined1 pad_e0[12];
    undefined4 field_ec;  /* seulement ecrit: le signe n est pas etabli */
    int      field_f0;
    int      field_f4;
    undefined1 pad_f8[28];
    uint     field_114;  /* aussi lu 2 bytes wide */
    uint     field_118;  /* aussi lu 2 bytes wide */
    undefined1 pad_11c[2];
    ushort   field_11e;
    undefined1 pad_120[4];
    uint     field_124;
    uint     field_128;
    uint     field_12c;
    uint     field_130;
    int      flagsB;
    int      flagsA;
    undefined4 field_13c;  /* seulement ecrit: le signe n est pas etabli */
    int      field_140;
    undefined4 characterData;  /* seulement ecrit: le signe n est pas etabli */
    int      field_148;
    undefined1 pad_14c[4];
    byte     field_150;
    byte     field_151;
    byte     field_152;
    undefined1 pad_153[3];
    ushort   field_156;
    ushort   field_158;
    ushort   field_15a;
    undefined2 field_15c;  /* seulement ecrit: le signe n est pas etabli */
    ushort   field_15e;  /* lu en signe ET en non signe */
    short    field_160;
    ushort   field_162;  /* lu en signe ET en non signe */
    undefined1 pad_164[6];
    byte     stateOpcode;
    byte     moveClass;
    undefined1 pad_16c[1];
    undefined1 field_16d;  /* seulement ecrit: le signe n est pas etabli */
    undefined1 pad_16e[2];
    undefined1 field_170;  /* seulement ecrit: le signe n est pas etabli */
    undefined1 field_171;  /* seulement ecrit: le signe n est pas etabli */
    undefined1 pad_172[1];
    byte     slotIndex;
    undefined1 pad_174[12];
    int      field_180;
    undefined1 pad_184[4];
    int      field_188;
    undefined1 pad_18c[68];
    int      field_1d0;
    undefined1 pad_1d4[76];
    int      field_220;
    byte     field_224;  /* lu en signe ET en non signe */
    byte     field_225;  /* lu en signe ET en non signe */
    byte     field_226;
    undefined1 pad_227[1];
    byte     field_228;
    byte     field_229;
    ushort   field_22a;  /* lu en signe ET en non signe */
    byte     inputFlags;
    byte     archetypeIndex;
    undefined1 pad_22e[3];
    byte     field_231;
    byte     field_232;
    byte     field_233;
    ushort   field_234;  /* lu en signe ET en non signe */
    ushort   field_236;  /* lu en signe ET en non signe */
    ushort   field_238;  /* lu en signe ET en non signe */
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
 * +0x80..+0xA8 EST UNE TABLE DE LIAISONS : CreateAttackEventTask y range onze
 * pointeurs vers les champs de l enregistrement lui-meme (+0x10, +0x40, +0x48, +0x60,
 * +0x78, +0x7E, +0x70, +0x50, +0x58, +0x7C), et +0x0C pointe sur cette table. Le
 * dernier appel, `FUN_80053970(travail, &PTR_DAT_800217F0, drapeaux)`, est ce qui la
 * relie a la VM d animation : ce sont ses variables.
 */
struct AttackEventRecord {
    undefined1 pad_0[12];
    void     *vmBindings;           /* +0x0C -> &bindings (+0x80) */
    undefined1 pad_10[44];
    struct TaskNode *attackerTaskNode;  /* +0x3C = g_CurrentTask a la creation */
    ushort   field_40;              /* +0x40..+0x4C : les six demi-mots recopies des */
    ushort   field_42;              /*   deux cibles passees a CreateAttackEventTask */
    ushort   field_44;
    undefined1 pad_46[2];
    ushort   field_48;
    ushort   field_4a;
    ushort   field_4c;
    undefined1 pad_4e[2];
    uint     field_50;
    uint     field_54;
    uint     field_58;
    uint     field_5c;
    undefined1 pad_60[12];
    uint     eventType;             /* +0x6C, >>8 == 0x80 : la forme cible alternative */
    int      targetTaskNode;        /* +0x70, rempli par la VM via vmBindings */
    undefined1 pad_74[4];
    int      eventFlags;            /* +0x78, initialise a 0x4000000 ; negatif ouvre le coup */
    short    field_7c;              /* +0x7C = le type passe a CreateAttackEventTask */
    undefined1 pad_7e[2];
    void     *bindings[11];         /* +0x80 : pointeurs dans l enregistrement lui-meme */
    undefined1 pad_ac[16];
    byte     field_bc;
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
    ushort   field_2;               /* +0x02, remis a zero a la creation */
    void     *entry;                /* +0x04 */
    void     *context;              /* +0x08 -> &workspace, ou 0 si taille nulle */
    uint     param5;                /* +0x0C */
    struct TaskNode *prev;          /* +0x10 */
    struct TaskNode *next;          /* +0x14 */
    /* +0x18 : l espace de travail, en ligne, de la taille demandee a CreateTask */
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
    int      field_10;                  /* teste par & 0x100000 en phase 8 (pilotage pad) */
    short    actingSlotTeamA;           /* 0..5   */
    short    actingSlotTeamB;           /* 6..11  */
    undefined1 pad_18[8];
    undefined1 slotDisplay[12][448];    /* 0x0020, foulee 0x1C0 : BuildSlotDigitQuads */
    int      fighterSlot[12];           /* 0x1520, espaces de travail de tache combattant */
    ushort   slotPose[12][4];           /* 0x1550, foulee 8, trois demi-mots utilises */
    ushort   slotRecord[12][10];        /* 0x15B0, foulee 0x14, le +0 porte les drapeaux */
    undefined1 pad_16a0[5824];
    uint     roundRequest;              /* 0x2D60 */
};

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

/* CORRECTION. J avais nomme +0x3C et +0x70 `attackerTaskNode` / `targetTaskNode`.
 * La disposition du noeud ci-dessus le CONTREDIT : FUN_8004EE48 fait
 * `*(cible + 8) = attaquant`, or +0x08 d un noeud est le CONTEXTE que le scheduler y
 * range -- y ecrire casserait la tache. Ces deux champs pointent donc sur des
 * ESPACES DE TRAVAIL (des contextes de tache), pas sur des noeuds ; et la chaine
 * `*(*(cible + 0x0C) + 8)` qui rend un combattant traverse alors un vrai noeud range
 * dans le champ +0x0C de cet espace de travail.
 * Renommes en consequence, et volontairement neutres : ce qui est etabli est la
 * FORME de la chaine, pas encore le role de chaque maillon. */
struct AttackEventRecord {
    undefined1 pad_0[60];
    int      attackerContext;   /* +0x3C, un espace de travail de tache */
    undefined1 pad_40[16];
    uint     field_50;
    uint     field_54;
    uint     field_58;
    uint     field_5c;
    undefined1 pad_60[12];
    uint     eventType;  /* aussi lu 1/2 bytes wide */
    int      targetContext;     /* +0x70, idem ; son +0x0C mene au combattant */
    undefined1 pad_74[4];
    int      eventFlags;
    undefined1 pad_7c[64];
    byte     field_bc;
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

/* LES TROIS TABLEAUX PARALLELES, ET POURQUOI ON PEUT L AFFIRMER.
 *
 * struct_fields.py ne rapporte que des decalages. C est l appariement qui prouve les
 * tableaux, et il est net : dans RunFighterSubstitution la boucle equipe A part de
 * `ctx` et la boucle equipe B de `ctx + 0x78`, avec le MEME decalage de base. Pour
 * chacun des trois tableaux, les deux moities apparaissent a exactement 6 foulees
 * d ecart :
 *
 *     0x1520 et 0x1538   ->  6 * 4      = 0x18    pointeurs de noeud de tache
 *     0x1550 et 0x1580   ->  6 * 8      = 0x30    enregistrements de 8 octets
 *     0x15B0 et 0x1628   ->  6 * 0x14   = 0x78    enregistrements de 0x14 octets
 *
 * Et les trois PAVENT sans trou ni recouvrement :
 *     0x1520 + 12*4    = 0x1550
 *     0x1550 + 12*8    = 0x15B0
 *     0x15B0 + 12*0x14 = 0x16A0
 *
 * Douze creneaux, six par equipe. Un decalage isole ne dit rien de tout cela ; le
 * pavage exact, si.
 *
 * A VERIFIER AVANT DE S EN SERVIR : le portage note ailleurs un tableau de pointeurs
 * de donnees de personnage a `ctx + 0x16A0 + creneau*4`, ce qui deborderait sur
 * roundRequest a 0x16B0 des le creneau 4. L une des deux lectures est fausse. Ne pas
 * trancher sans mesurer.
 */
struct BattleContext {
    undefined1 pad_0[16];
    int      field_10;              /* teste par & 0x100000 en phase 8 (pilotage pad) */
    short    actingSlotTeamA;       /* 0..5   */
    short    actingSlotTeamB;       /* 6..11  */
    undefined1 pad_18[5384];
    int      slotTaskNode[12];      /* 0x1520 */
    ushort   slotPose[12][4];       /* 0x1550, foulee 8, trois demi-mots utilises */
    ushort   slotRecord[12][10];    /* 0x15B0, foulee 0x14, le +0 porte les drapeaux */
    undefined1 pad_16a0[16];
    uint     roundRequest;          /* 0x16B0 */
};

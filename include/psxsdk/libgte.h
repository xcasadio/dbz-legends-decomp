#ifndef PSXSDK_LIBGTE_H
#define PSXSDK_LIBGTE_H

/**
 * PSX SDK - GTE Library
 * Geometry Transformation Engine functions and structures
 */

#include "types.h"

/* Vector types */
typedef struct VECTOR {
    s32 vx, vy, vz;
    s32 pad;
} VECTOR;

typedef struct SVECTOR {
    s16 vx, vy, vz;
    s16 pad;
} SVECTOR;

typedef struct CVECTOR {
    u8 r, g, b, cd;
} CVECTOR;

typedef struct DVECTOR {
    s16 vx, vy;
} DVECTOR;

/* Matrix type */
typedef struct MATRIX {
    s16 m[3][3];    /* Rotation matrix */
    s32 t[3];       /* Translation vector */
} MATRIX;

/* Vertex type with screen coords */
typedef struct VERT {
    SVECTOR v;      /* 3D vertex */
    DVECTOR sxy;    /* Screen coords */
    s32 sz;         /* Screen Z (depth) */
    CVECTOR c;      /* Color */
} VERT;

/*---------------------------------------------------------------------------
 * Function Prototypes
 *---------------------------------------------------------------------------*/

void InitGeom(void);
void SetGeomOffset(s32 ofx, s32 ofy);
void SetGeomScreen(s32 h);

void SetRotMatrix(MATRIX* m);
void SetLightMatrix(MATRIX* m);
void SetColorMatrix(MATRIX* m);
void SetTransMatrix(MATRIX* m);
void SetBackColor(s32 r, s32 g, s32 b);
void SetFarColor(s32 r, s32 g, s32 b);

void PushMatrix(void);
void PopMatrix(void);

void RotMatrix(SVECTOR* r, MATRIX* m);
void RotMatrixX(s32 r, MATRIX* m);
void RotMatrixY(s32 r, MATRIX* m);
void RotMatrixZ(s32 r, MATRIX* m);
void TransMatrix(MATRIX* m, VECTOR* v);
void ScaleMatrix(MATRIX* m, VECTOR* v);
void MulMatrix0(MATRIX* m0, MATRIX* m1, MATRIX* m2);
void MulMatrix(MATRIX* m0, MATRIX* m1);
void MulMatrix2(MATRIX* m0, MATRIX* m1);
void CompMatrix(MATRIX* m0, MATRIX* m1, MATRIX* m2);

void ApplyMatrix(MATRIX* m, SVECTOR* v0, VECTOR* v1);
void ApplyMatrixLV(MATRIX* m, VECTOR* v0, VECTOR* v1);
void ApplyMatrixSV(MATRIX* m, SVECTOR* v0, SVECTOR* v1);

s32 RotTransPers(SVECTOR* v0, s32* sxy, s32* p, s32* flag);
s32 RotTransPers3(SVECTOR* v0, SVECTOR* v1, SVECTOR* v2,
                   s32* sxy0, s32* sxy1, s32* sxy2,
                   s32* p, s32* flag);
s32 RotTransPers4(SVECTOR* v0, SVECTOR* v1, SVECTOR* v2, SVECTOR* v3,
                   s32* sxy0, s32* sxy1, s32* sxy2, s32* sxy3,
                   s32* p, s32* flag);
void RotTrans(SVECTOR* v0, VECTOR* v1, s32* flag);
void RotTransSV(SVECTOR* v0, SVECTOR* v1, s32* flag);

s32 RotAverageNclip3(SVECTOR* v0, SVECTOR* v1, SVECTOR* v2,
                      s32* sxy0, s32* sxy1, s32* sxy2,
                      s32* p, s32* otz, s32* flag);
s32 RotAverageNclip4(SVECTOR* v0, SVECTOR* v1, SVECTOR* v2, SVECTOR* v3,
                      s32* sxy0, s32* sxy1, s32* sxy2, s32* sxy3,
                      s32* p, s32* otz, s32* flag);

void NormalColorDpq(SVECTOR* v0, CVECTOR* v1, s32 p, CVECTOR* v2);
void NormalColorCol(SVECTOR* v0, CVECTOR* v1, CVECTOR* v2);

s32 NormalClip(s32 sxy0, s32 sxy1, s32 sxy2);
s32 AverageZ3(s32 sz0, s32 sz1, s32 sz2);
s32 AverageZ4(s32 sz0, s32 sz1, s32 sz2, s32 sz3);

void Square12(VECTOR* v0, VECTOR* v1);
void Square0(VECTOR* v0, VECTOR* v1);

s32 rsin(s32 a);
s32 rcos(s32 a);
s32 ratan2(s32 y, s32 x);
u32 SquareRoot0(u32 a);
u32 SquareRoot12(u32 a);

#endif /* PSXSDK_LIBGTE_H */

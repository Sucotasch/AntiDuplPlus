/*
* AntiDuplPlus Program (http://github.com/Sucotasch/AntiDuplPlus).
*
* Copyright (c) 2023-2026.
*
* Permission is hereby granted, free of charge, to any person obtaining a copy 
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights 
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
* copies of the Software, and to permit persons to whom the Software is 
* furnished to do so, subject to the following conditions:
*
* The above copyright notice and this permission notice shall be included in 
* all copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
* SOFTWARE.
*/
#ifndef __adPhash_h__
#define __adPhash_h__

#include "adConfig.h"

namespace ad
{
    //-------------------------------------------------------------------------
    // 256-битный перцептивный хэш (pHASH) из серого тамба (thumbSize x thumbSize).
    //
    // Рецепт (замерен на корпусе A, см. Research/2026-10-18-dupsearch):
    //   тамб -> bilinear 64x64 -> DCT-II по осям (без нормировки) -> блок [0:16, 0:16]
    //   -> медиана 256 коэффициентов -> бит = (коэффициент > медианы) -> 4 x uint64.
    //
    // variantCount == 1: только исходная ориентация (compare.transformedImage == FALSE).
    // variantCount == 8: замыкание группы D4 (4 поворота x отражение). Варианты выводятся
    // аналитически из одного DCT: транспозиция и смена знаков по индексам
    // (сдвиг по x даёт (-1)^u, сдвиг по y даёт (-1)^v), что точно совпадает с хэшированием
    // повёрнутого изображения, но в 8 раз дешевле.
    //
    // Различие пары: difference = 100 * hamming / 256 (см. TImageComparer_Hash).
    //-------------------------------------------------------------------------
    const int PHASH_VARIANT_MAX = 8;

    void ComputePhashVariants(const TUInt8* thumb, size_t thumbSize, int variantCount, TUInt64 out[PHASH_VARIANT_MAX][4]);
}

#endif//__adPhash_h__

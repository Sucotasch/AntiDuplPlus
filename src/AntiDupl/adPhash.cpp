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
#include "adPhash.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace ad
{
    namespace
    {
        const int PHASH_SIZE = 64;                              // сторона изображения после ресайза
        const int PHASH_BLOCK = 16;                             // блок низких частот 16x16
        const int PHASH_BITS = PHASH_BLOCK * PHASH_BLOCK;       // 256 бит

        const double PHASH_PI = 3.14159265358979323846;

        struct TCosTable
        {
            double value[PHASH_BLOCK][PHASH_SIZE];

            TCosTable()
            {
                for (int u = 0; u < PHASH_BLOCK; ++u)
                    for (int x = 0; x < PHASH_SIZE; ++x)
                        value[u][x] = ::cos(PHASH_PI * u * (2 * x + 1) / (2 * PHASH_SIZE));
            }
        };

        // Инициализация статической таблицы потокобезопасна (C++11), после неё — только чтение.
        const TCosTable& CosTable()
        {
            static const TCosTable table;
            return table;
        }

        // DCT-II без нормировки (аналог scipy.fftpack.dct(norm=None) без общего множителя 2^k,
        // который не влияет на сравнение с медианой) только для коэффициентов [0:16, 0:16]:
        //   F[u][v] = sum_x sum_y f[x][y] * cos(u, x) * cos(v, y)
        void DctLowBlock(const double (&f)[PHASH_SIZE][PHASH_SIZE], double (&block)[PHASH_BLOCK][PHASH_BLOCK])
        {
            const TCosTable& table = CosTable();

            double row[PHASH_BLOCK][PHASH_SIZE];
            for (int u = 0; u < PHASH_BLOCK; ++u)
            {
                const double* cosU = table.value[u];
                for (int y = 0; y < PHASH_SIZE; ++y)
                {
                    double sum = 0;
                    for (int x = 0; x < PHASH_SIZE; ++x)
                        sum += f[x][y] * cosU[x];
                    row[u][y] = sum;
                }
            }

            for (int u = 0; u < PHASH_BLOCK; ++u)
            {
                for (int v = 0; v < PHASH_BLOCK; ++v)
                {
                    const double* cosV = table.value[v];
                    double sum = 0;
                    for (int y = 0; y < PHASH_SIZE; ++y)
                        sum += row[u][y] * cosV[y];
                    block[u][v] = sum;
                }
            }
        }

        // Вариант D4 выводится из базового блока аналитически:
        //   variant bit 0 — транспозиция (F[v][u]);
        //   variant bit 1 — сдвиг по x (знак (-1)^r);
        //   variant bit 2 — сдвиг по y (знак (-1)^c).
        // Это в точности DCT повёрнутого/отражённого изображения (т.к. ресайз — тензорный
        // оператор, он коммутирует с этими преобразованиями).
        void PackVariant(const double (&block)[PHASH_BLOCK][PHASH_BLOCK], int variant, TUInt64 words[4])
        {
            const bool transposed = (variant & 1) != 0;
            const bool flipX = (variant & 2) != 0;
            const bool flipY = (variant & 4) != 0;

            double values[PHASH_BITS];
            int k = 0;
            for (int r = 0; r < PHASH_BLOCK; ++r)
            {
                for (int c = 0; c < PHASH_BLOCK; ++c)
                {
                    double v = transposed ? block[c][r] : block[r][c];
                    if (flipX && (r & 1))
                        v = -v;
                    if (flipY && (c & 1))
                        v = -v;
                    values[k++] = v;
                }
            }

            double sorted[PHASH_BITS];
            std::memcpy(sorted, values, sizeof(values));
            std::sort(sorted, sorted + PHASH_BITS);
            const double median = 0.5 * (sorted[PHASH_BITS / 2 - 1] + sorted[PHASH_BITS / 2]);

            words[0] = words[1] = words[2] = words[3] = 0;
            for (int b = 0; b < PHASH_BITS; ++b)
            {
                if (values[b] > median)
                    words[b / 64] |= (TUInt64)1 << (b % 64);
            }
        }
    }

    void ComputePhashVariants(const TUInt8* thumb, size_t thumbSize, int variantCount, TUInt64 out[PHASH_VARIANT_MAX][4])
    {
        if (variantCount < 1)
            variantCount = 1;
        if (variantCount > PHASH_VARIANT_MAX)
            variantCount = PHASH_VARIANT_MAX;

        if (thumb == NULL || thumbSize == 0)
        {
            std::memset(out, 0, sizeof(TUInt64) * 4 * variantCount);
            return;
        }

        // 1. Bilinear-ресайз тамба в 64x64 (метод ресайза на результат почти не влияет:
        //    замерено F1 0.9411 у bilinear против 0.9414 у LANCZOS).
        double f[PHASH_SIZE][PHASH_SIZE];
        const double scale = (double)thumbSize / PHASH_SIZE;
        for (int dy = 0; dy < PHASH_SIZE; ++dy)
        {
            double sy = (dy + 0.5) * scale - 0.5;
            int y0 = (int)std::floor(sy);
            double fy = sy - y0;
            if (y0 < 0) { y0 = 0; fy = 0.0; }
            if (y0 > (int)thumbSize - 1) { y0 = (int)thumbSize - 1; fy = 0.0; }
            const int y1 = (y0 + 1 < (int)thumbSize) ? y0 + 1 : y0;
            const TUInt8* row0 = thumb + (size_t)y0 * thumbSize;
            const TUInt8* row1 = thumb + (size_t)y1 * thumbSize;

            for (int dx = 0; dx < PHASH_SIZE; ++dx)
            {
                double sx = (dx + 0.5) * scale - 0.5;
                int x0 = (int)std::floor(sx);
                double fx = sx - x0;
                if (x0 < 0) { x0 = 0; fx = 0.0; }
                if (x0 > (int)thumbSize - 1) { x0 = (int)thumbSize - 1; fx = 0.0; }
                const int x1 = (x0 + 1 < (int)thumbSize) ? x0 + 1 : x0;

                const double top = row0[x0] * (1.0 - fx) + row0[x1] * fx;
                const double bottom = row1[x0] * (1.0 - fx) + row1[x1] * fx;
                f[dy][dx] = top * (1.0 - fy) + bottom * fy;
            }
        }

        // 2. Низкочастотный блок DCT и 3. биты по каждому варианту.
        double block[PHASH_BLOCK][PHASH_BLOCK];
        DctLowBlock(f, block);
        for (int v = 0; v < variantCount; ++v)
            PackVariant(block, v, out[v]);
    }
}

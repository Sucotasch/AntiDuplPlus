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
#include "adPerformance.h"
#include "adIO.h"
#include "adNvJpeg.h"

#ifdef AD_NVJPEG_ENABLE
#include <nvjpeg.h>
#include <cuda_runtime.h>
#include <windows.h>
#include <vector>

namespace ad
{
    // --- Типы функций nvJPEG (для динамической загрузки) ---
    typedef nvjpegStatus_t (*nvjpegCreateEx_t)(nvjpegBackend_t, nvjpegDevAllocator_t*, nvjpegPinnedAllocator_t*, unsigned int, nvjpegHandle_t*);
    typedef nvjpegStatus_t (*nvjpegDestroy_t)(nvjpegHandle_t);
    typedef nvjpegStatus_t (*nvjpegJpegStateCreate_t)(nvjpegHandle_t, nvjpegJpegState_t*);
    typedef nvjpegStatus_t (*nvjpegJpegStateDestroy_t)(nvjpegJpegState_t);
    typedef nvjpegStatus_t (*nvjpegGetImageInfo_t)(nvjpegHandle_t, const unsigned char*, size_t, int*, nvjpegChromaSubsampling_t*, int*, int*);
    typedef nvjpegStatus_t (*nvjpegDecode_t)(nvjpegHandle_t, nvjpegJpegState_t, const unsigned char*, size_t, nvjpegOutputFormat_t, nvjpegImage_t*, cudaStream_t);

    // --- Аллокаторы памяти (используем стандартные CUDA функции) ---
    int dev_malloc(void** p, size_t s) { return (int)cudaMalloc(p, s); }
    int dev_free(void* p) { return (int)cudaFree(p); }
    int host_malloc(void** p, size_t s, unsigned int f) { return (int)cudaHostAlloc(p, s, f); }
    int host_free(void* p) { return (int)cudaFreeHost(p); }

    // --- Структура для хранения указателей на загруженные функции ---
    struct NvJpegLib
    {
        HMODULE dll;
        nvjpegCreateEx_t CreateEx;
        nvjpegDestroy_t Destroy;
        nvjpegJpegStateCreate_t StateCreate;
        nvjpegJpegStateDestroy_t StateDestroy;
        nvjpegGetImageInfo_t GetImageInfo;
        nvjpegDecode_t Decode;

        NvJpegLib() : dll(nullptr), CreateEx(nullptr), Destroy(nullptr),
                      StateCreate(nullptr), StateDestroy(nullptr), GetImageInfo(nullptr), Decode(nullptr) {
            // Пытаемся загрузить библиотеку
            dll = LoadLibraryA("nvjpeg64_12.dll");
            if (!dll) return;

            // Получаем адреса функций
            CreateEx = (nvjpegCreateEx_t)GetProcAddress(dll, "nvjpegCreateEx");
            Destroy = (nvjpegDestroy_t)GetProcAddress(dll, "nvjpegDestroy");
            StateCreate = (nvjpegJpegStateCreate_t)GetProcAddress(dll, "nvjpegJpegStateCreate");
            StateDestroy = (nvjpegJpegStateDestroy_t)GetProcAddress(dll, "nvjpegJpegStateDestroy");
            GetImageInfo = (nvjpegGetImageInfo_t)GetProcAddress(dll, "nvjpegGetImageInfo");
            Decode = (nvjpegDecode_t)GetProcAddress(dll, "nvjpegDecode");

            // Если хотя бы одной функции нет - библиотека не подходит
            if (!CreateEx || !Destroy || !StateCreate || !StateDestroy || !GetImageInfo || !Decode) {
                FreeLibrary(dll);
                dll = nullptr;
                CreateEx = nullptr;
            }
        }

        ~NvJpegLib() {
            if (dll) FreeLibrary(dll);
        }

        bool isValid() const { return dll != nullptr; }
    };

    // --- Глобальное состояние (Handle) ---
    static bool s_loadAttempted = false;
    static NvJpegLib* s_lib = nullptr;
    static nvjpegHandle_t s_globalHandle = nullptr;

    static bool ensureLibLoaded() {
        if (s_loadAttempted) return s_lib && s_lib->isValid();
        s_loadAttempted = true;

        s_lib = new NvJpegLib();
        if (!s_lib->isValid()) return false;

        // Инициализируем nvJPEG Handle
        // Используем NVJPEG_BACKEND_DEFAULT (гибридный), так как он наиболее совместим с RTX 4xxx
        nvjpegDevAllocator_t dev_alloc = {&dev_malloc, &dev_free};
        nvjpegPinnedAllocator_t pinned_alloc = {&host_malloc, &host_free};

        nvjpegStatus_t status = s_lib->CreateEx(NVJPEG_BACKEND_DEFAULT, &dev_alloc, &pinned_alloc, 
                                                NVJPEG_FLAGS_DEFAULT, &s_globalHandle);
        if (status != NVJPEG_STATUS_SUCCESS) {
            delete s_lib;
            s_lib = nullptr;
            return false;
        }
        return true;
    }

    // --- Thread-Local состояние для декодирования (State + Stream) ---
    // nvjpegJpegState_t НЕ потокобезопасен, поэтому у каждого потока свой
    struct ThreadState
    {
        nvjpegJpegState_t jpegState;
        cudaStream_t stream;
        bool initialized;

        ThreadState() : jpegState(nullptr), stream(nullptr), initialized(false) {
            if (!ensureLibLoaded() || !s_globalHandle) return;

            // Создаем свой CUDA Stream, НЕ блокирующий (чтобы не дедлочить с другими задачами GPU)
            cudaError_t err = cudaStreamCreateWithFlags(&stream, cudaStreamNonBlocking);
            if (err != cudaSuccess) return;

            // Создаем состояние декодера для этого потока
            nvjpegStatus_t status = s_lib->StateCreate(s_globalHandle, &jpegState);
            if (status == NVJPEG_STATUS_SUCCESS) {
                initialized = true;
            } else {
                cudaStreamDestroy(stream);
                stream = nullptr;
            }
        }

        ~ThreadState() {
            if (jpegState && ensureLibLoaded()) {
                s_lib->StateDestroy(jpegState);
            }
            if (stream) {
                cudaStreamDestroy(stream);
            }
        }
    };

    thread_local ThreadState threadState;

    // --- Публичные методы класса ---

    bool TNvJpeg::IsAvailable() {
        return ensureLibLoaded();
    }

    TNvJpeg * TNvJpeg::Load(HGLOBAL hGlobal, int targetSize)
    {
        // Проверяем доступность библиотеки и инициализацию потока
        if (!hGlobal || !ensureLibLoaded() || !threadState.initialized)
            return NULL;

        const unsigned char * data = (const unsigned char*)::GlobalLock(hGlobal);
        size_t size = ::GlobalSize(hGlobal);
        
        TNvJpeg * pNvJpeg = NULL;

        // Быстрая проверка сигнатуры JPEG
        if (size < 4 || data[0] != 0xFF || data[1] != 0xD8 || data[2] != 0xFF) {
            ::GlobalUnlock(hGlobal);
            return NULL;
        }

        // --- Получаем информацию об изображении ---
        int nComponents = 0;
        nvjpegChromaSubsampling_t subsampling;
        int widths[NVJPEG_MAX_COMPONENT] = {0};
        int heights[NVJPEG_MAX_COMPONENT] = {0};

        nvjpegStatus_t status = s_lib->GetImageInfo(s_globalHandle, data, size, 
                                                    &nComponents, &subsampling, widths, heights);
        
        if (status != NVJPEG_STATUS_SUCCESS || nComponents == 0) {
            ::GlobalUnlock(hGlobal);
            return NULL; // Не удалось распознать или неподдерживаемый формат
        }

        int width = widths[0];
        int height = heights[0];

        if (width == 0 || height == 0) {
            ::GlobalUnlock(hGlobal);
            return NULL;
        }

        // --- Декодирование ---
        // Выделяем буфер на Host (Pinned Memory для скорости, если аллокатор允许)
        // Но для простоты и безопасности используем std::vector, nvjpegDecode сам скопирует в него
        // Нам нужно передать nvjpegImage. Для NVJPEG_OUTPUT_RGBI нужен один канал.
        
        size_t pitch = width * 3; // BGR = 3 байта на пиксель
        size_t bufferSize = pitch * height;

        // Pinned (page-locked) memory для прямого DMA GPU → RAM
        // thread_local — один буфер на поток, переиспользуется между вызовами
        static thread_local unsigned char* s_pinned_buffer = nullptr;
        static thread_local size_t s_pinned_buffer_size = 0;

        if (bufferSize > s_pinned_buffer_size) {
            if (s_pinned_buffer) {
                cudaFreeHost(s_pinned_buffer);
                s_pinned_buffer = nullptr;
            }
            cudaError_t err = cudaHostAlloc((void**)&s_pinned_buffer, bufferSize, cudaHostAllocDefault);
            if (err != cudaSuccess) {
                ::GlobalUnlock(hGlobal);
                return NULL;
            }
            s_pinned_buffer_size = bufferSize;
        }

        nvjpegImage_t nvjpegImage;
        nvjpegImage.channel[0] = s_pinned_buffer;
        nvjpegImage.pitch[0] = pitch;

        // Запускаем декодирование в наш отдельный Stream
        // Используем BGRI — nvJPEG выдаст B,G,R напрямую, что соответствует TView::Bgra32
        status = s_lib->Decode(s_globalHandle, threadState.jpegState, data, size,
                               NVJPEG_OUTPUT_BGRI, &nvjpegImage, threadState.stream);

        if (status != NVJPEG_STATUS_SUCCESS) {
            ::GlobalUnlock(hGlobal);
            return NULL;
        }

        // !!! КРИТИЧНО: Ждем завершения операций в нашем Stream, прежде чем читать данные !!!
        cudaStreamSynchronize(threadState.stream);

        // --- Конвертация BGR -> BGRA (добавляем альфа-канал) ---
        // NVJPEG_OUTPUT_BGRI выдаёт B,G,R — порядок уже правильный для Bgra32
        TView * pView = new TView(width, height, TView::Bgra32, NULL, 4);

        const unsigned char* src = s_pinned_buffer;
        unsigned char* dst = pView->data;
        size_t dstStride = pView->stride;

        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                // src: B G R B G R ... (BGRI from nvJPEG)
                // dst: B G R A B G R A ... (Bgra32)
                int srcIdx = y * pitch + x * 3;
                int dstIdx = y * dstStride + x * 4;

                dst[dstIdx + 0] = src[srcIdx + 0]; // B (уже на месте)
                dst[dstIdx + 1] = src[srcIdx + 1]; // G (уже на месте)
                dst[dstIdx + 2] = src[srcIdx + 2]; // R (уже на месте)
                dst[dstIdx + 3] = 255;             // A
            }
        }

        pNvJpeg = new TNvJpeg();
        pNvJpeg->m_format = TImage::Jpeg;
        pNvJpeg->m_pView = pView;
        pNvJpeg->m_origWidth = width;
        pNvJpeg->m_origHeight = height;

        ::GlobalUnlock(hGlobal);
        return pNvJpeg;
    }

    bool TNvJpeg::Supported(HGLOBAL hGlobal)
    {
        if (!hGlobal || !ensureLibLoaded())
            return false;
            
        const unsigned char * data = (const unsigned char*)::GlobalLock(hGlobal);
        size_t size = ::GlobalSize(hGlobal);
        bool supported = (size >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF);
        ::GlobalUnlock(hGlobal);
        return supported;
    }
}
#endif//AD_NVJPEG_ENABLE

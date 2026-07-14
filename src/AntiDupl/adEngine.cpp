/*
* AntiDupl.NET Program (http://ermig1979.github.io/AntiDupl).
*
* Copyright (c) 2002-2018 Yermalayeu Ihar.
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
#include "adInit.h"
#include "adStatus.h"
#include "adImageInfo.h"
#include "adOptions.h"
#include "adResult.h"
#include "adResultStorage.h"
#include "adImageDataStorage.h"
#include "adDatabaseRegistry.h"
#include "adMistakeStorage.h"
#include "adThreadManagement.h"
#include "adSearcher.h"
#include "adRecycleBin.h"
#include "adEngine.h"

// Debug logging - removed, using fwprintf to trace.log instead
#include "adPerformance.h"
#include "adLogger.h"
#include "adFileUtils.h"
#include "adGPUManager.h"
#include "adGPU.h"
#include "adStatus.h"
#include <windows.h>
#include <vector>
#include <sstream>

#define AD_DEBUG(msg) OutputDebugStringA(msg)
#define AD_DEBUG_FMT(msg, ...) \
    do { \
        char buf[512]; \
        snprintf(buf, sizeof(buf), msg, __VA_ARGS__); \
        OutputDebugStringA(buf); \
    } while(0)

namespace ad
{
    TEngine::TEngine(const TString & userPath)
        : _userPath(userPath)
    {
        AD_DEBUG("TEngine: Constructor starting\n");

#ifdef AD_LOGGER_ENABLE
        TLogger::s_logger.SetFileOut((UserPath() + TEXT("\\debug_log.txt")).c_str(), true);
#endif//AD_LOGGER_ENABLE

        AD_DEBUG("TEngine: Creating TInit\n");
        m_pInit = new TInit();

        AD_DEBUG("TEngine: Creating TOptions\n");
        m_pOptions = new TOptions(userPath);

        AD_DEBUG("TEngine: Creating TStatus\n");
        m_pStatus = new TStatus();

        AD_DEBUG("TEngine: Creating TGpuManager\n");
        m_pGpuManager = new TGpuManager();

        AD_DEBUG("TEngine: TGpuManager created, IsAvailable=1\n");

        if (m_pGpuManager->IsAvailable())
        {
            AD_DEBUG("TEngine: GPU is available, getting device info\n");

            const GpuDeviceInfo& info = m_pGpuManager->DeviceInfo();
            std::stringstream ss;
            ss << "GPU acceleration initialized: " << info.name
               << " (" << (info.totalGlobalMem / (1024 * 1024)) << " MB VRAM, Compute "
               << info.computeMajor << "." << info.computeMinor << ")";
#ifdef AD_LOGGER_ENABLE
            AD_LOG(ss.str().c_str());
#endif//AD_LOGGER_ENABLE

            // GPU Sanity Check: Test mathematical parity
            AD_DEBUG("TEngine: Starting GPU sanity check\n");

            const size_t testSize = 1024;
            uint8_t h_test1[testSize], h_test2[testSize];
            double cpuSum = 0;
            for(size_t i = 0; i < testSize; ++i) {
                h_test1[i] = (uint8_t)(i % 256);
                h_test2[i] = (uint8_t)(255 - (i % 256));
                double diff = (double)h_test1[i] - (double)h_test2[i];
                cpuSum += diff * diff;
            }

            AD_DEBUG("TEngine: Calling GpuCompareSquaredSum\n");

            double gpuSum = GpuCompareSquaredSum(h_test1, h_test2, testSize);

            AD_DEBUG("TEngine: GpuCompareSquaredSum returned\n");

            std::stringstream ts;
            ts << "CUDA Sanity Check: CPU=" << cpuSum << ", GPU=" << gpuSum;
            double tolerance = cpuSum * 0.001;  // 0.1% relative tolerance
            if (fabs(cpuSum - gpuSum) <= tolerance) {
                ts << " [SUCCESS - PARITY MATCH]";
            } else {
                ts << " [FAILURE - MATH MISMATCH] Tolerance: " << tolerance;
            }
#ifdef AD_LOGGER_ENABLE
            AD_LOG(ts.str().c_str());
#endif//AD_LOGGER_ENABLE
        }
        else
        {
            AD_DEBUG("TEngine: GPU not available\n");
#ifdef AD_LOGGER_ENABLE
            AD_LOG("GPU acceleration not available.");
#endif//AD_LOGGER_ENABLE
        }

        AD_DEBUG("TEngine: Creating storage objects\n");

        m_pMistakeStorage = new TMistakeStorage(this);
        m_pImageDataStorage = new TImageDataStorage(this);
        m_pRecycleBin = new TRecycleBin(this);
        m_pResult = new TResultStorage(this);
        m_pImageDataPtrs = new TImageDataPtrs();
        m_pCriticalSection = new TCriticalSection();
        m_pCompareManager = new TCompareManager(this);
        m_pCollectManager = new TCollectManager(this, m_pCompareManager);
        m_pSearcher = new TSearcher(this, m_pImageDataPtrs);
        m_skipComparisonDuringCollection = false;

        AD_DEBUG("TEngine: Constructor finished successfully\n");
    }

    TEngine::~TEngine()
    {
        delete m_pMistakeStorage;
        delete m_pImageDataStorage;
        delete m_pResult;
        delete m_pImageDataPtrs;
        delete m_pCriticalSection;
        delete m_pInit;
        delete m_pCompareManager;
        delete m_pCollectManager;
        delete m_pSearcher;
        delete m_pRecycleBin;
        delete m_pGpuManager;
        delete m_pStatus;
        delete m_pOptions;
#ifdef AD_LOGGER_ENABLE
#ifdef AD_PERFORMANCE_TEST
        TLogger::s_logger.SetThreadIdAnnotation(false);
        AD_LOG(TPerformanceMeasurerStorage::s_storage.Statistic());
#endif//AD_PERFORMANCE_TEST
        TLogger::s_logger.ResetOut();
#endif//AD_LOGGER_ENABLE
    }

    void TEngine::UpdateGpuDatabase()
    {
        AD_DEBUG("UpdateGpuDatabase: Starting\n");

        if (m_pGpuManager && m_pGpuManager->IsAvailable())
        {
            AD_DEBUG("UpdateGpuDatabase: GPU is available\n");

            const TImageDataStorage::TStorage& storage = m_pImageDataStorage->Storage();
            AD_DEBUG("UpdateGpuDatabase: Storage size\n");

            size_t reducedImageSize = m_pOptions->advanced.reducedImageSize;
            size_t thumbSize = reducedImageSize * reducedImageSize;
            AD_DEBUG("UpdateGpuDatabase: reducedImageSize and thumbSize calculated\n");

            // Ensure GPU has enough capacity for the current database
            AD_DEBUG("UpdateGpuDatabase: Calling EnsureCapacity\n");

            if (!m_pGpuManager->EnsureCapacity(storage.size(), thumbSize))
            {
                AD_DEBUG("UpdateGpuDatabase: EnsureCapacity FAILED\n");
#ifdef AD_LOGGER_ENABLE
                AD_LOG("GPU: Failed to ensure capacity for database.");
#endif
                return;
            }

            AD_DEBUG("UpdateGpuDatabase: EnsureCapacity succeeded\n");

            size_t count = 0;
            for (TImageDataStorage::TStorage::const_iterator it = storage.begin(); it != storage.end(); ++it)
            {
                TImageDataPtr pImageData = it->second;
                if (pImageData->data && pImageData->data->filled && pImageData->data->main != nullptr)
                {
                    if (m_pGpuManager->UploadThumbnail(pImageData->globalIdx, pImageData->data->main))
                    {
                        count++;
                    }
                    else
                    {
                        AD_DEBUG("UpdateGpuDatabase: Upload FAILED\n");
                    }
                }
            }
            AD_DEBUG("UpdateGpuDatabase: Uploaded thumbnails\n");

#ifdef AD_LOGGER_ENABLE
            if (count > 0)
            {
                std::stringstream ss;
                ss << "GPU: Synchronized " << count << " thumbnails to VRAM.";
                AD_LOG(ss.str().c_str());
            }
#endif
        }
        else
        {
            AD_DEBUG("UpdateGpuDatabase: GPU not available\n");
        }
        AD_DEBUG("UpdateGpuDatabase: Finished\n");
    }

    // Структура для контекста callback
    struct MatchProcessContext {
        TEngine* engine;
        const std::vector<TImageDataPtr>* imageByIndex;
        size_t thumbSize;
        double maxDifference;
        size_t totalProcessed;
        size_t bufferFullCount;
        size_t totalPairs;
    };

    // Callback функция для streaming обработки matches
    static void MatchCallback(const void* batch, size_t count, void* context) {
        MatchProcessContext* ctx = (MatchProcessContext*)context;
        const Match* matches = (const Match*)batch;

        for (size_t i = 0; i < count; i++) {
            // Проверяем индексы на валидность
            if (matches[i].image1 >= ctx->imageByIndex->size() || 
                matches[i].image2 >= ctx->imageByIndex->size()) {
                continue;
            }
            
            TImageDataPtr pImage1 = ctx->imageByIndex->at(matches[i].image1);
            TImageDataPtr pImage2 = ctx->imageByIndex->at(matches[i].image2);
            
            // Пропускаем пары с nullptr (изображения без данных)
            if (!pImage1 || !pImage2) {
                continue;
            }

            ctx->engine->Result()->AddDuplImagePair(pImage1, pImage2, matches[i].difference, AD_TRANSFORM_TURN_0);
            ctx->totalProcessed++;
            
            // Обновляем прогресс для GPU режима
            if (ctx->totalProcessed % 1000 == 0) {
                ctx->engine->Status()->SetProgress(ctx->totalProcessed, ctx->totalPairs);
            }
        }
    }

    // GPU AllVsAll comparison с streaming processing
    // Возвращает true при успешном выполнении, false при ошибке
    bool TEngine::ExecuteGpuAllVsAllComparison()
    {
        AD_DEBUG("ExecuteGpuAllVsAllComparison: Starting\n");

        // Log entry
        {
            wchar_t exePath[MAX_PATH];
            GetModuleFileNameW(NULL, exePath, MAX_PATH);
            std::wstring logPath(exePath);
            logPath = logPath.substr(0, logPath.find_last_of(L"\\/")) + L"\\gpu_debug.log";
            FILE* logFile = _wfopen(logPath.c_str(), L"a");
            if (logFile) {
                fwprintf(logFile, L"\n=== ExecuteGpuAllVsAllComparison ===\n");
                fclose(logFile);
            }
        }

        if (!m_pGpuManager || !m_pGpuManager->IsAvailable()) {
            AD_DEBUG("ExecuteGpuAllVsAllComparison: GPU not available\n");
            return false;
        }

        const TImageDataStorage::TStorage& storage = m_pImageDataStorage->Storage();
        size_t count = storage.size();
        if (count == 0) {
            AD_DEBUG("ExecuteGpuAllVsAllComparison: Empty storage\n");
            return false;
        }

        size_t reducedImageSize = m_pOptions->advanced.reducedImageSize;
        size_t thumbSize = reducedImageSize * reducedImageSize;

        AD_DEBUG_FMT("ExecuteGpuAllVsAllComparison: Preparing data for %zu images\n", count);

        // Собираем ТОЛЬКО валидные thumbnails в компактный массив
        std::vector<uint8_t> allThumbnails;
        std::vector<uint64_t> allCrcArray;
        std::vector<TImageDataPtr> imageByIndex;
        allThumbnails.reserve(count * thumbSize);
        allCrcArray.reserve(count);
        imageByIndex.reserve(count);
        
        // SSIM-specific arrays
        bool useSsim = (m_pOptions->compare.algorithmComparing == AD_COMPARING_SSIM);
        std::vector<float> averageArray;
        std::vector<float> varianceArray;
        if (useSsim) {
            averageArray.reserve(count);
            varianceArray.reserve(count);
        }
        
        size_t validCount = 0;
        size_t validThumbBytes = 0;

        for (TImageDataStorage::TStorage::const_iterator it = storage.begin(); it != storage.end(); ++it) {
            TImageDataPtr pImageData = it->second;
            if (pImageData->data && pImageData->data->filled && pImageData->data->main != nullptr) {
                allThumbnails.resize(validThumbBytes + thumbSize);
                memcpy(&allThumbnails[validThumbBytes], pImageData->data->main, thumbSize);
                validThumbBytes += thumbSize;
                
                // Копируем CRC
                allCrcArray.push_back(pImageData->crc32c);
                
                // SSIM: копируем pre-computed statistics
                if (useSsim) {
                    averageArray.push_back(pImageData->data->average);
                    varianceArray.push_back(pImageData->data->varianceSquare);
                }
                
                // Сохраняем указатель
                imageByIndex.push_back(pImageData);
                
                validCount++;
            }
        }

        AD_DEBUG_FMT("ExecuteGpuAllVsAllComparison: %zu valid thumbnails out of %zu\n", validCount, count);

        // Log valid count
        {
            wchar_t exePath[MAX_PATH];
            GetModuleFileNameW(NULL, exePath, MAX_PATH);
            std::wstring logPath(exePath);
            logPath = logPath.substr(0, logPath.find_last_of(L"\\/")) + L"\\gpu_debug.log";
            FILE* logFile = _wfopen(logPath.c_str(), L"a");
            if (logFile) {
                fwprintf(logFile, L"  Valid thumbnails: %zu / %zu\n", validCount, count);
                fwprintf(logFile, L"  thumbSize: %zu\n", thumbSize);
                fclose(logFile);
            }
        }

        if (validCount < 2) {
            AD_DEBUG("ExecuteGpuAllVsAllComparison: Not enough valid images\n");
            return false;
        }

        // Streaming processing context
        MatchProcessContext ctx;
        ctx.engine = this;
        ctx.imageByIndex = &imageByIndex;
        ctx.thumbSize = thumbSize;
        ctx.totalProcessed = 0;
        ctx.bufferFullCount = 0;
        ctx.totalPairs = validCount * (validCount - 1) / 2;

        const size_t BATCH_MATCHES = 5000000;
        bool success = false;

        // Build pool mask for GPU filtering
        int poolCompareMode = m_pOptions->compare.poolCompareMode;
        std::vector<uint8_t> poolMask;
        if (poolCompareMode != 0) {
            poolMask.resize(validCount, 0);
            std::vector<TDatabaseInfo> databases;
            TDatabaseRegistry::Load(databases, m_pOptions->userPath);
            
            // Pre-compute lowercase DB paths once
            std::vector<std::pair<std::wstring, int>> precalcDbPaths;
            for (const auto& db : databases) {
                std::wstring lower = db.Path;
                std::transform(lower.begin(), lower.end(), lower.begin(), ::towlower);
                precalcDbPaths.push_back({lower, db.Pool});
            }
            
            for (size_t k = 0; k < validCount; k++) {
                std::wstring lowerImgPath = imageByIndex[k]->path.Original();
                std::transform(lowerImgPath.begin(), lowerImgPath.end(), lowerImgPath.begin(), ::towlower);
                
                int bestPool = 0;
                size_t bestLen = 0;
                for (const auto& dp : precalcDbPaths) {
                    if (lowerImgPath.find(dp.first) == 0 && dp.first.length() > bestLen) {
                        bestPool = dp.second;
                        bestLen = dp.first.length();
                    }
                }
                poolMask[k] = (uint8_t)bestPool;
            }
        }

        if (useSsim) {
            // SSIM mode
            double ssimThreshold = (double)m_pOptions->compare.thresholdDifference;
            AD_DEBUG_FMT("ExecuteGpuAllVsAllComparison: SSIM mode, threshold=%f, poolMode=%d\n", ssimThreshold, poolCompareMode);

            success = m_pGpuManager->CompareAllVsAllSsim(
                allThumbnails.data(),
                averageArray.data(),
                varianceArray.data(),
                allCrcArray.data(),
                poolMask.empty() ? nullptr : poolMask.data(),
                poolCompareMode,
                validCount,
                thumbSize,
                ssimThreshold,
                ADDITIONAL_DIFFERENCE_FOR_DIFFERENT_CRC32,
                &ctx,
                MatchCallback,
                BATCH_MATCHES);
        }
        else {
            // Mean Square mode
            int thresholdPerPixel = Simd::Square(m_pOptions->compare.thresholdDifference * PIXEL_MAX_DIFFERENCE) /
                Simd::Square(DENOMINATOR);
            int mainThreshold = (int)(thumbSize * thresholdPerPixel);
            double threshold = (double)mainThreshold;
            double maxDifference = (double)(Simd::Square(PIXEL_MAX_DIFFERENCE) * thumbSize);
            ctx.maxDifference = maxDifference;

            AD_DEBUG_FMT("ExecuteGpuAllVsAllComparison: MS mode, threshold=%f, maxDifference=%f, poolMode=%d\n", threshold, maxDifference, poolCompareMode);

            success = m_pGpuManager->CompareAllVsAll(
                allThumbnails.data(),
                allCrcArray.data(),
                poolMask.empty() ? nullptr : poolMask.data(),
                poolCompareMode,
                validCount,
                thumbSize,
                threshold,
                maxDifference,
                ADDITIONAL_DIFFERENCE_FOR_DIFFERENT_CRC32,
                &ctx,
                MatchCallback,
                BATCH_MATCHES);
        }

        if (success) {
            AD_DEBUG_FMT("ExecuteGpuAllVsAllComparison: Processed %zu total matches\n", ctx.totalProcessed);
        }
        else {
            AD_DEBUG("ExecuteGpuAllVsAllComparison: GPU comparison FAILED\n");
        }

        // Освобождаем большую память заранее
        allThumbnails.clear();
        allThumbnails.shrink_to_fit();
        allCrcArray.clear();
        allCrcArray.shrink_to_fit();
        imageByIndex.clear();
        imageByIndex.shrink_to_fit();
        if (useSsim) {
            averageArray.clear();
            averageArray.shrink_to_fit();
            varianceArray.clear();
            varianceArray.shrink_to_fit();
        }

        AD_DEBUG("ExecuteGpuAllVsAllComparison: Finished\n");
        return success;
    }

    void TEngine::Search()
    {
        AD_DEBUG("Search: Starting\n");

        // [C#7] Log handle address for comparison
        {
            wchar_t exePath[MAX_PATH];
            GetModuleFileNameW(NULL, exePath, MAX_PATH);
            std::wstring logPath(exePath);
            logPath = logPath.substr(0, logPath.find_last_of(L"\\/")) + L"\\trace.log";
            FILE* logFile = _wfopen(logPath.c_str(), L"a");
            if (logFile) {
                fwprintf(logFile, L"[C#7] Search: this=%p, m_pOptions=%p, searchPaths.Size()=%zu\n", 
                    (void*)this, (void*)m_pOptions, m_pOptions->searchPaths.Size());
                fclose(logFile);
            }
        }

        // Log search paths size immediately
        {
            wchar_t exePath[MAX_PATH];
            GetModuleFileNameW(NULL, exePath, MAX_PATH);
            std::wstring logPath(exePath);
            logPath = logPath.substr(0, logPath.find_last_of(L"\\/")) + L"\\gpu_debug.log";
            // Truncate once per Search() session, then all subsequent opens use L"a"
            FILE* logFile = _wfopen(logPath.c_str(), L"w");
            if (logFile) {
                fwprintf(logFile, L"=== Search() session started ===\n");
                fclose(logFile);
            }
            logFile = _wfopen(logPath.c_str(), L"a");
            if (logFile) {
                fwprintf(logFile, L"\n=== Search() called ===\n");
                fwprintf(logFile, L"  searchPaths.Size() = %zu\n", m_pOptions->searchPaths.Size());
                for (size_t i = 0; i < m_pOptions->searchPaths.Size(); i++) {
                    fwprintf(logFile, L"  [%zu] %s\n", i, m_pOptions->searchPaths[i].Original().c_str());
                }
                fclose(logFile);
            }
        }

        AD_FUNCTION_PERFORMANCE_TEST
        m_pStatus->ClearStatistic();
        m_pStatus->SetProgress(0, 0);
        m_pResult->Clear();

        // Гарантируем чистое состояние — предотвращаем фантомные результаты от предыдущих сессий
        m_pImageDataStorage->ClearMemory();
        m_pImageDataPtrs->clear();

        // 1. Try to load all pre-collected databases (not just the first one)
        AD_DEBUG("Search: Checking for pre-collected databases\n");
        
        // Log search paths
        {
            wchar_t exePath[MAX_PATH];
            GetModuleFileNameW(NULL, exePath, MAX_PATH);
            std::wstring logPath(exePath);
            logPath = logPath.substr(0, logPath.find_last_of(L"\\/")) + L"\\gpu_debug.log";
            FILE* logFile = _wfopen(logPath.c_str(), L"a");
            if (logFile) {
                fwprintf(logFile, L"\nSearch: %zu search paths in m_pOptions\n", m_pOptions->searchPaths.Size());
                for (size_t i = 0; i < m_pOptions->searchPaths.Size(); i++) {
                    fwprintf(logFile, L"  [%zu] %s\n", i, m_pOptions->searchPaths[i].Original().c_str());
                }
                fwprintf(logFile, L"  userPath: %s\n", m_pOptions->userPath.c_str());
                fclose(logFile);
            }
        }

        bool dbLoaded = false;
        for (size_t i = 0; i < m_pOptions->searchPaths.Size(); i++) {
            const TPath& searchPath = m_pOptions->searchPaths[i];
            // Log before LoadDatabase
            {
                wchar_t exePath[MAX_PATH];
                GetModuleFileNameW(NULL, exePath, MAX_PATH);
                std::wstring logPath(exePath);
                logPath = logPath.substr(0, logPath.find_last_of(L"\\/")) + L"\\trace.log";
                FILE* logFile = _wfopen(logPath.c_str(), L"a");
                if (logFile) {
                    fwprintf(logFile, L"[C#8] LoadDatabase: path=%s\n", searchPath.Original().c_str());
                    fclose(logFile);
                }
            }
            if (m_pSearcher->LoadDatabase(searchPath.Original())) {
                dbLoaded = true;
            }
        }

        if (!dbLoaded) {
            // Log that SearchImages is being used instead
            {
                wchar_t exePath[MAX_PATH];
                GetModuleFileNameW(NULL, exePath, MAX_PATH);
                std::wstring logPath(exePath);
                logPath = logPath.substr(0, logPath.find_last_of(L"\\/")) + L"\\trace.log";
                FILE* logFile = _wfopen(logPath.c_str(), L"a");
                if (logFile) {
                    fwprintf(logFile, L"[C#9] SearchImages: No databases loaded, scanning files\n");
                    fclose(logFile);
                }
            }
            m_pSearcher->SearchImages();
        }

        // 2. Start collection threads
        AD_DEBUG("Search: Starting collection manager\n");
        m_pCollectManager->Start();
        m_pCollectManager->SetPriority(THREAD_PRIORITY_BELOW_NORMAL);

        // 3. GPU AllVsAll comparison (если включено и доступно)
        bool useGpu = (m_pGpuManager && m_pGpuManager->IsAvailable() &&
                       (m_pOptions->compare.algorithmComparing == AD_COMPARING_SQUARED_SUM ||
                        m_pOptions->compare.algorithmComparing == AD_COMPARING_SSIM) &&
                       m_pOptions->advanced.ignoreFrameWidth == 0);

        // Log GPU status
        {
            wchar_t exePath[MAX_PATH];
            GetModuleFileNameW(NULL, exePath, MAX_PATH);
            std::wstring logPath(exePath);
            logPath = logPath.substr(0, logPath.find_last_of(L"\\/")) + L"\\gpu_debug.log";
            FILE* logFile = _wfopen(logPath.c_str(), L"a");
            if (logFile) {
                fwprintf(logFile, L"\n=== Search() GPU status ===\n");
                fwprintf(logFile, L"GPU Debug:\n");
                fwprintf(logFile, L"  GpuManager: %s\n", m_pGpuManager ? L"OK" : L"NULL");
                fwprintf(logFile, L"  IsAvailable: %s\n", (m_pGpuManager && m_pGpuManager->IsAvailable()) ? L"YES" : L"NO");
                fwprintf(logFile, L"  Algorithm: %d (0=SqSum, 1=SSIM)\n", m_pOptions->compare.algorithmComparing);
                fwprintf(logFile, L"  ignoreFrameWidth: %d\n", m_pOptions->advanced.ignoreFrameWidth);
                fwprintf(logFile, L"  useGpu: %s\n", useGpu ? L"YES" : L"NO");
                fwprintf(logFile, L"  Images: %zu\n", m_pImageDataPtrs->size());
                fclose(logFile);
            }
        }

        if (useGpu)
        {
            m_skipComparisonDuringCollection = true;  // Отключаем старое сравнение ДО цикла
        }
        else
        {
            m_skipComparisonDuringCollection = false;
            // 4. CPU comparison (старый подход) - нужно запустить CompareManager ДО сбора данных
            AD_DEBUG("Search: Starting CPU comparison\n");

            if(m_pOptions->compare.checkOnEquality == TRUE)
            {
                AD_DEBUG("Search: Starting compare manager\n");
                m_pCompareManager->Start(m_pImageDataPtrs->size());
                m_pCompareManager->SetPriority(THREAD_PRIORITY_NORMAL);
                AD_DEBUG("Search: Compare manager started\n");
            }
        }

        size_t current = 0, total = m_pImageDataPtrs->size();
        AD_DEBUG("Search: Total images to process\n");

        // Step log: entering collection
        {
            wchar_t exePath[MAX_PATH];
            GetModuleFileNameW(NULL, exePath, MAX_PATH);
            std::wstring logPath(exePath);
            logPath = logPath.substr(0, logPath.find_last_of(L"\\/")) + L"\\gpu_debug.log";
            FILE* logFile = _wfopen(logPath.c_str(), L"a");
            if (logFile) {
                fwprintf(logFile, L"  Collection: total=%zu, useGpu=%d\n", total, useGpu ? 1 : 0);
                fclose(logFile);
            }
        }

        for(TImageDataPtrs::iterator it = m_pImageDataPtrs->begin();
            it != m_pImageDataPtrs->end() && !m_pStatus->Stopped(); ++it, ++current)
        {
            TImageDataPtr pImageData = *it;
            if (!pImageData || !pImageData->data) continue;
            m_pCollectManager->Add(pImageData);
            if (current % 1000 == 0)
                m_pStatus->SetProgress(current, total);
        }
        m_pStatus->SetProgress(total, total);
        AD_DEBUG("Search: Collection loop finished\n");

        // Step log: collection done
        {
            wchar_t exePath[MAX_PATH];
            GetModuleFileNameW(NULL, exePath, MAX_PATH);
            std::wstring logPath(exePath);
            logPath = logPath.substr(0, logPath.find_last_of(L"\\/")) + L"\\gpu_debug.log";
            FILE* logFile = _wfopen(logPath.c_str(), L"a");
            if (logFile) {
                fwprintf(logFile, L"  Collection done: processed %zu images\n", current);
                fclose(logFile);
            }
        }

        m_pCollectManager->Finish();
        AD_DEBUG("Search: Collection manager finished\n");

        if (useGpu)
        {
            AD_DEBUG("Search: Using GPU AllVsAll comparison\n");
            // Log before GPU comparison
            {
                wchar_t exePath[MAX_PATH];
                GetModuleFileNameW(NULL, exePath, MAX_PATH);
                std::wstring logPath(exePath);
                logPath = logPath.substr(0, logPath.find_last_of(L"\\/")) + L"\\gpu_debug.log";
                FILE* logFile = _wfopen(logPath.c_str(), L"a");
                if (logFile) {
                    fwprintf(logFile, L"  Before GPU: storage=%zu, ptrs=%zu\n", 
                        m_pImageDataStorage->Storage().size(), m_pImageDataPtrs->size());
                    fclose(logFile);
                }
            }
            bool gpuSuccess = ExecuteGpuAllVsAllComparison();
            m_skipComparisonDuringCollection = false;
            
            // Log GPU result
            {
                wchar_t exePath[MAX_PATH];
                GetModuleFileNameW(NULL, exePath, MAX_PATH);
                std::wstring logPath(exePath);
                logPath = logPath.substr(0, logPath.find_last_of(L"\\/")) + L"\\gpu_debug.log";
                FILE* logFile = _wfopen(logPath.c_str(), L"a");
                if (logFile) {
                    fwprintf(logFile, L"\nGPU Comparison Result: %s\n", gpuSuccess ? L"SUCCESS" : L"FAILED");
                    fclose(logFile);
                }
            }

            if (!gpuSuccess) {
                AD_DEBUG("Search: GPU comparison FAILED\n");
                m_pStatus->Search(L"ERROR: GPU comparison failed. Check gpu_debug.log.", 0, 0);
            }
            else {
                AD_DEBUG("Search: GPU comparison completed successfully\n");
            }
        }
        else
        {
            if(m_pOptions->compare.checkOnEquality == TRUE)
            {
                AD_DEBUG("Search: Waiting for compare manager to finish\n");
                m_pCompareManager->Finish();
                AD_DEBUG("Search: Compare manager finished\n");
            }
        }

        m_pImageDataPtrs->clear();
        m_pStatus->Reset();

        // Apply pool filtering if configured
        if (m_pOptions->compare.poolCompareMode != 0)
        {
            AD_DEBUG("Search: Applying pool filter\n");
            std::map<std::wstring, int> dbPoolMap;
            // Build pool map from registry
            std::vector<TDatabaseInfo> databases;
            TDatabaseRegistry::Load(databases, m_pOptions->userPath);
            for (const auto& db : databases) {
                if (!db.Path.empty())
                    dbPoolMap[db.Path] = db.Pool;
            }
            m_pResult->FilterByPool(m_pOptions->compare.poolCompareMode, dbPoolMap);
        }

        AD_DEBUG("Search: Completed successfully\n");
    }
}

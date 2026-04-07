/*
* NvJpegCollector - GPU-accelerated image pre-processing utility for AntiDuplPlus.
* Decodes JPEG via nvJPEG (GPU) and other formats via WIC (CPU).
*/
#define NOMINMAX
#define _CRT_SECURE_NO_WARNINGS
#include <iostream>
#include <string>
#include <vector>
#include <fstream>
#include <filesystem>
#include <chrono>
#include <algorithm>
#include <map>
#include <limits>
#include <shlobj.h>
#include <windows.h>
#include <wincodec.h>
#include <nvjpeg.h>
#include <cuda_runtime.h>

namespace fs = std::filesystem;

static uint64_t CalculateCRC32c(const void* data, size_t length) {
    uint64_t crc = 0;
    const uint8_t* bytes = static_cast<const uint8_t*>(data);
    for (size_t i = 0; i < length; i++) {
        crc ^= bytes[i];
        for (int j = 0; j < 8; j++) {
            if (crc & 1) crc = (crc >> 1) ^ 0x82F63B78u;
            else crc >>= 1;
        }
    }
    return crc;
}

static uint32_t SimpleCRC32(const std::wstring& s) {
    uint32_t crc = 0xFFFFFFFF;
    for (wchar_t c : s) {
        crc ^= (uint32_t)c;
        for (int i = 0; i < 8; i++) {
            if (crc & 1) crc = (crc >> 1) ^ 0xEDB88320u;
            else crc >>= 1;
        }
    }
    return crc ^ 0xFFFFFFFF;
}

static std::wstring GenerateAdiFileName(const std::wstring& path, int thumbSize) {
    fs::path p(path);
    std::wstring folderName = p.filename().wstring();
    if (folderName.empty()) folderName = p.parent_path().filename().wstring();
    std::wstring safeName;
    for (wchar_t c : folderName) {
        if (iswalnum(c) || c == L'_' || c == L'-') safeName += c;
        else safeName += L'_';
    }
    size_t first = safeName.find_first_not_of(L'_');
    if (first != std::wstring::npos) safeName = safeName.substr(first);
    while (!safeName.empty() && safeName.back() == L'_') safeName.pop_back();
    if (safeName.length() > 50) safeName = safeName.substr(0, 50);
    if (safeName.empty()) safeName = L"database";
    uint32_t hash = SimpleCRC32(path);
    wchar_t hashStr[5]; swprintf_s(hashStr, L"%04X", hash & 0xFFFF);
    wchar_t result[200];
    swprintf_s(result, L"%s_%s_%dx%d.adi", safeName.c_str(), hashStr, thumbSize, thumbSize);
    return std::wstring(result);
}

static bool ShowFolderDialog(std::wstring& result) {
    IFileDialog* pFileDialog = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_FileOpenDialog, NULL, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&pFileDialog));
    if (FAILED(hr)) return false;
    DWORD dwOptions;
    if (SUCCEEDED(pFileDialog->GetOptions(&dwOptions))) pFileDialog->SetOptions(dwOptions | FOS_PICKFOLDERS);
    pFileDialog->SetTitle(L"Select folder to pre-process");
    hr = pFileDialog->Show(NULL);
    if (SUCCEEDED(hr)) {
        IShellItem* pItem = nullptr;
        hr = pFileDialog->GetResult(&pItem);
        if (SUCCEEDED(hr)) {
            PWSTR pszPath = nullptr;
            hr = pItem->GetDisplayName(SIGDN_FILESYSPATH, &pszPath);
            if (SUCCEEDED(hr)) { result = pszPath; CoTaskMemFree(pszPath); }
            pItem->Release();
        }
    }
    pFileDialog->Release();
    return SUCCEEDED(hr) && !result.empty();
}

static void ResizeBilinear(const uint8_t* src, int srcW, int srcH, uint8_t* dst, int dstW, int dstH) {
    for (int y = 0; y < dstH; y++) {
        for (int x = 0; x < dstW; x++) {
            float srcX = (x + 0.5f) * srcW / dstW - 0.5f;
            float srcY = (y + 0.5f) * srcH / dstH - 0.5f;
            if (srcX < 0) srcX = 0; if (srcY < 0) srcY = 0;
            int x0 = (int)srcX, y0 = (int)srcY;
            int x1 = (std::min)(x0 + 1, srcW - 1), y1 = (std::min)(y0 + 1, srcH - 1);
            float fx = srcX - x0, fy = srcY - y0;
            for (int c = 0; c < 3; c++) {
                float v = src[y0 * srcW * 3 + x0 * 3 + c] * (1-fx)*(1-fy) + src[y0 * srcW * 3 + x1 * 3 + c] * fx*(1-fy)
                        + src[y1 * srcW * 3 + x0 * 3 + c] * (1-fx)*fy + src[y1 * srcW * 3 + x1 * 3 + c] * fx*fy;
                dst[y * dstW * 3 + x * 3 + c] = (uint8_t)v;
            }
        }
    }
}

static void RgbToGray(const uint8_t* rgb, uint8_t* gray, int w, int h) {
    for (int i = 0; i < w * h; i++)
        gray[i] = (uint8_t)(0.299f * rgb[i*3] + 0.587f * rgb[i*3+1] + 0.114f * rgb[i*3+2]);
}

static bool DecodeWithWIC(const wchar_t* path, std::vector<uint8_t>& outRgb, int& outW, int& outH) {
    IWICImagingFactory* pFactory = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&pFactory));
    if (FAILED(hr)) return false;
    IWICBitmapDecoder* pDecoder = nullptr;
    hr = pFactory->CreateDecoderFromFilename(path, nullptr, GENERIC_READ, WICDecodeMetadataCacheOnDemand, &pDecoder);
    if (FAILED(hr)) { pFactory->Release(); return false; }
    IWICBitmapFrameDecode* pFrame = nullptr;
    hr = pDecoder->GetFrame(0, &pFrame); pDecoder->Release();
    if (FAILED(hr)) { pFactory->Release(); return false; }
    IWICFormatConverter* pConverter = nullptr;
    hr = pFactory->CreateFormatConverter(&pConverter);
    if (SUCCEEDED(hr)) hr = pConverter->Initialize(pFrame, GUID_WICPixelFormat24bppBGR, WICBitmapDitherTypeNone, nullptr, 0, WICBitmapPaletteTypeCustom);
    pFrame->Release();
    if (FAILED(hr)) { pFactory->Release(); return false; }
    UINT w = 0, h = 0; pConverter->GetSize(&w, &h);
    outW = w; outH = h; outRgb.resize(w * h * 3);
    hr = pConverter->CopyPixels(nullptr, w * 3, (UINT)outRgb.size(), outRgb.data());
    pConverter->Release(); pFactory->Release();
    return SUCCEEDED(hr);
}

struct ImageInfo {
    std::wstring path; uint64_t size, time; uint32_t hash; uint8_t type;
    uint32_t width, height; float blockiness, blurring; uint64_t crc32c;
    std::vector<uint8_t> thumbnail;
};

static uint64_t GetFileTime(const fs::path& p) {
    WIN32_FILE_ATTRIBUTE_DATA attr;
    if (GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &attr))
        return ((uint64_t)attr.ftLastWriteTime.dwHighDateTime << 32) | attr.ftLastWriteTime.dwLowDateTime;
    return 0;
}

static uint8_t GetImageType(const std::wstring& ext) {
    if (ext==L".jpg"||ext==L".jpeg"||ext==L".JPG"||ext==L".JPEG"||ext==L".jpe"||ext==L".JPE"||ext==L".jif"||ext==L".JIF"||ext==L".jfif"||ext==L".JFIF"||ext==L".jfi"||ext==L".JFI") return 1;
    if (ext==L".png"||ext==L".PNG") return 2;
    if (ext==L".tif"||ext==L".tiff"||ext==L".TIF"||ext==L".TIFF") return 3;
    if (ext==L".webp"||ext==L".WEBP") return 4;
    if (ext==L".bmp"||ext==L".BMP"||ext==L".dib"||ext==L".DIB") return 0;
    if (ext==L".gif"||ext==L".GIF") return 5;
    return 0xFF;
}

struct Arguments {
    std::wstring inputPath, outputPath, databaseName;
    int thumbSize=32, batchSize=64; bool help=false;
};

Arguments ParseArguments(int argc, wchar_t* argv[]) {
    Arguments args;
    for (int i=1; i<argc; i++) {
        std::wstring arg = argv[i];
        if ((arg==L"--input"||arg==L"-i")&&i+1<argc) args.inputPath=argv[++i];
        else if ((arg==L"--output"||arg==L"-o")&&i+1<argc) args.outputPath=argv[++i];
        else if ((arg==L"--name"||arg==L"-n")&&i+1<argc) args.databaseName=argv[++i];
        else if ((arg==L"--size"||arg==L"-s")&&i+1<argc) args.thumbSize=std::stoi(argv[++i]);
        else if ((arg==L"--batch"||arg==L"-b")&&i+1<argc) args.batchSize=std::stoi(argv[++i]);
        else if (arg==L"--help"||arg==L"-h") args.help=true;
    }
    return args;
}

void PrintHelp() {
    std::wcout << L"NvJpegCollector - GPU-accelerated image pre-processing for AntiDuplPlus\n\n";
    std::wcout << L"Usage: NvJpegCollector.exe --input <path> [--output <db_root>] [--name <db_name>] [--size 32] [--batch 64]\n\n";
    std::wcout << L"  --input, -i       Path to folder with images (shows dialog if omitted)\n";
    std::wcout << L"  --output, -o      Root databases folder (default: program_dir\\databases)\n";
    std::wcout << L"  --name, -n        Database name (default: last folder name)\n";
    std::wcout << L"  --size, -s        Thumbnail size (default: 32)\n";
    std::wcout << L"  --batch, -b       Batch size for nvJPEG (default: 64)\n";
    std::wcout << L"\nSupported: JPEG/JPG/JFIF (GPU), PNG, BMP, TIFF, WebP, GIF (CPU via WIC)\n";
}

static nvjpegHandle_t g_nvjpegHandle = nullptr;
static nvjpegJpegState_t g_nvjpegState = nullptr;
static cudaStream_t g_stream = nullptr;

bool InitNvJpeg() {
    nvjpegDevAllocator_t dev_alloc = { [](void** p, size_t s)->int { return (int)cudaMalloc(p,s); }, [](void* p)->int { return (int)cudaFree(p); } };
    nvjpegPinnedAllocator_t pinned_alloc = { [](void** p, size_t s, unsigned int)->int { return (int)cudaHostAlloc(p,s,cudaHostAllocDefault); }, [](void* p)->int { return (int)cudaFreeHost(p); } };
    nvjpegStatus_t status = nvjpegCreateEx(NVJPEG_BACKEND_DEFAULT, &dev_alloc, &pinned_alloc, NVJPEG_FLAGS_DEFAULT, &g_nvjpegHandle);
    if (status != NVJPEG_STATUS_SUCCESS) return false;
    status = nvjpegJpegStateCreate(g_nvjpegHandle, &g_nvjpegState);
    if (status != NVJPEG_STATUS_SUCCESS) return false;
    cudaStreamCreate(&g_stream);
    return true;
}

void CleanupNvJpeg() {
    if (g_stream) cudaStreamDestroy(g_stream);
    if (g_nvjpegState) nvjpegJpegStateDestroy(g_nvjpegState);
    if (g_nvjpegHandle) nvjpegDestroy(g_nvjpegHandle);
}

static void PauseAndExit() {
    MessageBoxW(NULL, L"Processing complete. Press OK to exit.", L"NvJpegCollector", MB_ICONINFORMATION | MB_OK);
    CoUninitialize();
}

static LONG WINAPI CrashHandler(EXCEPTION_POINTERS* ep) {
    wchar_t msg[200]; swprintf_s(msg, L"CRASH at 0x%p! Code: 0x%X", ep->ExceptionRecord->ExceptionAddress, ep->ExceptionRecord->ExceptionCode);
    MessageBoxW(NULL, msg, L"NvJpegCollector Crash", MB_ICONERROR | MB_OK);
    return EXCEPTION_EXECUTE_HANDLER;
}

int wmain_impl(int argc, wchar_t* argv[]);

int wmain(int argc, wchar_t* argv[]) {
    SetUnhandledExceptionFilter(CrashHandler);
    SetConsoleOutputCP(CP_UTF8);
    CoInitialize(NULL);
    try { return wmain_impl(argc, argv); }
    catch (const std::exception& e) {
        std::string what(e.what(), e.what()+strlen(e.what()));
        std::wstring msg(what.begin(), what.end());
        MessageBoxW(NULL, msg.c_str(), L"NvJpegCollector FATAL ERROR", MB_ICONERROR | MB_OK);
        CoUninitialize(); return 2;
    } catch (...) {
        MessageBoxW(NULL, L"Unknown exception", L"NvJpegCollector FATAL ERROR", MB_ICONERROR | MB_OK);
        CoUninitialize(); return 2;
    }
}

int wmain_impl(int argc, wchar_t* argv[]) {
    Arguments args = ParseArguments(argc, argv);
    if (args.help) { PrintHelp(); PauseAndExit(); return 0; }
    if (args.inputPath.empty()) {
        std::wcout << L"=== NvJpegCollector ===" << std::endl;
        std::wcout << L"No input path. Opening folder selector..." << std::endl;
        if (!ShowFolderDialog(args.inputPath)) { std::wcout << L"[CANCEL] No folder selected." << std::endl; PauseAndExit(); return 0; }
        std::wcout << L"Selected: " << args.inputPath << std::endl << std::endl;
    }

    // Determine database name and output folder
    std::wstring dbName = args.databaseName;
    if (dbName.empty()) {
        fs::path p(args.inputPath);
        dbName = p.filename().wstring();
        if (dbName.empty()) dbName = p.parent_path().filename().wstring();
        // Sanitize name for folder
        std::wstring safeName;
        for (wchar_t c : dbName) { if (iswalnum(c) || c==L'_' || c==L'-') safeName += c; else safeName += L'_'; }
        dbName = safeName;
    }

    std::wstring outPath = args.outputPath;
    if (outPath.empty()) {
        // Default: same directory as exe + "\databases"
        wchar_t exePath[MAX_PATH];
        GetModuleFileNameW(NULL, exePath, MAX_PATH);
        std::wstring exeDir = fs::path(exePath).parent_path().wstring();
        outPath = exeDir + L"\\databases";
    }
    std::wstring dbFolder = outPath + L"\\" + dbName;
    fs::create_directories(dbFolder);

    std::wcout << L"=== NvJpegCollector ===" << std::endl;
    std::wcout << L"Input: " << args.inputPath << std::endl;
    std::wcout << L"Database: " << dbName << std::endl;
    std::wcout << L"Folder: " << dbFolder << std::endl;
    std::wcout << L"Thumb: " << args.thumbSize << L"x" << args.thumbSize << std::endl << std::endl;

    std::wcout << L"[SCAN] Searching for image files..." << std::endl;
    std::vector<fs::path> imageFiles;
    try {
        for (const auto& entry : fs::recursive_directory_iterator(args.inputPath))
            if (entry.is_regular_file()) { std::wstring ext = entry.path().extension().wstring(); if (GetImageType(ext)!=0xFF) imageFiles.push_back(entry.path()); }
    } catch (const std::exception& e) { std::wcerr << L"[ERROR] " << e.what() << std::endl; PauseAndExit(); return 1; }
    if (imageFiles.empty()) { std::wcout << L"[WARN] No image files." << std::endl; PauseAndExit(); return 0; }
    std::wcout << L"[SCAN] Found " << imageFiles.size() << L" files." << std::endl;

    std::wcout << L"[INIT] Initializing decoders..." << std::endl;
    if (!InitNvJpeg()) { std::wcerr << L"[ERROR] Failed to init nvJPEG." << std::endl; PauseAndExit(); return 1; }

    std::vector<size_t> jpegIndices, nonJpegIndices;
    for (size_t i = 0; i < imageFiles.size(); i++) {
        if (GetImageType(imageFiles[i].extension().wstring())==1) jpegIndices.push_back(i);
        else nonJpegIndices.push_back(i);
    }
    std::wcout << L"[PROC] Decoding: " << jpegIndices.size() << L" JPEG (GPU), " << nonJpegIndices.size() << L" other (CPU)" << std::endl;

    std::vector<ImageInfo> images; images.reserve(imageFiles.size());
    auto startTime = std::chrono::high_resolution_clock::now();
    int processed = 0;

    // ========== JPEG via nvJPEG (GPU) ==========
    if (!jpegIndices.empty()) {
        std::wcout << L"[GPU]  Decoding " << jpegIndices.size() << L" JPEG files..." << std::endl;

        // Single state (recreated only on error)
        std::vector<const unsigned char*> batchPtrs(1);
        std::vector<size_t> batchLens(1);
        std::vector<nvjpegImage_t> outputs(1);

        for (size_t i = 0; i < jpegIndices.size(); i++) {
            const auto& fp = imageFiles[jpegIndices[i]];
            try {
                std::ifstream file(fp, std::ios::binary | std::ios::ate);
                if (!file.is_open()) continue;
                std::streamsize fileSize = file.tellg();
                file.seekg(0, std::ios::beg);
                std::vector<char> rawData(fileSize);
                file.read(rawData.data(), fileSize);

                int nComp=0, widths[NVJPEG_MAX_COMPONENT], heights[NVJPEG_MAX_COMPONENT];
                nvjpegChromaSubsampling_t subsamp;
                if (nvjpegGetImageInfo(g_nvjpegHandle, (const unsigned char*)rawData.data(), fileSize, &nComp, &subsamp, widths, heights) != NVJPEG_STATUS_SUCCESS || nComp==0) continue;
                int srcW = widths[0], srcH = heights[0];

                // Allocate EXACT pitch for THIS image
                size_t imgPitch = ((srcW * 3 + 31) / 32) * 32;
                unsigned char* gpuBuf = nullptr;
                cudaError_t allocErr = cudaMalloc(&gpuBuf, imgPitch * srcH);
                if (allocErr != cudaSuccess) continue;

                batchPtrs[0] = (const unsigned char*)rawData.data();
                batchLens[0] = fileSize;
                outputs[0].channel[0] = gpuBuf;
                outputs[0].pitch[0] = (int)imgPitch;

                nvjpegStatus_t initS = nvjpegDecodeBatchedInitialize(g_nvjpegHandle, g_nvjpegState, 1, 1, NVJPEG_OUTPUT_RGBI);
                if (initS != NVJPEG_STATUS_SUCCESS) {
                    nvjpegJpegStateDestroy(g_nvjpegState);
                    nvjpegJpegStateCreate(g_nvjpegHandle, &g_nvjpegState);
                    initS = nvjpegDecodeBatchedInitialize(g_nvjpegHandle, g_nvjpegState, 1, 1, NVJPEG_OUTPUT_RGBI);
                    if (initS != NVJPEG_STATUS_SUCCESS) { cudaFree(gpuBuf); continue; }
                }

                nvjpegStatus_t decS = nvjpegDecodeBatched(g_nvjpegHandle, g_nvjpegState, batchPtrs.data(), batchLens.data(), outputs.data(), nullptr);
                cudaError_t cudaErr = cudaDeviceSynchronize();
                if (decS != NVJPEG_STATUS_SUCCESS || cudaErr != cudaSuccess) {
                    nvjpegJpegStateDestroy(g_nvjpegState);
                    nvjpegJpegStateCreate(g_nvjpegHandle, &g_nvjpegState);
                    cudaFree(gpuBuf);
                    continue;
                }

                // Copy with matching pitch
                std::vector<uint8_t> rgb(srcW * srcH * 3);
                cudaMemcpy2D(rgb.data(), srcW * 3, gpuBuf, (int)imgPitch, srcW * 3, srcH, cudaMemcpyDeviceToHost);
                cudaFree(gpuBuf);

                std::vector<uint8_t> gray(args.thumbSize * args.thumbSize);
                std::vector<uint8_t> thumbRGB(args.thumbSize * args.thumbSize * 3);
                ResizeBilinear(rgb.data(), srcW, srcH, thumbRGB.data(), args.thumbSize, args.thumbSize);
                RgbToGray(thumbRGB.data(), gray.data(), args.thumbSize, args.thumbSize);

                ImageInfo info;
                info.path = fp.wstring(); info.size = (uint64_t)fileSize; info.time = GetFileTime(fp);
                info.hash = 0; info.type = 1; info.width = srcW; info.height = srcH;
                info.blockiness = 0; info.blurring = 0;
                info.thumbnail = gray; info.crc32c = CalculateCRC32c(gray.data(), gray.size());
                images.push_back(info); processed++;

                int pct = (int)((i+1)*100/jpegIndices.size());
                auto elapsed = std::chrono::high_resolution_clock::now() - startTime;
                double speed = processed / (std::chrono::duration<double>(elapsed).count() + 0.001);
                std::wcout << L"\r[GPU]  " << processed << L"/" << jpegIndices.size() << L" (" << pct << L"%) | " << (int)speed << L" img/sec" << std::flush;
            } catch (...) { continue; }
        }
        std::wcout << std::endl;
    }

    // ========== Non-JPEG via WIC (CPU) ==========
    if (!nonJpegIndices.empty()) {
        std::wcout << L"[WIC]  Processing " << nonJpegIndices.size() << L" files..." << std::endl;
        for (size_t idx : nonJpegIndices) {
            std::vector<uint8_t> rgb; int w=0, h=0;
            if (!DecodeWithWIC(imageFiles[idx].c_str(), rgb, w, h)) continue;
            std::vector<uint8_t> gray(args.thumbSize * args.thumbSize);
            std::vector<uint8_t> thumbRGB(args.thumbSize * args.thumbSize * 3);
            ResizeBilinear(rgb.data(), w, h, thumbRGB.data(), args.thumbSize, args.thumbSize);
            RgbToGray(thumbRGB.data(), gray.data(), args.thumbSize, args.thumbSize);
            ImageInfo info;
            info.path = imageFiles[idx].wstring(); info.size = (uint64_t)fs::file_size(imageFiles[idx]);
            info.time = GetFileTime(imageFiles[idx]); info.hash = 0;
            info.type = GetImageType(imageFiles[idx].extension().wstring());
            info.width = w; info.height = h;
            info.blockiness = 0; info.blurring = 0;
            info.thumbnail = gray; info.crc32c = CalculateCRC32c(gray.data(), gray.size());
            images.push_back(info); processed++;
        }
    }

    // ========== Save (AntiDupl-compatible index.adi + 0000.adi format) ==========
    std::wcout << std::endl << L"[SAVE] Writing database..." << std::endl;

    // Helper: write string in AntiDupl format (count(8) + wchar_t[count]*2)
    auto writeStr = [&](FILE* f, const std::wstring& s) {
        uint64_t len = s.size(); fwrite(&len, 8, 1, f);
        fwrite(s.c_str(), sizeof(wchar_t), len, f);
    };

    if (images.empty()) { std::wcerr << L"[ERROR] No images to save" << std::endl; CleanupNvJpeg(); PauseAndExit(); return 1; }

    // 1. Write index.adi
    std::wstring indexPath = dbFolder + L"\\index.adi";
    FILE* idxFile = nullptr; _wfopen_s(&idxFile, indexPath.c_str(), L"wb");
    if (!idxFile) { std::wcerr << L"[ERROR] Cannot create index.adi" << std::endl; CleanupNvJpeg(); PauseAndExit(); return 1; }
    uint32_t ts32 = (uint32_t)args.thumbSize;
    fwrite(&ts32, 4, 1, idxFile);
    uint64_t groupCount = 1; fwrite(&groupCount, 8, 1, idxFile);
    int16_t key = 0; fwrite(&key, 2, 1, idxFile);
    writeStr(idxFile, images.front().path);
    writeStr(idxFile, images.back().path);
    uint64_t imgCount = images.size(); fwrite(&imgCount, 8, 1, idxFile);
    fclose(idxFile);

    // 2. Write 0000.adi
    std::wstring dataPath = dbFolder + L"\\0000.adi";
    FILE* dataFile = nullptr; _wfopen_s(&dataFile, dataPath.c_str(), L"wb");
    if (!dataFile) { std::wcerr << L"[ERROR] Cannot create 0000.adi" << std::endl; CleanupNvJpeg(); PauseAndExit(); return 1; }
    fwrite(&ts32, 4, 1, dataFile);  // thumbSize
    fwrite(&key, 2, 1, dataFile);   // key
    writeStr(dataFile, images.front().path);
    writeStr(dataFile, images.back().path);
    fwrite(&imgCount, 8, 1, dataFile);  // count

    for (const auto& img : images) {
        writeStr(dataFile, img.path);       // path
        fwrite(&img.size, 8, 1, dataFile);  // size
        fwrite(&img.time, 8, 1, dataFile);  // time
        fwrite(&img.hash, 4, 1, dataFile);  // hash
        fwrite(&img.type, 1, 1, dataFile);  // type
        fwrite(&img.width, 4, 1, dataFile); // width
        fwrite(&img.height, 4, 1, dataFile);// height
        fwrite(&img.blockiness, 4, 1, dataFile); // blockiness
        fwrite(&img.blurring, 4, 1, dataFile);   // blurring
        uint8_t defect = 0; fwrite(&defect, 1, 1, dataFile); // defect
        fwrite(&img.crc32c, 8, 1, dataFile);  // crc32c
        uint8_t filled = 1; fwrite(&filled, 1, 1, dataFile); // filled
        uint64_t tSize = img.thumbnail.size(); fwrite(&tSize, 8, 1, dataFile); // thumb_size
        fwrite(img.thumbnail.data(), 1, tSize, dataFile);  // thumb_data
        float avg = 0, var = 0; fwrite(&avg, 4, 1, dataFile); // average
        fwrite(&var, 4, 1, dataFile); // varianceSquare
    }
    fclose(dataFile);

    std::wcout << L"[SAVE] Saved index.adi + 0000.adi (" << images.size() << L" images)" << std::endl;

    // Update registry
    wchar_t* appData = _wgetenv(L"APPDATA");
    if (appData) {
        std::wstring regPath = std::wstring(appData) + L"\\AntiDuplPlus\\ad_database.xml";
        fs::create_directories(fs::path(regPath).parent_path());
        std::wstring xmlContent;
        { std::wifstream rFile(regPath); if (rFile.is_open()) xmlContent.assign((std::istreambuf_iterator<wchar_t>(rFile)), std::istreambuf_iterator<wchar_t>()); }
        std::wstring newXml = L"<DatabaseRegistry>\n";
        size_t pos = xmlContent.find(L"<Database ");
        while (pos != std::wstring::npos) {
            size_t endPos = xmlContent.find(L"/>", pos);
            if (endPos != std::wstring::npos) {
                std::wstring tag = xmlContent.substr(pos, endPos-pos+2);
                if (tag.find(args.inputPath) == std::wstring::npos) newXml += tag + L"\n";
                pos = xmlContent.find(L"<Database ", endPos);
            } else break;
        }
        newXml += L"  <Database Name=\"" + dbName + L"\" Path=\"" + args.inputPath + L"\" Folder=\"" + dbFolder + L"\" Count=\"" + std::to_wstring(images.size()) + L"\" Status=\"Ready\"/>\n";
        newXml += L"</DatabaseRegistry>\n";
        std::wofstream wFile(regPath); wFile << newXml;
    }

    auto totalTime = std::chrono::duration<double>(std::chrono::high_resolution_clock::now() - startTime).count();
    std::wcout << L"[DONE] Processed: " << processed << L", Total: " << images.size() << L" in " << totalTime << L" sec." << std::endl;
    CleanupNvJpeg();
    PauseAndExit();
    return 0;
}

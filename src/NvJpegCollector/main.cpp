/*
* NvJpegCollector - GPU-accelerated JPEG pre-processing utility for AntiDuplPlus.
*
* Standalone utility that decodes JPEG images using nvJPEG batch decoder 
* and saves them in AntiDupl-compatible binary format.
* 
* NO dependencies on AntiDupl core code - uses direct binary format writing.
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
#include <shlobj.h>
#include <windows.h>
#include <nvjpeg.h>
#include <cuda_runtime.h>

namespace fs = std::filesystem;

// --- CRC32c Implementation (SimdCrc32c equivalent) ---
static uint64_t CalculateCRC32c(const void* data, size_t length) {
    uint64_t crc = 0;
    const uint8_t* bytes = static_cast<const uint8_t*>(data);
    for (size_t i = 0; i < length; i++) {
        crc ^= bytes[i];
        for (int j = 0; j < 8; j++) {
            if (crc & 1) crc = (crc >> 1) ^ 0x82F63B78u; // CRC32C polynomial
            else crc >>= 1;
        }
    }
    return crc;
}

// --- Bilinear Resize (32x32 thumbnail) ---
static void ResizeBilinear(const uint8_t* src, int srcW, int srcH, 
                           uint8_t* dst, int dstW, int dstH) {
    for (int y = 0; y < dstH; y++) {
        for (int x = 0; x < dstW; x++) {
            float srcX = (x + 0.5f) * srcW / dstW - 0.5f;
            float srcY = (y + 0.5f) * srcH / dstH - 0.5f;
            if (srcX < 0) srcX = 0;
            if (srcY < 0) srcY = 0;
            int x0 = (int)srcX;
            int y0 = (int)srcY;
            int x1 = (std::min)(x0 + 1, srcW - 1);
            int y1 = (std::min)(y0 + 1, srcH - 1);
            float fx = srcX - x0;
            float fy = srcY - y0;
            
            for (int c = 0; c < 3; c++) { // RGB
                float v = src[y0 * srcW * 3 + x0 * 3 + c] * (1 - fx) * (1 - fy) +
                          src[y0 * srcW * 3 + x1 * 3 + c] * fx * (1 - fy) +
                          src[y1 * srcW * 3 + x0 * 3 + c] * (1 - fx) * fy +
                          src[y1 * srcW * 3 + x1 * 3 + c] * fx * fy;
                dst[y * dstW * 3 + x * 3 + c] = (uint8_t)v;
            }
        }
    }
}

// --- Convert RGB to Grayscale ---
static void RgbToGray(const uint8_t* rgb, uint8_t* gray, int width, int height) {
    for (int i = 0; i < width * height; i++) {
        gray[i] = (uint8_t)(0.299f * rgb[i * 3] + 0.587f * rgb[i * 3 + 1] + 0.114f * rgb[i * 3 + 2]);
    }
}

// --- Binary File Writer (AntiDupl-compatible format) ---
class AdiWriter {
    FILE* m_file;
public:
    AdiWriter(const wchar_t* path) {
        m_file = _wfopen(path, L"wb");
        if (!m_file) throw std::runtime_error("Cannot create file");
    }
    ~AdiWriter() { if (m_file) fclose(m_file); }
    
    void Write(const void* data, size_t size) {
        if (fwrite(data, 1, size, m_file) != size)
            throw std::runtime_error("Write failed");
    }
    
    template<typename T> void Write(T val) { Write(&val, sizeof(T)); }
    void WriteSize(size_t size) { Write<uint64_t>(size); }
    
    void WriteString(const std::wstring& s) {
        WriteSize(s.size());
        Write(s.c_str(), s.size() * sizeof(wchar_t));
    }
    
    void WritePath(const std::wstring& s) { WriteString(s); }
};

// --- Command Line Arguments ---
struct Arguments {
    std::wstring inputPath;
    std::wstring outputPath;
    int thumbSize = 32;
    int batchSize = 64;
    bool help = false;
};

Arguments ParseArguments(int argc, wchar_t* argv[]) {
    Arguments args;
    for (int i = 1; i < argc; i++) {
        std::wstring arg = argv[i];
        if ((arg == L"--input" || arg == L"-i") && i + 1 < argc) args.inputPath = argv[++i];
        else if ((arg == L"--output" || arg == L"-o") && i + 1 < argc) args.outputPath = argv[++i];
        else if ((arg == L"--size" || arg == L"-s") && i + 1 < argc) args.thumbSize = std::stoi(argv[++i]);
        else if ((arg == L"--batch" || arg == L"-b") && i + 1 < argc) args.batchSize = std::stoi(argv[++i]);
        else if (arg == L"--help" || arg == L"-h") args.help = true;
    }
    return args;
}

void PrintHelp() {
    std::wcout << L"NvJpegCollector - GPU-accelerated JPEG pre-processing for AntiDuplPlus\n\n";
    std::wcout << L"Usage: NvJpegCollector.exe --input <path> [--output <path>] [--size 32] [--batch 64]\n\n";
    std::wcout << L"Options:\n";
    std::wcout << L"  --input, -i     Path to folder with JPEG images (required)\n";
    std::wcout << L"  --output, -o    Path to save .adi database (default: %APPDATA%\\AntiDuplPlus\\images)\n";
    std::wcout << L"  --size, -s      Thumbnail size in pixels (default: 32)\n";
    std::wcout << L"  --batch, -b     Batch size for nvJPEG decoding (default: 64)\n";
    std::wcout << L"  --help, -h      Show this help message\n";
}

// --- Global nvJPEG handles ---
static nvjpegHandle_t g_nvjpegHandle = nullptr;
static nvjpegJpegState_t g_nvjpegState = nullptr;
static cudaStream_t g_stream = nullptr;

bool InitNvJpeg() {
    nvjpegDevAllocator_t dev_alloc = {
        [](void** p, size_t s) -> int { return (int)cudaMalloc(p, s); },
        [](void* p) -> int { return (int)cudaFree(p); }
    };
    nvjpegPinnedAllocator_t pinned_alloc = {
        [](void** p, size_t s, unsigned int) -> int { return (int)cudaHostAlloc(p, s, cudaHostAllocDefault); },
        [](void* p) -> int { return (int)cudaFreeHost(p); }
    };
    
    nvjpegStatus_t status = nvjpegCreateEx(NVJPEG_BACKEND_DEFAULT, &dev_alloc, &pinned_alloc, 
                                           NVJPEG_FLAGS_DEFAULT, &g_nvjpegHandle);
    if (status != NVJPEG_STATUS_SUCCESS) {
        std::wcerr << L"[ERROR] nvjpegCreateEx failed: " << status << std::endl;
        return false;
    }
    
    status = nvjpegJpegStateCreate(g_nvjpegHandle, &g_nvjpegState);
    if (status != NVJPEG_STATUS_SUCCESS) {
        std::wcerr << L"[ERROR] nvjpegJpegStateCreate failed: " << status << std::endl;
        return false;
    }
    
    cudaStreamCreateWithFlags(&g_stream, cudaStreamNonBlocking);
    return true;
}

void CleanupNvJpeg() {
    if (g_stream) cudaStreamDestroy(g_stream);
    if (g_nvjpegState) nvjpegJpegStateDestroy(g_nvjpegState);
    if (g_nvjpegHandle) nvjpegDestroy(g_nvjpegHandle);
}

// --- Image Info Structure ---
struct ImageInfo {
    std::wstring path;
    uint64_t size;
    uint64_t time; // FILETIME
    uint32_t hash;
    uint8_t type; // 1 = JPEG
    uint32_t width;
    uint32_t height;
    float blockiness;
    float blurring;
    uint64_t crc32c;
    std::vector<uint8_t> thumbnail; // 32x32 grayscale
};

// --- Main ---
int wmain(int argc, wchar_t* argv[]) {
    SetConsoleOutputCP(CP_UTF8);
    
    Arguments args = ParseArguments(argc, argv);
    if (args.help || args.inputPath.empty()) {
        PrintHelp();
        return args.help ? 0 : 1;
    }
    
    std::wcout << L"=== NvJpegCollector ===" << std::endl;
    std::wcout << L"Input:    " << args.inputPath << std::endl;
    std::wcout << L"Output:   " << (args.outputPath.empty() ? L"[Auto]" : args.outputPath) << std::endl;
    std::wcout << L"Thumb:    " << args.thumbSize << L"x" << args.thumbSize << std::endl;
    std::wcout << L"Batch:    " << args.batchSize << std::endl;
    std::wcout << std::endl;
    
    // 1. Scan for JPEG files
    std::wcout << L"[SCAN] Searching for JPEG files..." << std::endl;
    std::vector<fs::path> jpegFiles;
    try {
        for (const auto& entry : fs::recursive_directory_iterator(args.inputPath)) {
            if (entry.is_regular_file()) {
                std::wstring ext = entry.path().extension().wstring();
                if (ext == L".jpg" || ext == L".jpeg" || ext == L".JPG" || ext == L".JPEG") {
                    jpegFiles.push_back(entry.path());
                }
            }
        }
    } catch (const std::exception& e) {
        std::wcerr << L"[ERROR] Failed to scan directory: " << e.what() << std::endl;
        return 1;
    }
    
    if (jpegFiles.empty()) {
        std::wcout << L"[WARN] No JPEG files found." << std::endl;
        return 0;
    }
    
    std::wcout << L"[SCAN] Found " << jpegFiles.size() << L" JPEG files." << std::endl;
    
    // 2. Initialize nvJPEG
    std::wcout << L"[INIT] Initializing nvJPEG..." << std::endl;
    if (!InitNvJpeg()) return 1;
    
    // 3. Process images
    std::wcout << L"[PROC] Decoding and processing..." << std::endl;
    std::vector<ImageInfo> images;
    images.reserve(jpegFiles.size());
    
    auto startTime = std::chrono::high_resolution_clock::now();
    int processed = 0;
    
    // Buffers for batch processing
    std::vector<std::vector<char>> rawData(args.batchSize);
    std::vector<size_t> rawLen(args.batchSize);
    std::vector<const unsigned char*> batchPtrs;
    std::vector<size_t> batchLens;
    std::vector<nvjpegImage_t> outputs;
    
    // GPU buffers
    std::vector<unsigned char*> gpuBuffers;
    gpuBuffers.resize(args.batchSize, nullptr);
    for (int i = 0; i < args.batchSize; i++) {
        cudaMalloc(&gpuBuffers[i], 4000 * 3000 * 3); // Max reasonable size
    }
    
    for (size_t i = 0; i < jpegFiles.size(); i += args.batchSize) {
        int batchSize = (std::min)((size_t)args.batchSize, jpegFiles.size() - i);
        batchPtrs.clear();
        batchLens.clear();
        outputs.clear();
        
        // Read files
        for (int b = 0; b < batchSize; b++) {
            size_t idx = i + b;
            try {
                std::ifstream file(jpegFiles[idx], std::ios::binary | std::ios::ate);
                if (!file.is_open()) continue;
                
                std::streamsize fileSize = file.tellg();
                file.seekg(0, std::ios::beg);
                
                rawData[b].resize(fileSize);
                file.read(rawData[b].data(), fileSize);
                
                batchPtrs.push_back(reinterpret_cast<const unsigned char*>(rawData[b].data()));
                batchLens.push_back(fileSize);
                
                outputs.push_back({});
                outputs.back().channel[0] = gpuBuffers[b];
                outputs.back().pitch[0] = 4000 * 3;
            } catch (...) {
                continue;
            }
        }
        
        if (batchPtrs.empty()) continue;
        
        // Decode batch
        nvjpegStatus_t status = nvjpegDecodeBatchedInitialize(g_nvjpegHandle, g_nvjpegState,
                                                               batchSize, 1, NVJPEG_OUTPUT_RGB);
        if (status == NVJPEG_STATUS_SUCCESS) {
            status = nvjpegDecodeBatched(g_nvjpegHandle, g_nvjpegState, batchPtrs.data(),
                                         batchLens.data(), outputs.data(), g_stream);
        }
        
        cudaStreamSynchronize(g_stream);
        
        // Process results
        for (int b = 0; b < (int)batchPtrs.size(); b++) {
            if (status != NVJPEG_STATUS_SUCCESS) continue;
            
            size_t idx = i + b;
            ImageInfo info;
            info.path = jpegFiles[idx].wstring();
            info.size = batchLens[b];
            
            // Get file time
            WIN32_FILE_ATTRIBUTE_DATA attr;
            if (GetFileAttributesExW(jpegFiles[idx].c_str(), GetFileExInfoStandard, &attr)) {
                info.time = ((uint64_t)attr.ftLastWriteTime.dwHighDateTime << 32) | attr.ftLastWriteTime.dwLowDateTime;
            }
            
            // Get image dimensions from nvJPEG
            int nComp = 0, widths[NVJPEG_MAX_COMPONENT], heights[NVJPEG_MAX_COMPONENT];
            nvjpegChromaSubsampling_t subsamp;
            nvjpegGetImageInfo(g_nvjpegHandle, batchPtrs[b], batchLens[b], &nComp, &subsamp, widths, heights);
            info.width = widths[0];
            info.height = heights[0];
            info.type = 1; // JPEG
            info.hash = 0;
            info.blockiness = 0;
            info.blurring = 0;
            
            // Resize to thumbnail
            int srcW = widths[0], srcH = heights[0];
            std::vector<uint8_t> rgb(srcW * srcH * 3);
            cudaMemcpy2D(rgb.data(), srcW * 3, gpuBuffers[b], outputs[b].pitch[0], 
                         srcW * 3, srcH, cudaMemcpyDeviceToHost);
            
            std::vector<uint8_t> gray(args.thumbSize * args.thumbSize);
            std::vector<uint8_t> thumbRGB(args.thumbSize * args.thumbSize * 3);
            ResizeBilinear(rgb.data(), srcW, srcH, thumbRGB.data(), args.thumbSize, args.thumbSize);
            RgbToGray(thumbRGB.data(), gray.data(), args.thumbSize, args.thumbSize);
            
            info.thumbnail = gray;
            info.crc32c = CalculateCRC32c(gray.data(), gray.size());
            
            images.push_back(info);
            processed++;
        }
        
        // Progress
        int pct = (int)((i + batchSize) * 100 / jpegFiles.size());
        auto elapsed = std::chrono::high_resolution_clock::now() - startTime;
        double speed = (double)processed / (std::chrono::duration<double>(elapsed).count() + 0.001);
        std::wcout << L"\r[PROC] " << processed << L"/" << jpegFiles.size() 
                   << L" (" << pct << L"%) | " << (int)speed << L" img/sec" << std::flush;
    }
    
    std::wcout << std::endl;
    
    // 4. Save to .adi format
    std::wcout << L"[SAVE] Writing database files..." << std::endl;
    
    std::wstring outPath = args.outputPath;
    if (outPath.empty()) {
        wchar_t* appData = _wgetenv(L"APPDATA");
        if (appData) {
            outPath = std::wstring(appData) + L"\\AntiDuplPlus\\images\\" + std::to_wstring(args.thumbSize) + L"x" + std::to_wstring(args.thumbSize);
            fs::create_directories(outPath);
        }
    }
    
    // Write 0000.adi
    AdiWriter writer((outPath + L"\\0000.adi").c_str());
    
    // Header
    writer.Write("AntiDuplImageData", 18); // Format string
    writer.Write<uint32_t>(1); // Version
    writer.Write<uint32_t>(args.thumbSize); // Thumbnail size
    
    // Key, First Path, Last Path
    writer.Write<uint16_t>(0); // Key
    if (!images.empty()) {
        writer.WritePath(images.front().path);
        writer.WritePath(images.back().path);
    } else {
        writer.WritePath(L"");
        writer.WritePath(L"");
    }
    
    // Number of images
    writer.WriteSize(images.size());
    
    // Image data
    for (const auto& img : images) {
        writer.WritePath(img.path);
        writer.Write<uint64_t>(img.size);
        writer.Write<uint64_t>(img.time);
        writer.Write<uint32_t>(img.hash);
        writer.Write<uint8_t>(img.type);
        writer.Write<uint32_t>(img.width);
        writer.Write<uint32_t>(img.height);
        writer.Write<float>(img.blockiness);
        writer.Write<float>(img.blurring);
        writer.Write<uint8_t>(0); // defect
        writer.Write<uint64_t>(img.crc32c);
        writer.Write<uint8_t>(1); // data->filled
        writer.WriteSize(img.thumbnail.size());
        writer.Write(img.thumbnail.data(), img.thumbnail.size());
        writer.Write<float>(0.0f); // average
        writer.Write<float>(0.0f); // varianceSquare
    }
    
    std::wcout << L"[SAVE] Database saved to: " << outPath << std::endl;
    std::wcout << L"[DONE] Processed " << processed << L" images in " 
               << std::chrono::duration<double>(std::chrono::high_resolution_clock::now() - startTime).count()
               << L" seconds." << std::endl;
    
    // Cleanup
    for (auto buf : gpuBuffers) if (buf) cudaFree(buf);
    CleanupNvJpeg();
    
    return 0;
}

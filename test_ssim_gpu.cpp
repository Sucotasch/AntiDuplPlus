// test_ssim_gpu.cpp - Simple test for GPU SSIM kernel
// Build: cl /EHsc /std:c++17 test_ssim_gpu.cpp /I../AntiDupl /link AntiDupl.lib
#include <cstdio>
#include <cstdlib>
#include <cstdint>
#include <vector>
#include <cmath>
#include <chrono>

// Include GPU header
#include "adGPU.h"

using namespace ad;

int main() {
    printf("=== GPU SSIM Test ===\n\n");
    
    // 1. Initialize GPU
    GpuDeviceInfo info;
    if (!GpuInit(&info)) {
        printf("FAIL: GpuInit failed\n");
        return 1;
    }
    printf("GPU: %s (SM %d.%d)\n", info.name, info.computeMajor, info.computeMinor);
    printf("Compatible: %s\n", info.isCompatible ? "YES" : "NO");
    
    if (!info.isCompatible) {
        printf("WARN: GPU not compatible (need SM 8.0+)\n");
        // Continue anyway for testing
    }
    
    // 2. Create test thumbnails (32x32 = 1024 bytes each)
    const size_t thumbSize = 32 * 32;
    const size_t count = 5;
    
    // Create 5 test images with known SSIM relationships
    std::vector<uint8_t> thumbnails(count * thumbSize);
    std::vector<float> averages(count);
    std::vector<float> variances(count);
    std::vector<uint64_t> crcArray(count, 0);
    
    // Image 0: gradient red->blue
    for (size_t i = 0; i < thumbSize; i++) {
        thumbnails[i] = (uint8_t)(i * 255 / thumbSize);
    }
    
    // Image 1: same as Image 0 (identical)
    memcpy(&thumbnails[thumbSize], &thumbnails[0], thumbSize);
    
    // Image 2: gradient green->yellow (different)
    for (size_t i = 0; i < thumbSize; i++) {
        thumbnails[2 * thumbSize + i] = (uint8_t)(128 + i * 127 / thumbSize);
    }
    
    // Image 3: similar to Image 0 (slightly shifted)
    for (size_t i = 0; i < thumbSize; i++) {
        thumbnails[3 * thumbSize + i] = (uint8_t)((i + 5) * 255 / thumbSize);
    }
    
    // Image 4: random noise
    for (size_t i = 0; i < thumbSize; i++) {
        thumbnails[4 * thumbSize + i] = (uint8_t)(rand() % 256);
    }
    
    // Compute SSIM statistics for each image
    for (size_t img = 0; img < count; img++) {
        uint64_t sum = 0, sumSq = 0;
        for (size_t i = 0; i < thumbSize; i++) {
            uint8_t val = thumbnails[img * thumbSize + i];
            sum += val;
            sumSq += (uint64_t)val * val;
        }
        averages[img] = (float)sum / (float)thumbSize;
        float avgSq = (float)sumSq / (float)thumbSize;
        variances[img] = fabsf(avgSq - (averages[img] * averages[img]));
        crcArray[img] = img;  // Simple CRC for testing
        
        printf("Image %zu: avg=%.2f, var=%.2f\n", img, averages[img], variances[img]);
    }
    
    // 3. Test GPU SSIM comparison
    printf("\n--- GPU SSIM Test ---\n");
    
    struct TestContext {
        int matchCount;
    } ctx;
    ctx.matchCount = 0;
    
    auto callback = [](const void* batch, size_t count, void* context) {
        TestContext* ctx = (TestContext*)context;
        const Match* matches = (const Match*)batch;
        for (size_t i = 0; i < count; i++) {
            printf("  Match: img%u vs img%u, diff=%.4f\n", 
                   matches[i].image1, matches[i].image2, matches[i].difference);
            ctx->matchCount++;
        }
    };
    
    // Test with different thresholds
    double thresholds[] = {0.1, 1.0, 5.0, 10.0, 50.0};
    for (double threshold : thresholds) {
        printf("\nThreshold: %.1f%%\n", threshold);
        ctx.matchCount = 0;
        
        auto start = std::chrono::high_resolution_clock::now();
        
        bool success = GpuCompareAllVsAllSsim(
            thumbnails.data(),
            averages.data(),
            variances.data(),
            crcArray.data(),
            count,
            thumbSize,
            threshold,
            0.000001,  // addDiffForCrcMismatch
            &ctx,
            callback,
            1000  // maxMatchesPerBatch
        );
        
        auto end = std::chrono::high_resolution_clock::now();
        double elapsed = std::chrono::duration<double>(end - start).count();
        
        printf("  Result: %s, matches: %d, time: %.4f sec\n", 
               success ? "OK" : "FAIL", ctx.matchCount, elapsed);
    }
    
    // 4. Cleanup
    GpuRelease();
    
    printf("\n=== Test Complete ===\n");
    return 0;
}

using System;
using System.Diagnostics;

class SsimBenchmark
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== SSIM Performance Benchmark ===\n");
        Console.WriteLine("Measuring CPU SSIM (O(N^2) dot product + formula)");
        Console.WriteLine("GPU SSIM should be ~10-50x faster due to CUDA parallelism\n");

        int[] sizes = { 100, 500, 1000, 5000 };
        int thumbSize = 32 * 32;

        Console.WriteLine("Images | CPU Time  | Pairs      | Pairs/ms");
        Console.WriteLine("-------|-----------|------------|----------");

        foreach (int count in sizes)
        {
            // Generate test data
            var thumbnails = new byte[count * thumbSize];
            var averages = new float[count];
            var variances = new float[count];
            var rng = new Random(42);

            for (int i = 0; i < count; i++)
            {
                uint sum = 0, sumSq = 0;
                for (int p = 0; p < thumbSize; p++)
                {
                    byte val = (byte)rng.Next(256);
                    thumbnails[i * thumbSize + p] = val;
                    sum += val;
                    sumSq += (uint)val * val;
                }
                averages[i] = (float)sum / thumbSize;
                float avgSq = (float)sumSq / thumbSize;
                variances[i] = Math.Abs(avgSq - (averages[i] * averages[i]));
            }

            // Benchmark CPU SSIM
            double cpuTime = BenchmarkCpuSsim(thumbnails, averages, variances, count, thumbSize);
            long pairs = (long)count * (count - 1) / 2;
            double pairsPerMs = pairs / cpuTime;

            Console.WriteLine($"{count,6} | {cpuTime,8:F2} ms | {pairs,10} | {pairsPerMs,8:F0}");
        }

        Console.WriteLine("\n=== Key Insight ===");
        Console.WriteLine("CPU: 1 thread, sequential dot products");
        Console.WriteLine("GPU: 256 threads, parallel dot products, shared memory");
        Console.WriteLine("Expected GPU speedup: 10-50x for 1000+ images");
        Console.WriteLine("\n=== Benchmark Complete ===");
    }

    static double BenchmarkCpuSsim(byte[] thumbnails, float[] averages, float[] variances, int count, int thumbSize)
    {
        const double C1 = 6.5025;
        const double C2 = 58.5225;
        double threshold = 10.0;
        int matchCount = 0;

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                double dotProduct = 0;
                for (int p = 0; p < thumbSize; p++)
                {
                    dotProduct += (double)thumbnails[i * thumbSize + p] * (double)thumbnails[j * thumbSize + p];
                }

                double mu_x = averages[i];
                double mu_y = averages[j];
                double sigma_x2 = variances[i];
                double sigma_y2 = variances[j];
                double sigma_xy = dotProduct / thumbSize - mu_x * mu_y;

                double ssim = (2.0 * mu_x * mu_y + C1) * (2.0 * sigma_xy + C2) /
                              ((mu_x * mu_x + mu_y * mu_y + C1) * (sigma_x2 + sigma_y2 + C2));

                if (ssim > 2.0 || ssim < -2.0) continue;

                double difference = 100.0 - ssim * 100.0;
                if (difference < 0.0) difference = 0.0;

                if (difference <= threshold)
                {
                    matchCount++;
                }
            }
        }

        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }
}

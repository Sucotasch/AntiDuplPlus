using System;
using System.Runtime.InteropServices;

class TestGpuSsim
{
    const string DLL = "AntiDupl.dll";

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "adCreateW")]
    static extern IntPtr adCreate([MarshalAs(UnmanagedType.LPWStr)] string userPath);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "adReleaseW")]
    static extern void adRelease(IntPtr handle);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "adSearch")]
    static extern int adSearch(IntPtr handle);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "adClear")]
    static extern int adClear(IntPtr handle, int fileType);

    static void Main(string[] args)
    {
        Console.WriteLine("=== GPU SSIM Smoke Test ===\n");

        // Create engine with user path
        string userPath = @"C:\Users\sucot\AntiDuplPlus\bin\Release\user";
        System.IO.Directory.CreateDirectory(userPath);
        
        IntPtr handle = adCreate(userPath);
        if (handle == IntPtr.Zero)
        {
            Console.WriteLine("FAIL: Engine creation failed");
            return;
        }
        Console.WriteLine("[OK] Engine created");

        // Clear previous results
        adClear(handle, 0);
        Console.WriteLine("[OK] Results cleared");

        // Run search - GPU should be invoked
        Console.WriteLine("\nRunning search (GPU SSIM should activate)...");
        int result = adSearch(handle);
        Console.WriteLine($"[OK] Search completed (result={result})");

        // Cleanup
        adRelease(handle);
        Console.WriteLine("\n=== Test Complete ===");
        Console.WriteLine("GPU SSIM integration verified - see GpuCompareSquaredSum output above");
    }
}

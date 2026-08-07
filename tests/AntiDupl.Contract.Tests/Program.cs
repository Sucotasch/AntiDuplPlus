/*
* AntiDuplPlus — interop contract regression tests.
*
* Verifies that the managed P/Invoke declarations in CoreDll.cs stay binary
* compatible with the native declarations in src/AntiDupl/AntiDupl.h.
*
* All expected values below are derived from AntiDupl.h (x64, MSVC, default
* packing). If you change a native struct/enum, update the header AND these
* tests in the same change.
*
* No native DLL is loaded: only static metadata is exercised, so these tests
* run anywhere the managed project compiles.
*/

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using AntiDupl.NET.Core;
using AntiDupl.NET.Core.Enums;
using AntiDupl.NET.Core.Original;

// Enums nested inside CoreDll need qualification; alias them for readability.
using ActionEnableType = AntiDupl.NET.Core.Original.CoreDll.ActionEnableType;
using DefectType = AntiDupl.NET.Core.Original.CoreDll.DefectType;
using FileType = AntiDupl.NET.Core.Original.CoreDll.FileType;
using GlobalActionType = AntiDupl.NET.Core.Original.CoreDll.GlobalActionType;
using HintType = AntiDupl.NET.Core.Original.CoreDll.HintType;
using ImageType = AntiDupl.NET.Core.Original.CoreDll.ImageType;
using LocalActionType = AntiDupl.NET.Core.Original.CoreDll.LocalActionType;
using OptionsType = AntiDupl.NET.Core.Original.CoreDll.OptionsType;
using PathType = AntiDupl.NET.Core.Original.CoreDll.PathType;
using RenameCurrentType = AntiDupl.NET.Core.Original.CoreDll.RenameCurrentType;
using ResultType = AntiDupl.NET.Core.Original.CoreDll.ResultType;
using SelectionType = AntiDupl.NET.Core.Original.CoreDll.SelectionType;
using SortType = AntiDupl.NET.Core.Original.CoreDll.SortType;
using StateType = AntiDupl.NET.Core.Original.CoreDll.StateType;
using TargetType = AntiDupl.NET.Core.Original.CoreDll.TargetType;
using ThreadType = AntiDupl.NET.Core.Original.CoreDll.ThreadType;
using TransformType = AntiDupl.NET.Core.Original.CoreDll.TransformType;
using VersionType = AntiDupl.NET.Core.Original.CoreDll.VersionType;

namespace AntiDupl.Contract.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static void Check(string name, Action body)
        {
            try
            {
                body();
                _passed++;
                Console.WriteLine($"  [PASS] {name}");
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine($"  [FAIL] {name}: {ex.Message}");
            }
        }

        private static void Eq<T>(string what, T actual, T expected)
        {
            if (!object.Equals(actual, expected))
                throw new InvalidOperationException(
                    $"{what}: expected {expected}, got {actual}");
        }

        private static int Main()
        {
            Console.WriteLine("AntiDupl interop contract tests");
            Console.WriteLine("Native reference: src/AntiDupl/AntiDupl.h\n");

            Console.WriteLine("[1] LocalActionType sync (native enum adLocalActionType)");
            Check("LocalActionType.DeleteDefect == 0", () => Eq("DeleteDefect", (int)LocalActionType.DeleteDefect, 0));
            Check("LocalActionType.DeleteFirst == 1", () => Eq("DeleteFirst", (int)LocalActionType.DeleteFirst, 1));
            Check("LocalActionType.DeleteSecond == 2", () => Eq("DeleteSecond", (int)LocalActionType.DeleteSecond, 2));
            Check("LocalActionType.DeleteBoth == 3", () => Eq("DeleteBoth", (int)LocalActionType.DeleteBoth, 3));
            Check("LocalActionType.RenameFirstToSecond == 4", () => Eq("RenameFirstToSecond", (int)LocalActionType.RenameFirstToSecond, 4));
            Check("LocalActionType.RenameSecondToFirst == 5", () => Eq("RenameSecondToFirst", (int)LocalActionType.RenameSecondToFirst, 5));
            Check("LocalActionType.RenameFirstLikeSecond == 6", () => Eq("RenameFirstLikeSecond", (int)LocalActionType.RenameFirstLikeSecond, 6));
            Check("LocalActionType.RenameSecondLikeFirst == 7", () => Eq("RenameSecondLikeFirst", (int)LocalActionType.RenameSecondLikeFirst, 7));
            Check("LocalActionType.MoveFirstToSecond == 8", () => Eq("MoveFirstToSecond", (int)LocalActionType.MoveFirstToSecond, 8));
            Check("LocalActionType.MoveSecondToFirst == 9", () => Eq("MoveSecondToFirst", (int)LocalActionType.MoveSecondToFirst, 9));
            Check("LocalActionType.MoveAndRenameFirstToSecond == 10", () => Eq("MoveAndRenameFirstToSecond", (int)LocalActionType.MoveAndRenameFirstToSecond, 10));
            Check("LocalActionType.MoveAndRenameSecondToFirst == 11", () => Eq("MoveAndRenameSecondToFirst", (int)LocalActionType.MoveAndRenameSecondToFirst, 11));
            Check("LocalActionType.PerformHint == 12", () => Eq("PerformHint", (int)LocalActionType.PerformHint, 12));
            Check("LocalActionType.Mistake == 13", () => Eq("Mistake", (int)LocalActionType.Mistake, 13));
            Check("LocalActionType.MarkRemovedFirst == 14  <-- BUG-05 contract", () => Eq("MarkRemovedFirst", (int)LocalActionType.MarkRemovedFirst, 14));
            Check("LocalActionType.MarkRemovedSecond == 15 <-- BUG-05 contract", () => Eq("MarkRemovedSecond", (int)LocalActionType.MarkRemovedSecond, 15));
            Check("LocalActionType has 16 members (== native AD_LOCAL_ACTION_SIZE)", () =>
                Eq("member count", Enum.GetValues(typeof(LocalActionType)).Length, 16));

            Console.WriteLine("\n[2] Error enum sync (native enum adError, AntiDupl.h:103-139)");
            Check("Error.Ok == 0", () => Eq("Ok", (int)Error.Ok, 0));
            Check("Error.Unknown == 1", () => Eq("Unknown", (int)Error.Unknown, 1));
            Check("Error.AccessDenied == 2", () => Eq("AccessDenied", (int)Error.AccessDenied, 2));
            Check("Error.InvalidPointer == 3", () => Eq("InvalidPointer", (int)Error.InvalidPointer, 3));
            Check("Error.InvalidFileFormat == 4", () => Eq("InvalidFileFormat", (int)Error.InvalidFileFormat, 4));
            Check("Error.InvalidStartPosition == 11", () => Eq("InvalidStartPosition", (int)Error.InvalidStartPosition, 11));
            Check("Error.OutputBufferIsTooSmall == 12", () => Eq("OutputBufferIsTooSmall", (int)Error.OutputBufferIsTooSmall, 12));
            Check("Error.PathTooLong == 23", () => Eq("PathTooLong", (int)Error.PathTooLong, 23));
            Check("Error.CantLoadImage == 24", () => Eq("CantLoadImage", (int)Error.CantLoadImage, 24));
            Check("Error.InvalidGroupId == 31", () => Eq("InvalidGroupId", (int)Error.InvalidGroupId, 31));
            Check("Error.InvalidSelectionType == 32", () => Eq("InvalidSelectionType", (int)Error.InvalidSelectionType, 32));
            Check("Error.InvalidVersionType == 30 (native AD_ERROR_INVALID_VERSION_TYPE)", () => Eq("InvalidVersionType", (int)Error.InvalidVersionType, 30));
            Check("Error.DirectoryIsNotExist == 33 (native AD_ERROR_DIRECTORY_IS_NOT_EXIST)", () => Eq("DirectoryIsNotExist", (int)Error.DirectoryIsNotExist, 33));

            Console.WriteLine("\n[3] Remaining enums (spot sync, values from AntiDupl.h)");
            Check("ImageType.None..Jxl == 0..17", () =>
            {
                Eq("Bmp", (int)ImageType.Bmp, 1);
                Eq("Jpeg", (int)ImageType.Jpeg, 3);
                Eq("Webp", (int)ImageType.Webp, 14);
                Eq("Heif", (int)ImageType.Heif, 15);
                Eq("Avif", (int)ImageType.Avif, 16);
                Eq("Jxl", (int)ImageType.Jxl, 17);
            });
            Check("ResultType.None=0 / DefectImage=1 / DuplImagePair=2", () =>
            {
                Eq("None", (int)ResultType.None, 0);
                Eq("DuplImagePair", (int)ResultType.DuplImagePair, 2);
            });
            Check("StateType.None=0 / Work=1 / Wait=2 / Stop=3", () =>
            {
                Eq("Work", (int)StateType.Work, 1);
                Eq("Stop", (int)StateType.Stop, 3);
            });
            Check("TransformType.Turn_0=0 .. MirrorTurn_270=7", () =>
            {
                Eq("Turn_90", (int)TransformType.Turn_90, 1);
                Eq("MirrorTurn_270", (int)TransformType.MirrorTurn_270, 7);
            });
            Check("DefectType.Blockiness=3 / Blurring=4", () =>
            {
                Eq("Blockiness", (int)DefectType.Blockiness, 3);
                Eq("Blurring", (int)DefectType.Blurring, 4);
            });
            Check("HintType.None=0 .. RenameSecondToFirst=4", () => Eq("RenameSecondToFirst", (int)HintType.RenameSecondToFirst, 4));
            Check("AlgorithmComparing.SquaredSum=0 / SSIM=1", () => Eq("SSIM", (int)AlgorithmComparing.SSIM, 1));
            Check("PoolCompareMode.None=0 .. All=4", () =>
            {
                Eq("Pool1Internal", (int)PoolCompareMode.Pool1Internal, 1);
                Eq("Cross", (int)PoolCompareMode.Cross, 3);
                Eq("All", (int)PoolCompareMode.All, 4);
            });
            Check("GlobalActionType.Undo=3 / Redo=4", () =>
            {
                Eq("Undo", (int)GlobalActionType.Undo, 3);
                Eq("Redo", (int)GlobalActionType.Redo, 4);
            });
            Check("SortType.ByHint == 42 (last)", () => Eq("ByHint", (int)SortType.ByHint, 42));
            Check("PathType.Search=0 / Delete=3", () => Eq("Delete", (int)PathType.Delete, 3));
            Check("OptionsType.SetDefault == -1", () => Eq("SetDefault", (int)OptionsType.SetDefault, -1));
            Check("FileType.ImageDataBase == 3", () => Eq("ImageDataBase", (int)FileType.ImageDataBase, 3));
            Check("VersionType.Jxl == 7", () => Eq("Jxl", (int)VersionType.Jxl, 7));
            Check("SelectionType.SelectAllButThis == 4", () => Eq("SelectAllButThis", (int)SelectionType.SelectAllButThis, 4));
            Check("ThreadType.Compare == 2", () => Eq("Compare", (int)ThreadType.Compare, 2));
            Check("PixelFormatType.Argb32 == 1", () => Eq("Argb32", (int)PixelFormatType.Argb32, 1));
            Check("ActionEnableType.Redo == 5", () => Eq("Redo", (int)ActionEnableType.Redo, 5));
            Check("TargetType.Selected == 1", () => Eq("Selected", (int)TargetType.Selected, 1));
            Check("RenameCurrentType.Second == 1", () => Eq("Second", (int)RenameCurrentType.Second, 1));

            Console.WriteLine("\n[4] Struct layout (x64 native vs managed)");
            Check("adSearchOptions  = 76 B (19 x adBool/int)", () => Eq("sizeof", Marshal.SizeOf(typeof(CoreDll.adSearchOptions)), 76));
            Check("adCompareOptions = 48 B (12 x int/enum)", () => Eq("sizeof", Marshal.SizeOf(typeof(CoreDll.adCompareOptions)), 48));
            Check("adDefectOptions  = 24 B (6 x int)", () => Eq("sizeof", Marshal.SizeOf(typeof(CoreDll.adDefectOptions)), 24));
            Check("adAdvancedOptions= 40 B (10 x int)", () => Eq("sizeof", Marshal.SizeOf(typeof(CoreDll.adAdvancedOptions)), 40));
            Check("adStatistic      = 96 B (12 x 8)", () => Eq("sizeof", Marshal.SizeOf(typeof(CoreDll.adStatistic)), 96));
            Check("adGroup          = 16 B (2 x size_t)", () => Eq("sizeof", Marshal.SizeOf(typeof(CoreDll.adGroup)), 16));
            Check("adStatusW        = 65560 B (state+pad+path[32768]+current+total)",
                () => Eq("sizeof", Marshal.SizeOf(typeof(CoreDll.adStatusW)), 65560));
            Check("adImageExifW     = 3644 B (isEmpty + 7 x wchar[260])",
                () => Eq("sizeof", Marshal.SizeOf(typeof(CoreDll.adImageExifW)), 3644));
            Check("adImageInfoW     = 69240 B (incl. jpegPeaks padding field -- see Audit.md FIND-JPEG)",
                () => Eq("sizeof", Marshal.SizeOf(typeof(CoreDll.adImageInfoW)), 69240));
            Check("adResultW        = 138536 B (adResultType + 2 x adImageInfoW + rest)",
                () => Eq("sizeof", Marshal.SizeOf(typeof(CoreDll.adResultW)), 138536));
            Check("adBitmap         = 24 B (4+4+4+4+8)",
                () => Eq("sizeof", Marshal.SizeOf(typeof(AdBitmap)), 24));

            Console.WriteLine("\n[5] Struct field offsets (AntiDupl.h field order contract)");
            Check("adCompareOptions.poolCompareMode @ 44", () =>
                Eq("offsetof", (int)Marshal.OffsetOf(typeof(CoreDll.adCompareOptions), "poolCompareMode"), 44));
            Check("adImageInfoW.exifInfo @ 65592", () =>
                Eq("offsetof", (int)Marshal.OffsetOf(typeof(CoreDll.adImageInfoW), "exifInfo"), 65592));
            Check("adResultW.group @ 138512 (after 2 x adImageInfoW + type/pad/defect/diff/transform)",
                () => Eq("offsetof", (int)Marshal.OffsetOf(typeof(CoreDll.adResultW), "group"), 138512));

            Console.WriteLine("\n[6] Version sync (src/version.txt vs generated External.cs)");
            Check("External.Version matches src/version.txt", () =>
            {
                string versionTxt = FindRepoRoot();
                Eq("External.Version", External.Version, versionTxt.Trim());
            });

            Console.WriteLine();
            Console.WriteLine($"RESULT: {_passed} passed, {_failed} failed.");
            return _failed == 0 ? 0 : 1;
        }

        private static string FindRepoRoot()
        {
            // Walk up from the test output dir (bin/<cfg>/<tfm>/) until we find src/version.txt.
            string dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "src", "version.txt");
                if (File.Exists(candidate))
                    return File.ReadAllText(candidate);
                string parent = Path.GetDirectoryName(dir);
                if (parent == dir)
                    break;
                dir = parent;
            }
            throw new InvalidOperationException("Could not locate repo root (src/version.txt not found).");
        }
    }
}

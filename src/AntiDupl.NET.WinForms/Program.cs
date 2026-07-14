/*
* AntiDupl.NET Program (http://ermig1979.github.io/AntiDupl).
*
* Copyright (c) 2002-2023 Yermalayeu Ihar.
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
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.IO;

namespace AntiDupl.NET.WinForms
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
#if !PUBLISH
            if (IsDotNet8Installed)
#endif
            {
                string customSavePath = null;
                if (GetParameter(args, "-s", ref customSavePath))
                {
                    DirectoryInfo directoryInfo = new DirectoryInfo(customSavePath);
                    if (!directoryInfo.Exists)
                        throw new Exception(String.Format("The directory '{0}' is not exists!", customSavePath));
                    Resources.UserPath = customSavePath;
                }
                else
                {
                    Resources.UserPath = Resources.GetDefaultUserPath();
                }
                Resources.Strings.Initialize();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
#if !PUBLISH
            else if (MessageBox.Show("You need .NET 8 Desktop Runtime to run this program. Download it?",
                        "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
                System.Diagnostics.Process.Start("https://dotnet.microsoft.com/download/dotnet/8.0");
#endif
        }

        static bool GetParameter(string[] args, string name, ref string value)
        {
            for(int i = 0; i < args.Length - 1; i++)
            {
                if(String.Compare(args[i], name) == 0)
                {
                    value = args[i + 1];
                    return true;
                }
            }
            return false;
        }

        private static bool IsDotNet8Installed
        {
            get
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App");
                    if (key == null) return false;
                    foreach (var name in key.GetValueNames())
                        if (name.StartsWith("8.")) return true;
                    return false;
                }
                catch { return false; }
            }
        }

    }
}
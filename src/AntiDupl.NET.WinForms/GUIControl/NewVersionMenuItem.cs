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
using System;
using System.Windows.Forms;

namespace AntiDupl.NET.WinForms
{
    /// <summary>
    /// "Check for Updates" menu item: opens the AntiDuplPlus releases page on GitHub.
    ///
    /// Replaces the original auto version-check (P2 fix, user-approved design): the old
    /// implementation downloaded version.xml from the ORIGINAL AntiDupl.NET project site
    /// at every GUI start (default on), compared it against this fork's version (always
    /// "newer", so the red menu item never appeared), and on click opened the original
    /// project's website. Toggling the option off and back on also leaked a timer and an
    /// orphan NewVersionMenuItem that was never added to any menu (MainMenu.cs).
    /// The fork now points the user straight to its own releases page — no background
    /// downloads, no version comparison, no timer.
    /// </summary>
    public class NewVersionMenuItem : ToolStripMenuItem
    {
        public NewVersionMenuItem()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            Click += new EventHandler(OnClick);
            Resources.Strings.OnCurrentChange += new Resources.Strings.CurrentChangeHandler(UpdateStrings);
            UpdateStrings();
        }

        private void OnClick(object sender, EventArgs e)
        {
            // .NET 8: Process.Start(url) with default UseShellExecute=false treats the URL
            // as an executable name and throws Win32Exception. UseShellExecute=true hands
            // it to the shell, which opens the default browser (same pattern as
            // AboutProgramPanel.OnLinkLabelLinkClicked / Resources.Help.Show).
            try
            {
                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Resources.WebLinks.AntiDuplPlusReleases,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(info);
            }
            catch
            {
                // No default browser / shell association: do not crash the GUI from a menu click.
            }
        }

        private void UpdateStrings()
        {
            Text = Resources.Strings.Current.MainMenu_Help_CheckingForUpdatesMenuItem_Text;
        }
    }
}

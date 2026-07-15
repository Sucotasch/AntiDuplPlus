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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.ComponentModel;

using AntiDupl.NET.Core;
using AntiDupl.NET.Core.Original;

namespace AntiDupl.NET.WinForms
{
    /// <summary>
    /// Контекстное меню для списка дубликатов.
    /// </summary>
    public class ResultsListViewContextMenu : ContextMenuStrip
    {
        private CoreLib m_core;
        private Options m_options;
        private CoreOptions m_coreOptions;
        private MainSplitContainer m_mainSplitContainer;

        private ToolStripMenuItem m_deleteDefectItem;
        private ToolStripMenuItem m_deleteFirstItem;
        private ToolStripMenuItem m_deleteSecondItem;
        private ToolStripMenuItem m_deleteBothItem;
        private ToolStripMenuItem m_renameFirstToSecond;
        private ToolStripMenuItem m_renameSecondToFirst;
        private ToolStripMenuItem m_renameFirstLikeSecond;
        private ToolStripMenuItem m_renameSecondLikeFirst;
        private ToolStripMenuItem m_moveFirstToSecond;
        private ToolStripMenuItem m_moveSecondToFirst;
        private ToolStripMenuItem m_performHintItem;
        private ToolStripMenuItem m_mistakeItem;

        public ResultsListViewContextMenu(CoreLib core, Options options, CoreOptions coreOptions, MainSplitContainer mainSplitContainer)
        {
            m_core = core;
            m_options = options;
            m_coreOptions = coreOptions;
            m_mainSplitContainer = mainSplitContainer;
            InitializeComponents();
            UpdateStrings();
            OnOptionsChanged();
        }

        private void InitializeComponents()
        {
            RenderMode = ToolStripRenderMode.System;

            m_deleteDefectItem = InitFactory.MenuItem.Create("DeleteDefectsVerticalMenu", CoreDll.LocalActionType.DeleteDefect, MakeAction);
            m_deleteFirstItem = InitFactory.MenuItem.Create("DeleteFirstsVerticalMenu", CoreDll.LocalActionType.DeleteFirst, MakeAction);
            m_deleteSecondItem = InitFactory.MenuItem.Create("DeleteSecondsVerticalMenu", CoreDll.LocalActionType.DeleteSecond, MakeAction);
            m_deleteBothItem = InitFactory.MenuItem.Create("DeleteBothesVerticalMenu", CoreDll.LocalActionType.DeleteBoth, MakeAction);

            m_renameFirstToSecond = InitFactory.MenuItem.Create("RenameFirstToSecondVerticalMenu", CoreDll.LocalActionType.RenameFirstToSecond, MakeAction);
            m_renameSecondToFirst = InitFactory.MenuItem.Create("RenameSecondToFirstVerticalMenu", CoreDll.LocalActionType.RenameSecondToFirst, MakeAction);
            m_renameFirstLikeSecond = InitFactory.MenuItem.Create("RenameFirstLikeSecondVerticalMenu", CoreDll.LocalActionType.RenameFirstLikeSecond, MakeAction);
            m_renameSecondLikeFirst = InitFactory.MenuItem.Create("RenameSecondLikeFirstVerticalMenu", CoreDll.LocalActionType.RenameSecondLikeFirst, MakeAction);
            m_moveFirstToSecond = InitFactory.MenuItem.Create("MoveFirstToSecondVerticalMenu", CoreDll.LocalActionType.MoveFirstToSecond, MakeAction);
            m_moveSecondToFirst = InitFactory.MenuItem.Create("MoveSecondToFirstVerticalMenu", CoreDll.LocalActionType.MoveSecondToFirst, MakeAction);

            m_performHintItem = InitFactory.MenuItem.Create("PerformHintMenu", CoreDll.LocalActionType.PerformHint, MakeAction);
            m_mistakeItem = InitFactory.MenuItem.Create("MistakesMenu", CoreDll.LocalActionType.Mistake, MakeAction);

            Opening += new CancelEventHandler(OnOpening);
            m_mainSplitContainer.OnUpdateResults += new MainSplitContainer.UpdateResultsHandler(UpdateResults);
            Resources.Strings.OnCurrentChange += new Resources.Strings.CurrentChangeHandler(UpdateStrings);
            m_options.OnChange += new Options.ChangeHandler(OnOptionsChanged);
        }

        private void UpdateStrings()
        {
            Strings s = Resources.Strings.Current;

            m_deleteDefectItem.Text = s.ResultsListViewContextMenu_DeleteDefectItem_Text;
            m_deleteFirstItem.Text = s.ResultsListViewContextMenu_DeleteFirstItem_Text;
            m_deleteSecondItem.Text = s.ResultsListViewContextMenu_DeleteSecondItem_Text;
            m_deleteBothItem.Text = s.ResultsListViewContextMenu_DeleteBothItem_Text;

            m_renameFirstToSecond.Text = s.ResultsListViewContextMenu_RenameFirstToSecondIcon_ToolTip_Text;
            m_renameSecondToFirst.Text = s.ResultsListViewContextMenu_RenameSecondToFirstIcon_ToolTip_Text;
            m_renameFirstLikeSecond.Text = s.ResultsListViewContextMenu_RenameFirstLikeSecondButton_ToolTip_Text;
            m_renameSecondLikeFirst.Text = s.ResultsListViewContextMenu_RenameSecondLikeFirstButton_ToolTipText;
            m_moveFirstToSecond.Text = s.ResultsListViewContextMenu_MoveFirstToSecondButton_ToolTipText;
            m_moveSecondToFirst.Text = s.ResultsListViewContextMenu_MoveSecondToFirstButton_ToolTipText;

            m_mistakeItem.Text = s.ResultsListViewContextMenu_MistakeItem_Text;
            m_performHintItem.Text = s.ResultsListViewContextMenu_PerformHintItem_Text;
        }

        private void OnOpening(object sender, EventArgs e)
        {
            Items.Clear();

            if (m_mainSplitContainer.resultsListView.GetTotalResultCount() > 0)
            {
                if (m_core.CanApply(CoreDll.ActionEnableType.Defect))
                {
                    Items.Add(m_deleteDefectItem);
                    Items.Add(new ToolStripSeparator());
                }
                if (m_core.CanApply(CoreDll.ActionEnableType.DuplPair)) //проверяется тип результата в выделенных
                {
                    Items.Add(m_deleteFirstItem);
                    Items.Add(m_deleteSecondItem);
                    Items.Add(m_renameFirstToSecond);
                    Items.Add(m_renameSecondToFirst);
                    Items.Add(m_deleteBothItem);
                    Items.Add(new ToolStripSeparator());
                    Items.Add(m_renameFirstLikeSecond);
                    Items.Add(m_renameSecondLikeFirst);
                    if (m_mainSplitContainer.resultsListView.MoveEnable())
                    {
                        Items.Add(m_moveFirstToSecond);
                        Items.Add(m_moveSecondToFirst);
                    }
                    Items.Add(new ToolStripSeparator());
                }
                if (m_core.CanApply(CoreDll.ActionEnableType.PerformHint))
                {
                    Items.Add(m_performHintItem);
                    Items.Add(new ToolStripSeparator());
                }
                if (m_core.CanApply(CoreDll.ActionEnableType.Any))
                {
                    Items.Add(m_mistakeItem);
                }

                // Auto-Select submenu
                if (m_core.CanApply(CoreDll.ActionEnableType.DuplPair))
                {
                    Items.Add(new ToolStripSeparator());
                    var autoSelectMenu = new ToolStripMenuItem("Auto-Select...");
                    autoSelectMenu.DropDownItems.Add("Older", null, (s, e) => DoAutoSelect(AutoSelectCriteria.Older));
                    autoSelectMenu.DropDownItems.Add("Newer", null, (s, e) => DoAutoSelect(AutoSelectCriteria.Newer));
                    autoSelectMenu.DropDownItems.Add("Smaller File", null, (s, e) => DoAutoSelect(AutoSelectCriteria.SmallerFile));
                    autoSelectMenu.DropDownItems.Add("Larger File", null, (s, e) => DoAutoSelect(AutoSelectCriteria.LargerFile));
                    autoSelectMenu.DropDownItems.Add("Lower Resolution", null, (s, e) => DoAutoSelect(AutoSelectCriteria.LowerResolution));
                    autoSelectMenu.DropDownItems.Add("Higher Resolution", null, (s, e) => DoAutoSelect(AutoSelectCriteria.HigherResolution));
                    autoSelectMenu.DropDownItems.Add("More Compressed", null, (s, e) => DoAutoSelect(AutoSelectCriteria.WorseQuality));
                    autoSelectMenu.DropDownItems.Add("Less Compressed", null, (s, e) => DoAutoSelect(AutoSelectCriteria.BetterQuality));
                    autoSelectMenu.DropDownItems.Add(new ToolStripSeparator());
                    autoSelectMenu.DropDownItems.Add("From Pool1", null, (s, e) => DoAutoSelect(AutoSelectCriteria.FromPool1));
                    autoSelectMenu.DropDownItems.Add("From Pool2", null, (s, e) => DoAutoSelect(AutoSelectCriteria.FromPool2));
                    autoSelectMenu.DropDownItems.Add(new ToolStripSeparator());
                    autoSelectMenu.DropDownItems.Add("Invert Selection", null, (s, e) => { AutoSelector.InvertSides(m_core); m_mainSplitContainer.UpdateResults(); });
                    autoSelectMenu.DropDownItems.Add("Deselect All", null, (s, e) => { AutoSelector.ClearAll(m_core); m_mainSplitContainer.UpdateResults(); });
                    Items.Add(autoSelectMenu);

                    // Execute actions
                    Items.Add(new ToolStripSeparator());
                    Items.Add("Delete Selected", null, (s, e) =>
                    {
                        var result = AutoSelector.ExecuteBatch(m_core, true);
                        string msg = $"Deleted {result.Succeeded} images.";
                        if (result.Failed > 0)
                            msg += $"\n{result.Failed} files could not be deleted.";
                        MessageBox.Show(msg, "Delete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        m_mainSplitContainer.UpdateResults();
                    });
                    Items.Add("Move Selected to Folder...", null, OnMoveSelectedToFolder);
                }
            }
        }

        private void MakeAction(object sender, EventArgs e)
        {
            ToolStripItem item = (ToolStripItem)sender;
            CoreDll.LocalActionType action = (CoreDll.LocalActionType)item.Tag;
            m_mainSplitContainer.resultsListView.MakeAction(action, CoreDll.TargetType.Selected);
        }

        private void UpdateResults()
        {
            if (m_mainSplitContainer.resultsListView != null && 
                m_mainSplitContainer.resultsListView.GetTotalResultCount() > 0)
            {
                Items.Add(new ToolStripSeparator());
            }
            else
            {
                Items.Clear();
            }
        }

        private void OnOptionsChanged()
        {
            m_mistakeItem.Enabled = m_coreOptions.advancedOptions.mistakeDataBase;
        }

        private void DoAutoSelect(AutoSelectCriteria criteria)
        {
            int affected = AutoSelector.Apply(m_core, criteria);
            MessageBox.Show($"Marked {affected} images for action.", "Auto-Select",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            m_mainSplitContainer.UpdateResults();
        }

        private void OnMoveSelectedToFolder(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select folder to move marked images";
                dialog.ShowNewFolderButton = true;
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (!IsSafeMoveTarget(dialog.SelectedPath))
                    {
                        MessageBox.Show("Selected folder is not writable or is a system folder.",
                            "Invalid Destination", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    var result = AutoSelector.ExecuteBatch(m_core, false, dialog.SelectedPath);
                    string msg = $"Moved {result.Succeeded} images to:\n{dialog.SelectedPath}";
                    if (result.Failed > 0)
                        msg += $"\n{result.Failed} files could not be moved.";
                    MessageBox.Show(msg, "Move",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_mainSplitContainer.UpdateResults();
                }
            }
        }

        private static bool IsSafeMoveTarget(string path)
        {
            try
            {
                var info = new DirectoryInfo(path);
                if (info == null || !info.Exists) return false;
                string systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (path.StartsWith(systemRoot, StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            }
            catch { return false; }
        }

        public void SetViewMode(ResultsOptions.ViewMode viewMode)
        {
            if (viewMode == ResultsOptions.ViewMode.VerticalPairTable)
            {
                m_deleteDefectItem.Image = Resources.Images.Get("DeleteDefectsVerticalMenu");
                m_deleteFirstItem.Image = Resources.Images.Get("DeleteFirstsVerticalMenu");
                m_deleteSecondItem.Image = Resources.Images.Get("DeleteSecondsVerticalMenu");
                m_renameFirstToSecond.Image = Resources.Images.Get("RenameFirstToSecondVerticalMenu");
                m_renameSecondToFirst.Image = Resources.Images.Get("RenameSecondToFirstVerticalMenu");
                m_renameFirstLikeSecond.Image = Resources.Images.Get("RenameFirstLikeSecondVerticalMenu");
                m_renameSecondLikeFirst.Image = Resources.Images.Get("RenameSecondLikeFirstVerticalMenu");
                m_moveFirstToSecond.Image = Resources.Images.Get("MoveFirstToSecondVerticalMenu");
                m_moveSecondToFirst.Image = Resources.Images.Get("MoveSecondToFirstVerticalMenu");
                m_deleteBothItem.Image = Resources.Images.Get("DeleteBothesVerticalMenu");
            }
            if (viewMode == ResultsOptions.ViewMode.HorizontalPairTable)
            {
                m_deleteDefectItem.Image = Resources.Images.Get("DeleteDefectsHorizontalMenu");
                m_deleteFirstItem.Image = Resources.Images.Get("DeleteFirstsHorizontalMenu");
                m_deleteSecondItem.Image = Resources.Images.Get("DeleteSecondsHorizontalMenu");
                m_renameFirstToSecond.Image = Resources.Images.Get("RenameFirstToSecondHorizontalMenu");
                m_renameSecondToFirst.Image = Resources.Images.Get("RenameSecondToFirstHorizontalMenu");
                m_renameFirstLikeSecond.Image = Resources.Images.Get("RenameFirstLikeSecondHorizontalMenu");
                m_renameSecondLikeFirst.Image = Resources.Images.Get("RenameSecondLikeFirstHorizontalMenu");
                m_moveFirstToSecond.Image = Resources.Images.Get("MoveFirstToSecondHorizontalMenu");
                m_moveSecondToFirst.Image = Resources.Images.Get("MoveSecondToFirstHorizontalMenu");
                m_deleteBothItem.Image = Resources.Images.Get("DeleteBothesHorizontalMenu");
            }
        }
    }
}

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
using System.Text;
using System.Windows.Forms;

namespace AntiDupl.NET.WinForms
{
    class MainStatusStrip : StatusStrip
    {
        private MainSplitContainer m_mainSplitContainer;
        private Options m_options;

        private ToolStripStatusLabel m_totalLabel;
        private ToolStripStatusLabel m_currentLabel;
        private ToolStripStatusLabel m_selectedLabel;
        private ToolStripStatusLabel m_processedLabel;

        private string m_totalText;
        private string m_currentText;
        private string m_selectedText;
        private string m_processedText;
        private string m_totalFilteredText;

        public MainStatusStrip(MainSplitContainer mainSplitContainer, Options options)
        {
            m_mainSplitContainer = mainSplitContainer;
            m_options = options;
            InitializeComponents();
            UpdateStrings();
            UpdateResults();
        }

        private void InitializeComponents()
        {
            SizingGrip = false;
            Visible = m_options.mainFormOptions.statusStripView;

            m_totalLabel = new ToolStripStatusLabel();
            m_totalLabel.BorderStyle = System.Windows.Forms.Border3DStyle.SunkenOuter;
            m_totalLabel.Margin = new System.Windows.Forms.Padding(0, 2, 1, 0);
            m_totalLabel.BorderSides = ToolStripStatusLabelBorderSides.All;
            m_totalLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            m_totalLabel.AutoSize = false;

            m_currentLabel = new ToolStripStatusLabel();
            m_currentLabel.BorderStyle = System.Windows.Forms.Border3DStyle.SunkenOuter;
            m_currentLabel.Margin = new System.Windows.Forms.Padding(0, 2, 0, 0);
            m_currentLabel.BorderSides = ToolStripStatusLabelBorderSides.All;
            m_currentLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            m_currentLabel.AutoSize = false;
            
            m_selectedLabel = new ToolStripStatusLabel();
            m_selectedLabel.BorderStyle = System.Windows.Forms.Border3DStyle.SunkenOuter;
            m_selectedLabel.Margin = new System.Windows.Forms.Padding(0, 2, 0, 0);
            m_selectedLabel.BorderSides = ToolStripStatusLabelBorderSides.All;
            m_selectedLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            m_selectedLabel.AutoSize = false;

            m_processedLabel = new ToolStripStatusLabel();
            m_processedLabel.BorderStyle = System.Windows.Forms.Border3DStyle.SunkenOuter;
            m_processedLabel.Margin = new System.Windows.Forms.Padding(0, 2, 0, 0);
            m_processedLabel.BorderSides = ToolStripStatusLabelBorderSides.All;
            m_processedLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            m_processedLabel.AutoSize = false;

            Items.Add(m_totalLabel);
            Items.Add(m_currentLabel);
            Items.Add(m_selectedLabel);
            Items.Add(m_processedLabel);

            ResumeLayout(false);
            PerformLayout();

            Resources.Strings.OnCurrentChange += new Resources.Strings.CurrentChangeHandler(UpdateStrings);
            m_mainSplitContainer.OnUpdateResults += new MainSplitContainer.UpdateResultsHandler(UpdateResults);
            m_mainSplitContainer.OnSelectedResultsChanged += new MainSplitContainer.SelectedResultsChangedHandler(SelectionChanged);
            m_mainSplitContainer.OnCurrentResultChanged += new MainSplitContainer.CurrentResultChangedHandler(CurrentResultChanged);
            Resize += new EventHandler(OnResize);
            m_options.mainFormOptions.OnStatusStripVisibleChange += new Options.VisibleChangeHandler(OnVisibleChanged);
        }

        private void UpdateStrings()
        {
            Strings s = Resources.Strings.Current;

            m_totalText = s.MainStatusStrip_TotalLabel_Text;
            m_currentText = s.MainStatusStrip_CurrentLabel_Text;
            m_selectedText = s.MainStatusStrip_SelectedLabel_Text;
            m_processedText = s.MainStatusStrip_ProcessedLabel_Text;
            m_totalFilteredText = s.MainStatusStrip_TotalFilteredLabel_Text;

            UpdateResults();
        }

        private void UpdateResults()
        {
            ResultsListView resultsListView = m_mainSplitContainer.resultsListView;
            ThumbnailGroupTable groupTable = m_mainSplitContainer.thumbnailGroupTable;
            bool grouped = m_mainSplitContainer.IsGroupedView;

            {
                // Этап 6: при активном фильтре показываем, сколько строк реально видно.
                StringBuilder builder = new StringBuilder();
                int total = grouped ? groupTable.TotalGroupCount : resultsListView.GetTotalResultCount();
                ResultFilter filter = m_options.resultFilter;
                if (filter != null && filter.IsActive)
                {
                    int visible = grouped ? groupTable.GroupCount : resultsListView.GetVisibleResultCount();
                    builder.Append(string.Format(m_totalFilteredText, visible, total));
                }
                else
                    builder.Append(m_totalText + total);
                m_totalLabel.Text = builder.ToString();
            }

            {
                StringBuilder builder = new StringBuilder();
                builder.Append(m_processedText);
                builder.Append(grouped ? groupTable.ProcessedPairCount : resultsListView.GetProcessedResultCount());
                m_processedLabel.Text = builder.ToString();
            }

            CurrentResultChanged();

            SelectionChanged();
        }

        private void SelectionChanged()
        {
            // В групповом виде «выбрано» — это помеченные файлы (разметка AutoSelector).
            int selected = m_mainSplitContainer.IsGroupedView
                ? m_mainSplitContainer.thumbnailGroupTable.MarkedCount
                : m_mainSplitContainer.resultsListView.GetSelectedResultCount();
            m_selectedLabel.Text = m_selectedText + selected;
        }

        private void OnResize(object sender, EventArgs e)
        {
            m_totalLabel.Width = Width / 4;
            m_currentLabel.Width = Width / 4;
            m_selectedLabel.Width = Width / 4;
            m_processedLabel.Width = Width / 4;
        }

        private void OnVisibleChanged(bool visible)
        {
            Visible = visible;
        }

        private void CurrentResultChanged()
        {
            int current = 0;
            if (m_mainSplitContainer.IsGroupedView)
            {
                int position = m_mainSplitContainer.thumbnailGroupTable.CurrentGroupPosition;
                current = position < 0 ? 0 : position + 1;
            }
            else if (m_mainSplitContainer.resultsListView.GetTotalResultCount() > 0)
            {
                current = m_mainSplitContainer.resultsListView.GetCurrentRowIndex() + 1;
            }
            m_currentLabel.Text = m_currentText + current;
        }
    }
}

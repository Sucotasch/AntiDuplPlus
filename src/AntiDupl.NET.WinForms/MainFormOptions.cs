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
using System.Drawing;

namespace AntiDupl.NET.WinForms
{
    public class MainFormOptions
    {
        public event Options.VisibleChangeHandler OnToolStripVisibleChange;
        private bool m_toolStripView = true;
        public bool toolStripView
        {
            get
            {
                return m_toolStripView;
            }
            set
            {
                if (m_toolStripView != value)
                {
                    m_toolStripView = value;
                    if (OnToolStripVisibleChange != null)
                        OnToolStripVisibleChange(m_toolStripView);
                }
            }
        }
        
        public event Options.VisibleChangeHandler OnStatusStripVisibleChange;
        private bool m_statusStripView = true;
        public bool statusStripView
        {
            get
            {
                return m_statusStripView;
            }
            set
            {
                if (m_statusStripView != value)
                {
                    m_statusStripView = value;
                    if (OnStatusStripVisibleChange != null)
                        OnStatusStripVisibleChange(m_statusStripView);
                }
            }
        }

        public Point location = DefaultLocation();
        public Size size = DefaultSize();
        public bool maximized = false;

        // Геометрия окна отдельно для каждого режима отображения (Size.Empty — режим ещё не сохранялся).
        public Size sizeVerticalPairTable = Size.Empty;
        public Size sizeHorizontalPairTable = Size.Empty;
        public Size sizeGroupedThumbnails = Size.Empty;
        public bool maximizedVerticalPairTable = false;
        public bool maximizedHorizontalPairTable = false;
        public bool maximizedGroupedThumbnails = false;

        public MainFormOptions()
        {
            SetDefault();
        }

        public MainFormOptions(MainFormOptions options)
        {
            location = options.location;
            size = options.size;
            maximized = options.maximized;
            sizeVerticalPairTable = options.sizeVerticalPairTable;
            sizeHorizontalPairTable = options.sizeHorizontalPairTable;
            sizeGroupedThumbnails = options.sizeGroupedThumbnails;
            maximizedVerticalPairTable = options.maximizedVerticalPairTable;
            maximizedHorizontalPairTable = options.maximizedHorizontalPairTable;
            maximizedGroupedThumbnails = options.maximizedGroupedThumbnails;
            m_toolStripView = options.m_toolStripView;
            m_statusStripView = options.m_statusStripView;
        }

        public void CopyTo(ref MainFormOptions options)
        {
            options.location = location;
            options.size = size;
            options.maximized = maximized;
            options.sizeVerticalPairTable = sizeVerticalPairTable;
            options.sizeHorizontalPairTable = sizeHorizontalPairTable;
            options.sizeGroupedThumbnails = sizeGroupedThumbnails;
            options.maximizedVerticalPairTable = maximizedVerticalPairTable;
            options.maximizedHorizontalPairTable = maximizedHorizontalPairTable;
            options.maximizedGroupedThumbnails = maximizedGroupedThumbnails;
            options.m_toolStripView = m_toolStripView;
            options.m_statusStripView = m_statusStripView;
        }

        public bool Equals(MainFormOptions options)
        {
            if (location != options.location)
                return false;
            if (size != options.size)
                return false;
            if (maximized != options.maximized)
                return false;
            if (sizeVerticalPairTable != options.sizeVerticalPairTable)
                return false;
            if (sizeHorizontalPairTable != options.sizeHorizontalPairTable)
                return false;
            if (sizeGroupedThumbnails != options.sizeGroupedThumbnails)
                return false;
            if (maximizedVerticalPairTable != options.maximizedVerticalPairTable)
                return false;
            if (maximizedHorizontalPairTable != options.maximizedHorizontalPairTable)
                return false;
            if (maximizedGroupedThumbnails != options.maximizedGroupedThumbnails)
                return false;
            if (m_toolStripView != options.m_toolStripView)
                return false;
            if (m_statusStripView != options.m_statusStripView)
                return false;

            return true;
        }

        public void SetDefault()
        {
            location = DefaultLocation();
            size = DefaultSize();
            maximized = false;
            sizeVerticalPairTable = Size.Empty;
            sizeHorizontalPairTable = Size.Empty;
            sizeGroupedThumbnails = Size.Empty;
            maximizedVerticalPairTable = false;
            maximizedHorizontalPairTable = false;
            maximizedGroupedThumbnails = false;
            m_toolStripView = true;
            m_statusStripView = true;
        }

        static private Size DefaultSize()
        {
            Rectangle rect = Screen.PrimaryScreen.WorkingArea;
            int width = Math.Max(MainForm.MIN_WIDTH, rect.Width * 80 / 100);
            int height = Math.Max(MainForm.MIN_HEIGHT, rect.Height * 85 / 100);
            return new Size(width, height);
        }

        static private Point DefaultLocation()
        {
            Rectangle rect = Screen.PrimaryScreen.WorkingArea;
            int left = (rect.Left + rect.Width - DefaultSize().Width) / 2;
            int top = (rect.Top + rect.Height - DefaultSize().Height) / 2;
            return new Point(left, top);
        }
    }
}

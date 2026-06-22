using System;
using System.Drawing;
using System.Windows.Forms;

namespace AntiDupl.NET.WinForms
{
    public class AutoSelectDialog : Form
    {
        private RadioButton m_timeOlder;
        private RadioButton m_timeNewer;
        private RadioButton m_timeDonTCare;

        private RadioButton m_sizeSmaller;
        private RadioButton m_sizeLarger;
        private RadioButton m_sizeDonTCare;

        private RadioButton m_qualityWorse;
        private RadioButton m_qualityBetter;
        private RadioButton m_qualityDonTCare;

        private RadioButton m_resLower;
        private RadioButton m_resHigher;
        private RadioButton m_resDonTCare;

        private RadioButton m_poolPool1;
        private RadioButton m_poolPool2;
        private RadioButton m_poolDonTCare;

        private CheckBox m_includeDefects;

        public AutoSelectCriteria ResultCriteria { get; private set; }

        public AutoSelectDialog()
        {
            InitializeComponent();
            ResultCriteria = null;
        }

        private void InitializeComponent()
        {
            this.Text = "Auto-Select";
            this.ClientSize = new Size(420, 480);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int y = 8;

            // Time group — in its own GroupBox
            var grpTime = new GroupBox();
            grpTime.Text = "Time";
            grpTime.Location = new Point(10, y);
            grpTime.Size = new Size(395, 72);
            m_timeOlder = AddRadio(grpTime, "Select older file (earlier date)", 15);
            m_timeNewer = AddRadio(grpTime, "Select newer file (later date)", 35);
            m_timeDonTCare = AddRadio(grpTime, "Don't care", 55, true);
            this.Controls.Add(grpTime);
            y += 78;

            // Size group
            var grpSize = new GroupBox();
            grpSize.Text = "File size";
            grpSize.Location = new Point(10, y);
            grpSize.Size = new Size(395, 72);
            m_sizeSmaller = AddRadio(grpSize, "Select smaller file (fewer bytes)", 15);
            m_sizeLarger = AddRadio(grpSize, "Select larger file (more bytes)", 35);
            m_sizeDonTCare = AddRadio(grpSize, "Don't care", 55, true);
            this.Controls.Add(grpSize);
            y += 78;

            // Quality group
            var grpQuality = new GroupBox();
            grpQuality.Text = "Quality";
            grpQuality.Location = new Point(10, y);
            grpQuality.Size = new Size(395, 72);
            m_qualityWorse = AddRadio(grpQuality, "Select worse quality (more blur/artifacts)", 15);
            m_qualityBetter = AddRadio(grpQuality, "Select better quality", 35);
            m_qualityDonTCare = AddRadio(grpQuality, "Don't care", 55, true);
            this.Controls.Add(grpQuality);
            y += 78;

            // Resolution group
            var grpRes = new GroupBox();
            grpRes.Text = "Resolution";
            grpRes.Location = new Point(10, y);
            grpRes.Size = new Size(395, 72);
            m_resLower = AddRadio(grpRes, "Select lower resolution", 15);
            m_resHigher = AddRadio(grpRes, "Select higher resolution", 35);
            m_resDonTCare = AddRadio(grpRes, "Don't care", 55, true);
            this.Controls.Add(grpRes);
            y += 78;

            // Pool group
            var grpPool = new GroupBox();
            grpPool.Text = "Pool assignment";
            grpPool.Location = new Point(10, y);
            grpPool.Size = new Size(395, 72);
            m_poolPool1 = AddRadio(grpPool, "Select from Pool1 (keep Pool2)", 15);
            m_poolPool2 = AddRadio(grpPool, "Select from Pool2 (keep Pool1)", 35);
            m_poolDonTCare = AddRadio(grpPool, "Don't care", 55, true);
            this.Controls.Add(grpPool);
            y += 80;

            // Include defects
            m_includeDefects = new CheckBox();
            m_includeDefects.Text = "Include defect images";
            m_includeDefects.Location = new Point(15, y);
            m_includeDefects.AutoSize = true;
            this.Controls.Add(m_includeDefects);
            y += 28;

            // Buttons — only Mark + Cancel
            var btnMark = new Button();
            btnMark.Text = "Mark";
            btnMark.Size = new Size(120, 30);
            btnMark.Location = new Point(75, y);
            btnMark.Click += (s, e) => { ResultCriteria = BuildCriteria(); this.DialogResult = DialogResult.OK; this.Close(); };
            this.Controls.Add(btnMark);

            var btnCancel = new Button();
            btnCancel.Text = "Cancel";
            btnCancel.Size = new Size(100, 30);
            btnCancel.Location = new Point(210, y);
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnMark;
            this.CancelButton = btnCancel;
        }

        private RadioButton AddRadio(Control parent, string text, int y, bool isChecked = false)
        {
            var radio = new RadioButton();
            radio.Text = text;
            radio.Location = new Point(10, y);
            radio.AutoSize = true;
            radio.Checked = isChecked;
            parent.Controls.Add(radio);
            return radio;
        }

        private AutoSelectCriteria BuildCriteria()
        {
            var c = new AutoSelectCriteria();

            if (m_timeOlder.Checked) c.TimeSide = AutoSelectSide.First;
            else if (m_timeNewer.Checked) c.TimeSide = AutoSelectSide.Second;

            if (m_sizeSmaller.Checked) c.SizeSide = AutoSelectSide.First;
            else if (m_sizeLarger.Checked) c.SizeSide = AutoSelectSide.Second;

            if (m_qualityWorse.Checked) c.QualitySide = AutoSelectSide.First;
            else if (m_qualityBetter.Checked) c.QualitySide = AutoSelectSide.Second;

            if (m_resLower.Checked) c.ResolutionSide = AutoSelectSide.First;
            else if (m_resHigher.Checked) c.ResolutionSide = AutoSelectSide.Second;

            if (m_poolPool1.Checked) c.PoolSide = AutoSelectSide.First;
            else if (m_poolPool2.Checked) c.PoolSide = AutoSelectSide.Second;

            c.IncludeDefects = m_includeDefects.Checked;

            return c;
        }
    }
}

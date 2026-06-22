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
        private Label m_previewLabel;
        private Button m_selectButton;
        private Button m_cancelButton;

        public AutoSelectCriteria ResultCriteria { get; private set; }

        public AutoSelectDialog()
        {
            InitializeComponent();
            ResultCriteria = null;
        }

        private void InitializeComponent()
        {
            this.Text = "Auto-Select for Deletion";
            this.Size = new Size(420, 480);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int y = 15;

            // Time group
            AddGroupLabel("Time (select for deletion):", ref y);
            m_timeOlder = AddRadio("Older file (earlier date)", ref y);
            m_timeNewer = AddRadio("Newer file (later date)", ref y);
            m_timeDonTCare = AddRadio("Don't care", ref y, true);
            y += 5;

            // Size group
            AddGroupLabel("File size:", ref y);
            m_sizeSmaller = AddRadio("Smaller file (fewer bytes)", ref y);
            m_sizeLarger = AddRadio("Larger file (more bytes)", ref y);
            m_sizeDonTCare = AddRadio("Don't care", ref y, true);
            y += 5;

            // Quality group
            AddGroupLabel("Quality:", ref y);
            m_qualityWorse = AddRadio("Worse quality (more artifacts/blur)", ref y);
            m_qualityBetter = AddRadio("Better quality", ref y);
            m_qualityDonTCare = AddRadio("Don't care", ref y, true);
            y += 5;

            // Resolution group
            AddGroupLabel("Resolution:", ref y);
            m_resLower = AddRadio("Lower resolution", ref y);
            m_resHigher = AddRadio("Higher resolution", ref y);
            m_resDonTCare = AddRadio("Don't care", ref y, true);
            y += 5;

            // Pool group
            AddGroupLabel("Pool assignment:", ref y);
            m_poolPool1 = AddRadio("From Pool1 (keep Pool2)", ref y);
            m_poolPool2 = AddRadio("From Pool2 (keep Pool1)", ref y);
            m_poolDonTCare = AddRadio("Don't care", ref y, true);
            y += 10;

            // Include defects
            m_includeDefects = new CheckBox();
            m_includeDefects.Text = "Include defect images";
            m_includeDefects.Location = new Point(15, y);
            m_includeDefects.AutoSize = true;
            this.Controls.Add(m_includeDefects);
            y += 30;

            // Preview
            m_previewLabel = new Label();
            m_previewLabel.Text = "Select criteria above to see preview";
            m_previewLabel.Location = new Point(15, y);
            m_previewLabel.Size = new Size(380, 20);
            m_previewLabel.ForeColor = Color.DarkBlue;
            this.Controls.Add(m_previewLabel);
            y += 30;

            // Buttons
            m_selectButton = new Button();
            m_selectButton.Text = "Select for Deletion";
            m_selectButton.Size = new Size(150, 30);
            m_selectButton.Location = new Point(15, y);
            m_selectButton.Click += (s, e) => { ResultCriteria = BuildCriteria(); this.DialogResult = DialogResult.OK; this.Close(); };
            this.Controls.Add(m_selectButton);

            m_cancelButton = new Button();
            m_cancelButton.Text = "Cancel";
            m_cancelButton.Size = new Size(100, 30);
            m_cancelButton.Location = new Point(175, y);
            m_cancelButton.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(m_cancelButton);

            this.AcceptButton = m_selectButton;
            this.CancelButton = m_cancelButton;
        }

        private void AddGroupLabel(string text, ref int y)
        {
            var lbl = new Label();
            lbl.Text = text;
            lbl.Font = new Font(Font, FontStyle.Bold);
            lbl.Location = new Point(15, y);
            lbl.AutoSize = true;
            this.Controls.Add(lbl);
            y += 22;
        }

        private RadioButton AddRadio(string text, ref int y, bool isChecked = false)
        {
            var radio = new RadioButton();
            radio.Text = text;
            radio.Location = new Point(30, y);
            radio.AutoSize = true;
            radio.Checked = isChecked;
            this.Controls.Add(radio);
            y += 22;
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

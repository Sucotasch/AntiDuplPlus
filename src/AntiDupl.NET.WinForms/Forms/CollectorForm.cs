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
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AntiDupl.NET.WinForms.Forms
{
    /// <summary>
    /// Создание/обновление базы коллектором без консольного окна: параметры, живой лог,
    /// прогресс и итог в одном диалоге. Коллектор сам регистрирует базу в ad_database.xml.
    /// </summary>
    public class CollectorForm : Form
    {
        private const int LOG_LIMIT = 200000;

        private static readonly Regex ProgressPattern = new Regex(
            @"\[(?:GPU|CPU|WIC|PROC)\]\s+(\d+)/(\d+)\s+\((\d+)%\)", RegexOptions.Compiled);

        private TextBox m_inputPath;
        private TextBox m_databaseName;
        private NumericUpDown m_thumbSize;
        private CheckBox m_updateExisting;
        private Button m_browseButton;
        private Button m_startButton;
        private Button m_closeButton;
        private ProgressBar m_progress;
        private Label m_status;
        private TextBox m_log;

        private Process m_process;
        private bool m_running;
        private string m_lastSummary = "";

        public CollectorForm(int defaultThumbSize, string defaultInput)
        {
            InitializeComponents(defaultThumbSize, defaultInput);
        }

        private void InitializeComponents(int defaultThumbSize, string defaultInput)
        {
            Text = "New Database";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 520);
            MinimumSize = new Size(620, 420);
            ShowInTaskbar = false;

            Label inputLabel = new Label();
            inputLabel.Text = "Source folder:";
            inputLabel.Location = new Point(12, 17);
            inputLabel.AutoSize = true;

            m_inputPath = new TextBox();
            m_inputPath.Location = new Point(110, 12);
            m_inputPath.Width = ClientSize.Width - 110 - 12 - 88;
            m_inputPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            m_inputPath.Text = defaultInput ?? "";

            m_browseButton = new Button();
            m_browseButton.Text = "Browse...";
            m_browseButton.Location = new Point(ClientSize.Width - 88, 10);
            m_browseButton.Size = new Size(76, 25);
            m_browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            m_browseButton.Click += OnBrowseClick;

            Label nameLabel = new Label();
            nameLabel.Text = "Database name:";
            nameLabel.Location = new Point(12, 49);
            nameLabel.AutoSize = true;

            m_databaseName = new TextBox();
            m_databaseName.Location = new Point(110, 44);
            m_databaseName.Width = 240;
            m_databaseName.Text = DeriveDatabaseName(defaultInput);

            Label sizeLabel = new Label();
            sizeLabel.Text = "Thumbnail size:";
            sizeLabel.Location = new Point(370, 49);
            sizeLabel.AutoSize = true;

            m_thumbSize = new NumericUpDown();
            m_thumbSize.Location = new Point(470, 44);
            m_thumbSize.Width = 70;
            m_thumbSize.Minimum = 16;
            m_thumbSize.Maximum = 128;
            m_thumbSize.Value = Math.Min(Math.Max(defaultThumbSize, 16), 128);
            m_thumbSize.TextAlign = HorizontalAlignment.Right;

            m_updateExisting = new CheckBox();
            m_updateExisting.Text = "Update existing database (incremental)";
            m_updateExisting.Location = new Point(110, 72);
            m_updateExisting.AutoSize = true;

            m_progress = new ProgressBar();
            m_progress.Location = new Point(12, 100);
            m_progress.Size = new Size(ClientSize.Width - 24, 18);
            m_progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            m_status = new Label();
            m_status.Text = "Ready.";
            m_status.Location = new Point(12, 124);
            m_status.Size = new Size(ClientSize.Width - 24, 20);
            m_status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            m_status.AutoEllipsis = true;

            m_log = new TextBox();
            m_log.Location = new Point(12, 150);
            m_log.Size = new Size(ClientSize.Width - 24, ClientSize.Height - 202);
            m_log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            m_log.Multiline = true;
            m_log.ReadOnly = true;
            m_log.ScrollBars = ScrollBars.Vertical;
            m_log.BackColor = SystemColors.Window;
            m_log.Font = new Font(FontFamily.GenericMonospace, 8.5f);

            m_startButton = new Button();
            m_startButton.Text = "Create";
            m_startButton.Location = new Point(ClientSize.Width - 190, ClientSize.Height - 40);
            m_startButton.Size = new Size(85, 28);
            m_startButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            m_startButton.Click += OnStartClick;

            m_closeButton = new Button();
            m_closeButton.Text = "Close";
            m_closeButton.Location = new Point(ClientSize.Width - 98, ClientSize.Height - 40);
            m_closeButton.Size = new Size(85, 28);
            m_closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            m_closeButton.Click += OnCloseClick;

            Controls.Add(inputLabel);
            Controls.Add(m_inputPath);
            Controls.Add(m_browseButton);
            Controls.Add(nameLabel);
            Controls.Add(m_databaseName);
            Controls.Add(sizeLabel);
            Controls.Add(m_thumbSize);
            Controls.Add(m_updateExisting);
            Controls.Add(m_progress);
            Controls.Add(m_status);
            Controls.Add(m_log);
            Controls.Add(m_startButton);
            Controls.Add(m_closeButton);
        }

        private static string DeriveDatabaseName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string trimmed = path.TrimEnd('\\', '/');
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? "" : name;
        }

        private void OnBrowseClick(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select folder with images to collect the database";
                if (Directory.Exists(m_inputPath.Text))
                    dialog.SelectedPath = m_inputPath.Text;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                m_inputPath.Text = dialog.SelectedPath;
                if (m_databaseName.Text.Trim().Length == 0)
                    m_databaseName.Text = DeriveDatabaseName(dialog.SelectedPath);
            }
        }

        private void OnStartClick(object sender, EventArgs e)
        {
            if (m_running)
                return;

            string input = m_inputPath.Text.Trim();
            if (!Directory.Exists(input))
            {
                MessageBox.Show(this, "Source folder does not exist.", "New Database",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string name = m_databaseName.Text.Trim();
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, "Enter a valid database name.", "New Database",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string exePath = Path.Combine(Application.StartupPath, "NvJpegCollector.exe");
            if (!File.Exists(exePath))
            {
                MessageBox.Show(this, "NvJpegCollector.exe not found in application directory.",
                    "New Database", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string dbRoot = Path.Combine(Application.StartupPath, "databases");
            string dbFolder = Path.Combine(dbRoot, name);
            bool update = m_updateExisting.Checked;
            if (Directory.Exists(dbFolder) && !update)
            {
                if (MessageBox.Show(this,
                        string.Format("Database \"{0}\" already exists.\nRebuild it from scratch?", name),
                        "New Database", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }

            string args = string.Format("--input \"{0}\" --output \"{1}\" --name \"{2}\" --size {3} --no-pause{4}",
                input, dbRoot, name, (int)m_thumbSize.Value, update ? " --update" : "");

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            m_log.Clear();
            m_lastSummary = "";
            SetRunning(true);
            SetStatus(update ? "Updating database..." : "Creating database...");
            m_progress.Style = ProgressBarStyle.Marquee;
            m_progress.MarqueeAnimationSpeed = 30;

            try
            {
                m_process = Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                SetRunning(false);
                SetStatus("Failed to start NvJpegCollector.exe: " + ex.Message);
                return;
            }

            Process process = m_process;
            StartReader(process.StandardOutput);
            StartReader(process.StandardError);

            Thread waiter = new Thread(() =>
            {
                process.WaitForExit();
                int code = process.ExitCode;
                BeginInvokeIfNeeded(() => OnCollectorExited(code));
            });
            waiter.IsBackground = true;
            waiter.Start();
        }

        private void StartReader(StreamReader reader)
        {
            Thread thread = new Thread(() =>
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string current = line;
                    BeginInvokeIfNeeded(() => OnCollectorLine(current));
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void OnCollectorLine(string line)
        {
            if (line.Length == 0)
                return;

            Match progress = ProgressPattern.Match(line);
            if (progress.Success)
            {
                int done = int.Parse(progress.Groups[1].Value);
                int total = int.Parse(progress.Groups[2].Value);
                int percent = int.Parse(progress.Groups[3].Value);
                if (m_progress.Style != ProgressBarStyle.Blocks)
                {
                    m_progress.Style = ProgressBarStyle.Blocks;
                    m_progress.MarqueeAnimationSpeed = 0;
                }
                m_progress.Value = Math.Min(percent, 100);
                SetStatus(string.Format("Decoding {0} / {1} ({2}%)...", done, total, percent));
                return;
            }

            if (line.StartsWith("[STATS]"))
                return;

            AppendLog(line);

            if (line.StartsWith("[DONE]") || line.StartsWith("[SAVE] Saved") || line.StartsWith("[FAILED]"))
                m_lastSummary = line.StartsWith("[DONE]")
                    ? line.Substring("[DONE]".Length).Trim()
                    : line;
        }

        private void AppendLog(string line)
        {
            if (m_log.TextLength > LOG_LIMIT)
                m_log.Text = m_log.Text.Substring(m_log.TextLength / 2);
            m_log.AppendText(line + Environment.NewLine);
        }

        private void OnCollectorExited(int code)
        {
            SetRunning(false);

            if (code == 0)
            {
                if (m_progress.Style != ProgressBarStyle.Blocks)
                    m_progress.Style = ProgressBarStyle.Blocks;
                m_progress.Value = 100;
                SetStatus(m_lastSummary.Length > 0
                    ? "Done. " + m_lastSummary
                    : "Done. Database is registered and will be used by the next search.");
            }
            else if (code == 3)
            {
                m_progress.Style = ProgressBarStyle.Blocks;
                m_progress.Value = 0;
                SetStatus("Source folder is empty or unavailable - the database was left unchanged.");
            }
            else
            {
                m_progress.Style = ProgressBarStyle.Blocks;
                m_progress.Value = 0;
                SetStatus(string.Format("Failed (exit code {0}). See log.", code));
            }
        }

        private void SetRunning(bool running)
        {
            m_running = running;
            m_inputPath.Enabled = !running;
            m_browseButton.Enabled = !running;
            m_databaseName.Enabled = !running;
            m_thumbSize.Enabled = !running;
            m_updateExisting.Enabled = !running;
            m_startButton.Enabled = !running;
            m_closeButton.Text = running ? "Cancel" : "Close";
        }

        private void SetStatus(string text)
        {
            m_status.Text = text;
        }

        private void BeginInvokeIfNeeded(Action action)
        {
            if (IsDisposed || Disposing)
                return;
            try
            {
                if (InvokeRequired)
                    BeginInvoke(action);
                else
                    action();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void OnCloseClick(object sender, EventArgs e)
        {
            if (m_running)
            {
                if (MessageBox.Show(this, "Stop the collector?", "New Database",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                StopCollector();
            }
            Close();
        }

        private void StopCollector()
        {
            try
            {
                if (m_process != null && !m_process.HasExited)
                    m_process.Kill();
            }
            catch (Exception) { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (m_running && e.CloseReason == CloseReason.UserClosing)
            {
                if (MessageBox.Show(this, "The collector is still running. Stop it and close?", "New Database",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                StopCollector();
            }
            base.OnFormClosing(e);
        }
    }
}

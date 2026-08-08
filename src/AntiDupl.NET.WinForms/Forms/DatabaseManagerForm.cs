/*
 * AntiDuplPlus Program (http://github.com/Sucotasch/AntiDuplPlus).
 * Database Manager - UI for managing pre-collected image databases with pool support.
 */
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AntiDupl.NET.WinForms.Forms
{
    public partial class DatabaseManagerForm : Form
    {
        // Registry grid (all databases)
        private DataGridView m_registryGrid;
        private BindingList<DbEntry> m_registryData;

        // Pool grids
        private DataGridView m_pool1Grid;
        private DataGridView m_pool2Grid;
        private BindingList<DbEntry> m_pool1Data;
        private BindingList<DbEntry> m_pool2Data;

        // Buttons
        private Button m_btnAssignPool1;
        private Button m_btnAssignPool2;
        private Button m_btnRemovePool1;
        private Button m_btnRemovePool2;
        private Button m_btnOpenFolder;
        private Button m_btnRefresh;
        private Button m_btnUpdateAll;
        private Button m_btnClose;

        // Pool mode
        private ComboBox m_cmbPoolMode;
        private Label m_lblInfo;

        // Layout
        private SplitContainer m_splitMain;
        private SplitContainer m_splitPools;

        private const string RegistryFileName = "ad_database.xml";
        private const string PoolModeRegKey = @"Software\AntiDupl.NET\DatabaseManager";
        private List<DbEntry> m_allEntries = new List<DbEntry>();
        private bool m_dirty = false;
        private static int s_poolCompareMode = -1;
        private static int s_splitMainDistance = -1;
        private static int s_splitPoolsDistance = -1;

        public DatabaseManagerForm()
        {
            InitializeComponent();
            if (s_poolCompareMode < 0)
                s_poolCompareMode = LoadPoolMode();
            m_cmbPoolMode.SelectedIndex = s_poolCompareMode;
            LoadDatabases();
            this.FormClosing += (s, e) => { if (m_dirty) SaveDatabases(); };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RestoreWindowState();
            BeginInvoke(new Action(ApplySplitterDistances));
        }

        private void ApplySplitterDistances()
        {
            m_splitMain.Panel1MinSize = 360;
            m_splitMain.Panel2MinSize = 420;
            int mainMax = m_splitMain.Width - m_splitMain.Panel2MinSize - m_splitMain.SplitterWidth;
            if (mainMax >= m_splitMain.Panel1MinSize)
            {
                int splitMain = s_splitMainDistance;
                if (splitMain < m_splitMain.Panel1MinSize || splitMain > mainMax)
                    splitMain = m_splitMain.Width * 40 / 100;
                m_splitMain.SplitterDistance = Math.Min(Math.Max(splitMain, m_splitMain.Panel1MinSize), mainMax);
            }

            m_splitPools.Panel1MinSize = 120;
            m_splitPools.Panel2MinSize = 120;
            int poolsMax = m_splitPools.Height - m_splitPools.Panel2MinSize - m_splitPools.SplitterWidth;
            if (poolsMax >= m_splitPools.Panel1MinSize)
            {
                int splitPools = s_splitPoolsDistance;
                if (splitPools < m_splitPools.Panel1MinSize || splitPools > poolsMax)
                    splitPools = m_splitPools.Height / 2;
                m_splitPools.SplitterDistance = Math.Min(Math.Max(splitPools, m_splitPools.Panel1MinSize), poolsMax);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            SaveWindowState();
        }

        private void InitializeComponent()
        {
            this.Text = "Database Manager";
            this.MinimumSize = new Size(900, 450);
            this.StartPosition = FormStartPosition.CenterParent;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            this.Size = new Size(
                Math.Max(this.MinimumSize.Width, Math.Min(wa.Width, wa.Width * 75 / 100)),
                Math.Max(this.MinimumSize.Height, Math.Min(wa.Height, wa.Height * 80 / 100)));

            // Top panel: Info + Pool Mode
            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Top;
            topPanel.Height = 60;

            m_lblInfo = new Label();
            m_lblInfo.Text = "Assign databases to Pool1 (Reference) and Pool2 (Target) for cross-pool comparison.";
            m_lblInfo.Dock = DockStyle.Top;
            m_lblInfo.Height = 25;
            m_lblInfo.Padding = new Padding(10, 5, 10, 5);

            Panel poolModePanel = new Panel();
            poolModePanel.Dock = DockStyle.Top;
            poolModePanel.Height = 30;

            Label lblPoolMode = new Label();
            lblPoolMode.Text = "Pool Comparison Mode:";
            lblPoolMode.AutoSize = true;
            lblPoolMode.Location = new Point(10, 7);

            m_cmbPoolMode = new ComboBox();
            m_cmbPoolMode.DropDownStyle = ComboBoxStyle.DropDownList;
            m_cmbPoolMode.Items.AddRange(new object[] { 
                "None - Compare all (no pool filter)", 
                "Pool1 Internal - Only Pool1 vs Pool1", 
                "Pool2 Internal - Only Pool2 vs Pool2", 
                "Cross - Pool1 vs Pool2 only", 
                "All Pools - Only pooled images (Pool1+Pool2)" 
            });
            m_cmbPoolMode.Location = new Point(170, 4);
            m_cmbPoolMode.Width = 200;
            m_cmbPoolMode.SelectedIndexChanged += (s, e) => { 
                s_poolCompareMode = m_cmbPoolMode.SelectedIndex;
                SavePoolMode();
            };

            poolModePanel.Controls.Add(lblPoolMode);
            poolModePanel.Controls.Add(m_cmbPoolMode);

            topPanel.Controls.Add(poolModePanel);
            topPanel.Controls.Add(m_lblInfo);

            // Bottom panel: buttons
            Panel btnPanel = new Panel();
            btnPanel.Dock = DockStyle.Bottom;
            btnPanel.Height = 45;

            m_btnOpenFolder = CreateButton("Attach Database...", 10, BtnAttachDatabase_Click);
            m_btnRefresh = CreateButton("Refresh", 140, (s, e) => LoadDatabases());
            m_btnUpdateAll = CreateButton("Update All", 270, (s, e) => UpdateAllDatabases());
            m_btnClose = CreateButton("Close", 400, (s, e) => this.Close());

            btnPanel.Controls.Add(m_btnOpenFolder);
            btnPanel.Controls.Add(m_btnRefresh);
            btnPanel.Controls.Add(m_btnUpdateAll);
            btnPanel.Controls.Add(m_btnClose);

            // Center: 3-panel split
            m_splitMain = new SplitContainer();
            m_splitMain.Dock = DockStyle.Fill;
            m_splitMain.Orientation = Orientation.Vertical;

            // Left: Registry
            Panel registryPanel = CreatePanel("Registry (All)", Color.White);
            m_registryGrid = CreateGrid();
            m_registryGrid.Dock = DockStyle.Fill;
            registryPanel.Controls.Add(m_registryGrid);

            Panel registryBtnPanel = new Panel();
            registryBtnPanel.Dock = DockStyle.Bottom;
            registryBtnPanel.Height = 35;
            m_btnAssignPool1 = CreateButton("→ Pool1", 5, (s, e) => AssignSelected(1));
            m_btnAssignPool2 = CreateButton("→ Pool2", 110, (s, e) => AssignSelected(2));
            registryBtnPanel.Controls.Add(m_btnAssignPool1);
            registryBtnPanel.Controls.Add(m_btnAssignPool2);
            registryPanel.Controls.Add(registryBtnPanel);

            // Right: Pool1 + Pool2
            m_splitPools = new SplitContainer();
            m_splitPools.Dock = DockStyle.Fill;
            m_splitPools.Orientation = Orientation.Horizontal;

            // Pool1
            Panel pool1Panel = CreatePanel("Pool1 (Reference)", Color.FromArgb(230, 245, 255));
            m_pool1Grid = CreateGrid();
            m_pool1Grid.Dock = DockStyle.Fill;
            pool1Panel.Controls.Add(m_pool1Grid);

            Panel pool1BtnPanel = new Panel();
            pool1BtnPanel.Dock = DockStyle.Bottom;
            pool1BtnPanel.Height = 35;
            m_btnRemovePool1 = CreateButton("← Remove", 5, (s, e) => RemoveFromPool(1));
            pool1BtnPanel.Controls.Add(m_btnRemovePool1);
            pool1Panel.Controls.Add(pool1BtnPanel);

            // Pool2
            Panel pool2Panel = CreatePanel("Pool2 (Target)", Color.FromArgb(255, 245, 230));
            m_pool2Grid = CreateGrid();
            m_pool2Grid.Dock = DockStyle.Fill;
            pool2Panel.Controls.Add(m_pool2Grid);

            Panel pool2BtnPanel = new Panel();
            pool2BtnPanel.Dock = DockStyle.Bottom;
            pool2BtnPanel.Height = 35;
            m_btnRemovePool2 = CreateButton("← Remove", 5, (s, e) => RemoveFromPool(2));
            pool2BtnPanel.Controls.Add(m_btnRemovePool2);
            pool2Panel.Controls.Add(pool2BtnPanel);

            m_splitPools.Panel1.Controls.Add(pool1Panel);
            m_splitPools.Panel2.Controls.Add(pool2Panel);

            m_splitMain.Panel1.Controls.Add(registryPanel);
            m_splitMain.Panel2.Controls.Add(m_splitPools);

            this.Controls.Add(m_splitMain);
            this.Controls.Add(topPanel);
            this.Controls.Add(btnPanel);
        }

        private Button CreateButton(string text, int x, EventHandler onClick)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Size = new Size(120, 30);
            btn.Location = new Point(x, 5);
            btn.Click += onClick;
            return btn;
        }

        private Panel CreatePanel(string title, Color bgColor)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = bgColor;

            Label lbl = new Label();
            lbl.Text = title;
            lbl.Dock = DockStyle.Top;
            lbl.Height = 22;
            lbl.Font = new Font(Font, FontStyle.Bold);
            lbl.Padding = new Padding(5, 3, 5, 3);
            lbl.BackColor = Color.FromArgb(200, 200, 200);
            panel.Controls.Add(lbl);

            return panel;
        }

        private DataGridView CreateGrid()
        {
            DataGridView grid = new DataGridView();
            grid.AutoGenerateColumns = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.RowHeadersVisible = false;
            grid.CellContentClick += Grid_CellContentClick;

            var colEnabled = new DataGridViewCheckBoxColumn();
            colEnabled.Name = "Enabled";
            colEnabled.DataPropertyName = "Enabled";
            colEnabled.HeaderText = "On";
            colEnabled.Width = 35;
            colEnabled.MinimumWidth = 35;
            colEnabled.ReadOnly = false;
            grid.Columns.Add(colEnabled);

            var colName = new DataGridViewTextBoxColumn();
            colName.Name = "Name";
            colName.DataPropertyName = "Name";
            colName.HeaderText = "Name";
            colName.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colName.MinimumWidth = 150;
            grid.Columns.Add(colName);

            var colCount = new DataGridViewTextBoxColumn();
            colCount.Name = "ImageCount";
            colCount.DataPropertyName = "ImageCount";
            colCount.HeaderText = "Images";
            colCount.Width = 60;
            colCount.MinimumWidth = 60;
            grid.Columns.Add(colCount);

            var colStatus = new DataGridViewTextBoxColumn();
            colStatus.Name = "Status";
            colStatus.DataPropertyName = "Status";
            colStatus.HeaderText = "Status";
            colStatus.Width = 55;
            colStatus.MinimumWidth = 55;
            grid.Columns.Add(colStatus);

            var colUpdate = new DataGridViewButtonColumn();
            colUpdate.Name = "Update";
            colUpdate.HeaderText = "";
            colUpdate.Text = "Update";
            colUpdate.UseColumnTextForButtonValue = true;
            colUpdate.Width = 65;
            colUpdate.MinimumWidth = 65;
            colUpdate.FlatStyle = FlatStyle.Flat;
            grid.Columns.Add(colUpdate);

            var colDelete = new DataGridViewButtonColumn();
            colDelete.Name = "Delete";
            colDelete.HeaderText = "";
            colDelete.Text = "Delete";
            colDelete.UseColumnTextForButtonValue = true;
            colDelete.Width = 65;
            colDelete.MinimumWidth = 65;
            colDelete.FlatStyle = FlatStyle.Flat;
            grid.Columns.Add(colDelete);

            return grid;
        }

        private void LoadDatabases()
        {
            m_allEntries = LoadRegistry("");
            RefreshAllGrids();
            m_dirty = false;
        }

        private void Grid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex < 0 || e.RowIndex < 0) return;
            var grid = sender as DataGridView;
            if (grid == null) return;

            string colName = grid.Columns[e.ColumnIndex].Name;

            if (colName == "Enabled")
            {
                grid.EndEdit();
                m_dirty = true;
                SaveDatabases();
            }
            else if (colName == "Update")
            {
                var entry = grid.Rows[e.RowIndex].DataBoundItem as DbEntry;
                if (entry != null) UpdateDatabase(entry);
            }
            else if (colName == "Delete")
            {
                var entry = grid.Rows[e.RowIndex].DataBoundItem as DbEntry;
                if (entry != null) DeleteDatabase(entry);
            }
        }

        private void UpdateDatabase(DbEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Path) || string.IsNullOrEmpty(entry.Folder))
            {
                MessageBox.Show("Database path or folder is not set.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string nvJpegPath = Path.Combine(GetExeDir(), "NvJpegCollector.exe");
            if (!File.Exists(nvJpegPath))
            {
                MessageBox.Show("NvJpegCollector.exe not found in program directory.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string dbParent = Path.GetDirectoryName(entry.Folder);
            string dbName = Path.GetFileName(entry.Folder);

            var result = MessageBox.Show(
                $"Update database \"{entry.Name}\"?\n\nSource: {entry.Path}\nDatabase: {entry.Folder}\n\nThis will add new files and remove deleted files from the database.",
                "Update Database", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            entry.Status = "Updating...";
            SaveDatabases();
            RefreshAllGrids();

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = nvJpegPath,
                    Arguments = $"--input \"{entry.Path}\" --output \"{dbParent}\" --name \"{dbName}\" --update",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    if (proc.ExitCode == 0)
                    {
                        // Parse count from output
                        int newCount = entry.ImageCount;
                        var lines = stdout.Split('\n');
                        foreach (var line in lines)
                        {
                            if (line.Contains("[UPDATE] Final database:"))
                            {
                                var parts = line.Split(':');
                                if (parts.Length >= 2 && int.TryParse(parts[parts.Length - 1].Trim().Split(' ')[0], out int c))
                                    newCount = c;
                            }
                        }

                        entry.ImageCount = newCount;
                        entry.Status = "Ready";
                        MessageBox.Show($"Database updated successfully.\n{newCount} images.", "Update Complete",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        entry.Status = "Error";
                        MessageBox.Show($"Update failed (exit code {proc.ExitCode}):\n{stderr}\n{stdout}", "Update Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                entry.Status = "Error";
                MessageBox.Show($"Update failed: {ex.Message}", "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            SaveDatabases();
            RefreshAllGrids();
        }

        private void DeleteDatabase(DbEntry entry)
        {
            var result = MessageBox.Show(
                $"Delete database \"{entry.Name}\"?\n\nFolder: {entry.Folder}\n\nThe database folder will be moved to the Recycle Bin.",
                "Delete Database", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            try
            {
                if (!string.IsNullOrEmpty(entry.Folder) && Directory.Exists(entry.Folder))
                {
                    var shf = new SHFILEOPSTRUCT();
                    shf.wFunc = FO_DELETE;
                    shf.pFrom = entry.Folder + "\0\0";
                    shf.fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT;
                    int ret = SHFileOperation(ref shf);

                    if (ret != 0)
                    {
                        MessageBox.Show($"Failed to move to Recycle Bin (error {ret}).", "Delete Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                m_allEntries.Remove(entry);
                SaveDatabases();
                RefreshAllGrids();
                MessageBox.Show($"Database \"{entry.Name}\" moved to Recycle Bin.", "Deleted",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Delete failed: {ex.Message}", "Delete Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateAllDatabases()
        {
            var entriesToUpdate = m_allEntries.Where(e => e.Enabled && !string.IsNullOrEmpty(e.Path) && !string.IsNullOrEmpty(e.Folder)).ToList();
            if (entriesToUpdate.Count == 0)
            {
                MessageBox.Show("No databases to update.", "Update All", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string nvJpegPath = Path.Combine(GetExeDir(), "NvJpegCollector.exe");
            if (!File.Exists(nvJpegPath))
            {
                MessageBox.Show("NvJpegCollector.exe not found in program directory.", "Update All", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Update {entriesToUpdate.Count} database(s)?\n\nThis may take a while.",
                "Update All", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            int successCount = 0, failCount = 0;
            foreach (var entry in entriesToUpdate)
            {
                entry.Status = "Updating...";
                SaveDatabases();
                RefreshAllGrids();
                Application.DoEvents();

                try
                {
                    string dbParent = Path.GetDirectoryName(entry.Folder);
                    string dbName = Path.GetFileName(entry.Folder);

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = nvJpegPath,
                        Arguments = $"--input \"{entry.Path}\" --output \"{dbParent}\" --name \"{dbName}\" --update",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var proc = System.Diagnostics.Process.Start(psi))
                    {
                        string stdout = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit();

                        if (proc.ExitCode == 0)
                        {
                            int newCount = entry.ImageCount;
                            foreach (var line in stdout.Split('\n'))
                            {
                                if (line.Contains("[UPDATE] Final database:"))
                                {
                                    var parts = line.Split(':');
                                    if (parts.Length >= 2 && int.TryParse(parts[parts.Length - 1].Trim().Split(' ')[0], out int c))
                                        newCount = c;
                                }
                            }
                            entry.ImageCount = newCount;
                            entry.Status = "Ready";
                            successCount++;
                        }
                        else
                        {
                            entry.Status = "Error";
                            failCount++;
                        }
                    }
                }
                catch
                {
                    entry.Status = "Error";
                    failCount++;
                }
            }

            SaveDatabases();
            RefreshAllGrids();
            MessageBox.Show(
                $"Update complete.\n\nUpdated: {successCount}\nFailed: {failCount}",
                "Update All", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RefreshAllGrids()
        {
            var registry = m_allEntries.Where(e => e.Pool == 0).ToList();
            var pool1 = m_allEntries.Where(e => e.Pool == 1).ToList();
            var pool2 = m_allEntries.Where(e => e.Pool == 2).ToList();

            m_registryData = new BindingList<DbEntry>(registry);
            m_pool1Data = new BindingList<DbEntry>(pool1);
            m_pool2Data = new BindingList<DbEntry>(pool2);

            m_registryGrid.DataSource = m_registryData;
            m_pool1Grid.DataSource = m_pool1Data;
            m_pool2Grid.DataSource = m_pool2Data;
        }

        private void AssignSelected(int pool)
        {
            if (m_registryGrid.SelectedRows.Count == 0) return;
            foreach (DataGridViewRow row in m_registryGrid.SelectedRows)
            {
                var entry = row.DataBoundItem as DbEntry;
                if (entry != null) entry.Pool = pool;
            }
            SaveDatabases();
            RefreshAllGrids();
        }

        private void RemoveFromPool(int pool)
        {
            DataGridView grid = pool == 1 ? m_pool1Grid : m_pool2Grid;
            if (grid.SelectedRows.Count == 0) return;
            foreach (DataGridViewRow row in grid.SelectedRows)
            {
                var entry = row.DataBoundItem as DbEntry;
                if (entry != null) entry.Pool = 0;
            }
            SaveDatabases();
            RefreshAllGrids();
        }

        private void SaveDatabases()
        {
            string filePath = Path.Combine(GetRegistryDir(), RegistryFileName);
            Directory.CreateDirectory(GetRegistryDir());

            string content = "<DatabaseRegistry>\n";
            foreach (var entry in m_allEntries) {
                content += "  <Database";
                content += $" Path=\"{entry.Path}\"";
                if (!string.IsNullOrEmpty(entry.Folder)) content += $" Folder=\"{entry.Folder}\"";
                if (!string.IsNullOrEmpty(entry.Name)) content += $" Name=\"{entry.Name}\"";
                content += $" Enabled=\"{(entry.Enabled ? "true" : "false")}\"";
                content += $" ThumbSize=\"{entry.ThumbSize}\"";
                content += $" Count=\"{entry.ImageCount}\" Status=\"{entry.Status}\"";
                if (entry.Pool != 0) content += $" Pool=\"{entry.Pool}\"";
                content += "/>\n";
            }
            content += "</DatabaseRegistry>\n";
            File.WriteAllText(filePath, content);
        }

        private void BtnAttachDatabase_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select a database folder containing index.adi and 0000.adi";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string folder = dialog.SelectedPath;
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    MessageBox.Show("Selected folder does not exist.", "Attach Database", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string indexPath = Path.Combine(folder, "index.adi");
                if (!File.Exists(indexPath))
                {
                    MessageBox.Show("Selected folder does not contain index.adi.\n\n" +
                        "A valid database folder has index.adi and 0000.adi created by NvJpegCollector.",
                        "Attach Database", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string lower = folder.TrimEnd('\\', '/').ToLowerInvariant();
                foreach (var entry in m_allEntries)
                {
                    if (!string.IsNullOrEmpty(entry.Folder) &&
                        entry.Folder.TrimEnd('\\', '/').ToLowerInvariant() == lower)
                    {
                        MessageBox.Show($"Database \"{entry.Name}\" is already attached.", "Attach Database",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }

                int thumbSize;
                int imageCount;
                ParseAdiInfo(indexPath, out thumbSize, out imageCount);

                var db = new DbEntry
                {
                    Enabled = true,
                    Name = Path.GetFileName(folder.TrimEnd('\\', '/')),
                    Path = folder,
                    Folder = folder,
                    ImageCount = imageCount,
                    ThumbSize = thumbSize,
                    Status = "Ready",
                    Pool = 0
                };

                m_allEntries.Add(db);
                SaveDatabases();
                RefreshAllGrids();
                MessageBox.Show($"Database \"{db.Name}\" attached.\n\n{imageCount} images, thumb {thumbSize}x{thumbSize}.",
                    "Attach Database", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// Parses thumb size and image count from index.adi (both DLL-native "adii" and
        /// collector-native formats). On any parse failure defaults are returned.
        /// Collector-native layout: thumbSize(u32) + groupCount(u64) + per group:
        ///   key(i16) + firstLen(u64) + first(wchar) + lastLen(u64) + last(wchar) + imgCount(u64)
        /// </summary>
        private static void ParseAdiInfo(string indexPath, out int thumbSize, out int imageCount)
        {
            thumbSize = 32;
            imageCount = 0;
            try
            {
                using (var fs = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(fs))
                {
                    if (fs.Length < 12) return;

                    uint firstBytes = reader.ReadUInt32();
                    bool dllNative = (firstBytes == 0x69696461u); // "adii"
                    if (dllNative)
                    {
                        // DLL-native index layout is more complex; only count records by scanning
                        // the 0000.adi sibling file is unreliable here, so leave defaults.
                        return;
                    }

                    thumbSize = (int)firstBytes;
                    if (thumbSize <= 0 || thumbSize > 1024) thumbSize = 32;

                    ulong groupCount = reader.ReadUInt64();
                    if (groupCount > 1000000) return;

                    long total = 0;
                    for (ulong g = 0; g < groupCount; g++)
                    {
                        reader.ReadInt16();           // key
                        ulong firstLen = reader.ReadUInt64();
                        if (firstLen > 10000) return;
                        fs.Seek((long)firstLen * 2, SeekOrigin.Current);
                        ulong lastLen = reader.ReadUInt64();
                        if (lastLen > 10000) return;
                        fs.Seek((long)lastLen * 2, SeekOrigin.Current);
                        ulong imgCount = reader.ReadUInt64();
                        if (imgCount > 100000000) return;
                        total += (long)imgCount;
                    }

                    if (total >= 0 && total <= int.MaxValue)
                        imageCount = (int)total;
                }
            }
            catch
            {
                thumbSize = 32;
                imageCount = 0;
            }
        }

        // --- Portable Path helpers ---

        private static string GetExeDir() {
            return System.IO.Path.GetDirectoryName(Application.ExecutablePath);
        }

        private static string ResolvePath(string path) {
            if (string.IsNullOrEmpty(path)) return "";
            if (!System.IO.Path.IsPathRooted(path)) {
                return System.IO.Path.Combine(GetExeDir(), path);
            }
            return path;
        }

        private static string GetRegistryDir() {
            return GetExeDir();
        }

        private static List<DbEntry> LoadRegistry(string userPath) {
            var list = new List<DbEntry>();
            string registryDir = GetRegistryDir();
            string filePath = Path.Combine(registryDir, RegistryFileName);
            if (!File.Exists(filePath)) return list;

            string content = File.ReadAllText(filePath);
            int pos = 0;
            while ((pos = content.IndexOf("<Database ", pos)) >= 0) {
                int endPos = content.IndexOf("/>", pos);
                if (endPos < 0) break;
                string tag = content.Substring(pos, endPos - pos + 2);
                pos = endPos + 2;

                var entry = new DbEntry();
                entry.Name = GetAttr(tag, "Name");
                entry.Path = GetAttr(tag, "Path");
                entry.Folder = GetAttr(tag, "Folder");
                entry.ImageCount = int.Parse(GetAttr(tag, "Count") ?? "0");
                entry.Status = GetAttr(tag, "Status") ?? "Ready";
                entry.Enabled = GetAttr(tag, "Enabled") != "false";
                entry.ThumbSize = int.Parse(GetAttr(tag, "ThumbSize") ?? "32");
                entry.Pool = int.Parse(GetAttr(tag, "Pool") ?? "0");

                if (string.IsNullOrEmpty(entry.Name)) {
                    if (!string.IsNullOrEmpty(entry.Path)) {
                        try { entry.Name = System.IO.Path.GetFileName(entry.Path.TrimEnd('\\', '/')); } catch { entry.Name = entry.Path; }
                    } else {
                        entry.Name = "(Unknown)";
                    }
                }
                if (!string.IsNullOrEmpty(entry.Folder)) {
                    entry.Folder = ResolvePath(entry.Folder);
                }

                if (!string.IsNullOrEmpty(entry.Path))
                    list.Add(entry);
            }
            return list;
        }

        private static string GetAttr(string tag, string attr)
        {
            string search = attr + "=\"";
            int start = tag.IndexOf(search);
            if (start < 0) return null;
            start += search.Length;
            int end = tag.IndexOf("\"", start);
            if (end < 0) return null;
            return tag.Substring(start, end - start);
        }

        // --- Data model ---

        public class DbEntry
        {
            public bool Enabled { get; set; }
            public string Name { get; set; }
            public string Path { get; set; }
            public string Folder { get; set; }
            public int ImageCount { get; set; }
            public int ThumbSize { get; set; } = 32;
            public string Status { get; set; }
            public int Pool { get; set; } = 0;
        }

        /// <summary>
        /// Reads enabled database paths from ad_database.xml for search integration.
        /// </summary>
        public static List<string> GetEnabledDatabasePaths()
        {
            var paths = new List<string>();
            var entries = LoadRegistry("");
            foreach (var entry in entries)
            {
                if (entry.Enabled && entry.Status == "Ready" && !string.IsNullOrEmpty(entry.Path))
                    paths.Add(entry.Path);
            }
            
            // Debug: log to file
            try {
                string logPath = System.IO.Path.Combine(GetExeDir(), "cs_debug.log");
                using (var sw = new System.IO.StreamWriter(logPath, true))
                {
                    sw.WriteLine($"GetEnabledDatabasePaths: {entries.Count} entries, {paths.Count} enabled");
                    foreach (var p in paths)
                        sw.WriteLine($"  {p}");
                }
            } catch { }
            
            return paths;
        }

        /// <summary>
        /// Gets pool assignments from ad_database.xml.
        /// Returns dictionary: path -> pool ID (0=none, 1=Pool1, 2=Pool2)
        /// </summary>
        public static Dictionary<string, int> GetPoolAssignments()
        {
            var map = new Dictionary<string, int>();
            var entries = LoadRegistry("");
            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.Path))
                    map[entry.Path] = entry.Pool;
            }
            return map;
        }

        /// <summary>
        /// Gets the current pool comparison mode from the UI.
        /// </summary>
        public static int GetPoolCompareMode()
        {
            if (s_poolCompareMode < 0)
                s_poolCompareMode = LoadPoolMode();
            return s_poolCompareMode;
        }

        private static void SavePoolMode()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(PoolModeRegKey))
                {
                    key.SetValue("PoolCompareMode", s_poolCompareMode, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        private static int LoadPoolMode()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PoolModeRegKey))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("PoolCompareMode");
                        if (val is int v && v >= 0 && v <= 4)
                            return v;
                    }
                }
            }
            catch { }
            return 0;
        }

        // --- Window state persistence ---

        private const string BoundsRegKey = "MainFormBounds";
        private const string SplitMainRegKey = "SplitMainDistance";
        private const string SplitPoolsRegKey = "SplitPoolsDistance";

        private void SaveWindowState()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(PoolModeRegKey))
                {
                    if (WindowState != FormWindowState.Minimized)
                    {
                        key.SetValue(BoundsRegKey,
                            string.Format("{0},{1},{2},{3}", Location.X, Location.Y, Size.Width, Size.Height),
                            Microsoft.Win32.RegistryValueKind.String);
                    }
                    key.SetValue(SplitMainRegKey, m_splitMain.SplitterDistance, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue(SplitPoolsRegKey, m_splitPools.SplitterDistance, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        private void RestoreWindowState()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PoolModeRegKey))
                {
                    if (key == null) return;

                    var bounds = key.GetValue(BoundsRegKey) as string;
                    if (!string.IsNullOrEmpty(bounds))
                    {
                        string[] parts = bounds.Split(',');
                        if (parts.Length == 4 &&
                            int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y) &&
                            int.TryParse(parts[2], out int w) && int.TryParse(parts[3], out int h))
                        {
                            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                            w = Math.Max(MinimumSize.Width, Math.Min(w, wa.Width));
                            h = Math.Max(MinimumSize.Height, Math.Min(h, wa.Height));
                            x = Math.Max(wa.Left, Math.Min(x, wa.Right - w));
                            y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - h));
                            Location = new Point(x, y);
                            Size = new Size(w, h);
                        }
                    }

                    if (key.GetValue(SplitMainRegKey) is int sm) s_splitMainDistance = sm;
                    if (key.GetValue(SplitPoolsRegKey) is int sp) s_splitPoolsDistance = sp;
                }
            }
            catch { }
        }

        // --- Win32 Recycle Bin ---
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        const int FO_DELETE = 3;
        const int FOF_ALLOWUNDO = 0x40;
        const int FOF_NOCONFIRMATION = 0x10;
        const int FOF_SILENT = 0x4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }
    }
}

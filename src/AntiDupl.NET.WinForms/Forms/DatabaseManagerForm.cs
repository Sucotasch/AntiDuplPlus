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
        private Button m_btnClose;

        // Pool mode
        private ComboBox m_cmbPoolMode;
        private Label m_lblInfo;

        private const string RegistryFileName = "ad_database.xml";
        private List<DbEntry> m_allEntries = new List<DbEntry>();

        public DatabaseManagerForm()
        {
            InitializeComponent();
            LoadDatabases();
        }

        private void InitializeComponent()
        {
            this.Text = "Database Manager";
            this.Size = new Size(1100, 550);
            this.MinimumSize = new Size(900, 450);
            this.StartPosition = FormStartPosition.CenterParent;

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
            m_cmbPoolMode.Items.AddRange(new object[] { "None (All)", "Pool1 Internal", "Pool2 Internal", "Cross (Pool1 vs Pool2)", "All Pools" });
            m_cmbPoolMode.SelectedIndex = 0;
            m_cmbPoolMode.Location = new Point(170, 4);
            m_cmbPoolMode.Width = 200;

            poolModePanel.Controls.Add(lblPoolMode);
            poolModePanel.Controls.Add(m_cmbPoolMode);

            topPanel.Controls.Add(poolModePanel);
            topPanel.Controls.Add(m_lblInfo);

            // Bottom panel: buttons
            Panel btnPanel = new Panel();
            btnPanel.Dock = DockStyle.Bottom;
            btnPanel.Height = 45;

            m_btnOpenFolder = CreateButton("Open Folder", 10, BtnOpenFolder_Click);
            m_btnRefresh = CreateButton("Refresh", 140, (s, e) => LoadDatabases());
            m_btnClose = CreateButton("Close", 270, (s, e) => this.Close());

            btnPanel.Controls.Add(m_btnOpenFolder);
            btnPanel.Controls.Add(m_btnRefresh);
            btnPanel.Controls.Add(m_btnClose);

            // Center: 3-panel split
            SplitContainer splitMain = new SplitContainer();
            splitMain.Dock = DockStyle.Fill;
            splitMain.Orientation = Orientation.Vertical;
            splitMain.SplitterDistance = 400;

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
            SplitContainer splitPools = new SplitContainer();
            splitPools.Dock = DockStyle.Fill;
            splitPools.Orientation = Orientation.Horizontal;
            splitPools.SplitterDistance = 200;

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

            splitPools.Panel1.Controls.Add(pool1Panel);
            splitPools.Panel2.Controls.Add(pool2Panel);

            splitMain.Panel1.Controls.Add(registryPanel);
            splitMain.Panel2.Controls.Add(splitPools);

            this.Controls.Add(splitMain);
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

            var colEnabled = new DataGridViewCheckBoxColumn();
            colEnabled.Name = "Enabled";
            colEnabled.DataPropertyName = "Enabled";
            colEnabled.HeaderText = "On";
            colEnabled.Width = 35;
            colEnabled.ReadOnly = false;
            grid.Columns.Add(colEnabled);

            var colName = new DataGridViewTextBoxColumn();
            colName.Name = "Name";
            colName.DataPropertyName = "Name";
            colName.HeaderText = "Name";
            colName.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns.Add(colName);

            var colCount = new DataGridViewTextBoxColumn();
            colCount.Name = "ImageCount";
            colCount.DataPropertyName = "ImageCount";
            colCount.HeaderText = "Images";
            colCount.Width = 60;
            grid.Columns.Add(colCount);

            var colStatus = new DataGridViewTextBoxColumn();
            colStatus.Name = "Status";
            colStatus.DataPropertyName = "Status";
            colStatus.HeaderText = "Status";
            colStatus.Width = 55;
            grid.Columns.Add(colStatus);

            return grid;
        }

        private void LoadDatabases()
        {
            m_allEntries = LoadRegistry("");
            RefreshAllGrids();
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

        private void BtnOpenFolder_Click(object sender, EventArgs e)
        {
            if (m_registryGrid.SelectedRows.Count == 0) return;
            var row = m_registryGrid.SelectedRows[0];
            string folder = row.Cells["Folder"].Value as string;
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                System.Diagnostics.Process.Start("explorer.exe", folder);
            }
            else
            {
                MessageBox.Show("Database folder not found: " + (folder ?? "(empty)"), "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
    }
}

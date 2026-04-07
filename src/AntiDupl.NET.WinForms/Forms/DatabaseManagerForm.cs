/*
* AntiDuplPlus Program (http://github.com/Sucotasch/AntiDuplPlus).
* Database Manager - UI for managing pre-collected image databases.
*/
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AntiDupl.NET.WinForms.Forms
{
    public partial class DatabaseManagerForm : Form
    {
        private DataGridView m_grid;
        private Button m_btnRemove;
        private Button m_btnOpenFolder;
        private Button m_btnClose;
        private Label m_lblInfo;

        private const string RegistryFileName = "ad_database.xml";

        public DatabaseManagerForm()
        {
            InitializeComponent();
            LoadDatabases();
        }

        private void InitializeComponent()
        {
            this.Text = "Database Manager";
            this.Size = new Size(900, 500);
            this.MinimumSize = new Size(700, 400);
            this.StartPosition = FormStartPosition.CenterParent;

            // Info label
            m_lblInfo = new Label();
            m_lblInfo.Text = "Manage pre-collected image databases. Databases with Status='Ready' are loaded automatically.";
            m_lblInfo.Dock = DockStyle.Top;
            m_lblInfo.Height = 30;
            m_lblInfo.Padding = new Padding(10, 5, 10, 5);

            // Grid
            m_grid = new DataGridView();
            m_grid.Dock = DockStyle.Fill;
            m_grid.AutoGenerateColumns = false;
            m_grid.AllowUserToAddRows = false;
            m_grid.AllowUserToDeleteRows = false;
            m_grid.ReadOnly = true;
            m_grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            m_grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            var colName = new DataGridViewTextBoxColumn();
            colName.Name = "Name";
            colName.DataPropertyName = "Name";
            colName.HeaderText = "Database Name";
            colName.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            m_grid.Columns.Add(colName);

            var colPath = new DataGridViewTextBoxColumn();
            colPath.Name = "Path";
            colPath.DataPropertyName = "Path";
            colPath.HeaderText = "Search Path";
            colPath.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            m_grid.Columns.Add(colPath);

            var colFolder = new DataGridViewTextBoxColumn();
            colFolder.Name = "Folder";
            colFolder.DataPropertyName = "Folder";
            colFolder.HeaderText = "Database Folder";
            colFolder.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            m_grid.Columns.Add(colFolder);

            var colCount = new DataGridViewTextBoxColumn();
            colCount.Name = "ImageCount";
            colCount.DataPropertyName = "ImageCount";
            colCount.HeaderText = "Images";
            colCount.Width = 80;
            m_grid.Columns.Add(colCount);

            var colStatus = new DataGridViewTextBoxColumn();
            colStatus.Name = "Status";
            colStatus.DataPropertyName = "Status";
            colStatus.HeaderText = "Status";
            colStatus.Width = 80;
            m_grid.Columns.Add(colStatus);

            // Button panel
            Panel btnPanel = new Panel();
            btnPanel.Dock = DockStyle.Bottom;
            btnPanel.Height = 40;

            m_btnOpenFolder = new Button();
            m_btnOpenFolder.Text = "Open Folder";
            m_btnOpenFolder.Size = new Size(120, 30);
            m_btnOpenFolder.Location = new Point(10, 5);
            m_btnOpenFolder.Click += BtnOpenFolder_Click;

            m_btnRemove = new Button();
            m_btnRemove.Text = "Remove";
            m_btnRemove.Size = new Size(120, 30);
            m_btnRemove.Location = new Point(140, 5);
            m_btnRemove.Click += BtnRemove_Click;

            m_btnClose = new Button();
            m_btnClose.Text = "Close";
            m_btnClose.Size = new Size(120, 30);
            m_btnClose.Location = new Point(270, 5);
            m_btnClose.Click += (s, e) => this.Close();

            btnPanel.Controls.Add(m_btnOpenFolder);
            btnPanel.Controls.Add(m_btnRemove);
            btnPanel.Controls.Add(m_btnClose);

            this.Controls.Add(m_grid);
            this.Controls.Add(m_lblInfo);
            this.Controls.Add(btnPanel);
        }

        private void LoadDatabases()
        {
            var databases = LoadRegistry(""); // Uses ExeDir internally
            m_grid.DataSource = databases;
            m_grid.Refresh();
        }

        private void BtnOpenFolder_Click(object sender, EventArgs e)
        {
            if (m_grid.SelectedRows.Count == 0) return;
            var row = m_grid.SelectedRows[0];
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

        private void BtnRemove_Click(object sender, EventArgs e)
        {
            if (m_grid.SelectedRows.Count == 0) return;
            var row = m_grid.SelectedRows[0];
            string path = row.Cells["Path"].Value as string;
            if (string.IsNullOrEmpty(path)) return;

            var result = MessageBox.Show($"Remove database entry for:\n{path}\n\n(This will not delete the database files)",
                "Confirm Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                RemoveFromRegistry(path, "");
                LoadDatabases();
            }
        }

        // --- Portable Path helpers ---

        private static string GetExeDir() {
            return System.IO.Path.GetDirectoryName(Application.ExecutablePath);
        }

        private static string ResolvePath(string path) {
            if (string.IsNullOrEmpty(path)) return "";
            // If path is relative (doesn't start with drive letter or \), combine with ExeDir
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
            string registryDir = GetRegistryDir(); // Always use exe dir for portable registry
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
                
                // Fallback for old entries that don't have Name or Folder
                if (string.IsNullOrEmpty(entry.Name)) {
                    if (!string.IsNullOrEmpty(entry.Path)) {
                        try { entry.Name = System.IO.Path.GetFileName(entry.Path.TrimEnd('\\', '/')); } catch { entry.Name = entry.Path; }
                    } else {
                        entry.Name = "(Unknown)";
                    }
                }
                if (string.IsNullOrEmpty(entry.Folder)) {
                     // Fallback for old entries: assume default location or same dir as path
                     // But since we are moving to portable, we can't guess reliably. 
                     // We'll leave it empty or try to find index.adi near the path.
                } else {
                    // Resolve relative path
                    entry.Folder = ResolvePath(entry.Folder);
                }

                if (!string.IsNullOrEmpty(entry.Path))
                    list.Add(entry);
            }
            return list;
        }

        private static void RemoveFromRegistry(string path, string userPath)
        {
            string filePath = Path.Combine(GetRegistryDir(), RegistryFileName);
            if (!File.Exists(filePath)) return;

            string content = File.ReadAllText(filePath);
            string newContent = "<DatabaseRegistry>\n";
            int pos = 0;
            while ((pos = content.IndexOf("<Database ", pos)) >= 0)
            {
                int endPos = content.IndexOf("/>", pos);
                if (endPos < 0) break;
                string tag = content.Substring(pos, endPos - pos + 2);
                pos = endPos + 2;

                string dbPath = GetAttr(tag, "Path");
                if (dbPath != path)
                    newContent += tag + "\n";
            }
            newContent += "</DatabaseRegistry>\n";
            Directory.CreateDirectory(GetRegistryDir());
            File.WriteAllText(filePath, newContent);
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
            public string Name { get; set; }
            public string Path { get; set; }
            public string Folder { get; set; }
            public int ImageCount { get; set; }
            public string Status { get; set; }
        }
    }
}

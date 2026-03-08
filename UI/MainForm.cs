// ============================================================
// UI/MainForm.cs
// Thể hiện: WinForms, Cross-thread UI update, Event handling
// ============================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using IndustrialMonitor.Core;
using IndustrialMonitor.Hardware;
using IndustrialMonitor.Models;
using IndustrialMonitor.Network;

namespace IndustrialMonitor.UI
{
    /// <summary>
    /// Main Form - Màn hình chính của ứng dụng
    /// Thể hiện: WinForms, InvokeRequired, multi-thread UI update
    /// </summary>
    public class MainForm : Form
    {
        // --- Controls ---
        private TabControl tabControl;
        private TabPage tabDashboard, tabDevices, tabLogs, tabNetwork;
        private DataGridView dgvDevices;
        private RichTextBox rtbLogs;
        private ListView lvAlarms;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatus, lblThreadCount, lblConnections;
        private Panel pnlDashboard;
        private System.Windows.Forms.Timer uiRefreshTimer;

        // --- Business Logic ---
        private ThreadManager _threadManager;
        private WorkerPool _workerPool;
        private TcpServer _tcpServer;
        private SerialPortManager _serialManager;
        private readonly List<DeviceModel> _devices = new List<DeviceModel>();

        public MainForm()
        {
            InitializeComponents();
            InitializeSystem();
            SetupEventHandlers();
        }

        // ---- UI INITIALIZATION ----

        private void InitializeComponents()
        {
            this.Text = "IndustrialMonitor v1.0";
            this.Size = new Size(1200, 750);
            this.MinimumSize = new Size(900, 600);
            this.StartPosition = FormStartPosition.CenterScreen;

            // Status Strip
            statusStrip = new StatusStrip();
            lblStatus = new ToolStripStatusLabel("Ready") { Spring = false };
            lblThreadCount = new ToolStripStatusLabel("Threads: 0") { Spring = false };
            lblConnections = new ToolStripStatusLabel("Connections: 0") { Spring = true };
            statusStrip.Items.AddRange(new ToolStripItem[] { lblStatus, lblConnections, lblThreadCount });

            // Tab Control
            tabControl = new TabControl { Dock = DockStyle.Fill };
            tabDashboard = new TabPage("📊 Dashboard");
            tabDevices = new TabPage("🖥 Devices");
            tabLogs = new TabPage("📋 Logs");
            tabNetwork = new TabPage("🌐 Network");

            // Dashboard Panel
            pnlDashboard = CreateDashboardPanel();
            tabDashboard.Controls.Add(pnlDashboard);

            // Device Grid
            dgvDevices = CreateDeviceGrid();
            tabDevices.Controls.Add(dgvDevices);

            // Log View
            rtbLogs = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.LimeGreen,
                Font = new Font("Consolas", 9f)
            };
            tabLogs.Controls.Add(rtbLogs);

            // Network Tab
            tabNetwork.Controls.Add(CreateNetworkPanel());

            tabControl.TabPages.AddRange(new TabPage[] {
                tabDashboard, tabDevices, tabLogs, tabNetwork
            });

            // Menu Strip
            var menuStrip = CreateMenuStrip();

            this.Controls.AddRange(new Control[] { menuStrip, tabControl, statusStrip });

            // UI Refresh Timer
            uiRefreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
            uiRefreshTimer.Tick += (s, e) => RefreshStatusBar();
            uiRefreshTimer.Start();
        }

        private Panel CreateDashboardPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            var lblTitle = new Label
            {
                Text = "🏭 Industrial Monitor Dashboard",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 40,
                ForeColor = Color.DarkBlue
            };

            // Alarm list
            lvAlarms = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            lvAlarms.Columns.AddRange(new ColumnHeader[]
            {
                new ColumnHeader { Text = "Time", Width = 150 },
                new ColumnHeader { Text = "Device", Width = 100 },
                new ColumnHeader { Text = "Level", Width = 80 },
                new ColumnHeader { Text = "Message", Width = 400 }
            });

            panel.Controls.AddRange(new Control[] { lvAlarms, lblTitle });
            return panel;
        }

        private DataGridView CreateDeviceGrid()
        {
            var dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White
            };

            dgv.Columns.AddRange(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { Name = "colId", HeaderText = "ID", FillWeight = 5 },
                new DataGridViewTextBoxColumn { Name = "colName", HeaderText = "Device Name", FillWeight = 20 },
                new DataGridViewTextBoxColumn { Name = "colType", HeaderText = "Type", FillWeight = 10 },
                new DataGridViewTextBoxColumn { Name = "colStatus", HeaderText = "Status", FillWeight = 10 },
                new DataGridViewTextBoxColumn { Name = "colIp", HeaderText = "IP:Port", FillWeight = 15 },
                new DataGridViewTextBoxColumn { Name = "colCom", HeaderText = "COM Port", FillWeight = 10 },
                new DataGridViewTextBoxColumn { Name = "colLastSeen", HeaderText = "Last Seen", FillWeight = 15 }
            });

            // Color rows by status
            dgv.CellFormatting += (s, e) =>
            {
                if (e.RowIndex >= 0 && dgv.Columns[e.ColumnIndex].Name == "colStatus")
                {
                    var status = e.Value?.ToString();
                    e.CellStyle.ForeColor = status == "Online" ? Color.Green :
                                            status == "Error" ? Color.Red :
                                            status == "Warning" ? Color.Orange : Color.Gray;
                }
            };

            return dgv;
        }

        private Panel CreateNetworkPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            var grpServer = new GroupBox { Text = "TCP Server", Dock = DockStyle.Top, Height = 120 };

            var btnStartServer = new Button { Text = "Start Server", Location = new Point(10, 25), Size = new Size(100, 30) };
            var btnStopServer = new Button { Text = "Stop Server", Location = new Point(120, 25), Size = new Size(100, 30) };
            var lblServerStatus = new Label { Text = "Status: Stopped", Location = new Point(10, 65), AutoSize = true };

            btnStartServer.Click += (s, e) =>
            {
                _tcpServer.Start();
                lblServerStatus.Text = $"Status: Running on port {_tcpServer.Port}";
                lblServerStatus.ForeColor = Color.Green;
            };

            btnStopServer.Click += (s, e) =>
            {
                _tcpServer.Stop();
                lblServerStatus.Text = "Status: Stopped";
                lblServerStatus.ForeColor = Color.Red;
            };

            grpServer.Controls.AddRange(new Control[] { btnStartServer, btnStopServer, lblServerStatus });
            panel.Controls.Add(grpServer);
            return panel;
        }

        private MenuStrip CreateMenuStrip()
        {
            var menu = new MenuStrip();
            var fileMenu = new ToolStripMenuItem("File");
            var deviceMenu = new ToolStripMenuItem("Devices");
            var helpMenu = new ToolStripMenuItem("Help");

            fileMenu.DropDownItems.AddRange(new ToolStripItem[]
            {
                new ToolStripMenuItem("Connect All", null, (s,e) => ConnectAllDevices()),
                new ToolStripMenuItem("Disconnect All", null, (s,e) => DisconnectAllDevices()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Exit", null, (s,e) => this.Close())
            });

            deviceMenu.DropDownItems.Add("Add Device", null, (s, e) => ShowAddDeviceDialog());

            menu.Items.AddRange(new ToolStripItem[] { fileMenu, deviceMenu, helpMenu });
            return menu;
        }

        // ---- SYSTEM INITIALIZATION ----

        private void InitializeSystem()
        {
            _threadManager = new ThreadManager();
            _workerPool = new WorkerPool(AppConfig.Current.WorkerThreadCount);
            _tcpServer = new TcpServer(AppConfig.Current.ServerPort);

            // Setup Logger callback
            Logger.Instance.LogAdded += (s, e) =>
            {
                _workerPool.Enqueue(() => AppendLog(e.Message, e.Level));
            };

            // Add demo devices
            AddDemoDevices();
        }

        private void AddDemoDevices()
        {
            var devices = new[]
            {
                new DeviceModel { Id=1, Name="PLC_Line1",     Type=DeviceType.PLC,    IpAddress="192.168.1.10", Port=502  },
                new DeviceModel { Id=2, Name="Temp_Sensor_A", Type=DeviceType.Sensor, ComPort="COM3"                      },
                new DeviceModel { Id=3, Name="HMI_Main",      Type=DeviceType.HMI,    IpAddress="192.168.1.20", Port=5000 }
            };

            foreach (var model in devices)
            {
                _devices.Add(model);
                dgvDevices.Rows.Add(
                    model.Id,
                    model.Name,
                    model.Type,
                    model.Status,
                    string.IsNullOrEmpty(model.IpAddress) ? "" : $"{model.IpAddress}:{model.Port}",
                    model.ComPort,
                    model.LastSeen.ToString("HH:mm:ss")
                );
            }
        }

        // ---- EVENT HANDLERS ----

        private void SetupEventHandlers()
        {
            _tcpServer.ClientConnected += (s, clientId) =>
                SafeInvoke(() => {
                    AddAlarm(0, AlarmLevel.Info, $"New TCP client: {clientId}");
                    UpdateStatus($"Client connected: {clientId}");
                });

            _tcpServer.DataReceived += (s, e) =>
                SafeInvoke(() => UpdateStatus($"Data from {e.ClientId}"));

            this.FormClosing += MainForm_FormClosing;
        }

        // ---- CROSS-THREAD UI UPDATE ----

        /// <summary>
        /// SafeInvoke: cập nhật UI từ thread khác một cách an toàn
        /// Thể hiện: InvokeRequired pattern - cực kỳ quan trọng trong WinForms multi-thread
        /// </summary>
        private void SafeInvoke(Action action)
        {
            if (this.InvokeRequired)
                this.BeginInvoke(action);  // Async invoke (không block)
            else
                action();
        }

        private void AppendLog(string message, LogLevel level)
        {
            SafeInvoke(() =>
            {
                if (rtbLogs.Lines.Length > 1000)
                    rtbLogs.Clear();

                rtbLogs.SelectionStart = rtbLogs.TextLength;
                rtbLogs.SelectionLength = 0;
                rtbLogs.SelectionColor = level == LogLevel.Error ? Color.Red :
                                         level == LogLevel.Warning ? Color.Yellow : Color.LimeGreen;
                rtbLogs.AppendText(message + Environment.NewLine);
                rtbLogs.ScrollToCaret();
            });
        }

        private void AddAlarm(int deviceId, AlarmLevel level, string message)
        {
            var item = new ListViewItem(DateTime.Now.ToString("HH:mm:ss"));
            item.SubItems.AddRange(new[] {
                $"Device {deviceId}", level.ToString(), message
            });
            item.BackColor = level == AlarmLevel.Critical ? Color.LightCoral :
                             level == AlarmLevel.Warning ? Color.LightYellow : Color.LightGreen;
            lvAlarms.Items.Insert(0, item);
        }

        private void RefreshStatusBar()
        {
            lblThreadCount.Text = $"Threads: {_threadManager.ActiveThreadCount}";
            lblConnections.Text = $"TCP Connections: {_tcpServer.ConnectedClients}";
        }

        private void UpdateStatus(string msg)
        {
            lblStatus.Text = msg;
        }

        // ---- ACTIONS ----

        private void ConnectAllDevices()
        {
            if (_devices.Count == 0)
            {
                MessageBox.Show("No devices added yet.\nGo to Devices → Add Device first.",
                    "No Devices", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Logger.Instance.Log("Connecting all devices...", LogLevel.Info);

            foreach (var model in _devices)
            {
                // Giả lập kết nối thành công
                model.Status = DeviceStatus.Online;
                model.LastSeen = DateTime.Now;

                // Cập nhật row trong grid
                foreach (DataGridViewRow row in dgvDevices.Rows)
                {
                    if (row.Cells["colId"].Value?.ToString() == model.Id.ToString())
                    {
                        row.Cells["colStatus"].Value = model.Status.ToString();
                        row.Cells["colLastSeen"].Value = model.LastSeen.ToString("HH:mm:ss");
                        break;
                    }
                }

                AddAlarm(model.Id, AlarmLevel.Info, $"Device '{model.Name}' connected.");
                Logger.Instance.Log($"Connected: {model.Name}", LogLevel.Info);
            }

            UpdateStatus($"All devices connected ({_devices.Count})");
        }

        private void DisconnectAllDevices()
        {
            Logger.Instance.Log("Disconnecting all devices...", LogLevel.Info);
            foreach (var model in _devices)
            {
                model.Status = DeviceStatus.Offline;
                foreach (DataGridViewRow row in dgvDevices.Rows)
                {
                    if (row.Cells["colId"].Value?.ToString() == model.Id.ToString())
                    {
                        row.Cells["colStatus"].Value = model.Status.ToString();
                        break;
                    }
                }
            }
            UpdateStatus("All devices disconnected.");
        }

        private void ShowAddDeviceDialog()
        {
            using (var dlg = new AddDeviceDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.DeviceModel != null)
                {
                    var model = dlg.DeviceModel;
                    model.Id = dgvDevices.Rows.Count + 1;

                    // Add vào danh sách nội bộ
                    _devices.Add(model);

                    // Add row vào DataGridView
                    dgvDevices.Rows.Add(
                        model.Id,
                        model.Name,
                        model.Type,
                        model.Status,
                        string.IsNullOrEmpty(model.IpAddress) ? "" : $"{model.IpAddress}:{model.Port}",
                        model.ComPort,
                        model.LastSeen.ToString("HH:mm:ss")
                    );

                    Logger.Instance.Log($"Device added: [{model.Id}] {model.Name} ({model.Type})", LogLevel.Info);
                    AddAlarm(model.Id, AlarmLevel.Info, $"Device '{model.Name}' registered.");
                    tabControl.SelectedTab = tabDevices; // tự chuyển sang tab Devices
                }
            }
        }

        // ---- CLEANUP ----

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            uiRefreshTimer.Stop();
            _tcpServer.Stop();
            _threadManager.Dispose();
            _workerPool.Dispose();
        }
    }


    // ============================================================
    // UI/AddDeviceDialog.cs
    // Thể hiện: Custom Dialog Form
    // ============================================================

    public class AddDeviceDialog : Form
    {
        private TextBox txtName, txtIp, txtPort, txtCom;
        private ComboBox cmbType;
        private Button btnOk, btnCancel;

        public DeviceModel DeviceModel { get; private set; }

        public AddDeviceDialog()
        {
            this.Text = "Add New Device";
            this.Size = new Size(350, 280);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;

            int y = 15, h = 25, gap = 35;
            Action<string, Control> AddRow = (label, ctrl) =>
            {
                this.Controls.Add(new Label { Text = label, Location = new Point(10, y + 2), AutoSize = true });
                ctrl.Location = new Point(120, y);
                ctrl.Width = 200;
                this.Controls.Add(ctrl);
                y += gap;
            };

            txtName = new TextBox();
            cmbType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cmbType.Items.AddRange(Enum.GetNames(typeof(DeviceType)));
            cmbType.SelectedIndex = 0;
            txtIp = new TextBox { Text = "192.168.1.1" };
            txtPort = new TextBox { Text = "502" };
            txtCom = new TextBox { Text = "COM1" };

            AddRow("Device Name:", txtName);
            AddRow("Device Type:", cmbType);
            AddRow("IP Address:", txtIp);
            AddRow("Port:", txtPort);
            AddRow("COM Port:", txtCom);

            btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(120, y), Size = new Size(80, 28) };
            btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(210, y), Size = new Size(80, 28) };

            btnOk.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtName.Text))
                {
                    MessageBox.Show("Please enter a device name.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                DeviceModel = new DeviceModel
                {
                    Name = txtName.Text,
                    Type = (DeviceType)Enum.Parse(typeof(DeviceType), cmbType.SelectedItem.ToString()),
                    IpAddress = txtIp.Text,
                    Port = int.TryParse(txtPort.Text, out int p) ? p : 502,
                    ComPort = txtCom.Text
                };
            };

            this.Controls.AddRange(new Control[] { btnOk, btnCancel });
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
        }
    }


    // ============================================================
    // Program.cs - Entry point
    // ============================================================
    // using System;
    // using System.Windows.Forms;
    // 
    // [STAThread]
    // static void Main()
    // {
    //     Application.EnableVisualStyles();
    //     Application.SetCompatibleTextRenderingDefault(false);
    //     Application.Run(new MainForm());
    // }
}
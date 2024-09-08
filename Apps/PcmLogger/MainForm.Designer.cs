namespace PcmHacking
{
    partial class MainForm : MainFormBase
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.loggerProgress = new System.Windows.Forms.ProgressBar();
            this.dashboardTab = new System.Windows.Forms.TabPage();
            this.startStopSaving = new System.Windows.Forms.Button();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.tabs = new System.Windows.Forms.TabControl();
            this.configurationTab = new System.Windows.Forms.TabPage();
            this.disclaimer = new System.Windows.Forms.Label();
            this.logFilePath = new System.Windows.Forms.Label();
            this.openDirectory = new System.Windows.Forms.Button();
            this.selectButton = new System.Windows.Forms.Button();
            this.setDirectory = new System.Windows.Forms.Button();
            this.deviceDescription = new System.Windows.Forms.Label();
            this.profilesTab = new System.Windows.Forms.TabPage();
            this.removeProfileButton = new System.Windows.Forms.Button();
            this.openButton = new System.Windows.Forms.Button();
            this.newButton = new System.Windows.Forms.Button();
            this.profileList = new System.Windows.Forms.ListBox();
            this.saveAsButton = new System.Windows.Forms.Button();
            this.saveButton = new System.Windows.Forms.Button();
            this.parametersTab = new System.Windows.Forms.TabPage();
            this.parameterSearch = new System.Windows.Forms.TextBox();
            this.parameterGrid = new System.Windows.Forms.DataGridView();
            this.enabledColumn = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.Zoom = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.nameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.unitsColumn = new System.Windows.Forms.DataGridViewComboBoxColumn();
            this.canTab = new System.Windows.Forms.TabPage();
            this.canParameterGrid = new System.Windows.Forms.DataGridView();
            this.canParameterNameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.canParameterUnitsColumn = new System.Windows.Forms.DataGridViewComboBoxColumn();
            this.disableCanLogging = new System.Windows.Forms.RadioButton();
            this.enableCanLogging = new System.Windows.Forms.RadioButton();
            this.canDeviceDescription = new System.Windows.Forms.Label();
            this.selectCanButton = new System.Windows.Forms.Button();
            this.debugTab = new System.Windows.Forms.TabPage();
            this.debugLog = new System.Windows.Forms.TextBox();
            this.splitContainer2 = new System.Windows.Forms.SplitContainer();
            this.logValues = new System.Windows.Forms.TextBox();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.tabs.SuspendLayout();
            this.configurationTab.SuspendLayout();
            this.profilesTab.SuspendLayout();
            this.parametersTab.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.parameterGrid)).BeginInit();
            this.canTab.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.canParameterGrid)).BeginInit();
            this.debugTab.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer2)).BeginInit();
            this.splitContainer2.Panel1.SuspendLayout();
            this.splitContainer2.SuspendLayout();
            this.SuspendLayout();
            // 
            // loggerProgress
            // 
            this.loggerProgress.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.loggerProgress.Enabled = false;
            this.loggerProgress.Location = new System.Drawing.Point(244, 11);
            this.loggerProgress.MarqueeAnimationSpeed = 0;
            this.loggerProgress.Name = "loggerProgress";
            this.loggerProgress.Size = new System.Drawing.Size(706, 47);
            this.loggerProgress.Step = 0;
            this.loggerProgress.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.loggerProgress.TabIndex = 5;
            this.loggerProgress.Visible = false;
            // 
            // dashboardTab
            // 
            this.dashboardTab.Location = new System.Drawing.Point(4, 22);
            this.dashboardTab.Name = "dashboardTab";
            this.dashboardTab.Padding = new System.Windows.Forms.Padding(3);
            this.dashboardTab.Size = new System.Drawing.Size(743, 427);
            this.dashboardTab.TabIndex = 0;
            this.dashboardTab.Text = "Dashboard";
            this.dashboardTab.UseVisualStyleBackColor = true;
            // 
            // startStopSaving
            // 
            this.startStopSaving.Enabled = false;
            this.startStopSaving.Location = new System.Drawing.Point(12, 11);
            this.startStopSaving.Name = "startStopSaving";
            this.startStopSaving.Size = new System.Drawing.Size(215, 47);
            this.startStopSaving.TabIndex = 4;
            this.startStopSaving.Text = "Start &Recording";
            this.startStopSaving.UseVisualStyleBackColor = true;
            this.startStopSaving.Click += new System.EventHandler(this.startStopSaving_Click);
            // 
            // splitContainer1
            // 
            this.splitContainer1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.splitContainer1.Location = new System.Drawing.Point(0, 65);
            this.splitContainer1.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.tabs);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.splitContainer2);
            this.splitContainer1.Size = new System.Drawing.Size(960, 548);
            this.splitContainer1.SplitterDistance = 400;
            this.splitContainer1.SplitterWidth = 3;
            this.splitContainer1.TabIndex = 9;
            // 
            // tabs
            // 
            this.tabs.Controls.Add(this.configurationTab);
            this.tabs.Controls.Add(this.profilesTab);
            this.tabs.Controls.Add(this.parametersTab);
            this.tabs.Controls.Add(this.canTab);
            this.tabs.Controls.Add(this.debugTab);
            this.tabs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabs.Location = new System.Drawing.Point(0, 0);
            this.tabs.Name = "tabs";
            this.tabs.SelectedIndex = 0;
            this.tabs.Size = new System.Drawing.Size(400, 548);
            this.tabs.TabIndex = 8;
            // 
            // configurationTab
            // 
            this.configurationTab.Controls.Add(this.disclaimer);
            this.configurationTab.Controls.Add(this.logFilePath);
            this.configurationTab.Controls.Add(this.openDirectory);
            this.configurationTab.Controls.Add(this.selectButton);
            this.configurationTab.Controls.Add(this.setDirectory);
            this.configurationTab.Controls.Add(this.deviceDescription);
            this.configurationTab.Location = new System.Drawing.Point(4, 22);
            this.configurationTab.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.configurationTab.Name = "configurationTab";
            this.configurationTab.Size = new System.Drawing.Size(392, 522);
            this.configurationTab.TabIndex = 3;
            this.configurationTab.Text = "Configuration";
            this.configurationTab.UseVisualStyleBackColor = true;
            // 
            // disclaimer
            // 
            this.disclaimer.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.disclaimer.Location = new System.Drawing.Point(4, 145);
            this.disclaimer.Name = "disclaimer";
            this.disclaimer.Size = new System.Drawing.Size(331, 140);
            this.disclaimer.TabIndex = 10;
            this.disclaimer.Text = resources.GetString("disclaimer.Text");
            // 
            // logFilePath
            // 
            this.logFilePath.AutoSize = true;
            this.logFilePath.BackColor = System.Drawing.Color.Transparent;
            this.logFilePath.Location = new System.Drawing.Point(115, 69);
            this.logFilePath.Name = "logFilePath";
            this.logFilePath.Size = new System.Drawing.Size(49, 13);
            this.logFilePath.TabIndex = 7;
            this.logFilePath.Text = "Directory";
            // 
            // openDirectory
            // 
            this.openDirectory.Location = new System.Drawing.Point(4, 93);
            this.openDirectory.Name = "openDirectory";
            this.openDirectory.Size = new System.Drawing.Size(104, 23);
            this.openDirectory.TabIndex = 9;
            this.openDirectory.Text = "&Open Log Folder";
            this.openDirectory.UseVisualStyleBackColor = true;
            this.openDirectory.Click += new System.EventHandler(this.openDirectory_Click);
            // 
            // selectButton
            // 
            this.selectButton.Location = new System.Drawing.Point(3, 3);
            this.selectButton.Name = "selectButton";
            this.selectButton.Size = new System.Drawing.Size(216, 25);
            this.selectButton.TabIndex = 0;
            this.selectButton.Text = "&Select OBD2 Device";
            this.selectButton.UseVisualStyleBackColor = true;
            this.selectButton.Click += new System.EventHandler(this.selectButton_Click);
            // 
            // setDirectory
            // 
            this.setDirectory.Location = new System.Drawing.Point(4, 64);
            this.setDirectory.Name = "setDirectory";
            this.setDirectory.Size = new System.Drawing.Size(105, 23);
            this.setDirectory.TabIndex = 6;
            this.setDirectory.Text = "Set Log &Folder";
            this.setDirectory.UseVisualStyleBackColor = true;
            this.setDirectory.Click += new System.EventHandler(this.setDirectory_Click);
            // 
            // deviceDescription
            // 
            this.deviceDescription.AutoSize = true;
            this.deviceDescription.Location = new System.Drawing.Point(225, 9);
            this.deviceDescription.Name = "deviceDescription";
            this.deviceDescription.Size = new System.Drawing.Size(88, 13);
            this.deviceDescription.TabIndex = 1;
            this.deviceDescription.Text = "[selected device]";
            // 
            // profilesTab
            // 
            this.profilesTab.Controls.Add(this.removeProfileButton);
            this.profilesTab.Controls.Add(this.openButton);
            this.profilesTab.Controls.Add(this.newButton);
            this.profilesTab.Controls.Add(this.profileList);
            this.profilesTab.Controls.Add(this.saveAsButton);
            this.profilesTab.Controls.Add(this.saveButton);
            this.profilesTab.Location = new System.Drawing.Point(4, 22);
            this.profilesTab.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.profilesTab.Name = "profilesTab";
            this.profilesTab.Size = new System.Drawing.Size(459, 522);
            this.profilesTab.TabIndex = 4;
            this.profilesTab.Text = "Profiles";
            this.profilesTab.UseVisualStyleBackColor = true;
            // 
            // removeProfileButton
            // 
            this.removeProfileButton.Enabled = false;
            this.removeProfileButton.Location = new System.Drawing.Point(666, 3);
            this.removeProfileButton.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.removeProfileButton.Name = "removeProfileButton";
            this.removeProfileButton.Size = new System.Drawing.Size(75, 25);
            this.removeProfileButton.TabIndex = 5;
            this.removeProfileButton.Text = "&Remove";
            this.removeProfileButton.UseVisualStyleBackColor = true;
            this.removeProfileButton.Click += new System.EventHandler(this.removeProfileButton_Click);
            // 
            // openButton
            // 
            this.openButton.Location = new System.Drawing.Point(82, 2);
            this.openButton.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.openButton.Name = "openButton";
            this.openButton.Size = new System.Drawing.Size(75, 25);
            this.openButton.TabIndex = 4;
            this.openButton.Text = "&Open";
            this.openButton.UseVisualStyleBackColor = true;
            this.openButton.Click += new System.EventHandler(this.openButton_Click);
            // 
            // newButton
            // 
            this.newButton.Location = new System.Drawing.Point(2, 2);
            this.newButton.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.newButton.Name = "newButton";
            this.newButton.Size = new System.Drawing.Size(75, 25);
            this.newButton.TabIndex = 3;
            this.newButton.Text = "&New";
            this.newButton.UseVisualStyleBackColor = true;
            this.newButton.Click += new System.EventHandler(this.newButton_Click);
            // 
            // profileList
            // 
            this.profileList.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.profileList.FormattingEnabled = true;
            this.profileList.IntegralHeight = false;
            this.profileList.Location = new System.Drawing.Point(2, 32);
            this.profileList.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.profileList.Name = "profileList";
            this.profileList.Size = new System.Drawing.Size(517, 491);
            this.profileList.TabIndex = 2;
            this.profileList.SelectedIndexChanged += new System.EventHandler(this.profileList_SelectedIndexChanged);
            // 
            // saveAsButton
            // 
            this.saveAsButton.Location = new System.Drawing.Point(241, 2);
            this.saveAsButton.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.saveAsButton.Name = "saveAsButton";
            this.saveAsButton.Size = new System.Drawing.Size(75, 25);
            this.saveAsButton.TabIndex = 1;
            this.saveAsButton.Text = "Save &As";
            this.saveAsButton.UseVisualStyleBackColor = true;
            this.saveAsButton.Click += new System.EventHandler(this.saveAsButton_Click);
            // 
            // saveButton
            // 
            this.saveButton.Location = new System.Drawing.Point(161, 2);
            this.saveButton.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(75, 25);
            this.saveButton.TabIndex = 0;
            this.saveButton.Text = "&Save";
            this.saveButton.UseVisualStyleBackColor = true;
            this.saveButton.Click += new System.EventHandler(this.saveButton_Click);
            // 
            // parametersTab
            // 
            this.parametersTab.Controls.Add(this.parameterSearch);
            this.parametersTab.Controls.Add(this.parameterGrid);
            this.parametersTab.Location = new System.Drawing.Point(4, 22);
            this.parametersTab.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.parametersTab.Name = "parametersTab";
            this.parametersTab.Size = new System.Drawing.Size(459, 522);
            this.parametersTab.TabIndex = 2;
            this.parametersTab.Text = "Parameters";
            this.parametersTab.UseVisualStyleBackColor = true;
            // 
            // parameterSearch
            // 
            this.parameterSearch.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.parameterSearch.Location = new System.Drawing.Point(3, 3);
            this.parameterSearch.Name = "parameterSearch";
            this.parameterSearch.Size = new System.Drawing.Size(456, 20);
            this.parameterSearch.TabIndex = 1;
            this.parameterSearch.TextChanged += new System.EventHandler(this.parameterSearch_TextChanged);
            this.parameterSearch.Enter += new System.EventHandler(this.parameterSearch_Enter);
            this.parameterSearch.Leave += new System.EventHandler(this.parameterSearch_Leave);
            // 
            // parameterGrid
            // 
            this.parameterGrid.AllowUserToAddRows = false;
            this.parameterGrid.AllowUserToDeleteRows = false;
            this.parameterGrid.AllowUserToResizeRows = false;
            this.parameterGrid.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.parameterGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.parameterGrid.CausesValidation = false;
            this.parameterGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.parameterGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.enabledColumn,
            this.Zoom,
            this.nameColumn,
            this.unitsColumn});
            this.parameterGrid.Location = new System.Drawing.Point(0, 28);
            this.parameterGrid.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.parameterGrid.Name = "parameterGrid";
            this.parameterGrid.RowHeadersVisible = false;
            this.parameterGrid.RowHeadersWidth = 51;
            this.parameterGrid.RowTemplate.Height = 24;
            this.parameterGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.parameterGrid.ShowCellErrors = false;
            this.parameterGrid.ShowEditingIcon = false;
            this.parameterGrid.ShowRowErrors = false;
            this.parameterGrid.Size = new System.Drawing.Size(458, 499);
            this.parameterGrid.TabIndex = 0;
            this.parameterGrid.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.parameterGrid_CellContentClick);
            this.parameterGrid.CellValueChanged += new System.Windows.Forms.DataGridViewCellEventHandler(this.parameterGrid_CellValueChanged);
            this.parameterGrid.CurrentCellDirtyStateChanged += new System.EventHandler(this.parameterGrid_CurrentCellDirtyStateChanged);
            // 
            // enabledColumn
            // 
            this.enabledColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.ColumnHeader;
            this.enabledColumn.FillWeight = 10F;
            this.enabledColumn.HeaderText = "Enable";
            this.enabledColumn.MinimumWidth = 35;
            this.enabledColumn.Name = "enabledColumn";
            this.enabledColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            this.enabledColumn.Width = 65;
            // 
            // Zoom
            // 
            this.Zoom.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.ColumnHeader;
            this.Zoom.FillWeight = 10F;
            this.Zoom.HeaderText = "Zoom";
            this.Zoom.MinimumWidth = 35;
            this.Zoom.Name = "Zoom";
            this.Zoom.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.Zoom.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            this.Zoom.Width = 59;
            // 
            // nameColumn
            // 
            this.nameColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.nameColumn.FillWeight = 50F;
            this.nameColumn.HeaderText = "Name";
            this.nameColumn.MinimumWidth = 100;
            this.nameColumn.Name = "nameColumn";
            this.nameColumn.ReadOnly = true;
            // 
            // unitsColumn
            // 
            this.unitsColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.unitsColumn.FillWeight = 20F;
            this.unitsColumn.HeaderText = "Units";
            this.unitsColumn.MinimumWidth = 80;
            this.unitsColumn.Name = "unitsColumn";
            this.unitsColumn.Width = 80;
            // 
            // canTab
            // 
            this.canTab.Controls.Add(this.canParameterGrid);
            this.canTab.Controls.Add(this.disableCanLogging);
            this.canTab.Controls.Add(this.enableCanLogging);
            this.canTab.Controls.Add(this.canDeviceDescription);
            this.canTab.Controls.Add(this.selectCanButton);
            this.canTab.Location = new System.Drawing.Point(4, 22);
            this.canTab.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.canTab.Name = "canTab";
            this.canTab.Size = new System.Drawing.Size(459, 522);
            this.canTab.TabIndex = 5;
            this.canTab.Text = "CAN Bus";
            this.canTab.UseVisualStyleBackColor = true;
            // 
            // canParameterGrid
            // 
            this.canParameterGrid.AllowUserToAddRows = false;
            this.canParameterGrid.AllowUserToDeleteRows = false;
            this.canParameterGrid.AllowUserToResizeRows = false;
            this.canParameterGrid.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.canParameterGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.canParameterGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.canParameterNameColumn,
            this.canParameterUnitsColumn});
            this.canParameterGrid.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnEnter;
            this.canParameterGrid.Location = new System.Drawing.Point(2, 51);
            this.canParameterGrid.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.canParameterGrid.MultiSelect = false;
            this.canParameterGrid.Name = "canParameterGrid";
            this.canParameterGrid.RowHeadersVisible = false;
            this.canParameterGrid.RowHeadersWidth = 51;
            this.canParameterGrid.RowTemplate.Height = 24;
            this.canParameterGrid.Size = new System.Drawing.Size(518, 473);
            this.canParameterGrid.TabIndex = 17;
            this.canParameterGrid.CellValueChanged += new System.Windows.Forms.DataGridViewCellEventHandler(this.canParameterGrid_CellValueChanged);
            this.canParameterGrid.CurrentCellDirtyStateChanged += new System.EventHandler(this.canParameterGrid_CurrentCellDirtyStateChanged);
            this.canParameterGrid.DataError += new System.Windows.Forms.DataGridViewDataErrorEventHandler(this.canParameterGrid_DataError);
            // 
            // canParameterNameColumn
            // 
            this.canParameterNameColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.canParameterNameColumn.HeaderText = "Parameter Name";
            this.canParameterNameColumn.MinimumWidth = 200;
            this.canParameterNameColumn.Name = "canParameterNameColumn";
            this.canParameterNameColumn.ReadOnly = true;
            this.canParameterNameColumn.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            // 
            // canParameterUnitsColumn
            // 
            this.canParameterUnitsColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.canParameterUnitsColumn.FillWeight = 15F;
            this.canParameterUnitsColumn.HeaderText = "Units";
            this.canParameterUnitsColumn.MinimumWidth = 125;
            this.canParameterUnitsColumn.Name = "canParameterUnitsColumn";
            // 
            // disableCanLogging
            // 
            this.disableCanLogging.AutoSize = true;
            this.disableCanLogging.Checked = true;
            this.disableCanLogging.Location = new System.Drawing.Point(3, 24);
            this.disableCanLogging.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.disableCanLogging.Name = "disableCanLogging";
            this.disableCanLogging.Size = new System.Drawing.Size(126, 17);
            this.disableCanLogging.TabIndex = 16;
            this.disableCanLogging.TabStop = true;
            this.disableCanLogging.Text = "&Disable CAN Logging";
            this.disableCanLogging.UseVisualStyleBackColor = true;
            this.disableCanLogging.CheckedChanged += new System.EventHandler(this.disableCanLogging_CheckedChanged);
            // 
            // enableCanLogging
            // 
            this.enableCanLogging.AutoSize = true;
            this.enableCanLogging.Location = new System.Drawing.Point(3, 3);
            this.enableCanLogging.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.enableCanLogging.Name = "enableCanLogging";
            this.enableCanLogging.Size = new System.Drawing.Size(124, 17);
            this.enableCanLogging.TabIndex = 15;
            this.enableCanLogging.Text = "&Enable CAN Logging";
            this.enableCanLogging.UseVisualStyleBackColor = true;
            this.enableCanLogging.CheckedChanged += new System.EventHandler(this.enableCanLogging_CheckedChanged);
            // 
            // canDeviceDescription
            // 
            this.canDeviceDescription.AutoSize = true;
            this.canDeviceDescription.Enabled = false;
            this.canDeviceDescription.Location = new System.Drawing.Point(280, 26);
            this.canDeviceDescription.Name = "canDeviceDescription";
            this.canDeviceDescription.Size = new System.Drawing.Size(88, 13);
            this.canDeviceDescription.TabIndex = 14;
            this.canDeviceDescription.Text = "[selected device]";
            // 
            // selectCanButton
            // 
            this.selectCanButton.Enabled = false;
            this.selectCanButton.Location = new System.Drawing.Point(149, 20);
            this.selectCanButton.Name = "selectCanButton";
            this.selectCanButton.Size = new System.Drawing.Size(124, 25);
            this.selectCanButton.TabIndex = 13;
            this.selectCanButton.Text = "Select &CAN Device";
            this.selectCanButton.UseVisualStyleBackColor = true;
            this.selectCanButton.Click += new System.EventHandler(this.selectCanButton_Click);
            // 
            // debugTab
            // 
            this.debugTab.Controls.Add(this.debugLog);
            this.debugTab.Location = new System.Drawing.Point(4, 22);
            this.debugTab.Name = "debugTab";
            this.debugTab.Padding = new System.Windows.Forms.Padding(3, 3, 3, 3);
            this.debugTab.Size = new System.Drawing.Size(459, 522);
            this.debugTab.TabIndex = 1;
            this.debugTab.Text = "Debug";
            this.debugTab.UseVisualStyleBackColor = true;
            // 
            // debugLog
            // 
            this.debugLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.debugLog.Location = new System.Drawing.Point(3, 3);
            this.debugLog.Multiline = true;
            this.debugLog.Name = "debugLog";
            this.debugLog.ReadOnly = true;
            this.debugLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.debugLog.Size = new System.Drawing.Size(453, 516);
            this.debugLog.TabIndex = 0;
            // 
            // splitContainer2
            // 
            this.splitContainer2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer2.Location = new System.Drawing.Point(0, 0);
            this.splitContainer2.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.splitContainer2.Name = "splitContainer2";
            // 
            // splitContainer2.Panel1
            // 
            this.splitContainer2.Panel1.Controls.Add(this.logValues);
            this.splitContainer2.Panel1MinSize = 200;
            this.splitContainer2.Panel2MinSize = 200;
            this.splitContainer2.Size = new System.Drawing.Size(557, 548);
            this.splitContainer2.SplitterDistance = 252;
            this.splitContainer2.SplitterWidth = 3;
            this.splitContainer2.TabIndex = 1;
            // 
            // logValues
            // 
            this.logValues.Dock = System.Windows.Forms.DockStyle.Fill;
            this.logValues.Location = new System.Drawing.Point(0, 0);
            this.logValues.Multiline = true;
            this.logValues.Name = "logValues";
            this.logValues.ReadOnly = true;
            this.logValues.Size = new System.Drawing.Size(252, 548);
            this.logValues.TabIndex = 0;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(962, 612);
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.loggerProgress);
            this.Controls.Add(this.startStopSaving);
            this.Name = "MainForm";
            this.Text = "(window title is set programmatically)";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.tabs.ResumeLayout(false);
            this.configurationTab.ResumeLayout(false);
            this.configurationTab.PerformLayout();
            this.profilesTab.ResumeLayout(false);
            this.parametersTab.ResumeLayout(false);
            this.parametersTab.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.parameterGrid)).EndInit();
            this.canTab.ResumeLayout(false);
            this.canTab.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.canParameterGrid)).EndInit();
            this.debugTab.ResumeLayout(false);
            this.debugTab.PerformLayout();
            this.splitContainer2.Panel1.ResumeLayout(false);
            this.splitContainer2.Panel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer2)).EndInit();
            this.splitContainer2.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button startStopSaving;
        private System.Windows.Forms.TabPage dashboardTab;
        private System.Windows.Forms.ProgressBar loggerProgress;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.TabPage debugTab;
        private System.Windows.Forms.TextBox debugLog;
        private System.Windows.Forms.TabPage canTab;
        private System.Windows.Forms.DataGridView canParameterGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn canParameterNameColumn;
        private System.Windows.Forms.DataGridViewComboBoxColumn canParameterUnitsColumn;
        private System.Windows.Forms.RadioButton disableCanLogging;
        private System.Windows.Forms.RadioButton enableCanLogging;
        private System.Windows.Forms.Label canDeviceDescription;
        private System.Windows.Forms.Button selectCanButton;
        private System.Windows.Forms.TabPage parametersTab;
        private System.Windows.Forms.TextBox parameterSearch;
        private System.Windows.Forms.DataGridView parameterGrid;
        private System.Windows.Forms.TextBox logValues;
        private System.Windows.Forms.TabPage profilesTab;
        private System.Windows.Forms.Button removeProfileButton;
        private System.Windows.Forms.Button openButton;
        private System.Windows.Forms.Button newButton;
        private System.Windows.Forms.ListBox profileList;
        private System.Windows.Forms.Button saveAsButton;
        private System.Windows.Forms.Button saveButton;
        private System.Windows.Forms.TabPage configurationTab;
        private System.Windows.Forms.Label disclaimer;
        private System.Windows.Forms.Label logFilePath;
        private System.Windows.Forms.Button openDirectory;
        private System.Windows.Forms.Button selectButton;
        private System.Windows.Forms.Button setDirectory;
        private System.Windows.Forms.Label deviceDescription;
        private System.Windows.Forms.TabControl tabs;
        private System.Windows.Forms.SplitContainer splitContainer2;
        private System.Windows.Forms.DataGridViewCheckBoxColumn enabledColumn;
        private System.Windows.Forms.DataGridViewCheckBoxColumn Zoom;
        private System.Windows.Forms.DataGridViewTextBoxColumn nameColumn;
        private System.Windows.Forms.DataGridViewComboBoxColumn unitsColumn;
    }
}


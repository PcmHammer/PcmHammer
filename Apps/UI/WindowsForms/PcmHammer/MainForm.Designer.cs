namespace PcmHacking
{
    partial class MainForm
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
            this.interfaceBox = new System.Windows.Forms.GroupBox();
            this.reinitializeButton = new System.Windows.Forms.Button();
            this.selectButton = new System.Windows.Forms.Button();
            this.deviceDescription = new System.Windows.Forms.Label();
            this.operationsBox = new System.Windows.Forms.GroupBox();
            this.verifyPcmButton = new System.Windows.Forms.Button();
            this.readPcmButton = new System.Windows.Forms.Button();
            this.exitKernelButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            this.writeCalibrationButton = new System.Windows.Forms.Button();
            this.identifyPcmButton = new System.Windows.Forms.Button();
            this.fileBox = new System.Windows.Forms.GroupBox();
            this.loadedFileLabel = new System.Windows.Forms.Label();
            this.loadFileButton = new System.Windows.Forms.Button();
            this.saveFileButton = new System.Windows.Forms.Button();
            this.exportBinButton = new System.Windows.Forms.Button();
            this.tabs = new System.Windows.Forms.TabControl();
            this.resultsTab = new System.Windows.Forms.TabPage();
            this.userLog = new PcmHacking.LogListView();
            this.helpTab = new System.Windows.Forms.TabPage();
            this.helpWebBrowser = new System.Windows.Forms.WebBrowser();
            this.creditsTab = new System.Windows.Forms.TabPage();
            this.creditsWebBrowser = new System.Windows.Forms.WebBrowser();
            this.debugTab = new System.Windows.Forms.TabPage();
            this.debugLog = new PcmHacking.LogListView();
            this.menuStripMain = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.loadFileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.saveFileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.saveFileAsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.importBinToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exportBinToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.fileSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.fileSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.saveToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.saveResultsLogToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.saveDebugLogToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.saveBusMonitorLogToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exitApplicationToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemTools = new System.Windows.Forms.ToolStripMenuItem();
            this.readEntirePCMToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.verifyEntirePCMToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.modifyVINToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.writeParmetersCloneToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.writeOSCalibrationBootToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.writeFullToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.testFileChecksumsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            this.bruteForceUnlockToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.busMonitorToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.haltRunningKernelToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.pcmRecoveryToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.recoveryReadToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.recoveryWriteToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemOptions = new System.Windows.Forms.ToolStripMenuItem();
            this.userDefinedKeyToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.settingsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.statusStatusStrip = new System.Windows.Forms.StatusStrip();
            this.activityToolStripStatusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.retryCountToolStripStatusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.kbpsToolStripStatusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.progressBarToolStripProgressBar = new System.Windows.Forms.ToolStripProgressBar();
            this.percentDoneToolStripStatusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.timeRemainingToolStripStatusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.interfaceBox.SuspendLayout();
            this.operationsBox.SuspendLayout();
            this.fileBox.SuspendLayout();
            this.tabs.SuspendLayout();
            this.resultsTab.SuspendLayout();
            this.helpTab.SuspendLayout();
            this.creditsTab.SuspendLayout();
            this.debugTab.SuspendLayout();
            this.menuStripMain.SuspendLayout();
            this.statusStatusStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // interfaceBox
            // 
            this.interfaceBox.Controls.Add(this.reinitializeButton);
            this.interfaceBox.Controls.Add(this.selectButton);
            this.interfaceBox.Controls.Add(this.deviceDescription);
            this.interfaceBox.Location = new System.Drawing.Point(9, 26);
            this.interfaceBox.Margin = new System.Windows.Forms.Padding(2);
            this.interfaceBox.Name = "interfaceBox";
            this.interfaceBox.Padding = new System.Windows.Forms.Padding(2);
            this.interfaceBox.Size = new System.Drawing.Size(190, 93);
            this.interfaceBox.TabIndex = 0;
            this.interfaceBox.TabStop = false;
            this.interfaceBox.Text = "Device";
            // 
            // reinitializeButton
            // 
            this.reinitializeButton.Location = new System.Drawing.Point(4, 62);
            this.reinitializeButton.Margin = new System.Windows.Forms.Padding(2);
            this.reinitializeButton.Name = "reinitializeButton";
            this.reinitializeButton.Size = new System.Drawing.Size(182, 25);
            this.reinitializeButton.TabIndex = 2;
            this.reinitializeButton.Text = "Re-&Initialize Device";
            this.reinitializeButton.UseVisualStyleBackColor = true;
            this.reinitializeButton.Click += new System.EventHandler(this.reinitializeButton_Click);
            // 
            // selectButton
            // 
            this.selectButton.Location = new System.Drawing.Point(4, 32);
            this.selectButton.Name = "selectButton";
            this.selectButton.Size = new System.Drawing.Size(182, 25);
            this.selectButton.TabIndex = 1;
            this.selectButton.Text = "&Select Device";
            this.selectButton.UseVisualStyleBackColor = true;
            this.selectButton.Click += new System.EventHandler(this.selectButton_Click);
            //
            // deviceDescription
            //
            this.deviceDescription.AutoSize = false;
            this.deviceDescription.AutoEllipsis = true;
            this.deviceDescription.Location = new System.Drawing.Point(4, 16);
            this.deviceDescription.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.deviceDescription.Name = "deviceDescription";
            this.deviceDescription.Size = new System.Drawing.Size(180, 13);
            this.deviceDescription.TabIndex = 0;
            this.deviceDescription.Text = "Device name will be displayed here";
            // 
            // operationsBox
            // 
            this.operationsBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.operationsBox.Controls.Add(this.verifyPcmButton);
            this.operationsBox.Controls.Add(this.readPcmButton);
            this.operationsBox.Controls.Add(this.exitKernelButton);
            this.operationsBox.Controls.Add(this.cancelButton);
            this.operationsBox.Controls.Add(this.writeCalibrationButton);
            this.operationsBox.Controls.Add(this.identifyPcmButton);
            this.operationsBox.Location = new System.Drawing.Point(9, 290);
            this.operationsBox.Margin = new System.Windows.Forms.Padding(2);
            this.operationsBox.Name = "operationsBox";
            this.operationsBox.Padding = new System.Windows.Forms.Padding(2);
            this.operationsBox.Size = new System.Drawing.Size(190, 326);
            this.operationsBox.TabIndex = 1;
            this.operationsBox.TabStop = false;
            this.operationsBox.Text = "Operations";
            // 
            // verifyPcmButton
            // 
            this.verifyPcmButton.Location = new System.Drawing.Point(4, 105);
            this.verifyPcmButton.Margin = new System.Windows.Forms.Padding(2);
            this.verifyPcmButton.Name = "verifyPcmButton";
            this.verifyPcmButton.Size = new System.Drawing.Size(182, 25);
            this.verifyPcmButton.TabIndex = 6;
            this.verifyPcmButton.Text = "&Verify PCM";
            this.verifyPcmButton.UseVisualStyleBackColor = true;
            this.verifyPcmButton.Click += new System.EventHandler(this.quickComparisonButton_Click);
            // 
            // readPcmButton
            // 
            this.readPcmButton.Location = new System.Drawing.Point(4, 47);
            this.readPcmButton.Margin = new System.Windows.Forms.Padding(2);
            this.readPcmButton.Name = "readPcmButton";
            this.readPcmButton.Size = new System.Drawing.Size(182, 25);
            this.readPcmButton.TabIndex = 4;
            this.readPcmButton.Text = "Read P&CM";
            this.readPcmButton.UseVisualStyleBackColor = true;
            this.readPcmButton.Click += new System.EventHandler(this.readFullContentsButton_Click);
            //
            // exitKernelButton
            // 
            this.exitKernelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.exitKernelButton.Location = new System.Drawing.Point(4, 259);
            this.exitKernelButton.Margin = new System.Windows.Forms.Padding(2);
            this.exitKernelButton.Name = "exitKernelButton";
            this.exitKernelButton.Size = new System.Drawing.Size(182, 25);
            this.exitKernelButton.TabIndex = 9;
            this.exitKernelButton.Text = "&Halt Running Kernel";
            this.exitKernelButton.UseVisualStyleBackColor = true;
            this.exitKernelButton.Click += new System.EventHandler(this.testKernelButton_Click);
            // 
            // cancelButton
            // 
            this.cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.cancelButton.Location = new System.Drawing.Point(4, 293);
            this.cancelButton.Margin = new System.Windows.Forms.Padding(2);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(182, 25);
            this.cancelButton.TabIndex = 10;
            this.cancelButton.Text = "&Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            this.cancelButton.Click += new System.EventHandler(this.CancelButton_Click);
            // 
            // writeCalibrationButton
            // 
            this.writeCalibrationButton.Location = new System.Drawing.Point(4, 76);
            this.writeCalibrationButton.Margin = new System.Windows.Forms.Padding(2);
            this.writeCalibrationButton.Name = "writeCalibrationButton";
            this.writeCalibrationButton.Size = new System.Drawing.Size(182, 25);
            this.writeCalibrationButton.TabIndex = 5;
            this.writeCalibrationButton.Text = "&Write PCM";
            this.writeCalibrationButton.UseVisualStyleBackColor = true;
            this.writeCalibrationButton.Click += new System.EventHandler(this.writeCalibrationButton_Click);
            // 
            // identifyPcmButton
            // 
            this.identifyPcmButton.Location = new System.Drawing.Point(4, 18);
            this.identifyPcmButton.Margin = new System.Windows.Forms.Padding(2);
            this.identifyPcmButton.Name = "identifyPcmButton";
            this.identifyPcmButton.Size = new System.Drawing.Size(182, 25);
            this.identifyPcmButton.TabIndex = 0;
            this.identifyPcmButton.Text = "&Identify PCM";
            this.identifyPcmButton.UseVisualStyleBackColor = true;
            this.identifyPcmButton.Click += new System.EventHandler(this.identifyPcmButton_Click);
            //
            // fileBox
            //
            this.fileBox.Controls.Add(this.loadedFileLabel);
            this.fileBox.Controls.Add(this.loadFileButton);
            this.fileBox.Controls.Add(this.saveFileButton);
            this.fileBox.Controls.Add(this.exportBinButton);
            this.fileBox.Location = new System.Drawing.Point(9, 124);
            this.fileBox.Margin = new System.Windows.Forms.Padding(2);
            this.fileBox.Name = "fileBox";
            this.fileBox.Padding = new System.Windows.Forms.Padding(2);
            this.fileBox.Size = new System.Drawing.Size(190, 133);
            this.fileBox.TabIndex = 1;
            this.fileBox.TabStop = false;
            this.fileBox.Text = "File";
            //
            // loadedFileLabel
            //
            this.loadedFileLabel.AutoSize = false;
            this.loadedFileLabel.AutoEllipsis = true;
            this.loadedFileLabel.Location = new System.Drawing.Point(4, 16);
            this.loadedFileLabel.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.loadedFileLabel.Name = "loadedFileLabel";
            this.loadedFileLabel.Size = new System.Drawing.Size(182, 28);
            this.loadedFileLabel.TabIndex = 0;
            this.loadedFileLabel.Text = "No file loaded";
            //
            // loadFileButton
            //
            this.loadFileButton.Location = new System.Drawing.Point(4, 47);
            this.loadFileButton.Margin = new System.Windows.Forms.Padding(2);
            this.loadFileButton.Name = "loadFileButton";
            this.loadFileButton.Size = new System.Drawing.Size(182, 25);
            this.loadFileButton.TabIndex = 1;
            this.loadFileButton.Text = "&Load File...";
            this.loadFileButton.UseVisualStyleBackColor = true;
            this.loadFileButton.Click += new System.EventHandler(this.loadFileButton_Click);
            //
            // saveFileButton
            //
            this.saveFileButton.Location = new System.Drawing.Point(4, 74);
            this.saveFileButton.Margin = new System.Windows.Forms.Padding(2);
            this.saveFileButton.Name = "saveFileButton";
            this.saveFileButton.Size = new System.Drawing.Size(182, 25);
            this.saveFileButton.TabIndex = 2;
            this.saveFileButton.Text = "&Save File...";
            this.saveFileButton.UseVisualStyleBackColor = true;
            this.saveFileButton.Click += new System.EventHandler(this.saveFileButton_Click);
            //
            // exportBinButton
            //
            this.exportBinButton.Location = new System.Drawing.Point(4, 101);
            this.exportBinButton.Margin = new System.Windows.Forms.Padding(2);
            this.exportBinButton.Name = "exportBinButton";
            this.exportBinButton.Size = new System.Drawing.Size(182, 25);
            this.exportBinButton.TabIndex = 3;
            this.exportBinButton.Text = "&Export Bin...";
            this.exportBinButton.UseVisualStyleBackColor = true;
            this.exportBinButton.Click += new System.EventHandler(this.exportBinButton_Click);
            //
            // tabs
            //
            this.tabs.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tabs.Controls.Add(this.resultsTab);
            this.tabs.Controls.Add(this.helpTab);
            this.tabs.Controls.Add(this.creditsTab);
            this.tabs.Controls.Add(this.debugTab);
            this.tabs.Location = new System.Drawing.Point(204, 26);
            this.tabs.Margin = new System.Windows.Forms.Padding(2);
            this.tabs.Name = "tabs";
            this.tabs.SelectedIndex = 0;
            this.tabs.Size = new System.Drawing.Size(727, 590);
            this.tabs.TabIndex = 2;
            // 
            // resultsTab
            // 
            this.resultsTab.Controls.Add(this.userLog);
            this.resultsTab.Location = new System.Drawing.Point(4, 22);
            this.resultsTab.Margin = new System.Windows.Forms.Padding(2);
            this.resultsTab.Name = "resultsTab";
            this.resultsTab.Padding = new System.Windows.Forms.Padding(2);
            this.resultsTab.Size = new System.Drawing.Size(600, 429);
            this.resultsTab.TabIndex = 0;
            this.resultsTab.Text = "Results";
            this.resultsTab.UseVisualStyleBackColor = true;
            // 
            // userLog
            // 
            this.userLog.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.userLog.Font = new System.Drawing.Font("Consolas", 8.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.userLog.Location = new System.Drawing.Point(4, 5);
            this.userLog.Margin = new System.Windows.Forms.Padding(2);
            this.userLog.Name = "userLog";
            this.userLog.Size = new System.Drawing.Size(594, 420);
            this.userLog.TabIndex = 0;
            // 
            // helpTab
            // 
            this.helpTab.Controls.Add(this.helpWebBrowser);
            this.helpTab.Location = new System.Drawing.Point(4, 22);
            this.helpTab.Margin = new System.Windows.Forms.Padding(2);
            this.helpTab.Name = "helpTab";
            this.helpTab.Size = new System.Drawing.Size(600, 429);
            this.helpTab.TabIndex = 2;
            this.helpTab.Text = "Help";
            this.helpTab.UseVisualStyleBackColor = true;
            // 
            // helpWebBrowser
            // 
            this.helpWebBrowser.Dock = System.Windows.Forms.DockStyle.Fill;
            this.helpWebBrowser.Location = new System.Drawing.Point(0, 0);
            this.helpWebBrowser.Margin = new System.Windows.Forms.Padding(2);
            this.helpWebBrowser.MinimumSize = new System.Drawing.Size(15, 16);
            this.helpWebBrowser.Name = "helpWebBrowser";
            this.helpWebBrowser.Size = new System.Drawing.Size(600, 429);
            this.helpWebBrowser.TabIndex = 0;
            // 
            // creditsTab
            // 
            this.creditsTab.Controls.Add(this.creditsWebBrowser);
            this.creditsTab.Location = new System.Drawing.Point(4, 22);
            this.creditsTab.Name = "creditsTab";
            this.creditsTab.Padding = new System.Windows.Forms.Padding(3);
            this.creditsTab.Size = new System.Drawing.Size(600, 429);
            this.creditsTab.TabIndex = 3;
            this.creditsTab.Text = "Credits";
            this.creditsTab.UseVisualStyleBackColor = true;
            // 
            // creditsWebBrowser
            // 
            this.creditsWebBrowser.Dock = System.Windows.Forms.DockStyle.Fill;
            this.creditsWebBrowser.Location = new System.Drawing.Point(3, 3);
            this.creditsWebBrowser.Margin = new System.Windows.Forms.Padding(2);
            this.creditsWebBrowser.MinimumSize = new System.Drawing.Size(15, 16);
            this.creditsWebBrowser.Name = "creditsWebBrowser";
            this.creditsWebBrowser.Size = new System.Drawing.Size(594, 423);
            this.creditsWebBrowser.TabIndex = 1;
            // 
            // debugTab
            // 
            this.debugTab.Controls.Add(this.debugLog);
            this.debugTab.Location = new System.Drawing.Point(4, 22);
            this.debugTab.Margin = new System.Windows.Forms.Padding(2);
            this.debugTab.Name = "debugTab";
            this.debugTab.Padding = new System.Windows.Forms.Padding(2);
            this.debugTab.Size = new System.Drawing.Size(600, 429);
            this.debugTab.TabIndex = 1;
            this.debugTab.Text = "Debug Log";
            this.debugTab.UseVisualStyleBackColor = true;
            // 
            // debugLog
            // 
            this.debugLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.debugLog.Font = new System.Drawing.Font("Consolas", 8.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.debugLog.Location = new System.Drawing.Point(2, 2);
            this.debugLog.Margin = new System.Windows.Forms.Padding(2);
            this.debugLog.Name = "debugLog";
            this.debugLog.Size = new System.Drawing.Size(596, 425);
            this.debugLog.TabIndex = 0;
            // 
            // menuStripMain
            // 
            this.menuStripMain.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem,
            this.menuItemTools,
            this.menuItemOptions});
            this.menuStripMain.Location = new System.Drawing.Point(0, 0);
            this.menuStripMain.Name = "menuStripMain";
            this.menuStripMain.Size = new System.Drawing.Size(854, 24);
            this.menuStripMain.TabIndex = 3;
            this.menuStripMain.Text = "Main Menu";
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.loadFileToolStripMenuItem,
            this.saveFileToolStripMenuItem,
            this.saveFileAsToolStripMenuItem,
            this.fileSeparator1,
            this.importBinToolStripMenuItem,
            this.exportBinToolStripMenuItem,
            this.fileSeparator2,
            this.saveToolStripMenuItem,
            this.exitApplicationToolStripMenuItem});
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(37, 20);
            this.fileToolStripMenuItem.Text = "&File";
            //
            // loadFileToolStripMenuItem
            //
            this.loadFileToolStripMenuItem.Name = "loadFileToolStripMenuItem";
            this.loadFileToolStripMenuItem.Size = new System.Drawing.Size(200, 22);
            this.loadFileToolStripMenuItem.Text = "&Load File...";
            this.loadFileToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O)));
            this.loadFileToolStripMenuItem.Click += new System.EventHandler(this.loadFileButton_Click);
            //
            // saveFileToolStripMenuItem
            //
            this.saveFileToolStripMenuItem.Name = "saveFileToolStripMenuItem";
            this.saveFileToolStripMenuItem.Size = new System.Drawing.Size(200, 22);
            this.saveFileToolStripMenuItem.Text = "Save &File...";
            this.saveFileToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S)));
            this.saveFileToolStripMenuItem.Click += new System.EventHandler(this.saveFileButton_Click);
            //
            // saveFileAsToolStripMenuItem
            //
            this.saveFileAsToolStripMenuItem.Name = "saveFileAsToolStripMenuItem";
            this.saveFileAsToolStripMenuItem.Size = new System.Drawing.Size(200, 22);
            this.saveFileAsToolStripMenuItem.Text = "Save File &As...";
            this.saveFileAsToolStripMenuItem.Click += new System.EventHandler(this.saveFileAsMenuItem_Click);
            //
            // importBinToolStripMenuItem
            //
            this.importBinToolStripMenuItem.Name = "importBinToolStripMenuItem";
            this.importBinToolStripMenuItem.Size = new System.Drawing.Size(200, 22);
            this.importBinToolStripMenuItem.Text = "Import &Bin...";
            this.importBinToolStripMenuItem.Visible = false;
            this.importBinToolStripMenuItem.Click += new System.EventHandler(this.importBinButton_Click);
            //
            // exportBinToolStripMenuItem
            //
            this.exportBinToolStripMenuItem.Name = "exportBinToolStripMenuItem";
            this.exportBinToolStripMenuItem.Size = new System.Drawing.Size(200, 22);
            this.exportBinToolStripMenuItem.Text = "&Export Bin...";
            this.exportBinToolStripMenuItem.Click += new System.EventHandler(this.exportBinButton_Click);
            //
            // fileSeparator1
            //
            this.fileSeparator1.Name = "fileSeparator1";
            this.fileSeparator1.Size = new System.Drawing.Size(197, 6);
            //
            // fileSeparator2
            //
            this.fileSeparator2.Name = "fileSeparator2";
            this.fileSeparator2.Size = new System.Drawing.Size(197, 6);
            // 
            // saveToolStripMenuItem
            // 
            this.saveToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.saveResultsLogToolStripMenuItem,
            this.saveDebugLogToolStripMenuItem,
            this.saveBusMonitorLogToolStripMenuItem});
            this.saveToolStripMenuItem.Name = "saveToolStripMenuItem";
            this.saveToolStripMenuItem.Size = new System.Drawing.Size(98, 22);
            this.saveToolStripMenuItem.Text = "&Save";
            // 
            // saveResultsLogToolStripMenuItem
            // 
            this.saveResultsLogToolStripMenuItem.Name = "saveResultsLogToolStripMenuItem";
            this.saveResultsLogToolStripMenuItem.Size = new System.Drawing.Size(134, 22);
            this.saveResultsLogToolStripMenuItem.Text = "&Results Log";
            this.saveResultsLogToolStripMenuItem.Click += new System.EventHandler(this.saveResultsLogToolStripMenuItem_Click);
            // 
            // saveDebugLogToolStripMenuItem
            // 
            this.saveDebugLogToolStripMenuItem.Name = "saveDebugLogToolStripMenuItem";
            this.saveDebugLogToolStripMenuItem.Size = new System.Drawing.Size(134, 22);
            this.saveDebugLogToolStripMenuItem.Text = "&Debug Log";
            this.saveDebugLogToolStripMenuItem.Click += new System.EventHandler(this.saveDebugLogToolStripMenuItem_Click);
            //
            // saveBusMonitorLogToolStripMenuItem
            //
            this.saveBusMonitorLogToolStripMenuItem.Name = "saveBusMonitorLogToolStripMenuItem";
            this.saveBusMonitorLogToolStripMenuItem.Size = new System.Drawing.Size(134, 22);
            this.saveBusMonitorLogToolStripMenuItem.Text = "&Bus Monitor";
            this.saveBusMonitorLogToolStripMenuItem.Visible = false;
            this.saveBusMonitorLogToolStripMenuItem.Click += new System.EventHandler(this.saveBusMonitorLogToolStripMenuItem_Click);
            //
            // exitApplicationToolStripMenuItem
            // 
            this.exitApplicationToolStripMenuItem.Name = "exitApplicationToolStripMenuItem";
            this.exitApplicationToolStripMenuItem.Size = new System.Drawing.Size(98, 22);
            this.exitApplicationToolStripMenuItem.Text = "E&xit";
            this.exitApplicationToolStripMenuItem.Click += new System.EventHandler(this.exitApplicationToolStripMenuItem_Click);
            // 
            // menuItemTools
            // 
            this.menuItemTools.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.readEntirePCMToolStripMenuItem,
            this.verifyEntirePCMToolStripMenuItem,
            this.modifyVINToolStripMenuItem,
            this.toolStripSeparator1,
            this.writeParmetersCloneToolStripMenuItem,
            this.writeOSCalibrationBootToolStripMenuItem,
            this.writeFullToolStripMenuItem,
            this.toolStripSeparator2,
            this.pcmRecoveryToolStripMenuItem,
            this.testFileChecksumsToolStripMenuItem,
            this.bruteForceUnlockToolStripMenuItem,
            this.busMonitorToolStripMenuItem,
            this.toolStripSeparator3,
            this.haltRunningKernelToolStripMenuItem});
            this.menuItemTools.Name = "menuItemTools";
            this.menuItemTools.Size = new System.Drawing.Size(46, 20);
            this.menuItemTools.Text = "&Tools";
            // 
            // readEntirePCMToolStripMenuItem
            // 
            this.readEntirePCMToolStripMenuItem.Name = "readEntirePCMToolStripMenuItem";
            this.readEntirePCMToolStripMenuItem.Size = new System.Drawing.Size(225, 22);
            this.readEntirePCMToolStripMenuItem.Text = "&Read Entire PCM";
            this.readEntirePCMToolStripMenuItem.Click += new System.EventHandler(this.readFullContentsButton_Click);
            // 
            // verifyEntirePCMToolStripMenuItem
            // 
            this.verifyEntirePCMToolStripMenuItem.Name = "verifyEntirePCMToolStripMenuItem";
            this.verifyEntirePCMToolStripMenuItem.Size = new System.Drawing.Size(225, 22);
            this.verifyEntirePCMToolStripMenuItem.Text = "&Verify Entire PCM";
            this.verifyEntirePCMToolStripMenuItem.Click += new System.EventHandler(this.quickComparisonButton_Click);
            // 
            // modifyVINToolStripMenuItem
            // 
            this.modifyVINToolStripMenuItem.Name = "modifyVINToolStripMenuItem";
            this.modifyVINToolStripMenuItem.Size = new System.Drawing.Size(225, 22);
            this.modifyVINToolStripMenuItem.Text = "&Change VIN";
            this.modifyVINToolStripMenuItem.Click += new System.EventHandler(this.modifyVinButton_Click);
            // 
            // writeParmetersCloneToolStripMenuItem
            // 
            this.writeParmetersCloneToolStripMenuItem.Name = "writeParmetersCloneToolStripMenuItem";
            this.writeParmetersCloneToolStripMenuItem.Size = new System.Drawing.Size(225, 22);
            this.writeParmetersCloneToolStripMenuItem.Text = "Write &Parameters";
            this.writeParmetersCloneToolStripMenuItem.Click += new System.EventHandler(this.writeParametersButton_Click);
            // 
            // writeOSCalibrationBootToolStripMenuItem
            // 
            this.writeOSCalibrationBootToolStripMenuItem.Name = "writeOSCalibrationBootToolStripMenuItem";
            this.writeOSCalibrationBootToolStripMenuItem.Size = new System.Drawing.Size(225, 22);
            this.writeOSCalibrationBootToolStripMenuItem.Text = "Write &OS, Calibration && Boot";
            this.writeOSCalibrationBootToolStripMenuItem.Click += new System.EventHandler(this.writeOSCalibrationBootToolStripMenuItem_Click);
            // 
            // writeFullToolStripMenuItem
            // 
            this.writeFullToolStripMenuItem.Name = "writeFullToolStripMenuItem";
            this.writeFullToolStripMenuItem.Size = new System.Drawing.Size(225, 22);
            this.writeFullToolStripMenuItem.Text = "Write &Full Flash (Clone)";
            this.writeFullToolStripMenuItem.Click += new System.EventHandler(this.writeFullToolStripMenuItem_Click);
            // 
            // testFileChecksumsToolStripMenuItem
            // 
            this.testFileChecksumsToolStripMenuItem.Name = "testFileChecksumsToolStripMenuItem";
            this.testFileChecksumsToolStripMenuItem.Size = new System.Drawing.Size(225, 22);
            this.testFileChecksumsToolStripMenuItem.Text = "&Test File Checksums";
            this.testFileChecksumsToolStripMenuItem.Click += new System.EventHandler(this.testFileChecksumsToolStripMenuItem_Click);
            //
            // toolStripSeparator3
            //
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            this.toolStripSeparator3.Size = new System.Drawing.Size(222, 6);
            //
            // bruteForceUnlockToolStripMenuItem
            //
            this.bruteForceUnlockToolStripMenuItem.Name = "bruteForceUnlockToolStripMenuItem";
            this.bruteForceUnlockToolStripMenuItem.Size = new System.Drawing.Size(225, 22);
            this.bruteForceUnlockToolStripMenuItem.Text = "Brute Force &Unlock";
            this.bruteForceUnlockToolStripMenuItem.ToolTipText = "Search for the PCM security key. This can take a very long time.";
            this.bruteForceUnlockToolStripMenuItem.Click += new System.EventHandler(this.bruteForceUnlockToolStripMenuItem_Click);
            //
            // busMonitorToolStripMenuItem
            //
            this.busMonitorToolStripMenuItem.Name = "busMonitorToolStripMenuItem";
            this.busMonitorToolStripMenuItem.Size = new System.Drawing.Size(225, 22);
            this.busMonitorToolStripMenuItem.Text = "Bus &Monitor";
            this.busMonitorToolStripMenuItem.ToolTipText = "Show the Bus Monitor tab and switch to it.";
            this.busMonitorToolStripMenuItem.Click += new System.EventHandler(this.busMonitorToolStripMenuItem_Click);
            //
            // pcmRecoveryToolStripMenuItem
            //
            this.pcmRecoveryToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.recoveryReadToolStripMenuItem,
            this.recoveryWriteToolStripMenuItem});
            this.pcmRecoveryToolStripMenuItem.Name = "pcmRecoveryToolStripMenuItem";
            this.pcmRecoveryToolStripMenuItem.Size = new System.Drawing.Size(225, 22);
            this.pcmRecoveryToolStripMenuItem.Text = "PCM &Recovery";
            //
            // recoveryReadToolStripMenuItem
            //
            this.recoveryReadToolStripMenuItem.Name = "recoveryReadToolStripMenuItem";
            this.recoveryReadToolStripMenuItem.Size = new System.Drawing.Size(140, 22);
            this.recoveryReadToolStripMenuItem.Text = "&Read...";
            this.recoveryReadToolStripMenuItem.Click += new System.EventHandler(this.recoveryReadToolStripMenuItem_Click);
            //
            // recoveryWriteToolStripMenuItem
            //
            this.recoveryWriteToolStripMenuItem.Name = "recoveryWriteToolStripMenuItem";
            this.recoveryWriteToolStripMenuItem.Size = new System.Drawing.Size(140, 22);
            this.recoveryWriteToolStripMenuItem.Text = "&Write...";
            this.recoveryWriteToolStripMenuItem.Click += new System.EventHandler(this.recoveryWriteToolStripMenuItem_Click);
            //
            // haltRunningKernelToolStripMenuItem
            //
            this.haltRunningKernelToolStripMenuItem.Name = "haltRunningKernelToolStripMenuItem";
            this.haltRunningKernelToolStripMenuItem.Size = new System.Drawing.Size(225, 22);
            this.haltRunningKernelToolStripMenuItem.Text = "&Halt Running Kernel";
            this.haltRunningKernelToolStripMenuItem.Click += new System.EventHandler(this.testKernelButton_Click);
            //
            // menuItemOptions
            // 
            this.menuItemOptions.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.userDefinedKeyToolStripMenuItem,
            this.settingsToolStripMenuItem});
            this.menuItemOptions.Name = "menuItemOptions";
            this.menuItemOptions.Size = new System.Drawing.Size(61, 20);
            this.menuItemOptions.Text = "&Options";
            // 
            // userDefinedKeyToolStripMenuItem
            // 
            this.userDefinedKeyToolStripMenuItem.CheckOnClick = true;
            this.userDefinedKeyToolStripMenuItem.Name = "userDefinedKeyToolStripMenuItem";
            this.userDefinedKeyToolStripMenuItem.Size = new System.Drawing.Size(163, 22);
            this.userDefinedKeyToolStripMenuItem.Text = "&User Defined Key";
            this.userDefinedKeyToolStripMenuItem.ToolTipText = "Valid for current device selection, Application instance or Toggle Action.";
            this.userDefinedKeyToolStripMenuItem.Click += new System.EventHandler(this.userDefinedKeyToolStripMenuItem_Click);
            // 
            // settingsToolStripMenuItem
            // 
            this.settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            this.settingsToolStripMenuItem.Size = new System.Drawing.Size(163, 22);
            this.settingsToolStripMenuItem.Text = "&Settings";
            this.settingsToolStripMenuItem.Click += new System.EventHandler(this.settingsToolStripMenuItem_Click);
            // 
            // statusStatusStrip
            // 
            this.statusStatusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.activityToolStripStatusLabel,
            this.retryCountToolStripStatusLabel,
            this.kbpsToolStripStatusLabel,
            this.progressBarToolStripProgressBar,
            this.percentDoneToolStripStatusLabel,
            this.timeRemainingToolStripStatusLabel});
            this.statusStatusStrip.Location = new System.Drawing.Point(0, 491);
            this.statusStatusStrip.Name = "statusStatusStrip";
            this.statusStatusStrip.ShowItemToolTips = true;
            this.statusStatusStrip.Size = new System.Drawing.Size(854, 22);
            this.statusStatusStrip.TabIndex = 4;
            this.statusStatusStrip.Text = "Status Strip";
            // 
            // activityToolStripStatusLabel
            // 
            this.activityToolStripStatusLabel.AutoSize = false;
            this.activityToolStripStatusLabel.Name = "activityToolStripStatusLabel";
            this.activityToolStripStatusLabel.Size = new System.Drawing.Size(332, 17);
            this.activityToolStripStatusLabel.Spring = true;
            this.activityToolStripStatusLabel.Text = "Activity";
            this.activityToolStripStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.activityToolStripStatusLabel.ToolTipText = "Current Activity";
            // 
            // retryCountToolStripStatusLabel
            // 
            this.retryCountToolStripStatusLabel.AutoSize = false;
            this.retryCountToolStripStatusLabel.Name = "retryCountToolStripStatusLabel";
            this.retryCountToolStripStatusLabel.Size = new System.Drawing.Size(65, 17);
            this.retryCountToolStripStatusLabel.Text = "Retry";
            this.retryCountToolStripStatusLabel.ToolTipText = "Retry Count";
            // 
            // kbpsToolStripStatusLabel
            // 
            this.kbpsToolStripStatusLabel.AutoSize = false;
            this.kbpsToolStripStatusLabel.Name = "kbpsToolStripStatusLabel";
            this.kbpsToolStripStatusLabel.Size = new System.Drawing.Size(65, 17);
            this.kbpsToolStripStatusLabel.Text = "Kbps";
            this.kbpsToolStripStatusLabel.ToolTipText = "Kilobits Per Second";
            // 
            // progressBarToolStripProgressBar
            // 
            this.progressBarToolStripProgressBar.AutoSize = false;
            this.progressBarToolStripProgressBar.Name = "progressBarToolStripProgressBar";
            this.progressBarToolStripProgressBar.Size = new System.Drawing.Size(250, 16);
            // 
            // percentDoneToolStripStatusLabel
            // 
            this.percentDoneToolStripStatusLabel.AutoSize = false;
            this.percentDoneToolStripStatusLabel.Name = "percentDoneToolStripStatusLabel";
            this.percentDoneToolStripStatusLabel.Size = new System.Drawing.Size(50, 17);
            this.percentDoneToolStripStatusLabel.Text = "Percent";
            this.percentDoneToolStripStatusLabel.ToolTipText = "Percent Completed";
            // 
            // timeRemainingToolStripStatusLabel
            // 
            this.timeRemainingToolStripStatusLabel.AutoSize = false;
            this.timeRemainingToolStripStatusLabel.Name = "timeRemainingToolStripStatusLabel";
            this.timeRemainingToolStripStatusLabel.Size = new System.Drawing.Size(75, 17);
            this.timeRemainingToolStripStatusLabel.Text = "Time Remaining";
            this.timeRemainingToolStripStatusLabel.ToolTipText = "Time Remaining (T minus)";
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(222, 6);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(222, 6);
            // 
            // MainForm
            // 
            this.AcceptButton = this.identifyPcmButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(939, 650);
            this.Controls.Add(this.statusStatusStrip);
            this.Controls.Add(this.tabs);
            this.Controls.Add(this.operationsBox);
            this.Controls.Add(this.fileBox);
            this.Controls.Add(this.interfaceBox);
            this.Controls.Add(this.menuStripMain);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MainMenuStrip = this.menuStripMain;
            this.Margin = new System.Windows.Forms.Padding(2);
            this.Name = "MainForm";
            this.Text = "PCM Hammer";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.interfaceBox.ResumeLayout(false);
            this.operationsBox.ResumeLayout(false);
            this.fileBox.ResumeLayout(false);
            this.tabs.ResumeLayout(false);
            this.resultsTab.ResumeLayout(false);
            this.resultsTab.PerformLayout();
            this.helpTab.ResumeLayout(false);
            this.creditsTab.ResumeLayout(false);
            this.debugTab.ResumeLayout(false);
            this.debugTab.PerformLayout();
            this.menuStripMain.ResumeLayout(false);
            this.menuStripMain.PerformLayout();
            this.statusStatusStrip.ResumeLayout(false);
            this.statusStatusStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox interfaceBox;
        private System.Windows.Forms.GroupBox operationsBox;
        private System.Windows.Forms.GroupBox fileBox;
        private System.Windows.Forms.Label loadedFileLabel;
        private System.Windows.Forms.Button loadFileButton;
        private System.Windows.Forms.Button saveFileButton;
        private System.Windows.Forms.Button exportBinButton;
        private System.Windows.Forms.Button verifyPcmButton;
        private System.Windows.Forms.Button readPcmButton;
        private System.Windows.Forms.Button writeCalibrationButton;
        private System.Windows.Forms.Button identifyPcmButton;
        private System.Windows.Forms.TabControl tabs;
        private System.Windows.Forms.TabPage resultsTab;
        private PcmHacking.LogListView userLog;
        private System.Windows.Forms.TabPage debugTab;
        private PcmHacking.LogListView debugLog;
        private System.Windows.Forms.Button reinitializeButton;
        private System.Windows.Forms.Button selectButton;
        private System.Windows.Forms.Label deviceDescription;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.TabPage helpTab;
        private System.Windows.Forms.WebBrowser helpWebBrowser;
        private System.Windows.Forms.Button exitKernelButton;
        private System.Windows.Forms.TabPage creditsTab;
        private System.Windows.Forms.WebBrowser creditsWebBrowser;
        private System.Windows.Forms.MenuStrip menuStripMain;
        private System.Windows.Forms.ToolStripMenuItem menuItemOptions;
        private System.Windows.Forms.ToolStripMenuItem menuItemTools;
        private System.Windows.Forms.ToolStripMenuItem readEntirePCMToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem verifyEntirePCMToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem modifyVINToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem writeParmetersCloneToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem writeOSCalibrationBootToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem writeFullToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem settingsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem loadFileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem saveFileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem saveFileAsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem importBinToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exportBinToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator fileSeparator1;
        private System.Windows.Forms.ToolStripSeparator fileSeparator2;
        private System.Windows.Forms.ToolStripMenuItem saveToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem saveResultsLogToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem saveDebugLogToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem saveBusMonitorLogToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exitApplicationToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem userDefinedKeyToolStripMenuItem;
        private System.Windows.Forms.StatusStrip statusStatusStrip;
        private System.Windows.Forms.ToolStripStatusLabel activityToolStripStatusLabel;
        private System.Windows.Forms.ToolStripStatusLabel timeRemainingToolStripStatusLabel;
        private System.Windows.Forms.ToolStripStatusLabel percentDoneToolStripStatusLabel;
        private System.Windows.Forms.ToolStripStatusLabel retryCountToolStripStatusLabel;
        private System.Windows.Forms.ToolStripProgressBar progressBarToolStripProgressBar;
        private System.Windows.Forms.ToolStripStatusLabel kbpsToolStripStatusLabel;
        private System.Windows.Forms.ToolStripMenuItem testFileChecksumsToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
        private System.Windows.Forms.ToolStripMenuItem bruteForceUnlockToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem busMonitorToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem haltRunningKernelToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem pcmRecoveryToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem recoveryReadToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem recoveryWriteToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
    }
}


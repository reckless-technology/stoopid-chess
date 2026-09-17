namespace StoopidChess.Gui
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.chessBoardPictureBox = new System.Windows.Forms.PictureBox();
            this.captureBinPictureBox = new System.Windows.Forms.PictureBox();
            this.verticalSplitContainer = new System.Windows.Forms.SplitContainer();
            this.boardCaptureSplitContainer = new System.Windows.Forms.SplitContainer();
            this.moveListTextBox = new System.Windows.Forms.RichTextBox();
            this.mainMenuStrip = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.saveToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.loadToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.gameToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.undoLastMoveToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.setLevelToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.beginnerMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.easyToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.mediumToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.hardToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.bestslowToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.newGameToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.whiteToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.blackToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.viewToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.flipBoardToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.helpToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.engineToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.engineVersusEngineToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.saveFileDialog = new System.Windows.Forms.SaveFileDialog();
            this.openFileDialog = new System.Windows.Forms.OpenFileDialog();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.youArePlayingLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.humanColourLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.colourToMoveAnnotationLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.colourToMoveLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.timeElapsedLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel1 = new System.Windows.Forms.ToolStripStatusLabel();
            this.difficultyLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel2 = new System.Windows.Forms.ToolStripStatusLabel();
            this.whiteClockLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel3 = new System.Windows.Forms.ToolStripStatusLabel();
            this.blackClockLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.contextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.clearSquareToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.whiteToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.whitePawnToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.whiteKnightToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.whiteBishopToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.whiteRookToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.whiteQueenToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.whiteKingToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.blackToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.blackPawnToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.blackKnightToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.blackBishopToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.blackRookToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.blackQueenToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.blackKingToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.clearEntireBoardToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.stopThinkingButton = new System.Windows.Forms.Button();
            this.switchSidesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize)(this.chessBoardPictureBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.captureBinPictureBox)).BeginInit();
            this.verticalSplitContainer.Panel1.SuspendLayout();
            this.verticalSplitContainer.Panel2.SuspendLayout();
            this.verticalSplitContainer.SuspendLayout();
            this.boardCaptureSplitContainer.Panel1.SuspendLayout();
            this.boardCaptureSplitContainer.Panel2.SuspendLayout();
            this.boardCaptureSplitContainer.SuspendLayout();
            this.mainMenuStrip.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.contextMenuStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // chessBoardPictureBox
            // 
            this.chessBoardPictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.chessBoardPictureBox.Location = new System.Drawing.Point(0, 0);
            this.chessBoardPictureBox.Name = "chessBoardPictureBox";
            this.chessBoardPictureBox.Size = new System.Drawing.Size(512, 512);
            this.chessBoardPictureBox.TabIndex = 0;
            this.chessBoardPictureBox.TabStop = false;
            this.chessBoardPictureBox.MouseMove += new System.Windows.Forms.MouseEventHandler(this.OnBoardMouseMove);
            this.chessBoardPictureBox.MouseClick += new System.Windows.Forms.MouseEventHandler(this.OnBoardMouseClick);
            this.chessBoardPictureBox.MouseDown += new System.Windows.Forms.MouseEventHandler(this.OnBoardMouseDown);
            this.chessBoardPictureBox.Paint += new System.Windows.Forms.PaintEventHandler(this.OnBoardPaint);
            this.chessBoardPictureBox.MouseUp += new System.Windows.Forms.MouseEventHandler(this.OnBoardMouseUp);
            this.chessBoardPictureBox.SizeChanged += new System.EventHandler(this.OnBoardSizeChanged);
            // 
            // captureBinPictureBox
            // 
            this.captureBinPictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.captureBinPictureBox.Location = new System.Drawing.Point(0, 0);
            this.captureBinPictureBox.Name = "captureBinPictureBox";
            this.captureBinPictureBox.Size = new System.Drawing.Size(512, 64);
            this.captureBinPictureBox.TabIndex = 9;
            this.captureBinPictureBox.TabStop = false;
            this.captureBinPictureBox.Paint += new System.Windows.Forms.PaintEventHandler(this.OnCaptureBinPaint);
            // 
            // verticalSplitContainer
            // 
            this.verticalSplitContainer.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.verticalSplitContainer.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.verticalSplitContainer.Location = new System.Drawing.Point(0, 27);
            this.verticalSplitContainer.Name = "verticalSplitContainer";
            // 
            // verticalSplitContainer.Panel1
            // 
            this.verticalSplitContainer.Panel1.Controls.Add(this.boardCaptureSplitContainer);
            // 
            // verticalSplitContainer.Panel2
            // 
            this.verticalSplitContainer.Panel2.Controls.Add(this.moveListTextBox);
            this.verticalSplitContainer.Size = new System.Drawing.Size(744, 589);
            this.verticalSplitContainer.SplitterDistance = 516;
            this.verticalSplitContainer.SplitterWidth = 5;
            this.verticalSplitContainer.TabIndex = 15;
            this.verticalSplitContainer.TabStop = false;
            // 
            // boardCaptureSplitContainer
            // 
            this.boardCaptureSplitContainer.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.boardCaptureSplitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.boardCaptureSplitContainer.Location = new System.Drawing.Point(0, 0);
            this.boardCaptureSplitContainer.Name = "boardCaptureSplitContainer";
            this.boardCaptureSplitContainer.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // boardCaptureSplitContainer.Panel1
            // 
            this.boardCaptureSplitContainer.Panel1.Controls.Add(this.chessBoardPictureBox);
            // 
            // boardCaptureSplitContainer.Panel2
            // 
            this.boardCaptureSplitContainer.Panel2.Controls.Add(this.captureBinPictureBox);
            this.boardCaptureSplitContainer.Size = new System.Drawing.Size(516, 589);
            this.boardCaptureSplitContainer.SplitterDistance = 516;
            this.boardCaptureSplitContainer.SplitterWidth = 5;
            this.boardCaptureSplitContainer.TabIndex = 1;
            this.boardCaptureSplitContainer.TabStop = false;
            // 
            // moveListTextBox
            // 
            this.moveListTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.moveListTextBox.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.moveListTextBox.Location = new System.Drawing.Point(0, 0);
            this.moveListTextBox.Name = "moveListTextBox";
            this.moveListTextBox.ReadOnly = true;
            this.moveListTextBox.Size = new System.Drawing.Size(219, 585);
            this.moveListTextBox.TabIndex = 2;
            this.moveListTextBox.TabStop = false;
            this.moveListTextBox.Text = "";
            // 
            // mainMenuStrip
            // 
            this.mainMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem,
            this.gameToolStripMenuItem,
            this.viewToolStripMenuItem,
            this.helpToolStripMenuItem,
            this.engineToolStripMenuItem});
            this.mainMenuStrip.Location = new System.Drawing.Point(0, 0);
            this.mainMenuStrip.Name = "mainMenuStrip";
            this.mainMenuStrip.Size = new System.Drawing.Size(744, 24);
            this.mainMenuStrip.TabIndex = 16;
            this.mainMenuStrip.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.saveToolStripMenuItem,
            this.loadToolStripMenuItem,
            this.exitToolStripMenuItem});
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(37, 20);
            this.fileToolStripMenuItem.Text = "File";
            // 
            // saveToolStripMenuItem
            // 
            this.saveToolStripMenuItem.Name = "saveToolStripMenuItem";
            this.saveToolStripMenuItem.Size = new System.Drawing.Size(133, 22);
            this.saveToolStripMenuItem.Text = "Save game";
            this.saveToolStripMenuItem.Click += new System.EventHandler(this.OnSaveGame);
            // 
            // loadToolStripMenuItem
            // 
            this.loadToolStripMenuItem.Name = "loadToolStripMenuItem";
            this.loadToolStripMenuItem.Size = new System.Drawing.Size(133, 22);
            this.loadToolStripMenuItem.Text = "Load game";
            this.loadToolStripMenuItem.Click += new System.EventHandler(this.OnLoadGame);
            // 
            // exitToolStripMenuItem
            // 
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(133, 22);
            this.exitToolStripMenuItem.Text = "Exit";
            this.exitToolStripMenuItem.Click += new System.EventHandler(this.OnExit);
            // 
            // gameToolStripMenuItem
            // 
            this.gameToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.undoLastMoveToolStripMenuItem,
            this.setLevelToolStripMenuItem,
            this.newGameToolStripMenuItem,
            this.switchSidesToolStripMenuItem});
            this.gameToolStripMenuItem.Name = "gameToolStripMenuItem";
            this.gameToolStripMenuItem.Size = new System.Drawing.Size(50, 20);
            this.gameToolStripMenuItem.Text = "Game";
            // 
            // undoLastMoveToolStripMenuItem
            // 
            this.undoLastMoveToolStripMenuItem.Name = "undoLastMoveToolStripMenuItem";
            this.undoLastMoveToolStripMenuItem.Size = new System.Drawing.Size(157, 22);
            this.undoLastMoveToolStripMenuItem.Text = "Undo last move";
            this.undoLastMoveToolStripMenuItem.Click += new System.EventHandler(this.OnUndoMove);
            // 
            // setLevelToolStripMenuItem
            // 
            this.setLevelToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.beginnerMenuItem,
            this.easyToolStripMenuItem,
            this.mediumToolStripMenuItem,
            this.hardToolStripMenuItem,
            this.bestslowToolStripMenuItem});
            this.setLevelToolStripMenuItem.Name = "setLevelToolStripMenuItem";
            this.setLevelToolStripMenuItem.Size = new System.Drawing.Size(157, 22);
            this.setLevelToolStripMenuItem.Text = "Set level";
            // 
            // beginnerMenuItem
            // 
            this.beginnerMenuItem.Name = "beginnerMenuItem";
            this.beginnerMenuItem.Size = new System.Drawing.Size(131, 22);
            this.beginnerMenuItem.Text = "Beginner";
            this.beginnerMenuItem.Click += new System.EventHandler(this.OnLevelBeginner);
            // 
            // easyToolStripMenuItem
            // 
            this.easyToolStripMenuItem.Name = "easyToolStripMenuItem";
            this.easyToolStripMenuItem.Size = new System.Drawing.Size(131, 22);
            this.easyToolStripMenuItem.Text = "Easy";
            this.easyToolStripMenuItem.Click += new System.EventHandler(this.OnLevelEasy);
            // 
            // mediumToolStripMenuItem
            // 
            this.mediumToolStripMenuItem.Name = "mediumToolStripMenuItem";
            this.mediumToolStripMenuItem.Size = new System.Drawing.Size(131, 22);
            this.mediumToolStripMenuItem.Text = "Medium";
            this.mediumToolStripMenuItem.Click += new System.EventHandler(this.OnLevelMedium);
            // 
            // hardToolStripMenuItem
            // 
            this.hardToolStripMenuItem.Name = "hardToolStripMenuItem";
            this.hardToolStripMenuItem.Size = new System.Drawing.Size(131, 22);
            this.hardToolStripMenuItem.Text = "Hard";
            this.hardToolStripMenuItem.Click += new System.EventHandler(this.OnLevelHard);
            // 
            // bestslowToolStripMenuItem
            // 
            this.bestslowToolStripMenuItem.Name = "bestslowToolStripMenuItem";
            this.bestslowToolStripMenuItem.Size = new System.Drawing.Size(131, 22);
            this.bestslowToolStripMenuItem.Text = "Best (slow)";
            this.bestslowToolStripMenuItem.Click += new System.EventHandler(this.OnLevelBest);
            // 
            // newGameToolStripMenuItem
            // 
            this.newGameToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.whiteToolStripMenuItem,
            this.blackToolStripMenuItem});
            this.newGameToolStripMenuItem.Name = "newGameToolStripMenuItem";
            this.newGameToolStripMenuItem.Size = new System.Drawing.Size(157, 22);
            this.newGameToolStripMenuItem.Text = "New Game";
            // 
            // whiteToolStripMenuItem
            // 
            this.whiteToolStripMenuItem.Name = "whiteToolStripMenuItem";
            this.whiteToolStripMenuItem.Size = new System.Drawing.Size(105, 22);
            this.whiteToolStripMenuItem.Text = "White";
            this.whiteToolStripMenuItem.Click += new System.EventHandler(this.OnNewGameWhite);
            // 
            // blackToolStripMenuItem
            // 
            this.blackToolStripMenuItem.Name = "blackToolStripMenuItem";
            this.blackToolStripMenuItem.Size = new System.Drawing.Size(105, 22);
            this.blackToolStripMenuItem.Text = "Black";
            this.blackToolStripMenuItem.Click += new System.EventHandler(this.OnNewGameBlack);
            // 
            // viewToolStripMenuItem
            // 
            this.viewToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.flipBoardToolStripMenuItem});
            this.viewToolStripMenuItem.Name = "viewToolStripMenuItem";
            this.viewToolStripMenuItem.Size = new System.Drawing.Size(44, 20);
            this.viewToolStripMenuItem.Text = "View";
            // 
            // flipBoardToolStripMenuItem
            // 
            this.flipBoardToolStripMenuItem.Name = "flipBoardToolStripMenuItem";
            this.flipBoardToolStripMenuItem.Size = new System.Drawing.Size(127, 22);
            this.flipBoardToolStripMenuItem.Text = "Flip board";
            this.flipBoardToolStripMenuItem.Click += new System.EventHandler(this.OnFlipBoard);
            // 
            // helpToolStripMenuItem
            // 
            this.helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            this.helpToolStripMenuItem.Size = new System.Drawing.Size(44, 20);
            this.helpToolStripMenuItem.Text = "Help";
            this.helpToolStripMenuItem.Click += new System.EventHandler(this.OnHelp);
            // 
            // engineToolStripMenuItem
            // 
            this.engineToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.engineVersusEngineToolStripMenuItem});
            this.engineToolStripMenuItem.Name = "engineToolStripMenuItem";
            this.engineToolStripMenuItem.Size = new System.Drawing.Size(54, 20);
            this.engineToolStripMenuItem.Text = "&Engine";
            // 
            // engineVersusEngineToolStripMenuItem
            // 
            this.engineVersusEngineToolStripMenuItem.Name = "engineVersusEngineToolStripMenuItem";
            this.engineVersusEngineToolStripMenuItem.Size = new System.Drawing.Size(151, 22);
            this.engineVersusEngineToolStripMenuItem.Text = "&Play engine against itself";
            this.engineVersusEngineToolStripMenuItem.Click += new System.EventHandler(this.OnEngineVersusEngine);
            // 
            // saveFileDialog
            // 
            this.saveFileDialog.DefaultExt = "chess";
            this.saveFileDialog.Filter = "chess|*.chess|All files|*.*";
            this.saveFileDialog.InitialDirectory = ".";
            // 
            // openFileDialog
            // 
            this.openFileDialog.DefaultExt = "chess";
            this.openFileDialog.FileName = "openFileDialog";
            this.openFileDialog.Filter = "chess|*.chess|All files|*.*";
            // 
            // statusStrip
            // 
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.youArePlayingLabel,
            this.humanColourLabel,
            this.colourToMoveAnnotationLabel,
            this.colourToMoveLabel,
            this.timeElapsedLabel,
            this.toolStripStatusLabel1,
            this.difficultyLabel,
            this.toolStripStatusLabel2,
            this.whiteClockLabel,
            this.toolStripStatusLabel3,
            this.blackClockLabel});
            this.statusStrip.Location = new System.Drawing.Point(0, 619);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(744, 22);
            this.statusStrip.TabIndex = 17;
            this.statusStrip.Text = "statusStrip1";
            // 
            // youArePlayingLabel
            // 
            this.youArePlayingLabel.Name = "youArePlayingLabel";
            this.youArePlayingLabel.Size = new System.Drawing.Size(92, 17);
            this.youArePlayingLabel.Text = "You are playing:";
            // 
            // humanColourLabel
            // 
            this.humanColourLabel.Name = "humanColourLabel";
            this.humanColourLabel.Size = new System.Drawing.Size(43, 17);
            this.humanColourLabel.Text = "WHITE";
            // 
            // colourToMoveAnnotationLabel
            // 
            this.colourToMoveAnnotationLabel.Name = "colourToMoveAnnotationLabel";
            this.colourToMoveAnnotationLabel.Size = new System.Drawing.Size(93, 17);
            this.colourToMoveAnnotationLabel.Text = "Colour to move:";
            // 
            // colourToMoveLabel
            // 
            this.colourToMoveLabel.Name = "colourToMoveLabel";
            this.colourToMoveLabel.Size = new System.Drawing.Size(43, 17);
            this.colourToMoveLabel.Text = "WHITE";
            // 
            // timeElapsedLabel
            // 
            this.timeElapsedLabel.Name = "timeElapsedLabel";
            this.timeElapsedLabel.Size = new System.Drawing.Size(0, 17);
            // 
            // toolStripStatusLabel1
            // 
            this.toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            this.toolStripStatusLabel1.Size = new System.Drawing.Size(58, 17);
            this.toolStripStatusLabel1.Text = "Difficulty:";
            // 
            // difficultyLabel
            // 
            this.difficultyLabel.Name = "difficultyLabel";
            this.difficultyLabel.Size = new System.Drawing.Size(30, 17);
            this.difficultyLabel.Text = "Easy";
            // 
            // toolStripStatusLabel2
            // 
            this.toolStripStatusLabel2.Name = "toolStripStatusLabel2";
            this.toolStripStatusLabel2.Size = new System.Drawing.Size(72, 17);
            this.toolStripStatusLabel2.Text = "White clock:";
            // 
            // whiteClockLabel
            // 
            this.whiteClockLabel.Name = "whiteClockLabel";
            this.whiteClockLabel.Size = new System.Drawing.Size(49, 17);
            this.whiteClockLabel.Text = "00:00:00";
            // 
            // toolStripStatusLabel3
            // 
            this.toolStripStatusLabel3.Name = "toolStripStatusLabel3";
            this.toolStripStatusLabel3.Size = new System.Drawing.Size(69, 17);
            this.toolStripStatusLabel3.Text = "Black clock:";
            // 
            // blackClockLabel
            // 
            this.blackClockLabel.Name = "blackClockLabel";
            this.blackClockLabel.Size = new System.Drawing.Size(49, 17);
            this.blackClockLabel.Text = "00:00:00";
            // 
            // contextMenuStrip
            // 
            this.contextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.clearSquareToolStripMenuItem,
            this.whiteToolStripMenuItem1,
            this.blackToolStripMenuItem1,
            this.clearEntireBoardToolStripMenuItem});
            this.contextMenuStrip.Name = "contextMenuStrip1";
            this.contextMenuStrip.Size = new System.Drawing.Size(169, 92);
            // 
            // clearSquareToolStripMenuItem
            // 
            this.clearSquareToolStripMenuItem.Name = "clearSquareToolStripMenuItem";
            this.clearSquareToolStripMenuItem.Size = new System.Drawing.Size(168, 22);
            this.clearSquareToolStripMenuItem.Text = "Clear square";
            this.clearSquareToolStripMenuItem.Click += new System.EventHandler(this.OnClearSquare);
            // 
            // whiteToolStripMenuItem1
            // 
            this.whiteToolStripMenuItem1.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.whitePawnToolStripMenuItem,
            this.whiteKnightToolStripMenuItem,
            this.whiteBishopToolStripMenuItem,
            this.whiteRookToolStripMenuItem,
            this.whiteQueenToolStripMenuItem,
            this.whiteKingToolStripMenuItem});
            this.whiteToolStripMenuItem1.Name = "whiteToolStripMenuItem1";
            this.whiteToolStripMenuItem1.Size = new System.Drawing.Size(168, 22);
            this.whiteToolStripMenuItem1.Text = "White";
            // 
            // whitePawnToolStripMenuItem
            // 
            this.whitePawnToolStripMenuItem.Name = "whitePawnToolStripMenuItem";
            this.whitePawnToolStripMenuItem.Size = new System.Drawing.Size(110, 22);
            this.whitePawnToolStripMenuItem.Text = "Pawn";
            this.whitePawnToolStripMenuItem.Click += new System.EventHandler(this.OnSetWhitePawn);
            // 
            // whiteKnightToolStripMenuItem
            // 
            this.whiteKnightToolStripMenuItem.Name = "whiteKnightToolStripMenuItem";
            this.whiteKnightToolStripMenuItem.Size = new System.Drawing.Size(110, 22);
            this.whiteKnightToolStripMenuItem.Text = "Knight";
            this.whiteKnightToolStripMenuItem.Click += new System.EventHandler(this.OnSetWhiteKnight);
            // 
            // whiteBishopToolStripMenuItem
            // 
            this.whiteBishopToolStripMenuItem.Name = "whiteBishopToolStripMenuItem";
            this.whiteBishopToolStripMenuItem.Size = new System.Drawing.Size(110, 22);
            this.whiteBishopToolStripMenuItem.Text = "Bishop";
            this.whiteBishopToolStripMenuItem.Click += new System.EventHandler(this.OnSetWhiteBishop);
            // 
            // whiteRookToolStripMenuItem
            // 
            this.whiteRookToolStripMenuItem.Name = "whiteRookToolStripMenuItem";
            this.whiteRookToolStripMenuItem.Size = new System.Drawing.Size(110, 22);
            this.whiteRookToolStripMenuItem.Text = "Rook";
            this.whiteRookToolStripMenuItem.Click += new System.EventHandler(this.OnSetWhiteRook);
            // 
            // whiteQueenToolStripMenuItem
            // 
            this.whiteQueenToolStripMenuItem.Name = "whiteQueenToolStripMenuItem";
            this.whiteQueenToolStripMenuItem.Size = new System.Drawing.Size(110, 22);
            this.whiteQueenToolStripMenuItem.Text = "Queen";
            this.whiteQueenToolStripMenuItem.Click += new System.EventHandler(this.OnSetWhiteQueen);
            // 
            // whiteKingToolStripMenuItem
            // 
            this.whiteKingToolStripMenuItem.Name = "whiteKingToolStripMenuItem";
            this.whiteKingToolStripMenuItem.Size = new System.Drawing.Size(110, 22);
            this.whiteKingToolStripMenuItem.Text = "King";
            this.whiteKingToolStripMenuItem.Click += new System.EventHandler(this.OnSetWhiteKing);
            // 
            // blackToolStripMenuItem1
            // 
            this.blackToolStripMenuItem1.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.blackPawnToolStripMenuItem,
            this.blackKnightToolStripMenuItem,
            this.blackBishopToolStripMenuItem,
            this.blackRookToolStripMenuItem,
            this.blackQueenToolStripMenuItem,
            this.blackKingToolStripMenuItem});
            this.blackToolStripMenuItem1.Name = "blackToolStripMenuItem1";
            this.blackToolStripMenuItem1.Size = new System.Drawing.Size(168, 22);
            this.blackToolStripMenuItem1.Text = "Black";
            // 
            // blackPawnToolStripMenuItem
            // 
            this.blackPawnToolStripMenuItem.Name = "blackPawnToolStripMenuItem";
            this.blackPawnToolStripMenuItem.Size = new System.Drawing.Size(110, 22);
            this.blackPawnToolStripMenuItem.Text = "Pawn";
            this.blackPawnToolStripMenuItem.Click += new System.EventHandler(this.OnSetBlackPawn);
            // 
            // blackKnightToolStripMenuItem
            // 
            this.blackKnightToolStripMenuItem.Name = "blackKnightToolStripMenuItem";
            this.blackKnightToolStripMenuItem.Size = new System.Drawing.Size(110, 22);
            this.blackKnightToolStripMenuItem.Text = "Knight";
            this.blackKnightToolStripMenuItem.Click += new System.EventHandler(this.OnSetBlackKnight);
            // 
            // blackBishopToolStripMenuItem
            // 
            this.blackBishopToolStripMenuItem.Name = "blackBishopToolStripMenuItem";
            this.blackBishopToolStripMenuItem.Size = new System.Drawing.Size(110, 22);
            this.blackBishopToolStripMenuItem.Text = "Bishop";
            this.blackBishopToolStripMenuItem.Click += new System.EventHandler(this.OnSetBlackBishop);
            // 
            // blackRookToolStripMenuItem
            // 
            this.blackRookToolStripMenuItem.Name = "blackRookToolStripMenuItem";
            this.blackRookToolStripMenuItem.Size = new System.Drawing.Size(110, 22);
            this.blackRookToolStripMenuItem.Text = "Rook";
            this.blackRookToolStripMenuItem.Click += new System.EventHandler(this.OnSetBlackRook);
            // 
            // blackQueenToolStripMenuItem
            // 
            this.blackQueenToolStripMenuItem.Name = "blackQueenToolStripMenuItem";
            this.blackQueenToolStripMenuItem.Size = new System.Drawing.Size(110, 22);
            this.blackQueenToolStripMenuItem.Text = "Queen";
            this.blackQueenToolStripMenuItem.Click += new System.EventHandler(this.OnSetBlackQueen);
            // 
            // blackKingToolStripMenuItem
            // 
            this.blackKingToolStripMenuItem.Name = "blackKingToolStripMenuItem";
            this.blackKingToolStripMenuItem.Size = new System.Drawing.Size(110, 22);
            this.blackKingToolStripMenuItem.Text = "King";
            this.blackKingToolStripMenuItem.Click += new System.EventHandler(this.OnSetBlackKing);
            // 
            // clearEntireBoardToolStripMenuItem
            // 
            this.clearEntireBoardToolStripMenuItem.Name = "clearEntireBoardToolStripMenuItem";
            this.clearEntireBoardToolStripMenuItem.Size = new System.Drawing.Size(168, 22);
            this.clearEntireBoardToolStripMenuItem.Text = "Clear entire board";
            this.clearEntireBoardToolStripMenuItem.Click += new System.EventHandler(this.OnClearBoard);
            // 
            // stopThinkingButton
            // 
            this.stopThinkingButton.Location = new System.Drawing.Point(444, 1);
            this.stopThinkingButton.Name = "stopThinkingButton";
            this.stopThinkingButton.Size = new System.Drawing.Size(69, 23);
            this.stopThinkingButton.TabIndex = 18;
            this.stopThinkingButton.Text = "Move now!";
            this.stopThinkingButton.UseVisualStyleBackColor = true;
            this.stopThinkingButton.Click += new System.EventHandler(this.OnMoveNow);
            // 
            // switchSidesToolStripMenuItem
            // 
            this.switchSidesToolStripMenuItem.Name = "switchSidesToolStripMenuItem";
            this.switchSidesToolStripMenuItem.Size = new System.Drawing.Size(157, 22);
            this.switchSidesToolStripMenuItem.Text = "Switch sides";
            this.switchSidesToolStripMenuItem.Click += new System.EventHandler(this.OnSwitchSides);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(744, 641);
            this.Controls.Add(this.stopThinkingButton);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.verticalSplitContainer);
            this.Controls.Add(this.mainMenuStrip);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "MainForm";
            this.Text = "Stoopid Chess";
            ((System.ComponentModel.ISupportInitialize)(this.chessBoardPictureBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.captureBinPictureBox)).EndInit();
            this.verticalSplitContainer.Panel1.ResumeLayout(false);
            this.verticalSplitContainer.Panel2.ResumeLayout(false);
            this.verticalSplitContainer.ResumeLayout(false);
            this.boardCaptureSplitContainer.Panel1.ResumeLayout(false);
            this.boardCaptureSplitContainer.Panel2.ResumeLayout(false);
            this.boardCaptureSplitContainer.ResumeLayout(false);
            this.mainMenuStrip.ResumeLayout(false);
            this.mainMenuStrip.PerformLayout();
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.contextMenuStrip.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.PictureBox chessBoardPictureBox;
        private System.Windows.Forms.PictureBox captureBinPictureBox;
        private System.Windows.Forms.SplitContainer verticalSplitContainer;
        private System.Windows.Forms.MenuStrip mainMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem saveToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem loadToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem gameToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem undoLastMoveToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem setLevelToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem easyToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem mediumToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem hardToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem newGameToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem viewToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem flipBoardToolStripMenuItem;
        private System.Windows.Forms.SaveFileDialog saveFileDialog;
        private System.Windows.Forms.OpenFileDialog openFileDialog;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.SplitContainer boardCaptureSplitContainer;
        private System.Windows.Forms.ToolStripStatusLabel youArePlayingLabel;
        private System.Windows.Forms.ToolStripStatusLabel humanColourLabel;
        private System.Windows.Forms.ToolStripStatusLabel colourToMoveAnnotationLabel;
        private System.Windows.Forms.ToolStripStatusLabel colourToMoveLabel;
        private System.Windows.Forms.ToolStripStatusLabel timeElapsedLabel;
        private System.Windows.Forms.ToolStripMenuItem whiteToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem blackToolStripMenuItem;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem clearSquareToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem whiteToolStripMenuItem1;
        private System.Windows.Forms.ToolStripMenuItem whitePawnToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem whiteKnightToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem whiteBishopToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem whiteRookToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem whiteQueenToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem whiteKingToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem blackToolStripMenuItem1;
        private System.Windows.Forms.ToolStripMenuItem blackPawnToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem blackKnightToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem blackBishopToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem blackRookToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem blackQueenToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem blackKingToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem helpToolStripMenuItem;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel1;
        private System.Windows.Forms.ToolStripStatusLabel difficultyLabel;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel2;
        private System.Windows.Forms.ToolStripStatusLabel whiteClockLabel;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel3;
        private System.Windows.Forms.ToolStripStatusLabel blackClockLabel;
        private System.Windows.Forms.ToolStripMenuItem beginnerMenuItem;
        private System.Windows.Forms.ToolStripMenuItem clearEntireBoardToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem engineToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem engineVersusEngineToolStripMenuItem;
        private System.Windows.Forms.RichTextBox moveListTextBox;
        private System.Windows.Forms.ToolStripMenuItem bestslowToolStripMenuItem;
        private System.Windows.Forms.Button stopThinkingButton;
        private System.Windows.Forms.ToolStripMenuItem switchSidesToolStripMenuItem;
    }
}


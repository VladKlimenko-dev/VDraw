namespace VDraw
{
	partial class SettingsForm
	{
		/// <summary>Требуется конструктору. Не удалять.</summary>
		private System.ComponentModel.IContainer components = null;

		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}

			base.Dispose(disposing);
		}

		#region Код, автоматически созданный конструктором форм Windows

		private void InitializeComponent()
		{
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SettingsForm));
			tabs = new TabControl();
			tabView = new TabPage();
			chkShowRulers = new CheckBox();
			chkShowGuidelines = new CheckBox();
			chkShowGrid = new CheckBox();
			chkShowPrintMargins = new CheckBox();
			lblCanvas = new Label();
			colCanvas = new Button();
			lblPageBack = new Label();
			colPageBack = new Button();
			lblPageBorder = new Label();
			colPageBorder = new Button();
			lblPageShadow = new Label();
			colPageShadow = new Button();
			tabGrid = new TabPage();
			lblGridMinor = new Label();
			colGridMinor = new Button();
			lblGridMedium = new Label();
			colGridMedium = new Button();
			lblGridMajor = new Label();
			colGridMajor = new Button();
			lblGridZero = new Label();
			colGridZero = new Button();
			lblPrintBorder = new Label();
			colPrintBorder = new Button();
			tabRulers = new TabPage();
			lblRulerThickness = new Label();
			numRulerThickness = new NumericUpDown();
			lblRulerZeroMark = new Label();
			numRulerZeroMark = new NumericUpDown();
			lblRulerMajorMark = new Label();
			numRulerMajorMark = new NumericUpDown();
			lblRulerMinorMark = new Label();
			numRulerMinorMark = new NumericUpDown();
			tabSnapping = new TabPage();
			chkSnapObjects = new CheckBox();
			chkSnapGuidelines = new CheckBox();
			chkDynamicGuides = new CheckBox();
			lblSnapDistance = new Label();
			numSnapDistance = new NumericUpDown();
			lblSnapPointSize = new Label();
			numSnapPointSize = new NumericUpDown();
			lblSnapPoint = new Label();
			colSnapPoint = new Button();
			lblSnapLine = new Label();
			colSnapLine = new Button();
			tabShapes = new TabPage();
			lblShapeLine = new Label();
			colShapeLine = new Button();
			lblLineThickness = new Label();
			numLineThickness = new NumericUpDown();
			lblPatternLength = new Label();
			numPatternLength = new NumericUpDown();
			lblShapeFill = new Label();
			colShapeFill = new Button();
			tabHandles = new TabPage();
			lblHandleSize = new Label();
			numHandleSize = new NumericUpDown();
			lblHandleLineWidth = new Label();
			numHandleLineWidth = new NumericUpDown();
			lblHandleFill = new Label();
			colHandleFill = new Button();
			lblHandleLine = new Label();
			colHandleLine = new Button();
			lblGripRadius = new Label();
			numGripRadius = new NumericUpDown();
			lblCurvePointSize = new Label();
			numCurvePointSize = new NumericUpDown();
			lblCurveCpSize = new Label();
			numCurveCpSize = new NumericUpDown();
			cancelButton = new Button();
			applyButton = new Button();
			resetButton = new Button();
			tabs.SuspendLayout();
			tabView.SuspendLayout();
			tabGrid.SuspendLayout();
			tabRulers.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)numRulerThickness).BeginInit();
			((System.ComponentModel.ISupportInitialize)numRulerZeroMark).BeginInit();
			((System.ComponentModel.ISupportInitialize)numRulerMajorMark).BeginInit();
			((System.ComponentModel.ISupportInitialize)numRulerMinorMark).BeginInit();
			tabSnapping.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)numSnapDistance).BeginInit();
			((System.ComponentModel.ISupportInitialize)numSnapPointSize).BeginInit();
			tabShapes.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)numLineThickness).BeginInit();
			((System.ComponentModel.ISupportInitialize)numPatternLength).BeginInit();
			tabHandles.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)numHandleSize).BeginInit();
			((System.ComponentModel.ISupportInitialize)numHandleLineWidth).BeginInit();
			((System.ComponentModel.ISupportInitialize)numGripRadius).BeginInit();
			((System.ComponentModel.ISupportInitialize)numCurvePointSize).BeginInit();
			((System.ComponentModel.ISupportInitialize)numCurveCpSize).BeginInit();
			SuspendLayout();
			// 
			// tabs
			// 
			tabs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			tabs.Controls.Add(tabView);
			tabs.Controls.Add(tabGrid);
			tabs.Controls.Add(tabRulers);
			tabs.Controls.Add(tabSnapping);
			tabs.Controls.Add(tabShapes);
			tabs.Controls.Add(tabHandles);
			tabs.Location = new Point(12, 12);
			tabs.Name = "tabs";
			tabs.SelectedIndex = 0;
			tabs.Size = new Size(560, 319);
			tabs.TabIndex = 0;
			// 
			// tabView
			// 
			tabView.Controls.Add(chkShowRulers);
			tabView.Controls.Add(chkShowGuidelines);
			tabView.Controls.Add(chkShowGrid);
			tabView.Controls.Add(chkShowPrintMargins);
			tabView.Controls.Add(lblCanvas);
			tabView.Controls.Add(colCanvas);
			tabView.Controls.Add(lblPageBack);
			tabView.Controls.Add(colPageBack);
			tabView.Controls.Add(lblPageBorder);
			tabView.Controls.Add(colPageBorder);
			tabView.Controls.Add(lblPageShadow);
			tabView.Controls.Add(colPageShadow);
			tabView.Location = new Point(4, 29);
			tabView.Name = "tabView";
			tabView.Padding = new Padding(3);
			tabView.Size = new Size(552, 286);
			tabView.TabIndex = 0;
			tabView.Text = "View";
			tabView.UseVisualStyleBackColor = true;
			// 
			// chkShowRulers
			// 
			chkShowRulers.AutoSize = true;
			chkShowRulers.Location = new Point(15, 15);
			chkShowRulers.Name = "chkShowRulers";
			chkShowRulers.Size = new Size(107, 24);
			chkShowRulers.TabIndex = 3;
			chkShowRulers.Text = "Show rulers";
			chkShowRulers.UseVisualStyleBackColor = true;
			// 
			// chkShowGuidelines
			// 
			chkShowGuidelines.AutoSize = true;
			chkShowGuidelines.Location = new Point(15, 47);
			chkShowGuidelines.Name = "chkShowGuidelines";
			chkShowGuidelines.Size = new Size(139, 24);
			chkShowGuidelines.TabIndex = 4;
			chkShowGuidelines.Text = "Show guidelines";
			chkShowGuidelines.UseVisualStyleBackColor = true;
			// 
			// chkShowGrid
			// 
			chkShowGrid.AutoSize = true;
			chkShowGrid.Location = new Point(15, 79);
			chkShowGrid.Name = "chkShowGrid";
			chkShowGrid.Size = new Size(98, 24);
			chkShowGrid.TabIndex = 5;
			chkShowGrid.Text = "Show grid";
			chkShowGrid.UseVisualStyleBackColor = true;
			// 
			// chkShowPrintMargins
			// 
			chkShowPrintMargins.AutoSize = true;
			chkShowPrintMargins.Location = new Point(15, 111);
			chkShowPrintMargins.Name = "chkShowPrintMargins";
			chkShowPrintMargins.Size = new Size(159, 24);
			chkShowPrintMargins.TabIndex = 6;
			chkShowPrintMargins.Text = "Show print margins";
			chkShowPrintMargins.UseVisualStyleBackColor = true;
			// 
			// lblCanvas
			// 
			lblCanvas.AutoSize = true;
			lblCanvas.Location = new Point(15, 145);
			lblCanvas.Name = "lblCanvas";
			lblCanvas.Size = new Size(138, 20);
			lblCanvas.TabIndex = 7;
			lblCanvas.Text = "Canvas background";
			// 
			// colCanvas
			// 
			colCanvas.BackColor = Color.White;
			colCanvas.FlatStyle = FlatStyle.Flat;
			colCanvas.Location = new Point(249, 141);
			colCanvas.Name = "colCanvas";
			colCanvas.Size = new Size(90, 26);
			colCanvas.TabIndex = 8;
			colCanvas.UseVisualStyleBackColor = false;
			// 
			// lblPageBack
			// 
			lblPageBack.AutoSize = true;
			lblPageBack.Location = new Point(15, 177);
			lblPageBack.Name = "lblPageBack";
			lblPageBack.Size = new Size(124, 20);
			lblPageBack.TabIndex = 9;
			lblPageBack.Text = "Page background";
			// 
			// colPageBack
			// 
			colPageBack.BackColor = Color.White;
			colPageBack.FlatStyle = FlatStyle.Flat;
			colPageBack.Location = new Point(249, 173);
			colPageBack.Name = "colPageBack";
			colPageBack.Size = new Size(90, 26);
			colPageBack.TabIndex = 10;
			colPageBack.UseVisualStyleBackColor = false;
			// 
			// lblPageBorder
			// 
			lblPageBorder.AutoSize = true;
			lblPageBorder.Location = new Point(15, 209);
			lblPageBorder.Name = "lblPageBorder";
			lblPageBorder.Size = new Size(90, 20);
			lblPageBorder.TabIndex = 11;
			lblPageBorder.Text = "Page border";
			// 
			// colPageBorder
			// 
			colPageBorder.BackColor = Color.White;
			colPageBorder.FlatStyle = FlatStyle.Flat;
			colPageBorder.Location = new Point(249, 205);
			colPageBorder.Name = "colPageBorder";
			colPageBorder.Size = new Size(90, 26);
			colPageBorder.TabIndex = 12;
			colPageBorder.UseVisualStyleBackColor = false;
			// 
			// lblPageShadow
			// 
			lblPageShadow.AutoSize = true;
			lblPageShadow.Location = new Point(15, 241);
			lblPageShadow.Name = "lblPageShadow";
			lblPageShadow.Size = new Size(96, 20);
			lblPageShadow.TabIndex = 13;
			lblPageShadow.Text = "Page shadow";
			// 
			// colPageShadow
			// 
			colPageShadow.BackColor = Color.White;
			colPageShadow.FlatStyle = FlatStyle.Flat;
			colPageShadow.Location = new Point(249, 237);
			colPageShadow.Name = "colPageShadow";
			colPageShadow.Size = new Size(90, 26);
			colPageShadow.TabIndex = 14;
			colPageShadow.UseVisualStyleBackColor = false;
			// 
			// tabGrid
			// 
			tabGrid.Controls.Add(lblGridMinor);
			tabGrid.Controls.Add(colGridMinor);
			tabGrid.Controls.Add(lblGridMedium);
			tabGrid.Controls.Add(colGridMedium);
			tabGrid.Controls.Add(lblGridMajor);
			tabGrid.Controls.Add(colGridMajor);
			tabGrid.Controls.Add(lblGridZero);
			tabGrid.Controls.Add(colGridZero);
			tabGrid.Controls.Add(lblPrintBorder);
			tabGrid.Controls.Add(colPrintBorder);
			tabGrid.Location = new Point(4, 29);
			tabGrid.Name = "tabGrid";
			tabGrid.Padding = new Padding(3);
			tabGrid.Size = new Size(552, 286);
			tabGrid.TabIndex = 1;
			tabGrid.Text = "Grid";
			tabGrid.UseVisualStyleBackColor = true;
			// 
			// lblGridMinor
			// 
			lblGridMinor.AutoSize = true;
			lblGridMinor.Location = new Point(16, 22);
			lblGridMinor.Name = "lblGridMinor";
			lblGridMinor.Size = new Size(82, 20);
			lblGridMinor.TabIndex = 15;
			lblGridMinor.Text = "Minor lines";
			// 
			// colGridMinor
			// 
			colGridMinor.BackColor = Color.White;
			colGridMinor.FlatStyle = FlatStyle.Flat;
			colGridMinor.Location = new Point(250, 18);
			colGridMinor.Name = "colGridMinor";
			colGridMinor.Size = new Size(90, 26);
			colGridMinor.TabIndex = 16;
			colGridMinor.UseVisualStyleBackColor = false;
			// 
			// lblGridMedium
			// 
			lblGridMedium.AutoSize = true;
			lblGridMedium.Location = new Point(16, 54);
			lblGridMedium.Name = "lblGridMedium";
			lblGridMedium.Size = new Size(103, 20);
			lblGridMedium.TabIndex = 17;
			lblGridMedium.Text = "Every fifth line";
			// 
			// colGridMedium
			// 
			colGridMedium.BackColor = Color.White;
			colGridMedium.FlatStyle = FlatStyle.Flat;
			colGridMedium.Location = new Point(250, 50);
			colGridMedium.Name = "colGridMedium";
			colGridMedium.Size = new Size(90, 26);
			colGridMedium.TabIndex = 18;
			colGridMedium.UseVisualStyleBackColor = false;
			// 
			// lblGridMajor
			// 
			lblGridMajor.AutoSize = true;
			lblGridMajor.Location = new Point(16, 86);
			lblGridMajor.Name = "lblGridMajor";
			lblGridMajor.Size = new Size(110, 20);
			lblGridMajor.TabIndex = 19;
			lblGridMajor.Text = "Every tenth line";
			// 
			// colGridMajor
			// 
			colGridMajor.BackColor = Color.White;
			colGridMajor.FlatStyle = FlatStyle.Flat;
			colGridMajor.Location = new Point(250, 82);
			colGridMajor.Name = "colGridMajor";
			colGridMajor.Size = new Size(90, 26);
			colGridMajor.TabIndex = 20;
			colGridMajor.UseVisualStyleBackColor = false;
			// 
			// lblGridZero
			// 
			lblGridZero.AutoSize = true;
			lblGridZero.Location = new Point(16, 118);
			lblGridZero.Name = "lblGridZero";
			lblGridZero.Size = new Size(83, 20);
			lblGridZero.TabIndex = 21;
			lblGridZero.Text = "Origin axes";
			// 
			// colGridZero
			// 
			colGridZero.BackColor = Color.White;
			colGridZero.FlatStyle = FlatStyle.Flat;
			colGridZero.Location = new Point(250, 114);
			colGridZero.Name = "colGridZero";
			colGridZero.Size = new Size(90, 26);
			colGridZero.TabIndex = 22;
			colGridZero.UseVisualStyleBackColor = false;
			// 
			// lblPrintBorder
			// 
			lblPrintBorder.AutoSize = true;
			lblPrintBorder.Location = new Point(16, 150);
			lblPrintBorder.Name = "lblPrintBorder";
			lblPrintBorder.Size = new Size(150, 20);
			lblPrintBorder.TabIndex = 23;
			lblPrintBorder.Text = "Printable area border";
			// 
			// colPrintBorder
			// 
			colPrintBorder.BackColor = Color.White;
			colPrintBorder.FlatStyle = FlatStyle.Flat;
			colPrintBorder.Location = new Point(250, 146);
			colPrintBorder.Name = "colPrintBorder";
			colPrintBorder.Size = new Size(90, 26);
			colPrintBorder.TabIndex = 24;
			colPrintBorder.UseVisualStyleBackColor = false;
			// 
			// tabRulers
			// 
			tabRulers.Controls.Add(lblRulerThickness);
			tabRulers.Controls.Add(numRulerThickness);
			tabRulers.Controls.Add(lblRulerZeroMark);
			tabRulers.Controls.Add(numRulerZeroMark);
			tabRulers.Controls.Add(lblRulerMajorMark);
			tabRulers.Controls.Add(numRulerMajorMark);
			tabRulers.Controls.Add(lblRulerMinorMark);
			tabRulers.Controls.Add(numRulerMinorMark);
			tabRulers.Location = new Point(4, 29);
			tabRulers.Name = "tabRulers";
			tabRulers.Padding = new Padding(3);
			tabRulers.Size = new Size(552, 286);
			tabRulers.TabIndex = 2;
			tabRulers.Text = "Rulers";
			tabRulers.UseVisualStyleBackColor = true;
			// 
			// lblRulerThickness
			// 
			lblRulerThickness.AutoSize = true;
			lblRulerThickness.Location = new Point(16, 22);
			lblRulerThickness.Name = "lblRulerThickness";
			lblRulerThickness.Size = new Size(106, 20);
			lblRulerThickness.TabIndex = 25;
			lblRulerThickness.Text = "Ruler thickness";
			// 
			// numRulerThickness
			// 
			numRulerThickness.Location = new Point(250, 18);
			numRulerThickness.Maximum = new decimal(new int[] { 80, 0, 0, 0 });
			numRulerThickness.Minimum = new decimal(new int[] { 8, 0, 0, 0 });
			numRulerThickness.Name = "numRulerThickness";
			numRulerThickness.Size = new Size(90, 27);
			numRulerThickness.TabIndex = 26;
			numRulerThickness.Value = new decimal(new int[] { 8, 0, 0, 0 });
			// 
			// lblRulerZeroMark
			// 
			lblRulerZeroMark.AutoSize = true;
			lblRulerZeroMark.Location = new Point(16, 54);
			lblRulerZeroMark.Name = "lblRulerZeroMark";
			lblRulerZeroMark.Size = new Size(133, 20);
			lblRulerZeroMark.TabIndex = 27;
			lblRulerZeroMark.Text = "Origin mark length";
			// 
			// numRulerZeroMark
			// 
			numRulerZeroMark.Location = new Point(250, 50);
			numRulerZeroMark.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
			numRulerZeroMark.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
			numRulerZeroMark.Name = "numRulerZeroMark";
			numRulerZeroMark.Size = new Size(90, 27);
			numRulerZeroMark.TabIndex = 28;
			numRulerZeroMark.Value = new decimal(new int[] { 2, 0, 0, 0 });
			// 
			// lblRulerMajorMark
			// 
			lblRulerMajorMark.AutoSize = true;
			lblRulerMajorMark.Location = new Point(16, 86);
			lblRulerMajorMark.Name = "lblRulerMajorMark";
			lblRulerMajorMark.Size = new Size(131, 20);
			lblRulerMajorMark.TabIndex = 29;
			lblRulerMajorMark.Text = "Major mark length";
			// 
			// numRulerMajorMark
			// 
			numRulerMajorMark.Location = new Point(250, 82);
			numRulerMajorMark.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
			numRulerMajorMark.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
			numRulerMajorMark.Name = "numRulerMajorMark";
			numRulerMajorMark.Size = new Size(90, 27);
			numRulerMajorMark.TabIndex = 30;
			numRulerMajorMark.Value = new decimal(new int[] { 2, 0, 0, 0 });
			// 
			// lblRulerMinorMark
			// 
			lblRulerMinorMark.AutoSize = true;
			lblRulerMinorMark.Location = new Point(16, 118);
			lblRulerMinorMark.Name = "lblRulerMinorMark";
			lblRulerMinorMark.Size = new Size(131, 20);
			lblRulerMinorMark.TabIndex = 31;
			lblRulerMinorMark.Text = "Minor mark length";
			// 
			// numRulerMinorMark
			// 
			numRulerMinorMark.Location = new Point(250, 114);
			numRulerMinorMark.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
			numRulerMinorMark.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			numRulerMinorMark.Name = "numRulerMinorMark";
			numRulerMinorMark.Size = new Size(90, 27);
			numRulerMinorMark.TabIndex = 32;
			numRulerMinorMark.Value = new decimal(new int[] { 1, 0, 0, 0 });
			// 
			// tabSnapping
			// 
			tabSnapping.Controls.Add(chkSnapObjects);
			tabSnapping.Controls.Add(chkSnapGuidelines);
			tabSnapping.Controls.Add(chkDynamicGuides);
			tabSnapping.Controls.Add(lblSnapDistance);
			tabSnapping.Controls.Add(numSnapDistance);
			tabSnapping.Controls.Add(lblSnapPointSize);
			tabSnapping.Controls.Add(numSnapPointSize);
			tabSnapping.Controls.Add(lblSnapPoint);
			tabSnapping.Controls.Add(colSnapPoint);
			tabSnapping.Controls.Add(lblSnapLine);
			tabSnapping.Controls.Add(colSnapLine);
			tabSnapping.Location = new Point(4, 29);
			tabSnapping.Name = "tabSnapping";
			tabSnapping.Padding = new Padding(3);
			tabSnapping.Size = new Size(552, 286);
			tabSnapping.TabIndex = 3;
			tabSnapping.Text = "Snapping";
			tabSnapping.UseVisualStyleBackColor = true;
			// 
			// chkSnapObjects
			// 
			chkSnapObjects.AutoSize = true;
			chkSnapObjects.Location = new Point(16, 20);
			chkSnapObjects.Name = "chkSnapObjects";
			chkSnapObjects.Size = new Size(134, 24);
			chkSnapObjects.TabIndex = 33;
			chkSnapObjects.Text = "Snap to objects";
			chkSnapObjects.UseVisualStyleBackColor = true;
			// 
			// chkSnapGuidelines
			// 
			chkSnapGuidelines.AutoSize = true;
			chkSnapGuidelines.Location = new Point(16, 52);
			chkSnapGuidelines.Name = "chkSnapGuidelines";
			chkSnapGuidelines.Size = new Size(154, 24);
			chkSnapGuidelines.TabIndex = 34;
			chkSnapGuidelines.Text = "Snap to guidelines";
			chkSnapGuidelines.UseVisualStyleBackColor = true;
			// 
			// chkDynamicGuides
			// 
			chkDynamicGuides.AutoSize = true;
			chkDynamicGuides.Location = new Point(16, 84);
			chkDynamicGuides.Name = "chkDynamicGuides";
			chkDynamicGuides.Size = new Size(137, 24);
			chkDynamicGuides.TabIndex = 35;
			chkDynamicGuides.Text = "Dynamic guides";
			chkDynamicGuides.UseVisualStyleBackColor = true;
			// 
			// lblSnapDistance
			// 
			lblSnapDistance.AutoSize = true;
			lblSnapDistance.Location = new Point(16, 118);
			lblSnapDistance.Name = "lblSnapDistance";
			lblSnapDistance.Size = new Size(131, 20);
			lblSnapDistance.TabIndex = 36;
			lblSnapDistance.Text = "Snap distance (px)";
			// 
			// numSnapDistance
			// 
			numSnapDistance.Location = new Point(250, 114);
			numSnapDistance.Maximum = new decimal(new int[] { 200, 0, 0, 0 });
			numSnapDistance.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			numSnapDistance.Name = "numSnapDistance";
			numSnapDistance.Size = new Size(90, 27);
			numSnapDistance.TabIndex = 37;
			numSnapDistance.Value = new decimal(new int[] { 1, 0, 0, 0 });
			// 
			// lblSnapPointSize
			// 
			lblSnapPointSize.AutoSize = true;
			lblSnapPointSize.Location = new Point(16, 150);
			lblSnapPointSize.Name = "lblSnapPointSize";
			lblSnapPointSize.Size = new Size(121, 20);
			lblSnapPointSize.TabIndex = 38;
			lblSnapPointSize.Text = "Snap marker size";
			// 
			// numSnapPointSize
			// 
			numSnapPointSize.Location = new Point(250, 146);
			numSnapPointSize.Maximum = new decimal(new int[] { 40, 0, 0, 0 });
			numSnapPointSize.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			numSnapPointSize.Name = "numSnapPointSize";
			numSnapPointSize.Size = new Size(90, 27);
			numSnapPointSize.TabIndex = 39;
			numSnapPointSize.Value = new decimal(new int[] { 1, 0, 0, 0 });
			// 
			// lblSnapPoint
			// 
			lblSnapPoint.AutoSize = true;
			lblSnapPoint.Location = new Point(16, 182);
			lblSnapPoint.Name = "lblSnapPoint";
			lblSnapPoint.Size = new Size(138, 20);
			lblSnapPoint.TabIndex = 40;
			lblSnapPoint.Text = "Snap marker colour";
			// 
			// colSnapPoint
			// 
			colSnapPoint.BackColor = Color.White;
			colSnapPoint.FlatStyle = FlatStyle.Flat;
			colSnapPoint.Location = new Point(250, 178);
			colSnapPoint.Name = "colSnapPoint";
			colSnapPoint.Size = new Size(90, 26);
			colSnapPoint.TabIndex = 41;
			colSnapPoint.UseVisualStyleBackColor = false;
			// 
			// lblSnapLine
			// 
			lblSnapLine.AutoSize = true;
			lblSnapLine.Location = new Point(16, 214);
			lblSnapLine.Name = "lblSnapLine";
			lblSnapLine.Size = new Size(116, 20);
			lblSnapLine.TabIndex = 42;
			lblSnapLine.Text = "Snap line colour";
			// 
			// colSnapLine
			// 
			colSnapLine.BackColor = Color.White;
			colSnapLine.FlatStyle = FlatStyle.Flat;
			colSnapLine.Location = new Point(250, 210);
			colSnapLine.Name = "colSnapLine";
			colSnapLine.Size = new Size(90, 26);
			colSnapLine.TabIndex = 43;
			colSnapLine.UseVisualStyleBackColor = false;
			// 
			// tabShapes
			// 
			tabShapes.Controls.Add(lblShapeLine);
			tabShapes.Controls.Add(colShapeLine);
			tabShapes.Controls.Add(lblLineThickness);
			tabShapes.Controls.Add(numLineThickness);
			tabShapes.Controls.Add(lblPatternLength);
			tabShapes.Controls.Add(numPatternLength);
			tabShapes.Controls.Add(lblShapeFill);
			tabShapes.Controls.Add(colShapeFill);
			tabShapes.Location = new Point(4, 29);
			tabShapes.Name = "tabShapes";
			tabShapes.Padding = new Padding(3);
			tabShapes.Size = new Size(552, 286);
			tabShapes.TabIndex = 4;
			tabShapes.Text = "Shape defaults";
			tabShapes.UseVisualStyleBackColor = true;
			// 
			// lblShapeLine
			// 
			lblShapeLine.AutoSize = true;
			lblShapeLine.Location = new Point(16, 22);
			lblShapeLine.Name = "lblShapeLine";
			lblShapeLine.Size = new Size(154, 20);
			lblShapeLine.TabIndex = 44;
			lblShapeLine.Text = "Default outline colour";
			// 
			// colShapeLine
			// 
			colShapeLine.BackColor = Color.White;
			colShapeLine.FlatStyle = FlatStyle.Flat;
			colShapeLine.Location = new Point(250, 18);
			colShapeLine.Name = "colShapeLine";
			colShapeLine.Size = new Size(90, 26);
			colShapeLine.TabIndex = 45;
			colShapeLine.UseVisualStyleBackColor = false;
			// 
			// lblLineThickness
			// 
			lblLineThickness.AutoSize = true;
			lblLineThickness.Location = new Point(16, 54);
			lblLineThickness.Name = "lblLineThickness";
			lblLineThickness.Size = new Size(189, 20);
			lblLineThickness.TabIndex = 46;
			lblLineThickness.Text = "Default outline width (mm)";
			// 
			// numLineThickness
			// 
			numLineThickness.DecimalPlaces = 2;
			numLineThickness.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
			numLineThickness.Location = new Point(250, 50);
			numLineThickness.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
			numLineThickness.Name = "numLineThickness";
			numLineThickness.Size = new Size(90, 27);
			numLineThickness.TabIndex = 47;
			// 
			// lblPatternLength
			// 
			lblPatternLength.AutoSize = true;
			lblPatternLength.Location = new Point(16, 86);
			lblPatternLength.Name = "lblPatternLength";
			lblPatternLength.Size = new Size(170, 20);
			lblPatternLength.TabIndex = 48;
			lblPatternLength.Text = "Default dash cycle (mm)";
			// 
			// numPatternLength
			// 
			numPatternLength.DecimalPlaces = 1;
			numPatternLength.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
			numPatternLength.Location = new Point(250, 82);
			numPatternLength.Maximum = new decimal(new int[] { 200, 0, 0, 0 });
			numPatternLength.Name = "numPatternLength";
			numPatternLength.Size = new Size(90, 27);
			numPatternLength.TabIndex = 49;
			// 
			// lblShapeFill
			// 
			lblShapeFill.AutoSize = true;
			lblShapeFill.Location = new Point(16, 118);
			lblShapeFill.Name = "lblShapeFill";
			lblShapeFill.Size = new Size(125, 20);
			lblShapeFill.TabIndex = 50;
			lblShapeFill.Text = "Default fill colour";
			// 
			// colShapeFill
			// 
			colShapeFill.BackColor = Color.White;
			colShapeFill.FlatStyle = FlatStyle.Flat;
			colShapeFill.Location = new Point(250, 114);
			colShapeFill.Name = "colShapeFill";
			colShapeFill.Size = new Size(90, 26);
			colShapeFill.TabIndex = 51;
			colShapeFill.UseVisualStyleBackColor = false;
			// 
			// tabHandles
			// 
			tabHandles.Controls.Add(lblHandleSize);
			tabHandles.Controls.Add(numHandleSize);
			tabHandles.Controls.Add(lblHandleLineWidth);
			tabHandles.Controls.Add(numHandleLineWidth);
			tabHandles.Controls.Add(lblHandleFill);
			tabHandles.Controls.Add(colHandleFill);
			tabHandles.Controls.Add(lblHandleLine);
			tabHandles.Controls.Add(colHandleLine);
			tabHandles.Controls.Add(lblGripRadius);
			tabHandles.Controls.Add(numGripRadius);
			tabHandles.Controls.Add(lblCurvePointSize);
			tabHandles.Controls.Add(numCurvePointSize);
			tabHandles.Controls.Add(lblCurveCpSize);
			tabHandles.Controls.Add(numCurveCpSize);
			tabHandles.Location = new Point(4, 29);
			tabHandles.Name = "tabHandles";
			tabHandles.Padding = new Padding(3);
			tabHandles.Size = new Size(552, 286);
			tabHandles.TabIndex = 5;
			tabHandles.Text = "Handles";
			tabHandles.UseVisualStyleBackColor = true;
			// 
			// lblHandleSize
			// 
			lblHandleSize.AutoSize = true;
			lblHandleSize.Location = new Point(16, 22);
			lblHandleSize.Name = "lblHandleSize";
			lblHandleSize.Size = new Size(86, 20);
			lblHandleSize.TabIndex = 52;
			lblHandleSize.Text = "Handle size";
			// 
			// numHandleSize
			// 
			numHandleSize.Location = new Point(250, 18);
			numHandleSize.Maximum = new decimal(new int[] { 40, 0, 0, 0 });
			numHandleSize.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
			numHandleSize.Name = "numHandleSize";
			numHandleSize.Size = new Size(90, 27);
			numHandleSize.TabIndex = 53;
			numHandleSize.Value = new decimal(new int[] { 2, 0, 0, 0 });
			// 
			// lblHandleLineWidth
			// 
			lblHandleLineWidth.AutoSize = true;
			lblHandleLineWidth.Location = new Point(16, 54);
			lblHandleLineWidth.Name = "lblHandleLineWidth";
			lblHandleLineWidth.Size = new Size(148, 20);
			lblHandleLineWidth.TabIndex = 54;
			lblHandleLineWidth.Text = "Handle outline width";
			// 
			// numHandleLineWidth
			// 
			numHandleLineWidth.DecimalPlaces = 1;
			numHandleLineWidth.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
			numHandleLineWidth.Location = new Point(250, 50);
			numHandleLineWidth.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
			numHandleLineWidth.Name = "numHandleLineWidth";
			numHandleLineWidth.Size = new Size(90, 27);
			numHandleLineWidth.TabIndex = 55;
			// 
			// lblHandleFill
			// 
			lblHandleFill.AutoSize = true;
			lblHandleFill.Location = new Point(16, 86);
			lblHandleFill.Name = "lblHandleFill";
			lblHandleFill.Size = new Size(78, 20);
			lblHandleFill.TabIndex = 56;
			lblHandleFill.Text = "Handle fill";
			// 
			// colHandleFill
			// 
			colHandleFill.BackColor = Color.White;
			colHandleFill.FlatStyle = FlatStyle.Flat;
			colHandleFill.Location = new Point(250, 82);
			colHandleFill.Name = "colHandleFill";
			colHandleFill.Size = new Size(90, 26);
			colHandleFill.TabIndex = 57;
			colHandleFill.UseVisualStyleBackColor = false;
			// 
			// lblHandleLine
			// 
			lblHandleLine.AutoSize = true;
			lblHandleLine.Location = new Point(16, 118);
			lblHandleLine.Name = "lblHandleLine";
			lblHandleLine.Size = new Size(107, 20);
			lblHandleLine.TabIndex = 58;
			lblHandleLine.Text = "Handle outline";
			// 
			// colHandleLine
			// 
			colHandleLine.BackColor = Color.White;
			colHandleLine.FlatStyle = FlatStyle.Flat;
			colHandleLine.Location = new Point(250, 114);
			colHandleLine.Name = "colHandleLine";
			colHandleLine.Size = new Size(90, 26);
			colHandleLine.TabIndex = 59;
			colHandleLine.UseVisualStyleBackColor = false;
			// 
			// lblGripRadius
			// 
			lblGripRadius.AutoSize = true;
			lblGripRadius.Location = new Point(16, 150);
			lblGripRadius.Name = "lblGripRadius";
			lblGripRadius.Size = new Size(115, 20);
			lblGripRadius.TabIndex = 60;
			lblGripRadius.Text = "Grab radius (px)";
			// 
			// numGripRadius
			// 
			numGripRadius.Location = new Point(250, 146);
			numGripRadius.Maximum = new decimal(new int[] { 80, 0, 0, 0 });
			numGripRadius.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
			numGripRadius.Name = "numGripRadius";
			numGripRadius.Size = new Size(90, 27);
			numGripRadius.TabIndex = 61;
			numGripRadius.Value = new decimal(new int[] { 2, 0, 0, 0 });
			// 
			// lblCurvePointSize
			// 
			lblCurvePointSize.AutoSize = true;
			lblCurvePointSize.Location = new Point(16, 182);
			lblCurvePointSize.Name = "lblCurvePointSize";
			lblCurvePointSize.Size = new Size(113, 20);
			lblCurvePointSize.TabIndex = 62;
			lblCurvePointSize.Text = "Curve node size";
			// 
			// numCurvePointSize
			// 
			numCurvePointSize.Location = new Point(250, 178);
			numCurvePointSize.Maximum = new decimal(new int[] { 40, 0, 0, 0 });
			numCurvePointSize.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
			numCurvePointSize.Name = "numCurvePointSize";
			numCurvePointSize.Size = new Size(90, 27);
			numCurvePointSize.TabIndex = 63;
			numCurvePointSize.Value = new decimal(new int[] { 2, 0, 0, 0 });
			// 
			// lblCurveCpSize
			// 
			lblCurveCpSize.AutoSize = true;
			lblCurveCpSize.Location = new Point(16, 214);
			lblCurveCpSize.Name = "lblCurveCpSize";
			lblCurveCpSize.Size = new Size(126, 20);
			lblCurveCpSize.TabIndex = 64;
			lblCurveCpSize.Text = "Control point size";
			// 
			// numCurveCpSize
			// 
			numCurveCpSize.Location = new Point(250, 210);
			numCurveCpSize.Maximum = new decimal(new int[] { 40, 0, 0, 0 });
			numCurveCpSize.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
			numCurveCpSize.Name = "numCurveCpSize";
			numCurveCpSize.Size = new Size(90, 27);
			numCurveCpSize.TabIndex = 65;
			numCurveCpSize.Value = new decimal(new int[] { 2, 0, 0, 0 });
			// 
			// cancelButton
			// 
			cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
			cancelButton.DialogResult = DialogResult.Cancel;
			cancelButton.Location = new Point(350, 337);
			cancelButton.Name = "cancelButton";
			cancelButton.Size = new Size(108, 30);
			cancelButton.TabIndex = 68;
			cancelButton.Text = "Cancel";
			cancelButton.UseVisualStyleBackColor = true;
			// 
			// applyButton
			// 
			applyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
			applyButton.DialogResult = DialogResult.OK;
			applyButton.Location = new Point(464, 337);
			applyButton.Name = "applyButton";
			applyButton.Size = new Size(108, 30);
			applyButton.TabIndex = 69;
			applyButton.Text = "Apply";
			applyButton.UseVisualStyleBackColor = true;
			applyButton.Click += applyButton_Click;
			// 
			// resetButton
			// 
			resetButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
			resetButton.Location = new Point(12, 337);
			resetButton.Name = "resetButton";
			resetButton.Size = new Size(150, 30);
			resetButton.TabIndex = 66;
			resetButton.Text = "Restore defaults";
			resetButton.UseVisualStyleBackColor = true;
			// 
			// SettingsForm
			// 
			AutoScaleDimensions = new SizeF(8F, 20F);
			AutoScaleMode = AutoScaleMode.Font;
			CancelButton = cancelButton;
			ClientSize = new Size(584, 379);
			Controls.Add(tabs);
			Controls.Add(resetButton);
			Controls.Add(cancelButton);
			Controls.Add(applyButton);
			Icon = (Icon)resources.GetObject("$this.Icon");
			MaximizeBox = false;
			MinimizeBox = false;
			Name = "SettingsForm";
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			Text = "Settings";
			tabs.ResumeLayout(false);
			tabView.ResumeLayout(false);
			tabView.PerformLayout();
			tabGrid.ResumeLayout(false);
			tabGrid.PerformLayout();
			tabRulers.ResumeLayout(false);
			tabRulers.PerformLayout();
			((System.ComponentModel.ISupportInitialize)numRulerThickness).EndInit();
			((System.ComponentModel.ISupportInitialize)numRulerZeroMark).EndInit();
			((System.ComponentModel.ISupportInitialize)numRulerMajorMark).EndInit();
			((System.ComponentModel.ISupportInitialize)numRulerMinorMark).EndInit();
			tabSnapping.ResumeLayout(false);
			tabSnapping.PerformLayout();
			((System.ComponentModel.ISupportInitialize)numSnapDistance).EndInit();
			((System.ComponentModel.ISupportInitialize)numSnapPointSize).EndInit();
			tabShapes.ResumeLayout(false);
			tabShapes.PerformLayout();
			((System.ComponentModel.ISupportInitialize)numLineThickness).EndInit();
			((System.ComponentModel.ISupportInitialize)numPatternLength).EndInit();
			tabHandles.ResumeLayout(false);
			tabHandles.PerformLayout();
			((System.ComponentModel.ISupportInitialize)numHandleSize).EndInit();
			((System.ComponentModel.ISupportInitialize)numHandleLineWidth).EndInit();
			((System.ComponentModel.ISupportInitialize)numGripRadius).EndInit();
			((System.ComponentModel.ISupportInitialize)numCurvePointSize).EndInit();
			((System.ComponentModel.ISupportInitialize)numCurveCpSize).EndInit();
			ResumeLayout(false);
		}

		#endregion

		private TabControl tabs;
		private TabPage tabView;
		private TabPage tabGrid;
		private TabPage tabRulers;
		private TabPage tabSnapping;
		private TabPage tabShapes;
		private TabPage tabHandles;
		private CheckBox chkShowRulers;
		private CheckBox chkShowGuidelines;
		private CheckBox chkShowGrid;
		private CheckBox chkShowPrintMargins;
		private Label lblCanvas;
		private Button colCanvas;
		private Label lblPageBack;
		private Button colPageBack;
		private Label lblPageBorder;
		private Button colPageBorder;
		private Label lblPageShadow;
		private Button colPageShadow;
		private Label lblGridMinor;
		private Button colGridMinor;
		private Label lblGridMedium;
		private Button colGridMedium;
		private Label lblGridMajor;
		private Button colGridMajor;
		private Label lblGridZero;
		private Button colGridZero;
		private Label lblPrintBorder;
		private Button colPrintBorder;
		private Label lblRulerThickness;
		private NumericUpDown numRulerThickness;
		private Label lblRulerZeroMark;
		private NumericUpDown numRulerZeroMark;
		private Label lblRulerMajorMark;
		private NumericUpDown numRulerMajorMark;
		private Label lblRulerMinorMark;
		private NumericUpDown numRulerMinorMark;
		private CheckBox chkSnapObjects;
		private CheckBox chkSnapGuidelines;
		private CheckBox chkDynamicGuides;
		private Label lblSnapDistance;
		private NumericUpDown numSnapDistance;
		private Label lblSnapPointSize;
		private NumericUpDown numSnapPointSize;
		private Label lblSnapPoint;
		private Button colSnapPoint;
		private Label lblSnapLine;
		private Button colSnapLine;
		private Label lblShapeLine;
		private Button colShapeLine;
		private Label lblLineThickness;
		private NumericUpDown numLineThickness;
		private Label lblPatternLength;
		private NumericUpDown numPatternLength;
		private Label lblShapeFill;
		private Button colShapeFill;
		private Label lblHandleSize;
		private NumericUpDown numHandleSize;
		private Label lblHandleLineWidth;
		private NumericUpDown numHandleLineWidth;
		private Label lblHandleFill;
		private Button colHandleFill;
		private Label lblHandleLine;
		private Button colHandleLine;
		private Label lblGripRadius;
		private NumericUpDown numGripRadius;
		private Label lblCurvePointSize;
		private NumericUpDown numCurvePointSize;
		private Label lblCurveCpSize;
		private NumericUpDown numCurveCpSize;
		private Button cancelButton;
		private Button applyButton;
		private Button resetButton;
	}
}

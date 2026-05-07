namespace OCRbyLLoneBot
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            textBox1 = new TextBox();
            textBox2 = new TextBox();
            label1 = new Label();
            label2 = new Label();
            button1 = new Button();
            button2 = new Button();
            toolStrip1 = new ToolStrip();
            toolStripLabel1 = new ToolStripLabel();
            toolStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // textBox1
            // 
            textBox1.Location = new Point(86, 45);
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(650, 26);
            textBox1.TabIndex = 0;
            textBox1.Click += textBox1_Click;
            // 
            // textBox2
            // 
            textBox2.Location = new Point(86, 92);
            textBox2.Multiline = true;
            textBox2.Name = "textBox2";
            textBox2.Size = new Size(650, 349);
            textBox2.TabIndex = 1;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(9, 48);
            label1.Name = "label1";
            label1.Size = new Size(71, 16);
            label1.TabIndex = 2;
            label1.Text = "安装ID：";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(9, 255);
            label2.Name = "label2";
            label2.Size = new Size(71, 16);
            label2.TabIndex = 3;
            label2.Text = "确认ID：";
            // 
            // button1
            // 
            button1.Location = new Point(240, 457);
            button1.Name = "button1";
            button1.Size = new Size(108, 35);
            button1.TabIndex = 4;
            button1.Text = "清空";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // button2
            // 
            button2.Location = new Point(407, 457);
            button2.Name = "button2";
            button2.Size = new Size(121, 35);
            button2.TabIndex = 5;
            button2.Text = "复制确认ID";
            button2.UseVisualStyleBackColor = true;
            button2.Click += button2_Click;
            // 
            // toolStrip1
            // 
            toolStrip1.Dock = DockStyle.Bottom;
            toolStrip1.Items.AddRange(new ToolStripItem[] { toolStripLabel1 });
            toolStrip1.Location = new Point(0, 493);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.Size = new Size(752, 25);
            toolStrip1.TabIndex = 6;
            toolStrip1.Text = "toolStrip1";
            // 
            // toolStripLabel1
            // 
            toolStripLabel1.Name = "toolStripLabel1";
            toolStripLabel1.Size = new Size(96, 22);
            toolStripLabel1.Text = "toolStripLabel1";
            // 
            // Form1
            // 
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(752, 518);
            Controls.Add(toolStrip1);
            Controls.Add(button2);
            Controls.Add(button1);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(textBox2);
            Controls.Add(textBox1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Margin = new Padding(2);
            MaximumSize = new Size(752, 518);
            MinimumSize = new Size(752, 518);
            Name = "Form1";
            Text = "获取确认ID";
            ZoomScaleRect = new Rectangle(15, 15, 689, 454);
            FormClosing += Form1_FormClosing;
            Load += Form1_Load;
            toolStrip1.ResumeLayout(false);
            toolStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox textBox1;
        private TextBox textBox2;
        private Label label1;
        private Label label2;
        private Button button1;
        private Button button2;
        private ToolStrip toolStrip1;
        private ToolStripLabel toolStripLabel1;
    }
}

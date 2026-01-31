namespace DbzLegendsAnalyserWinForms.Controls
{
    partial class EFF_AUTO_B_Control
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            listBoxOffsets = new ListBox();
            label1 = new Label();
            imageViewerControl1 = new PsxTools2.ImageViewerControl();
            SuspendLayout();
            // 
            // listBoxOffsets
            // 
            listBoxOffsets.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            listBoxOffsets.FormattingEnabled = true;
            listBoxOffsets.Location = new Point(0, 18);
            listBoxOffsets.Name = "listBoxOffsets";
            listBoxOffsets.Size = new Size(131, 289);
            listBoxOffsets.TabIndex = 0;
            listBoxOffsets.SelectedIndexChanged += listBoxOffsets_SelectedIndexChanged;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(3, 0);
            label1.Name = "label1";
            label1.Size = new Size(44, 15);
            label1.TabIndex = 1;
            label1.Text = "Offsets";
            // 
            // imageViewerControl1
            // 
            imageViewerControl1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            imageViewerControl1.Image = null;
            imageViewerControl1.Location = new Point(137, 3);
            imageViewerControl1.Name = "imageViewerControl1";
            imageViewerControl1.Size = new Size(468, 318);
            imageViewerControl1.TabIndex = 2;
            // 
            // OV_CHR_A_Control
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(imageViewerControl1);
            Controls.Add(label1);
            Controls.Add(listBoxOffsets);
            Name = "OV_CHR_A_Control";
            Size = new Size(608, 324);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ListBox listBoxOffsets;
        private Label label1;
        private PsxTools2.ImageViewerControl imageViewerControl1;
    }
}

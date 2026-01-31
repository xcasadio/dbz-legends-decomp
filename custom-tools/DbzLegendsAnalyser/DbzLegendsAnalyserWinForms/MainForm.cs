namespace DbzLegendsAnalyserWinForms
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.InitialDirectory = @"D:\development\repo\dbz-legends-decomp\data";
            var result = folderBrowserDialog.ShowDialog();

            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
            {
                oV_chR_A_Control1.Initialize(folderBrowserDialog.SelectedPath);
                loaD_B_Control1.Initialize(folderBrowserDialog.SelectedPath);
            }
        }
    }
}

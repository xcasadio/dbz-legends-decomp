using DbzLegendsAnalyserWinForms.Controls;

namespace DbzLegendsAnalyserWinForms
{
    public partial class MainForm : Form
    {
        private readonly Dictionary<string, Type> _controlTypes = new()
        {
            { "CHR_DATA\\OV_CHR_A.B", typeof(OV_CHR_A_Control) },
            { "CHR_DATA\\LOAD.B", typeof(LOAD_B_Control) },
            { "CHR_DATA\\FACE.B", typeof(FACE_B_Control) },
            { "CHR_DATA\\EFF_AUTO.B", typeof(EFF_AUTO_B_Control) },
            { "STG\\STG1MD.B", typeof(STG_MD_Control) },
            { "STG\\STG2MD.B", typeof(STG_MD_Control) },
            { "STG\\STG3MD.B", typeof(STG_MD_Control) },
            { "STG\\STG4MD.B", typeof(STG_MD_Control) },
            { "STG\\STG5MD.B", typeof(STG_MD_Control) },
            { "STG\\STG6MD.B", typeof(STG_MD_Control) },
            { "STG\\STG7MD.B", typeof(STG_MD_Control) },
            { "STG\\STG8MD.B", typeof(STG_MD_Control) },
            { "STG\\STG1TX.B", typeof(STG_TX_Control) },
            { "STG\\STG2TX.B", typeof(STG_TX_Control) },
            { "STG\\STG3TX.B", typeof(STG_TX_Control) },
            { "STG\\STG4TX.B", typeof(STG_TX_Control) },
            { "STG\\STG5TX.B", typeof(STG_TX_Control) },
            { "STG\\STG6TX.B", typeof(STG_TX_Control) },
            { "STG\\STG7TX.B", typeof(STG_TX_Control) },
            { "STG\\STG8TX.B", typeof(STG_TX_Control) },
            { "SUB\\TITLE.B", typeof(TITLE_B_Control) }
        };

        private string _gamePath;

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
                _gamePath = folderBrowserDialog.SelectedPath;
                listBoxFiles.Items.Clear();

                foreach (var key in _controlTypes.Keys)
                {
                    listBoxFiles.Items.Add(key);
                }
            }
        }

        private void listBoxFiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBoxFiles.SelectedIndex == -1)
            {
                return;
            }

            if (splitContainer1.Panel2.Controls.Count > 0)
            {
                splitContainer1.Panel2.Controls[0].Dispose();
                splitContainer1.Panel2.Controls.Clear();
            }

            var selectedFile = listBoxFiles.SelectedItem.ToString();
            var controlType = _controlTypes.GetValueOrDefault(selectedFile);

            if (controlType == null)
            {
                return;
            }

            splitContainer1.Panel2.SuspendLayout();

            var controlInstance = Activator.CreateInstance(controlType) as AnalyserControl;
            controlInstance.Initialize(Path.Combine(_gamePath, selectedFile));
            splitContainer1.Panel2.Controls.Add(controlInstance);
            controlInstance.Dock = DockStyle.Fill;

            splitContainer1.Panel2.ResumeLayout();
        }
    }
}

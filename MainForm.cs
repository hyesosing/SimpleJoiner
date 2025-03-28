using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using MetroFramework.Forms;
using MetroFramework.Controls;

namespace SimpleJoiner
{
    public partial class MainForm : MetroForm
    {
        private PEJoiner _joiner = new PEJoiner();

        public MainForm()
        {
            InitializeComponent();
            UpdateFileList();
        }

        private void UpdateFileList()
        {
            lvFiles.Items.Clear();
            foreach (var file in _joiner.GetFiles())
            {
                ListViewItem item = new ListViewItem(file.FileName);
                item.SubItems.Add(file.FilePath);
                lvFiles.Items.Add(item);
            }
        }

        private void btnAddFile_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Исполняемые файлы (*.exe;*.dll)|*.exe;*.dll|Все файлы (*.*)|*.*";
                dialog.Multiselect = true;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    foreach (string file in dialog.FileNames)
                    {
                        try
                        {
                            _joiner.AddFile(file);
                        }
                        catch (Exception ex)
                        {
                            MetroFramework.MetroMessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    UpdateFileList();
                }
            }
        }

        private void btnRemoveFile_Click(object sender, EventArgs e)
        {
            if (lvFiles.SelectedItems.Count > 0)
            {
                foreach (ListViewItem item in lvFiles.SelectedItems)
                {
                    _joiner.RemoveFile(item.Index);
                }
                UpdateFileList();
            }
        }

        private void btnJoin_Click(object sender, EventArgs e)
        {
            if (_joiner.GetFiles().Count == 0)
            {
                MetroFramework.MetroMessageBox.Show(this, "Добавьте файлы для склейки", "Предупреждение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Исполняемые файлы (*.exe)|*.exe";
                dialog.FileName = "joined.exe";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        Cursor = Cursors.WaitCursor;
                        _joiner.JoinFiles(dialog.FileName);
                        Cursor = Cursors.Default;
                        MetroFramework.MetroMessageBox.Show(this, "Файлы успешно склеены!", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        Cursor = Cursors.Default;
                        
                        string detailError = _joiner.GetLastError();
                        string errorMessage = ex.Message;
                        
                        using (var errorForm = new ErrorDetailsForm(errorMessage, 
                            string.IsNullOrEmpty(detailError) ? ex.ToString() : detailError))
                        {
                            errorForm.ShowDialog(this);
                        }
                    }
                }
            }
        }

        private void btnAbout_Click(object sender, EventArgs e)
        {
            MetroFramework.MetroMessageBox.Show(this, "Простой склейщик PE файлов\nВерсия 1.0\n\n© 2023", "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            while (_joiner.GetFiles().Count > 0)
            {
                _joiner.RemoveFile(0);
            }
            UpdateFileList();
        }
    }

    public class ErrorDetailsForm : MetroForm
    {
        private MetroFramework.Controls.MetroTextBox txtDetails;
        
        public ErrorDetailsForm(string errorTitle, string errorDetails)
        {
            this.Width = 700;
            this.Height = 500;
            this.Text = "Детали ошибки";
            this.Resizable = true;
            
            var lblError = new MetroFramework.Controls.MetroLabel();
            lblError.Text = "Ошибка при склейке файлов: Ошибка при компиляции программы:";
            lblError.Location = new Point(20, 60);
            lblError.Width = this.Width - 40;
            lblError.AutoSize = true;
            lblError.WrapToLine = true;
            this.Controls.Add(lblError);

            var lblError2 = new MetroFramework.Controls.MetroLabel();
            lblError2.Text = "Ошибка компиляции (код 1):";
            lblError2.Location = new Point(20, 90);
            lblError2.Width = this.Width - 40;
            lblError2.AutoSize = true;
            lblError2.Font = new Font(lblError2.Font, FontStyle.Bold);
            this.Controls.Add(lblError2);
            
            txtDetails = new MetroFramework.Controls.MetroTextBox();
            txtDetails.Text = errorDetails;
            txtDetails.Location = new Point(20, 120);
            txtDetails.Width = this.Width - 40;
            txtDetails.Height = this.Height - 170;
            txtDetails.Multiline = true;
            txtDetails.ScrollBars = ScrollBars.Both;
            txtDetails.ReadOnly = true;
            txtDetails.Font = new Font("Consolas", 10, FontStyle.Regular);
            this.Controls.Add(txtDetails);
            
            var btnClose = new MetroFramework.Controls.MetroButton();
            btnClose.Text = "Закрыть";
            btnClose.Width = 100;
            btnClose.Location = new Point(this.Width - 120, this.Height - 40);
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
            
            this.Resize += (s, e) => 
            {
                txtDetails.Width = this.Width - 40;
                txtDetails.Height = this.Height - 170;
                btnClose.Location = new Point(this.Width - 120, this.Height - 40);
            };
        }
    }
} 
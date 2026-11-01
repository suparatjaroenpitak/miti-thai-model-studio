using ThaiModelStudio;

ApplicationConfiguration.Initialize();
Application.SetDefaultFont(new Font("Leelawadee UI", 10F));
Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "เกิดข้อผิดพลาด", MessageBoxButtons.OK, MessageBoxIcon.Error);
try
{
    Application.Run(new MainForm());
}
catch (Exception ex)
{
    try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup-error.log"), ex.ToString()); } catch { }
    MessageBox.Show("เปิดแอปไม่สำเร็จ\n" + ex.Message, "มิติ สตูดิโอ", MessageBoxButtons.OK, MessageBoxIcon.Error);
}

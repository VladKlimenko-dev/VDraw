using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Перехватчики ставятся ПЕРВЫМИ, до всего остального: ошибка
            // при создании главного окна — обычное дело в чужом окружении,
            // и именно она нужнее всего в журнале. Установи мы их после,
            // такой запуск закончился бы молча.
            ErrorLog.Install();

            try
            {
                // To customize application configuration such as set high DPI settings or default font,
                // see https://aka.ms/applicationconfiguration.
                ApplicationConfiguration.Initialize();

                // Настройки читаются до создания окна: тема, линейки и сетка
                // должны быть выбраны к моменту первой отрисовки, иначе
                // пользователь увидит вспышку исходного оформления.
                SettingsStore.Load();
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Ошибка при запуске", ex, fatal: true);

                MessageBox.Show(
                    "VDraw не смог запуститься." + Environment.NewLine + Environment.NewLine +
                    ex.Message + Environment.NewLine + Environment.NewLine +
                    "Подробности записаны в файл:" + Environment.NewLine + ErrorLog.FilePath,
                    "VDraw",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                throw;
            }
            finally
            {
                ErrorLog.Note("Завершение работы.");
            }
        }
    }
}
// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// MainWindow.xaml.cs - выбор режима работы терминала (1/2/3).
// Выбрали режим -> открылось окно терминала, окно выбора спряталось.
// Обратной дороги для клиента нет (киоск!), вернуться может только
// обладатель через панель редактирования.
// ============================================================================

using System.Windows;
using AvtoParkingKAI.Services;

namespace AvtoParkingKAI
{
    public partial class MainWindow : KioskWindow
    {
        public MainWindow()
        {
            // ВНИМАНИЕ: если обладатель уже выбрал режим этого терминала в EditPanel,
            // это окно вообще не создаётся - см. App.xaml.cs (OnStartup сам решает,
            // какое окно показать при старте, MainWindow.xaml больше не StartupUri).
            InitializeComponent();
            App.ZagruzitLogotip(imgLogo); // заглушка: kai.png кладётся рядом с exe
            PodklyuchitStorozhVremeni(overlayVremya);
            // контрастное выделение выбранного элемента (дополняет стили:
            // цвет рамки - противоположный цвету элемента, см. VydelenieService)
            VydelenieService.Podklyuchit(this);
        }

        // Переходы в режимы идут через общий реестр окон App.PokazatOknoRezhima:
        // он сам покажет нужный interface и спрячет это окно (не Close! иначе
        // приложение завершится как "последнее окно"). Тот же реестр использует
        // кнопка "ВЕРНУТЬСЯ В ТЕРМИНАЛ" в EditPanel для переключения "на лету".

        private void BtnTerminal1_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            App.PokazatOknoRezhima(1);
        }

        private void BtnTerminal2_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            App.PokazatOknoRezhima(2);
        }

        private void BtnTerminal3_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            App.PokazatOknoRezhima(3);
        }

        private void BtnYazyk_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            LanguageManager.PereklyuchitYazyk();
        }
    }
}

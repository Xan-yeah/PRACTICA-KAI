// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
//Программу разработали: Гарафутдинов А.Р.; Зайнабиддинов И.К.
//---------
// App.xaml.cs - точка входа. Здесь:
//   1) RenderMode.SoftwareOnly - чтобы анимации не тормозили на "ведре" ведь у нас ориентир слабое "ужасное железо"
//      (по ТЗ железо терминала может быть от Raspberry-класса до игрового ПК);
//   2) глобальные сервисы: защищённые часы + USB-наблюдатель;
//   3) первый запуск: хэшируем пароль по умолчанию "qwerty";
//   4) глобальный перехват исключений - терминал не имеет права упасть
//      с белым окном смерти перед клиентом на шлагбауме.
// ============================================================================

using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AvtoParkingKAI.Services;

namespace AvtoParkingKAI
{
    public partial class App : Application
    {
        // ---- глобальные сервисы (один экземпляр на всю программу) ----

        /// <summary>Защищённые часы (NTP + Environment.TickCount).</summary>
        public static VremyaService Vremya = new VremyaService();

        /// <summary>Наблюдатель USB-карт (ManagementEventWatcher).</summary>
        public static UsbKartaService UsbKarta = new UsbKartaService();

        /// <summary>
        /// Флаг "можно закрываться". Ставится ТОЛЬКО из EditPanel (обладатель).
        /// Все окна в Closing проверяют его и отменяют Alt+F4 и прочие попытки.
        /// </summary>
        public static bool RazreshenoZakrytie = false;

        // единственный экземпляр панели обладателя (живёт скрыто, чтобы
        // таймер блокировки попыток продолжал тикать в фоне)
        private static EditPanel edinstvennayaPanel;

        // единые экземпляры окон-режимов: индекс 0 - окно выбора (MainWindow),
        // 1..3 - interface1..3. Создаются лениво и дальше переиспользуются
        // (Show/Hide, не Close), чтобы при переключении вида работы из EditPanel
        // подписки на USB-события не плодились дубликатами
        private static Window[] oknaRezhimov = new Window[4];

        public App()
        {
            // программный рендер: на слабом железе (терминал-"ведро") DirectX
            // может отсутствовать/глючить, а софтверный рендер стабилен везде
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }

        protected override void OnStartup(StartupEventArgs argumenty)
        {
            base.OnStartup(argumenty);

            // терминал не должен умирать ни при каком исключении -
            // логируем в отладку и живём дальше (антивандальность на уровне ПО)
            DispatcherUnhandledException += (otpravitel, oshibka) =>
            {
                System.Diagnostics.Debug.WriteLine("ПОЙМАНО: " + oshibka.Exception.Message);
                oshibka.Handled = true;
            };

            // первый запуск: пароль обладателя по умолчанию "qwerty" (из ТЗ),
            // но в настройках храним ТОЛЬКО SHA-256 хэш, никакого открытого текста
            if (string.IsNullOrEmpty(AvtoParkingKAI.Properties.Settings.Default.ParolHash))
            {
                AvtoParkingKAI.Properties.Settings.Default.ParolHash = BezopasnostService.HashSha256("qwerty");
                AvtoParkingKAI.Properties.Settings.Default.Save();
            }

            // восстанавливаем язык, выбранный в прошлый раз
            LanguageManager.UstanovitYazyk(AvtoParkingKAI.Properties.Settings.Default.Yazyk);

            // запускаем сервисы
            Vremya.Start();
            UsbKarta.Start();

            // антивандальный щит: глотаем Win, Alt+Tab, Ctrl+Esc и прочие
            // системные комбинации, чтобы вандал не свернул терминал
            // (подробности и честные ограничения - в AntivandalService.cs)
            AntivandalService.Vklyuchit();

            // стартовое окно: если обладатель уже выбрал режим терминала в EditPanel
            // (комбобокс "вид работы"), окно выбора (MainWindow) не нужно вовсе -
            // сразу открываем нужный interface. 0 = режим не настроен - как раньше,
            // показываем окно выбора. Явный выбор окна тут (а не StartupUri в
            // App.xaml) нужен именно для того, чтобы окно выбора не мелькало на
            // экране перед автоматическим переключением.
            PokazatOknoRezhima(AvtoParkingKAI.Properties.Settings.Default.RezhimTerminala);
        }

        /// <summary>
        /// Показать окно нужного вида работы (0 = окно выбора, 1..3 = interface1..3)
        /// и спрятать все остальные. Общая точка для: старта программы, кнопок
        /// выбора в MainWindow и кнопки "ВЕРНУТЬСЯ В ТЕРМИНАЛ" в EditPanel -
        /// благодаря ей обладатель может переключить вид работы "на лету",
        /// не завершая программу ради простого теста настроек.
        /// Окна переиспользуются (Show/Hide), скрытые окна игнорируют USB-события
        /// по проверке IsVisible в своих обработчиках.
        /// </summary>
        public static void PokazatOknoRezhima(int rezhim)
        {
            if (rezhim < 0 || rezhim >= oknaRezhimov.Length)
            {
                rezhim = 0; // защита от дурака: мусор в настройках = окно выбора
            }

            if (oknaRezhimov[rezhim] == null)
            {
                switch (rezhim)
                {
                    case 1:
                        oknaRezhimov[rezhim] = new interface1();
                        break;
                    case 2:
                        oknaRezhimov[rezhim] = new interface2();
                        break;
                    case 3:
                        oknaRezhimov[rezhim] = new interface3();
                        break;
                    default:
                        oknaRezhimov[rezhim] = new MainWindow();
                        break;
                }
            }

            // сначала показываем новое окно, потом прячем старые - без мигания
            // пустого рабочего стола между ними
            oknaRezhimov[rezhim].Show();
            for (int i = 0; i < oknaRezhimov.Length; i++)
            {
                if (i != rezhim && oknaRezhimov[i] != null && oknaRezhimov[i].IsVisible)
                {
                    oknaRezhimov[i].Hide();
                }
            }
        }

        // ================= единицы измерения M и N =================

        /// <summary>
        /// M (бесплатная стоянка) в МИНУТАХ - с учётом единицы измерения, выбранной
        /// обладателем в EditPanel (комбобокс "единица времени для M и N").
        /// Вся математика терминалов всегда считает в минутах, поэтому перевод
        /// собран в одном месте.
        /// </summary>
        public static double BesplatnoMvMinutah()
        {
            return PerevestiVMinuty(AvtoParkingKAI.Properties.Settings.Default.BesplatnoMinutM);
        }

        /// <summary>N (время на выезд после оплаты) в МИНУТАХ - см. BesplatnoMvMinutah.</summary>
        public static double VyezdNvMinutah()
        {
            return PerevestiVMinuty(AvtoParkingKAI.Properties.Settings.Default.VyezdMinutN);
        }

        private static double PerevestiVMinuty(int znachenie)
        {
            // сама математика перевода живёт в RaschetService (чистая функция,
            // покрыта unit-тестами) - здесь только достаём настройку
            return RaschetService.PerevestiVMinuty(znachenie,
                AvtoParkingKAI.Properties.Settings.Default.EdinitsaVremeniMN);
        }

        protected override void OnExit(ExitEventArgs argumenty)
        {
            AntivandalService.Vyklyuchit(); // хук не должен пережить программу
            UsbKarta.Stop();
            base.OnExit(argumenty);
        }

        /// <summary>
        /// Показать панель обладателя (вызывается по клавише "`"/"ё" из любого окна).
        /// Панель одна на всю программу - скрывается, но не умирает,
        /// чтобы 5-минутный таймер блокировки попыток работал в фоне.
        /// </summary>
        public static void PokazatPanelObladatelya()
        {
            if (edinstvennayaPanel == null)
            {
                edinstvennayaPanel = new EditPanel();
            }
            edinstvennayaPanel.PokazatPanel();
        }

        /// <summary>
        /// Полное завершение программы. Доступно ТОЛЬКО обладателю из EditPanel.
        /// </summary>
        public static void ZavershitProgrammu()
        {
            RazreshenoZakrytie = true;
            Current.Shutdown();
        }

        /// <summary>
        /// ЗАГЛУШКА логотипа: png-файлы в проект пока не добавлены, поэтому
        /// картинка НЕ вшита в ресурсы. Если положить kai.png рядом с exe -
        /// логотип появится на всех окнах; если файла нет - просто пустое
        /// место, программа НЕ падает.
        /// </summary>
        public static void ZagruzitLogotip(System.Windows.Controls.Image imgLogo)
        {
            try
            {
                string putKFaylu = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kai.png");
                if (System.IO.File.Exists(putKFaylu))
                {
                    System.Windows.Media.Imaging.BitmapImage kartinka = new System.Windows.Media.Imaging.BitmapImage();
                    kartinka.BeginInit();
                    kartinka.UriSource = new Uri(putKFaylu);
                    kartinka.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    kartinka.EndInit();
                    imgLogo.Source = kartinka;
                }
            }
            catch (Exception)
            {
                // битый png - терминал важнее логотипа, живём без картинки
            }
        }
    }
}

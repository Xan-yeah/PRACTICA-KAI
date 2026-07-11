// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// EditPanel.xaml.cs - РЕЖИМ ОБЛАДАТЕЛЯ (вход, сброс пароля, настройки).
//
// Правила из ТЗ, реализованные тут:
//   - логин "studentKAI", код продукта "1234567890", пароль по умолч. "qwerty";
//   - логин: ТОЛЬКО латиница и цифры, без пробелов (жёсткий PreviewTextInput);
//   - код продукта: РОВНО 10 символов [a-zA-Z0-9], без пробелов и спецсимволов;
//   - 4 суммарные попытки ввода кода (после 1-й ошибки появляется labelINFO
//     "осталось 3 попытки", далее 2, 1);
//   - попытки кончились -> кнопка гаснет, в labelINFO таймер "ДО СЛЕДУЮЩЕЙ
//     ПОПЫТКИ ... 5:00" (DispatcherTimer тикает В ФОНЕ даже когда панель
//     скрыта), а сама панель через 10 секунд ПРЯЧЕТСЯ (Hide) - вернуть её
//     можно только повторным нажатием "`" ("ё");
//   - верные логин+код -> поля сброса скрываются, появляются 2 textbox
//     нового пароля + labelPasswordINFOedit; ввод СТАРОГО пароля запрещён -
//     вылетает Popup "пожалуйста введите другой пароль так-как вы вводите
//     прошлый"; кнопка AcceptPassword жмётся мышью или Ctrl+Enter;
//   - пароль хранится ТОЛЬКО как SHA-256 хэш в Properties.Settings.Default.
// ============================================================================
//
// --- история рассуждений (черновик) ---
// v1: хранил пароль открытым текстом в Settings - на защите такое стыдно
//     показывать. Переделано дома под все условия: SHA-256, сравниваем хэши.
// v2: таймер блокировки останавливался, когда панель скрывалась (я вешал
//     его на Visibility). Убрал зависимость: окно живёт всегда (Hide, не
//     Close), таймер тикает в фоне - как и требует ТЗ.
// v3: для смены времени пробовал Process.Start("cmd", "/c time ...") -
//     костыль и мигает консоль. Заменил на честный WinAPI SetLocalTime.
// --------------------------------------

using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AvtoParkingKAI.Services;

namespace AvtoParkingKAI
{
    public partial class EditPanel : Window
    {
        // учётные данные из ТЗ
        private const string LOGIN_ETALON = "studentKAI";
        private const string KOD_PRODUKTA_ETALON = "1234567890";

        private const int VSEGO_POPYTOK = 4;          // суммарно 4 попытки (1 + 3)
        private const int BLOKIROVKA_SEKUND = 5 * 60; // 5 минут до следующей попытки
        private const int SKRYTIE_SEKUND = 10;        // панель видна 10 сек после блокировки

        private int ostalosPopytok = VSEGO_POPYTOK;
        private int blokirovkaOstalosSekund;          // обратный отсчёт блокировки
        private bool zablokirovanSbros;               // идёт 5-минутная блокировка

        // true, если на panelNovyParol попали кнопкой "Изменить пароль" из panelRedakt
        // (обладатель уже вошёл и аутентифицирован), а не через "забыли пароль" -
        // тогда после смены пароля возвращаемся в panelRedakt, а не на panelVhod
        private bool smenaParolyIzRedakt;

        private DispatcherTimer taymerBlokirovki;     // тикает В ФОНЕ (окно не умирает)
        private DispatcherTimer taymerSkrytiya;       // 10 секунд до Hide()
        private DispatcherTimer taymerPopup;          // автозакрытие Popup про старый пароль

        public EditPanel()
        {
            InitializeComponent();
            App.ZagruzitLogotip(imgLogo); // заглушка: kai.png кладётся рядом с exe
            // контрастное выделение выбранного элемента (дополняет стили:
            // цвет рамки - противоположный цвету элемента, см. VydelenieService)
            VydelenieService.Podklyuchit(this);

            // мышь тут РАЗРЕШЕНА (единственное окно программы!) -
            // ничего не делаем: у Window курсор по умолчанию виден.

            taymerBlokirovki = new DispatcherTimer();
            taymerBlokirovki.Interval = TimeSpan.FromSeconds(1);
            taymerBlokirovki.Tick += TaymerBlokirovki_Tick;

            taymerSkrytiya = new DispatcherTimer();
            taymerSkrytiya.Interval = TimeSpan.FromSeconds(SKRYTIE_SEKUND);
            taymerSkrytiya.Tick += TaymerSkrytiya_Tick;

            taymerPopup = new DispatcherTimer();
            taymerPopup.Interval = TimeSpan.FromSeconds(3);
            taymerPopup.Tick += (otpravitel, argumenty) =>
            {
                taymerPopup.Stop();
                popupStaryParol.IsOpen = false;
            };

            // живые часы на панели редактирования
            App.Vremya.SekundnyTik += () =>
            {
                runSysVremya.Text = App.Vremya.Seychas().ToString("dd.MM.yyyy HH:mm:ss");
            };
        }

        // ================= показ/скрытие панели =================

        /// <summary>
        /// Вызывается по "`"/"ё" из терминалов (через App). Работает как toggle:
        /// видимую панель прячет, спрятанную - показывает заново.
        /// </summary>
        public void PokazatPanel()
        {
            if (IsVisible)
            {
                Hide();
                return;
            }

            // при каждом показе начинаем со входа (безопасность!),
            // но состояние блокировки сброса сохраняется - таймер-то фоновый
            PokazatTolkoPanel(panelVhod);
            pwdParol.Password = "";
            txtParolVidimy.Text = "";
            labelOshibkaVhoda.Visibility = Visibility.Collapsed;
            labelParolIzmenen.Visibility = Visibility.Collapsed;

            Show();
            Activate();
            pwdParol.Focus();
        }

        /// <summary>Показать ровно одну из четырёх панелей-состояний.</summary>
        private void PokazatTolkoPanel(UIElement nuzhnayaPanel)
        {
            panelVhod.Visibility = nuzhnayaPanel == panelVhod ? Visibility.Visible : Visibility.Collapsed;
            panelSbros.Visibility = nuzhnayaPanel == panelSbros ? Visibility.Visible : Visibility.Collapsed;
            panelNovyParol.Visibility = nuzhnayaPanel == panelNovyParol ? Visibility.Visible : Visibility.Collapsed;
            panelRedakt.Visibility = nuzhnayaPanel == panelRedakt ? Visibility.Visible : Visibility.Collapsed;
        }

        protected override void OnClosing(CancelEventArgs argumenty)
        {
            // крестика нет, но Alt+F4 никто не отменял: вместо закрытия - прячемся,
            // иначе умрут фоновые таймеры блокировки
            if (!App.RazreshenoZakrytie)
            {
                argumenty.Cancel = true;
                Hide();
            }
            base.OnClosing(argumenty);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs argumenty)
        {
            // "`"/"ё" - спрятать панель обратно
            if (argumenty.Key == Key.Oem3)
            {
                Hide();
                argumenty.Handled = true;
                return;
            }

            // Ctrl+Enter - контекстное действие текущей панели (по ТЗ)
            if (argumenty.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (panelVhod.Visibility == Visibility.Visible)
                {
                    // на панели входа Ctrl+Enter = клик по "забыли пароль?"
                    OtkrytSbros();
                }
                else if (panelSbros.Visibility == Visibility.Visible && btnSbrosit.IsEnabled)
                {
                    BtnSbrosit_Click(btnSbrosit, new RoutedEventArgs());
                }
                else if (panelNovyParol.Visibility == Visibility.Visible)
                {
                    BtnAcceptPassword_Click(btnAcceptPassword, new RoutedEventArgs());
                }
                argumenty.Handled = true;
                return;
            }

            // обычный Enter в поле пароля = "Войти" (удобство)
            if (argumenty.Key == Key.Enter && panelVhod.Visibility == Visibility.Visible)
            {
                BtnVoyti_Click(btnVoyti, new RoutedEventArgs());
                argumenty.Handled = true;
                return;
            }

            base.OnPreviewKeyDown(argumenty);
        }

        private void BtnYazyk_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            LanguageManager.PereklyuchitYazyk();
        }

        // ================= ПАНЕЛЬ ВХОДА =================

        /// <summary>Чекбокс "показать пароль": подменяем PasswordBox открытым TextBox.</summary>
        private void ChkPokazatParol_Checked(object otpravitel, RoutedEventArgs argumenty)
        {
            txtParolVidimy.Text = pwdParol.Password;
            txtParolVidimy.Visibility = Visibility.Visible;
            pwdParol.Visibility = Visibility.Collapsed;
        }

        private void ChkPokazatParol_Unchecked(object otpravitel, RoutedEventArgs argumenty)
        {
            pwdParol.Password = txtParolVidimy.Text;
            pwdParol.Visibility = Visibility.Visible;
            txtParolVidimy.Visibility = Visibility.Collapsed;
        }

        /// <summary>Пароль берём из того поля, которое сейчас видно.</summary>
        private string TekushiyVvodParolya()
        {
            return chkPokazatParol.IsChecked == true ? txtParolVidimy.Text : pwdParol.Password;
        }

        private void BtnVoyti_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            string vvedennyHash = BezopasnostService.HashSha256(TekushiyVvodParolya());

            if (vvedennyHash == Properties.Settings.Default.ParolHash)
            {
                // добро пожаловать, обладатель
                labelOshibkaVhoda.Visibility = Visibility.Collapsed;
                ZagruzitNastroykiVPolya();
                PokazatTolkoPanel(panelRedakt);
            }
            else
            {
                labelOshibkaVhoda.Visibility = Visibility.Visible;
            }
        }

        private void LinkZabyliParol_Click(object otpravitel, MouseButtonEventArgs argumenty)
        {
            OtkrytSbros();
        }

        private void OtkrytSbros()
        {
            PokazatTolkoPanel(panelSbros);
            labelLoginPodskazka.Visibility = Visibility.Collapsed;
            // если блокировка ещё идёт - labelINFO уже показывает отсчёт, не трогаем
            if (!zablokirovanSbros)
            {
                labelINFO.Visibility = Visibility.Collapsed;
            }
            txtLogin.Focus();
        }

        // ================= ПАНЕЛЬ СБРОСА =================

        /// <summary>
        /// Жёсткая UI-валидация логина: пускаем ТОЛЬКО [a-zA-Z0-9].
        /// Кириллица/спецсимвол не печатается вовсе + вылезает подсказка.
        /// </summary>
        private void TxtLogin_PreviewTextInput(object otpravitel, TextCompositionEventArgs argumenty)
        {
            bool dopustimo = Regex.IsMatch(argumenty.Text, "^[a-zA-Z0-9]+$");
            if (!dopustimo)
            {
                labelLoginPodskazka.Visibility = Visibility.Visible;
                argumenty.Handled = true; // символ в поле НЕ попадает
            }
            else
            {
                labelLoginPodskazka.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>Код продукта: только латиница и цифры (без спецсимволов).</summary>
        private void TxtKodProdukta_PreviewTextInput(object otpravitel, TextCompositionEventArgs argumenty)
        {
            argumenty.Handled = !Regex.IsMatch(argumenty.Text, "^[a-zA-Z0-9]+$");
        }

        /// <summary>Пробел PreviewTextInput не ловит - режем его отдельно (и в логине, и в коде).</summary>
        private void ZapretProbela_PreviewKeyDown(object otpravitel, KeyEventArgs argumenty)
        {
            if (argumenty.Key == Key.Space)
            {
                if (otpravitel == txtLogin)
                {
                    labelLoginPodskazka.Visibility = Visibility.Visible;
                }
                argumenty.Handled = true;
            }
        }

        private void BtnSbrosit_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            if (zablokirovanSbros)
            {
                return; // кнопка и так выключена, но защита от дурака не помешает
            }

            string login = txtLogin.Text;
            string kod = txtKodProdukta.Text;

            // контрольная валидация формата (вдруг вставили текст через Ctrl+V)
            bool loginKorrekten = Regex.IsMatch(login, "^[a-zA-Z0-9]+$");
            bool kodKorrekten = Regex.IsMatch(kod, "^[a-zA-Z0-9]{10}$");

            if (!loginKorrekten)
            {
                labelLoginPodskazka.Visibility = Visibility.Visible;
                return; // формат логина не считаем попыткой
            }
            labelLoginPodskazka.Visibility = Visibility.Collapsed;

            if (login == LOGIN_ETALON && kodKorrekten && kod == KOD_PRODUKTA_ETALON)
            {
                // данные верны: скрываем поля сброса, показываем ввод нового пароля
                labelINFO.Visibility = Visibility.Collapsed;
                ostalosPopytok = VSEGO_POPYTOK; // попытки прощаем
                txtNovyParol1.Text = "";
                txtNovyParol2.Text = "";
                labelNesovpadenie.Visibility = Visibility.Collapsed;
                PokazatTolkoPanel(panelNovyParol);
                txtNovyParol1.Focus();
                return;
            }

            // НЕВЕРНО: сжигаем попытку
            ostalosPopytok = ostalosPopytok - 1;

            if (ostalosPopytok > 0)
            {
                // "у вас 3 попытки ввести код продукта", потом 2, потом 1
                labelINFO.Text = string.Format(LanguageManager.Stroka("loc_NevernyKodFmt"), ostalosPopytok);
                labelINFO.Visibility = Visibility.Visible;
            }
            else
            {
                VklyuchitBlokirovku();
            }
        }

        /// <summary>Попытки кончились: кнопка гаснет, 5 минут отсчёта, панель прячется через 10 сек.</summary>
        private void VklyuchitBlokirovku()
        {
            zablokirovanSbros = true;
            btnSbrosit.IsEnabled = false;

            blokirovkaOstalosSekund = BLOKIROVKA_SEKUND;
            ObnovitTekstBlokirovki();
            labelINFO.Visibility = Visibility.Visible;

            taymerBlokirovki.Start(); // тикает в фоне даже при скрытой панели
            taymerSkrytiya.Start();   // а через 10 секунд панель спрячется
        }

        private void TaymerBlokirovki_Tick(object otpravitel, EventArgs argumenty)
        {
            blokirovkaOstalosSekund = blokirovkaOstalosSekund - 1;

            if (blokirovkaOstalosSekund <= 0)
            {
                // амнистия: снова можно пробовать сбросить пароль
                taymerBlokirovki.Stop();
                zablokirovanSbros = false;
                ostalosPopytok = VSEGO_POPYTOK;
                btnSbrosit.IsEnabled = true;
                labelINFO.Visibility = Visibility.Collapsed;
            }
            else
            {
                ObnovitTekstBlokirovki();
            }
        }

        private void ObnovitTekstBlokirovki()
        {
            // "*" из ТЗ = оставшееся время, формат М:СС
            string vremya = (blokirovkaOstalosSekund / 60) + ":" + (blokirovkaOstalosSekund % 60).ToString("00");
            labelINFO.Text = string.Format(LanguageManager.Stroka("loc_BlokFmt"), vremya);
        }

        private void TaymerSkrytiya_Tick(object otpravitel, EventArgs argumenty)
        {
            taymerSkrytiya.Stop();
            Hide(); // просто скрытие (hide), как и просили в ТЗ
        }

        // ================= ПАНЕЛЬ НОВОГО ПАРОЛЯ =================

        private void BtnAcceptPassword_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            string parol1 = txtNovyParol1.Text;
            string parol2 = txtNovyParol2.Text;

            // защита от дурака: пустой пароль или расхождение полей
            if (string.IsNullOrEmpty(parol1) || parol1 != parol2)
            {
                labelNesovpadenie.Visibility = Visibility.Visible;
                return;
            }
            labelNesovpadenie.Visibility = Visibility.Collapsed;

            // ЗАПРЕТ старого пароля: сравниваем ХЭШИ, открытый пароль нигде не лежит
            if (BezopasnostService.HashSha256(parol1) == Properties.Settings.Default.ParolHash)
            {
                popupStaryParol.IsOpen = true;  // "вы вводите прошлый"
                taymerPopup.Start();            // сам закроется через 3 секунды
                return;
            }

            // всё честно - сохраняем новый хэш
            Properties.Settings.Default.ParolHash = BezopasnostService.HashSha256(parol1);
            Properties.Settings.Default.Save();

            if (smenaParolyIzRedakt)
            {
                // сюда попали кнопкой "Изменить пароль" из panelRedakt - обладатель уже
                // вошёл, повторный ввод логина/пароля не нужен, остаёмся в редактировании
                smenaParolyIzRedakt = false;
                PokazatTolkoPanel(panelRedakt);
                PokazatRezultat("loc_ParolIzmenen", true);
            }
            else
            {
                // пришли через "забыли пароль" - возвращаемся на вход с зелёной весточкой
                PokazatTolkoPanel(panelVhod);
                pwdParol.Password = "";
                txtParolVidimy.Text = "";
                labelParolIzmenen.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Прямая смена пароля из panelRedakt (без хождения через "забыли пароль") -
        /// обладатель уже аутентифицирован входом в панель, поэтому сразу открываем
        /// поля нового пароля.
        /// </summary>
        private void BtnIzmenitParol_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            smenaParolyIzRedakt = true;
            txtNovyParol1.Text = "";
            txtNovyParol2.Text = "";
            labelNesovpadenie.Visibility = Visibility.Collapsed;
            PokazatTolkoPanel(panelNovyParol);
            txtNovyParol1.Focus();
        }

        /// <summary>Кнопка "Отмена" на панели нового пароля - раньше пути назад тут не было.</summary>
        private void BtnOtmenaNovyParol_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            if (smenaParolyIzRedakt)
            {
                smenaParolyIzRedakt = false;
                PokazatTolkoPanel(panelRedakt);
            }
            else
            {
                PokazatTolkoPanel(panelVhod);
                pwdParol.Password = "";
                txtParolVidimy.Text = "";
            }
        }

        // ================= ПАНЕЛЬ РЕДАКТИРОВАНИЯ =================

        private void ZagruzitNastroykiVPolya()
        {
            txtCena.Text = Properties.Settings.Default.CenaZaChas.ToString("0.##");
            txtBesplatnoM.Text = Properties.Settings.Default.BesplatnoMinutM.ToString();
            txtVyezdN.Text = Properties.Settings.Default.VyezdMinutN.ToString();
            txtNovoeVremya.Text = App.Vremya.Seychas().ToString("dd.MM.yyyy HH:mm");
            // индексы пунктов комбобоксов 1-в-1 совпадают со значениями настроек -
            // см. порядок ComboBoxItem-ов в EditPanel.xaml
            cmbRezhimTerminala.SelectedIndex = Properties.Settings.Default.RezhimTerminala;
            cmbEdinitsaVremeni.SelectedIndex = Properties.Settings.Default.EdinitsaVremeniMN;
            labelRezultat.Visibility = Visibility.Collapsed;
        }

        private void PokazatRezultat(string klyuchStroki, bool uspeh)
        {
            labelRezultat.SetResourceReference(TextBlock.TextProperty, klyuchStroki);
            labelRezultat.Foreground = (Brush)FindResource(uspeh ? "UspehBrush" : "OshibkaBrush");
            labelRezultat.Visibility = Visibility.Visible;
        }

        private void BtnSohranit_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            // изолированная жёсткая валидация: числа строго > 0, иначе вежливая ошибка
            double novayaCena;
            int novoeM;
            int novoeN;

            bool cenaOk = double.TryParse(txtCena.Text.Replace(',', '.'), NumberStyles.Float,
                                          CultureInfo.InvariantCulture, out novayaCena) && novayaCena > 0;
            bool mOk = int.TryParse(txtBesplatnoM.Text, out novoeM) && novoeM >= 0;
            bool nOk = int.TryParse(txtVyezdN.Text, out novoeN) && novoeN > 0;

            if (!cenaOk || !mOk || !nOk)
            {
                PokazatRezultat("loc_OshibkaVvoda", false);
                return;
            }

            Properties.Settings.Default.CenaZaChas = novayaCena;
            Properties.Settings.Default.BesplatnoMinutM = novoeM;
            Properties.Settings.Default.VyezdMinutN = novoeN;
            Properties.Settings.Default.RezhimTerminala = cmbRezhimTerminala.SelectedIndex >= 0
                ? cmbRezhimTerminala.SelectedIndex
                : 0;
            Properties.Settings.Default.EdinitsaVremeniMN = cmbEdinitsaVremeni.SelectedIndex >= 0
                ? cmbEdinitsaVremeni.SelectedIndex
                : 0;
            Properties.Settings.Default.Save();
            PokazatRezultat("loc_Sohraneno", true);
        }

        private void BtnSozdatDispKey_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            // адресный выбор флешки: при нескольких вставленных сначала берётся
            // уже существующий ключ (перезапись), потом пустая, и только в крайнем
            // случае - карта-пропуск (её роль будет заменена на ключ)
            DriveInfo fleshka = App.UsbKarta.NaytiKartuDlyaKlyucha();
            if (fleshka == null)
            {
                PokazatRezultat("loc_NetFleshki", false);
                return;
            }
            bool uspeh = App.UsbKarta.SozdatKlyuchDispetchera(fleshka);
            PokazatRezultat(uspeh ? "loc_DispKeyGotov" : "loc_NetFleshki", uspeh);
        }

        private void BtnVernutsya_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            // возвращаемся НЕ в то окно, из которого открыли панель, а в тот вид
            // работы, что выбран в комбобоксе: обладатель мог сменить режим и сразу
            // нажать "вернуться" для быстрого теста - раньше ему приходилось
            // завершать и перезапускать программу целиком. Выбор фиксируем в
            // настройках, чтобы поведение после перезапуска совпадало с тем,
            // что обладатель видит на экране.
            int rezhim = cmbRezhimTerminala.SelectedIndex >= 0
                ? cmbRezhimTerminala.SelectedIndex
                : Properties.Settings.Default.RezhimTerminala;
            if (rezhim != Properties.Settings.Default.RezhimTerminala)
            {
                Properties.Settings.Default.RezhimTerminala = rezhim;
                Properties.Settings.Default.Save();
            }

            App.PokazatOknoRezhima(rezhim);
            Hide(); // терминал снова остаётся один на один с клиентами
        }

        private void BtnZavershit_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            App.ZavershitProgrammu(); // ЕДИНСТВЕННЫЙ легальный выход из программы
        }

        // ================= смена системного времени (WinAPI) =================

        // структура для kernel32!SetLocalTime - поля строго в таком порядке
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear;
            public ushort wMonth;
            public ushort wDayOfWeek;
            public ushort wDay;
            public ushort wHour;
            public ushort wMinute;
            public ushort wSecond;
            public ushort wMilliseconds;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetLocalTime(ref SYSTEMTIME novoeVremya);

        private void BtnUstanovitVremya_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            DateTime razobrannoeVremya;
            bool formatOk = DateTime.TryParseExact(txtNovoeVremya.Text.Trim(),
                new[] { "dd.MM.yyyy HH:mm", "dd.MM.yyyy HH:mm:ss" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out razobrannoeVremya);

            if (!formatOk)
            {
                PokazatRezultat("loc_OshibkaVvoda", false);
                return;
            }

            SYSTEMTIME st = new SYSTEMTIME();
            st.wYear = (ushort)razobrannoeVremya.Year;
            st.wMonth = (ushort)razobrannoeVremya.Month;
            st.wDay = (ushort)razobrannoeVremya.Day;
            st.wHour = (ushort)razobrannoeVremya.Hour;
            st.wMinute = (ushort)razobrannoeVremya.Minute;
            st.wSecond = (ushort)razobrannoeVremya.Second;

            if (SetLocalTime(ref st))
            {
                // говорим сторожевым часам, что перевод времени ЗАКОННЫЙ,
                // иначе они решат что терминал взломали и заблокируются
                App.Vremya.PrinyatNovoeVremyaOs();
                PokazatRezultat("loc_VremyaUstanovleno", true);
            }
            else
            {
                // без прав администратора WinAPI откажет
                PokazatRezultat("loc_TrebuetsyaAdmin", false);
            }
        }
    }
}

// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// interface2.xaml.cs - ТЕРМИНАЛ №2 (оплата парковки + информационный режим).
//
// Формула расчёта живёт в Services/RaschetService.cs (чистые функции,
// покрытые unit-тестами): начатый час = целый час, M бесплатных минут.
//
// Терминал работает в ДВУХ ролях:
//   - справочная: водитель вставил карту "просто посмотреть" - на экране
//     живой расчёт с секундным счётчиком до следующего повышения цены,
//     на карту при этом НИЧЕГО не пишется;
//   - платёжная: оплата "через терминал" (тёмная POS-карточка с сегментной
//     шкалой) или "онлайн" - страница интернет-оплаты с полными реквизитами
//     (номер карты + срок действия + CVC) и обработкой с веб-спиннером;
//     по правке ТЗ оба способа обрабатываются визуально ПО-РАЗНОМУ.
// ============================================================================
//
// --- история рассуждений (черновик) ---
// v1: summa = (int)(minuty/60) * cena  -> водитель стоял 1ч59м, платил за 1 час,
//     обладатель терял деньги. Math.Ceiling решил вопрос.
// v2: эмуляцию банка делал через MessageBox.Show("Оплачено") - выглядело
//     убого для демонстрации. Переделано дома под все условия: модальный
//     оверлей + ProgressBar, "Соединение с банком..." -> "ОДОБРЕНО".
// v3: в бесплатный период терминал СРАЗУ помечал карту оплаченной - водитель,
//     который просто посмотрел цену в первые минуты, уезжал бесплатно даже
//     через несколько часов (найдено тестированием). Убрано: бесплатный
//     период теперь чисто информационный, решение "платить" принимает
//     только сам водитель кнопкой.
// НФС-СЧИТЫВАТЕЛЯ НЕТ, ТЕРМИНАЛА БАНКА НЕТ - всё эмулируется программно,
// программа демонстрационная и для коммерции не предназначена.
// --------------------------------------

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AvtoParkingKAI.Services;

namespace AvtoParkingKAI
{
    public partial class interface2 : KioskWindow
    {
        private PropuskData tekushiyPropusk;   // считанный с карты пропуск
        private double summaKOplate;           // рассчитанная сумма, руб

        private bool zhdemDispKlyuch;          // нажата "Вызов диспетчера", ждём его флешку
        private bool lgotaAktivna;             // ключ диспетчера принят, следующая карта - бесплатно

        private const int SEKUND_NA_OTVET_DISP = 60; // сколько ждём диспетчера, прежде чем сдаться

        private DispatcherTimer bankTaymer;    // двигает прогресс "банка" (оба визуала)
        private DispatcherTimer bankFinishTaymer; // пауза на показ "ОДОБРЕНО"
        private DispatcherTimer taymerOzhidaniyaDisp; // 60 секунд ожидания диспетчера

        // ---- обработка платежа: два РАЗНЫХ визуала при общей логике ----
        // терминал = сегментная шкала-"заряд", онлайн = веб-спиннер (см. XAML)
        private const int KOLVO_SEGMENTOV = 12;   // ячеек в шкале терминальной оплаты
        private Border[] segmentyBanka;           // сами ячейки (создаются в конструкторе)
        private bool bankZapushenOnlayn;          // каким способом запущена текущая оплата
        private double bankProgress;              // 0..100, общий для обоих визуалов

        public interface2()
        {
            InitializeComponent();
            App.ZagruzitLogotip(imgLogo); // заглушка: kai.png кладётся рядом с exe
            PodklyuchitStorozhVremeni(overlayVremya);
            // контрастное выделение выбранного элемента (дополняет стили:
            // цвет рамки - противоположный цвету элемента, см. VydelenieService)
            VydelenieService.Podklyuchit(this);

            App.UsbKarta.KartaVstavlena += UsbKarta_KartaVstavlena;
            App.UsbKarta.KartaIzvlechena += UsbKarta_KartaIzvlechena;

            // "банк думает": 50 мс * 50 шагов = 2.5 секунды соединения
            bankTaymer = new DispatcherTimer();
            bankTaymer.Interval = TimeSpan.FromMilliseconds(50);
            bankTaymer.Tick += BankTaymer_Tick;

            // сегментная шкала терминальной оплаты: KOLVO_SEGMENTOV ячеек,
            // создаём кодом, чтобы не копировать 12 одинаковых Border в XAML
            segmentyBanka = new Border[KOLVO_SEGMENTOV];
            for (int i = 0; i < KOLVO_SEGMENTOV; i++)
            {
                segmentyBanka[i] = new Border
                {
                    Width = 36,
                    Height = 30,
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(4, 0, 4, 0),
                    Background = (Brush)FindResource("FonBrush"),
                    BorderBrush = (Brush)FindResource("ObvodkaBrush"),
                    BorderThickness = new Thickness(1)
                };
                panelSegmenty.Children.Add(segmentyBanka[i]);
            }

            // пауза, чтобы клиент успел прочитать "ОДОБРЕНО"
            bankFinishTaymer = new DispatcherTimer();
            bankFinishTaymer.Interval = TimeSpan.FromSeconds(2.5);
            bankFinishTaymer.Tick += BankFinishTaymer_Tick;

            // диспетчер мог "не услышать" вызов - через минуту сдаёмся сами,
            // не заставляя клиента ждать у терминала вечно (человеческий фактор)
            taymerOzhidaniyaDisp = new DispatcherTimer();
            taymerOzhidaniyaDisp.Interval = TimeSpan.FromSeconds(SEKUND_NA_OTVET_DISP);
            taymerOzhidaniyaDisp.Tick += TaymerOzhidaniyaDisp_Tick;

            // живой информационный блок: раз в секунду освежаем стоянку, сумму
            // и счётчик до следующего повышения цены (часы уже тикают в App.Vremya)
            App.Vremya.SekundnyTik += ObnovitZhivoyRaschet;

            // при КАЖДОМ показе окна (запуск или возврат из другого вида работы
            // через EditPanel) перечитываем считыватель - карта могла быть
            // вставлена заранее или пока окно было спрятано. Оверлеи не трогаем:
            // если ждём диспетчера/банк/оплату, состояние продолжается с того же места.
            IsVisibleChanged += (otpravitel, argumenty) =>
            {
                if (IsVisible &&
                    overlayBank.Visibility != Visibility.Visible &&
                    overlayOnlayn.Visibility != Visibility.Visible &&
                    overlayDispetcher.Visibility != Visibility.Visible)
                {
                    ProveritKartu();
                }
            };
        }

        /// <summary>Ctrl+Enter жмёт актуальную кнопку: онлайн-ОПЛАТИТЬ или терминал.</summary>
        protected override Button KnopkaPoUmolchaniyu()
        {
            if (overlayOnlayn.Visibility == Visibility.Visible)
            {
                return btnOplatitOnlineOk;
            }
            if (panelKnopkiOplaty.Visibility == Visibility.Visible)
            {
                return btnOplatitTerminal;
            }
            return null;
        }

        private void BtnYazyk_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            LanguageManager.PereklyuchitYazyk();
        }

        // ---------------- USB: карта появилась/исчезла ----------------

        private void UsbKarta_KartaVstavlena()
        {
            // окно спрятано (обладатель переключил вид работы) - не вмешиваемся
            if (!IsVisible)
            {
                return;
            }

            // режим ожидания диспетчера: интересует ТОЛЬКО его служебная флешка
            if (zhdemDispKlyuch)
            {
                PrinyatKlyuchDispetcheraEsliEst();
                return;
            }

            // льгота активна - ждём именно карту КЛИЕНТА. В считывателе при этом
            // может торчать и флешка диспетчера (обе одновременно - ровно тот
            // случай, что уронил оплату на тестировании), поэтому ищем карту
            // клиента адресно, а не "первую попавшуюся флешку"
            if (lgotaAktivna)
            {
                if (App.UsbKarta.NaytiKartuKlienta() == null)
                {
                    return; // вставили что-то не то (например, второй ключ) - ждём дальше
                }
                overlayDispetcher.Visibility = Visibility.Collapsed;
            }

            ProveritKartu();
        }

        private void UsbKarta_KartaIzvlechena()
        {
            if (!IsVisible)
            {
                return;
            }

            // банк "думает" - пусть додумает, запись сама отвалится с ошибкой
            if (overlayBank.Visibility == Visibility.Visible)
            {
                return;
            }

            // клиент выдернул карту прямо на странице оплаты - платить уже не
            // за что, страницу закрываем и перечитываем считыватель
            if (overlayOnlayn.Visibility == Visibility.Visible)
            {
                if (App.UsbKarta.NaytiKartuKlienta() == null)
                {
                    overlayOnlayn.Visibility = Visibility.Collapsed;
                    ProveritKartu();
                }
                return;
            }

            // ждём диспетчера / его ключ принят, а карты клиента ещё нет:
            // оверлей живёт своей жизнью, выдёргивание флешек его не сбивает
            if (overlayDispetcher.Visibility == Visibility.Visible)
            {
                return;
            }

            // вынуть могли ЛЮБУЮ из нескольких флешек (например, диспетчер забрал
            // свой ключ, а карта клиента осталась в считывателе) - поэтому не
            // сбрасываем экран вслепую, а честно перечитываем, что осталось
            ProveritKartu();
        }

        // ---------------- расчёт стоимости ----------------

        private void ProveritKartu()
        {
            // адресный поиск карты КЛИЕНТА: флешка с пропуском, а если пропуска
            // нет - пустая; ключ диспетчера картой клиента не считается никогда
            DriveInfo karta = App.UsbKarta.NaytiKartuKlienta();
            if (karta == null)
            {
                PokazatOzhidanie();
                return;
            }

            tekushiyPropusk = App.UsbKarta.ProchitatPropusk(karta);
            if (tekushiyPropusk == null)
            {
                // флешка есть, а пропуска на ней нет / он подделан.
                // Ошибка показывается ВМЕСТО приглашения "поднесите карту"
                // (раньше - вместе с ним, внизу панели: группа расползалась
                // и текст исключения выглядел неотцентрованным). Как только
                // битую карту вынут, ProveritKartu вернёт приглашение на место.
                PokazatOzhidanie();
                labelPodnesite.Visibility = Visibility.Collapsed;
                labelKartaBitaya.Visibility = Visibility.Visible;
                return;
            }

            PokazatRaschet(karta);
        }

        private void PokazatRaschet(DriveInfo karta)
        {
            labelKartaBitaya.Visibility = Visibility.Collapsed;
            labelPodnesite.Visibility = Visibility.Collapsed;
            panelRaschet.Visibility = Visibility.Visible;
            labelStatus.Visibility = Visibility.Collapsed;
            panelKnopkiOplaty.Visibility = Visibility.Collapsed;

            // 1) ЛЬГОТА ОТ ДИСПЕТЧЕРА - главнее всего остального: неважно, идёт
            //    ли ещё бесплатное время или уже закончилось, ключ диспетчера в
            //    ЛЮБОМ случае делает карту оплаченной с ценой 0 руб. Раньше эти
            //    два состояния конфликтовали (бесплатный период перехватывал
            //    карту раньше льготы) - теперь они в симбиозе
            if (lgotaAktivna)
            {
                lgotaAktivna = false;
                ObnovitRaschetNaEkrane();  // время заезда и стоянка - на экран
                OformitOplatu(karta);      // IsPaid = true, цена по льготе = 0
                summaKOplate = 0.0;
                runSumma.Text = "0";
                panelInfoBlok.Visibility = Visibility.Collapsed;
                PokazatStatus("loc_Lgota");
                return;
            }

            ObnovitRaschetNaEkrane();

            // 2) уже оплачено ранее
            if (tekushiyPropusk.IsPaid)
            {
                PokazatStatus("loc_UzheOplacheno");
                return;
            }

            // 3) бесплатное время ещё идёт. РАНЬШЕ терминал здесь сразу помечал
            //    карту оплаченной - и водитель, просто ПОСМОТРЕВШИЙ стоимость в
            //    первые минуты, уезжал бесплатно даже через несколько часов
            //    (найдено тестированием). Теперь просмотр чисто информационный:
            //    на карту НИЧЕГО не пишем, живой счётчик показывает, когда
            //    начнётся платная стоянка; на выезде терминал №3 сам выпустит
            //    бесплатно, если водитель реально уложился в M минут
            if (summaKOplate <= 0.0)
            {
                PokazatStatus("loc_Besplatno");
                return;
            }

            // 4) надо платить - кнопки оплаты (решение принимает сам водитель,
            //    вставленная карта до нажатия кнопки не меняется)
            panelKnopkiOplaty.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// "Живая" часть экрана расчёта: стоянка, текущая сумма и информационный
        /// блок со счётчиком до следующего повышения цены. Вызывается и при
        /// показе расчёта, и каждую секунду из ObnovitZhivoyRaschet.
        /// </summary>
        private void ObnovitRaschetNaEkrane()
        {
            DateTime vremyaZaezda = tekushiyPropusk.GetVremyaZaezda();
            TimeSpan stoyanka = App.Vremya.Seychas() - vremyaZaezda;
            if (stoyanka.Ticks < 0)
            {
                stoyanka = TimeSpan.Zero; // защита от дурака: карта "из будущего"
            }

            runZaezd.Text = vremyaZaezda.ToString("dd.MM.yyyy HH:mm");
            runStoyanka.Text = ((int)stoyanka.TotalHours) + " " + LanguageManager.Stroka("loc_Chas") +
                               " " + stoyanka.Minutes + " " + LanguageManager.Stroka("loc_Min");

            double besplatnoMinutM = App.BesplatnoMvMinutah();
            double cenaZaChas = Properties.Settings.Default.CenaZaChas;

            summaKOplate = RaschetService.RasschitatSummu(stoyanka.TotalMinutes, besplatnoMinutM, cenaZaChas);
            runSumma.Text = summaKOplate.ToString("0.##");

            if (tekushiyPropusk.IsPaid)
            {
                panelInfoBlok.Visibility = Visibility.Collapsed; // платить нечего - счётчик не нужен
                return;
            }

            // счётчик до следующего повышения цены + сумма после повышения.
            // Формат МИН:СС (в бесплатный период минут может быть и больше 60)
            TimeSpan doPovysheniya = TimeSpan.FromMinutes(
                RaschetService.DoPovysheniyaMinut(stoyanka.TotalMinutes, besplatnoMinutM));
            runInfoTaymer.Text = ((int)doPovysheniya.TotalMinutes) + ":" + doPovysheniya.Seconds.ToString("00");
            runInfoSleduyushaya.Text = RaschetService
                .SleduyushayaSumma(stoyanka.TotalMinutes, besplatnoMinutM, cenaZaChas).ToString("0.##");
            runInfoPodpis.SetResourceReference(Run.TextProperty,
                summaKOplate <= 0.0 ? "loc_DoKontsaBesplatnogo" : "loc_DoPovysheniya");
            panelInfoBlok.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Секундный тик защищённых часов: держит экран расчёта "живым" - стоянка
        /// и сумма растут на глазах, счётчик до повышения тикает вниз. Если
        /// бесплатный период истёк прямо перед клиентом, информационный экран
        /// сам превращается в экран оплаты.
        /// </summary>
        private void ObnovitZhivoyRaschet()
        {
            if (!IsVisible || tekushiyPropusk == null || tekushiyPropusk.IsPaid)
            {
                return;
            }
            if (panelRaschet.Visibility != Visibility.Visible)
            {
                return;
            }
            if (overlayBank.Visibility == Visibility.Visible ||
                overlayOnlayn.Visibility == Visibility.Visible ||
                overlayDispetcher.Visibility == Visibility.Visible)
            {
                return; // поверх идёт оплата/диспетчер - экран под оверлеем не дёргаем
            }

            ObnovitRaschetNaEkrane();

            // бесплатное время закончилось прямо на глазах: статус "выезд
            // бесплатный" убираем, кнопки оплаты показываем
            if (summaKOplate > 0.0 && panelKnopkiOplaty.Visibility != Visibility.Visible)
            {
                labelStatus.Visibility = Visibility.Collapsed;
                panelKnopkiOplaty.Visibility = Visibility.Visible;
            }
        }

        private void PokazatStatus(string klyuchStroki)
        {
            panelKnopkiOplaty.Visibility = Visibility.Collapsed;
            labelStatus.SetResourceReference(TextBlock.TextProperty, klyuchStroki);
            labelStatus.Visibility = Visibility.Visible;
        }

        /// <summary>Пометить пропуск оплаченным и перезаписать карту.</summary>
        private bool OformitOplatu(DriveInfo karta)
        {
            tekushiyPropusk.IsPaid = true;
            tekushiyPropusk.VremyaOplatyTicks = App.Vremya.Seychas().Ticks;
            return App.UsbKarta.ZapisatPropusk(karta, tekushiyPropusk);
        }

        // ---------------- способы оплаты ----------------

        private void BtnOplatitTerminal_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            ZapustitBank(false); // визуал POS-терминала (сегментная шкала)
        }

        // ---------------- страница онлайн-оплаты ----------------

        private bool formatiruemPole; // защита от рекурсии TextChanged при автоформате

        private void BtnOplatitOnline_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            // открываем "веб-страницу" оплаты с чистыми полями и актуальной суммой
            txtNomerKarty.Text = "";
            txtSrokKarty.Text = "";
            pwdCvc.Password = "";
            labelOshibkaOplaty.Visibility = Visibility.Collapsed;
            runSummaOnlayn.Text = summaKOplate.ToString("0.##");
            overlayOnlayn.Visibility = Visibility.Visible;
            txtNomerKarty.Focus();
        }

        /// <summary>Жёсткая UI-валидация: во все поля страницы оплаты входят только цифры.</summary>
        private void PoleTolkoTsifry_PreviewTextInput(object otpravitel, System.Windows.Input.TextCompositionEventArgs argumenty)
        {
            argumenty.Handled = !Regex.IsMatch(argumenty.Text, "^[0-9]+$");
        }

        /// <summary>Автоформат номера карты: цифры группируются по 4 ("0000 0000 0000 0000").</summary>
        private void TxtNomerKarty_TextChanged(object otpravitel, TextChangedEventArgs argumenty)
        {
            if (formatiruemPole)
            {
                return;
            }
            formatiruemPole = true;

            string tsifry = Regex.Replace(txtNomerKarty.Text, "[^0-9]", "");
            if (tsifry.Length > 16)
            {
                tsifry = tsifry.Substring(0, 16);
            }

            StringBuilder sbornik = new StringBuilder();
            for (int i = 0; i < tsifry.Length; i++)
            {
                if (i > 0 && i % 4 == 0)
                {
                    sbornik.Append(' ');
                }
                sbornik.Append(tsifry[i]);
            }

            txtNomerKarty.Text = sbornik.ToString();
            txtNomerKarty.CaretIndex = txtNomerKarty.Text.Length;
            formatiruemPole = false;
        }

        /// <summary>Автоформат срока действия: "ММГГ" превращается в "ММ/ГГ" по мере ввода.</summary>
        private void TxtSrokKarty_TextChanged(object otpravitel, TextChangedEventArgs argumenty)
        {
            if (formatiruemPole)
            {
                return;
            }
            formatiruemPole = true;

            string tsifry = Regex.Replace(txtSrokKarty.Text, "[^0-9]", "");
            if (tsifry.Length > 4)
            {
                tsifry = tsifry.Substring(0, 4);
            }

            txtSrokKarty.Text = tsifry.Length > 2
                ? tsifry.Substring(0, 2) + "/" + tsifry.Substring(2)
                : tsifry;
            txtSrokKarty.CaretIndex = txtSrokKarty.Text.Length;
            formatiruemPole = false;
        }

        private void PokazatOshibkuOplaty(string klyuchStroki)
        {
            labelOshibkaOplaty.SetResourceReference(TextBlock.TextProperty, klyuchStroki);
            labelOshibkaOplaty.Visibility = Visibility.Visible;
        }

        private void BtnOplatitOnlineOk_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            // проверки реквизитов - чистые функции RaschetService (покрыты
            // unit-тестами): 16 цифр номера, срок ММ/ГГ не в прошлом, CVC 3 цифры
            if (!RaschetService.NomerKartyKorrekten(txtNomerKarty.Text))
            {
                PokazatOshibkuOplaty("loc_Neverno16");
                return;
            }
            if (!RaschetService.SrokKorrekten(txtSrokKarty.Text, App.Vremya.Seychas()))
            {
                PokazatOshibkuOplaty("loc_NevernySrok");
                return;
            }
            if (!RaschetService.CvcKorrekten(pwdCvc.Password))
            {
                PokazatOshibkuOplaty("loc_NevernyCvc");
                return;
            }

            // реквизиты в порядке - "страница" отправляет платёж в тот же банк
            overlayOnlayn.Visibility = Visibility.Collapsed;
            ZapustitBank(true); // визуал веб-страницы (круговой спиннер)
        }

        private void BtnOtmenaOnlayn_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            overlayOnlayn.Visibility = Visibility.Collapsed;
        }

        // ---------------- эмуляция банка ----------------
        // Логика одна (таймер + запись оплаты на карту), а ВИЗУАЛА два:
        // терминальная оплата рисуется сегментной шкалой в тёмной POS-карточке,
        // онлайн - вращающимся спиннером на светлой "веб-странице" (правка ТЗ:
        // способы оплаты должны выглядеть по-разному, а не одним ProgressBar).

        private void ZapustitBank(bool onlayn)
        {
            bankZapushenOnlayn = onlayn;
            bankProgress = 0;

            panelBankTerminal.Visibility = onlayn ? Visibility.Collapsed : Visibility.Visible;
            panelBankOnlayn.Visibility = onlayn ? Visibility.Visible : Visibility.Collapsed;

            if (onlayn)
            {
                labelBankStatusOnlayn.SetResourceReference(TextBlock.TextProperty, "loc_ObrabotkaPlatezha");
                labelBankStatusOnlayn.Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x72));
                spinnerDuga.Stroke = new SolidColorBrush(Color.FromRgb(0x29, 0x62, 0xFF));
                labelProcentOnlayn.Text = "0%";

                // бесконечное вращение дуги, пока "банк думает"
                DoubleAnimation vrashchenie = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1));
                vrashchenie.RepeatBehavior = RepeatBehavior.Forever;
                spinnerPovorot.BeginAnimation(RotateTransform.AngleProperty, vrashchenie);
            }
            else
            {
                labelBankStatusTerminal.SetResourceReference(TextBlock.TextProperty, "loc_Soedinenie");
                labelBankStatusTerminal.Foreground = (Brush)FindResource("OzhidanieBrush");
                labelProcentTerminal.Text = "0%";
                ObnovitSegmenty(0);
            }

            overlayBank.Visibility = Visibility.Visible;
            bankTaymer.Start();
        }

        /// <summary>Зажигает ячейки сегментной шкалы пропорционально прогрессу.</summary>
        private void ObnovitSegmenty(double procent)
        {
            int zazhech = (int)Math.Round(procent / 100.0 * KOLVO_SEGMENTOV);
            for (int i = 0; i < KOLVO_SEGMENTOV; i++)
            {
                segmentyBanka[i].Background = i < zazhech
                    ? (Brush)FindResource("UspehBrush")
                    : (Brush)FindResource("FonBrush");
            }
        }

        private void BankTaymer_Tick(object otpravitel, EventArgs argumenty)
        {
            bankProgress = bankProgress + 2; // 50 мс * 50 шагов ~ 2.5 сек
            if (bankProgress > 100)
            {
                bankProgress = 100;
            }

            // рисуем прогресс на том визуале, которым запущена оплата
            if (bankZapushenOnlayn)
            {
                labelProcentOnlayn.Text = ((int)bankProgress) + "%";
            }
            else
            {
                ObnovitSegmenty(bankProgress);
                labelProcentTerminal.Text = ((int)bankProgress) + "%";
            }

            if (bankProgress < 100)
            {
                return;
            }

            bankTaymer.Stop();

            // "банк одобрил" - пишем оплату на карту КЛИЕНТА (не на первую
            // попавшуюся флешку: рядом может торчать ключ диспетчера)
            DriveInfo karta = App.UsbKarta.NaytiKartuKlienta();
            bool uspeh = karta != null && tekushiyPropusk != null && OformitOplatu(karta);

            PokazatItogBanka(uspeh);
            bankFinishTaymer.Start();
        }

        /// <summary>Итог оплаты на активном визуале: "ОДОБРЕНО" или ошибка карты.</summary>
        private void PokazatItogBanka(bool uspeh)
        {
            string klyuchStroki = uspeh ? "loc_Odobreno" : "loc_KartaNeSchitana";

            if (bankZapushenOnlayn)
            {
                // спиннер останавливаем, вместо процентов - галочка/крестик
                spinnerPovorot.BeginAnimation(RotateTransform.AngleProperty, null);
                labelProcentOnlayn.Text = uspeh ? "✓" : "✕";

                // на светлой странице неоновые цвета терминала не читаются -
                // берём тёмно-зелёный/красный, как на реальных веб-страницах
                SolidColorBrush itogovayaKist = new SolidColorBrush(uspeh
                    ? Color.FromRgb(0x1B, 0x87, 0x3F)
                    : Color.FromRgb(0xD8, 0x43, 0x15));
                labelBankStatusOnlayn.SetResourceReference(TextBlock.TextProperty, klyuchStroki);
                labelBankStatusOnlayn.Foreground = itogovayaKist;
                spinnerDuga.Stroke = itogovayaKist;
            }
            else
            {
                labelBankStatusTerminal.SetResourceReference(TextBlock.TextProperty, klyuchStroki);
                labelBankStatusTerminal.Foreground =
                    (Brush)FindResource(uspeh ? "UspehBrush" : "OshibkaBrush");
                labelProcentTerminal.Text = uspeh ? "✓" : "✕";

                if (!uspeh)
                {
                    // ошибка: вся шкала перекрашивается в оранжевый (запрет)
                    for (int i = 0; i < KOLVO_SEGMENTOV; i++)
                    {
                        segmentyBanka[i].Background = (Brush)FindResource("OshibkaBrush");
                    }
                }
            }
        }

        private void BankFinishTaymer_Tick(object otpravitel, EventArgs argumenty)
        {
            bankFinishTaymer.Stop();
            spinnerPovorot.BeginAnimation(RotateTransform.AngleProperty, null); // на случай, если ещё крутится
            overlayBank.Visibility = Visibility.Collapsed;
            ProveritKartu(); // перечитываем карту - покажет "УЖЕ ОПЛАЧЕНО" или ошибку
        }

        // ---------------- диспетчер (льготы) ----------------

        private void BtnDispetcher_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            zhdemDispKlyuch = true;
            labelDispStatus.SetResourceReference(TextBlock.TextProperty, "loc_DispVyzvan");
            overlayDispetcher.Visibility = Visibility.Visible; // блокируем весь интерфейс
            taymerOzhidaniyaDisp.Start(); // если диспетчер "не услышал" - сдаёмся через минуту

            // ключ диспетчера мог УЖЕ торчать в считывателе (диспетчер стоит рядом) -
            // не заставляем его передёргивать флешку
            PrinyatKlyuchDispetcheraEsliEst();
        }

        /// <summary>
        /// Общая точка принятия ключа диспетчера (и по событию вставки, и по
        /// проверке "а вдруг уже вставлен"). Ищет ключ АДРЕСНО среди всех флешек:
        /// карта клиента может быть вставлена одновременно с ключом, и раньше
        /// терминал хватал первую попавшуюся флешку и не узнавал в ней ключ.
        /// Если карта клиента уже в считывателе - льгота оформляется сразу,
        /// без ожидания отдельного события вставки (ровно тот сценарий с двумя
        /// флешками, на котором тестирование поймало ошибку).
        /// </summary>
        private void PrinyatKlyuchDispetcheraEsliEst()
        {
            if (!zhdemDispKlyuch)
            {
                return;
            }
            if (App.UsbKarta.NaytiKartuTipa(TipKarty.KlyuchDispetchera) == null)
            {
                return; // ключа среди вставленных флешек нет - продолжаем ждать
            }

            zhdemDispKlyuch = false;
            lgotaAktivna = true;
            taymerOzhidaniyaDisp.Stop(); // диспетчер откликнулся - таймаут больше не нужен
            // просим вставить карту клиента для записи бесплатного выезда
            labelDispStatus.SetResourceReference(TextBlock.TextProperty, "loc_VstavteKartuLgota");

            // карта клиента могла быть вставлена ЗАРАНЕЕ - оформляем не дожидаясь
            if (App.UsbKarta.NaytiKartuKlienta() != null)
            {
                overlayDispetcher.Visibility = Visibility.Collapsed;
                ProveritKartu();
            }
        }

        /// <summary>
        /// Esc ИЛИ истекшая минута ожидания - отменяем вызов диспетчера и
        /// возвращаемся к обычному расчёту (человеческий фактор: диспетчер мог
        /// не услышать вызов, ждать его вечно клиент не должен).
        /// </summary>
        private void OtmenitVyzovDispetchera()
        {
            taymerOzhidaniyaDisp.Stop();
            zhdemDispKlyuch = false;
            overlayDispetcher.Visibility = Visibility.Collapsed;
            ProveritKartu(); // перечитываем карту - покажет обычный расчёт, если карта на месте
        }

        private void TaymerOzhidaniyaDisp_Tick(object otpravitel, EventArgs argumenty)
        {
            OtmenitVyzovDispetchera();
        }

        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs argumenty)
        {
            // Esc на странице онлайн-оплаты = кнопка "Отмена"
            if (argumenty.Key == System.Windows.Input.Key.Escape &&
                overlayOnlayn.Visibility == Visibility.Visible)
            {
                overlayOnlayn.Visibility = Visibility.Collapsed;
                argumenty.Handled = true;
                return;
            }

            // Esc - закрыть панель ожидания диспетчера немедленно, не дожидаясь минуты.
            // Работает ТОЛЬКО пока ждём именно диспетчера (не после того, как его ключ
            // уже принят и мы ждём карту клиента для оформления льготы).
            if (argumenty.Key == System.Windows.Input.Key.Escape && zhdemDispKlyuch)
            {
                OtmenitVyzovDispetchera();
                argumenty.Handled = true;
                return;
            }
            base.OnPreviewKeyDown(argumenty);
        }

        // ---------------- вспомогательное ----------------

        private void PokazatOzhidanie()
        {
            tekushiyPropusk = null;
            panelRaschet.Visibility = Visibility.Collapsed;
            labelKartaBitaya.Visibility = Visibility.Collapsed;
            labelPodnesite.Visibility = Visibility.Visible;
        }
    }
}

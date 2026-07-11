// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// interface1.xaml.cs - ТЕРМИНАЛ №1 (въезд, выдача карты-пропуска).
//Программу разработали: Гарафутдинов А.Р.; Зайнабиддинов И.К.
//---------
// Машина состояний терминала:
//   Gotov          - ждём нажатия зелёной кнопки
//   OzhidanieKarty - кнопка нажата, ждём USB-"карту" в считывателе
//   Proezd         - карта записана, шлагбаум открыт, тикает 15 секунд
// ============================================================================

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AvtoParkingKAI.Services;

namespace AvtoParkingKAI
{
    public partial class interface1 : KioskWindow
    {
        // сколько секунд шлагбаум открыт (эмуляция проезда одного авто).
        // РОВНО 15 по ТЗ: за это время одна машина проехать успевает,
        // а вторая "паровозиком" уже вряд ли
        private const int SEKUND_NA_PROEZD = 15;

        // состояния терминала (см. шапку файла)
        private enum Sostoyanie { Gotov, OzhidanieKarty, Proezd }

        private Sostoyanie tekusheeSostoyanie = Sostoyanie.Gotov;
        private DispatcherTimer taymerProezda;   // обратный отсчёт шлагбаума
        private int ostalosSekund;

        public interface1()
        {
            InitializeComponent();
            App.ZagruzitLogotip(imgLogo); // заглушка: kai.png кладётся рядом с exe
            PodklyuchitStorozhVremeni(overlayVremya);
            // контрастное выделение выбранного элемента (дополняет стили:
            // цвет рамки - противоположный цвету элемента, см. VydelenieService)
            VydelenieService.Podklyuchit(this);

            // секундный таймер шлагбаума
            taymerProezda = new DispatcherTimer();
            taymerProezda.Interval = TimeSpan.FromSeconds(1);
            taymerProezda.Tick += TaymerProezda_Tick;

            // карту могут вставить в любой момент - слушаем USB-сервис
            App.UsbKarta.KartaVstavlena += UsbKarta_KartaVstavlena;

            // водитель вынул только что записанную карту - значит забрал её и
            // уезжает, дальше ждать нечего: закрываем шлагбаум досрочно, чтобы
            // следующая машина сразу могла получить свою карту-пропуск
            App.UsbKarta.KartaIzvlechena += UsbKarta_KartaIzvlechena;

            // цена за час могла поменяться обладателем - освежаем каждую секунду
            App.Vremya.SekundnyTik += ObnovitCenu;
            ObnovitCenu();

            // при возврате в этот вид работы из другого (через EditPanel) окно
            // могло пропустить вставку карты, пока было спрятано - дочитываем
            IsVisibleChanged += (otpravitel, argumenty) =>
            {
                if (IsVisible && tekusheeSostoyanie == Sostoyanie.OzhidanieKarty)
                {
                    DriveInfo karta = App.UsbKarta.NaytiKartuDlyaVydachi();
                    if (karta != null)
                    {
                        VydatKartu(karta);
                    }
                }
            };
        }

        /// <summary>Ctrl+Enter активирует главную зелёную кнопку.</summary>
        protected override Button KnopkaPoUmolchaniyu()
        {
            return btnPoluchit;
        }

        private void ObnovitCenu()
        {
            runCena.Text = Properties.Settings.Default.CenaZaChas.ToString("0.##");
        }

        private void BtnYazyk_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            LanguageManager.PereklyuchitYazyk();
        }

        // ---------------- главная кнопка ----------------

        private void BtnPoluchit_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            if (tekusheeSostoyanie != Sostoyanie.Gotov)
            {
                return; // защита от дурака: двойное нажатие ничего не ломает
            }

            tekusheeSostoyanie = Sostoyanie.OzhidanieKarty;
            btnPoluchit.IsEnabled = false;
            labelOshibka.Visibility = Visibility.Collapsed;
            labelOzhidanie.Visibility = Visibility.Visible;

            // если "карта" уже торчит в считывателе - пишем сразу
            // (NaytiKartuDlyaVydachi: при нескольких флешках предпочитает пустую,
            // чтобы не переписывать без нужды чужой пропуск или ключ диспетчера)
            DriveInfo gotovayaKarta = App.UsbKarta.NaytiKartuDlyaVydachi();
            if (gotovayaKarta != null)
            {
                VydatKartu(gotovayaKarta);
            }
        }

        // ---------------- события USB ----------------

        private void UsbKarta_KartaVstavlena()
        {
            // окно спрятано (обладатель переключил вид работы) - не вмешиваемся,
            // событием займётся видимый сейчас терминал
            if (!IsVisible)
            {
                return;
            }

            // реагируем только если терминал ЖДЁТ карту
            if (tekusheeSostoyanie != Sostoyanie.OzhidanieKarty)
            {
                return;
            }

            DriveInfo karta = App.UsbKarta.NaytiKartuDlyaVydachi();
            if (karta != null)
            {
                VydatKartu(karta);
            }
        }

        /// <summary>
        /// Карта вынута из считывателя. Если шлагбаум как раз открыт (карту только
        /// что выдали) - водитель забрал её и уезжает, ждать полные 15 секунд не нужно,
        /// терминал сразу готов к следующей машине.
        /// </summary>
        private void UsbKarta_KartaIzvlechena()
        {
            if (!IsVisible)
            {
                return;
            }

            if (tekusheeSostoyanie == Sostoyanie.Proezd)
            {
                ZakrytShlagbaum();
            }
        }

        /// <summary>
        /// Запись пропуска на карту. Время заезда берём из ЗАЩИЩЁННЫХ часов -
        /// перевод стрелок ОС на расчёт не влияет.
        /// </summary>
        private void VydatKartu(DriveInfo karta)
        {
            PropuskData propusk = PropuskData.NovyPropusk(App.Vremya.Seychas());
            bool uspeh = App.UsbKarta.VydatNovyPropusk(karta, propusk);

            labelOzhidanie.Visibility = Visibility.Collapsed;

            if (!uspeh)
            {
                // флешка защищена от записи/битая: сообщаем и возвращаемся в готовность
                labelOshibka.Visibility = Visibility.Visible;
                tekusheeSostoyanie = Sostoyanie.Gotov;
                btnPoluchit.IsEnabled = true;
                return;
            }

            // карта записана: открываем шлагбаум
            tekusheeSostoyanie = Sostoyanie.Proezd;
            labelNeZabudte.Visibility = Visibility.Visible;   // КРУПНЫЙ текст
            labelProezzhayte.Visibility = Visibility.Visible; // текст поменьше
            labelTaymer.Visibility = Visibility.Visible;

            ostalosSekund = SEKUND_NA_PROEZD;
            labelTaymer.Text = ostalosSekund.ToString();
            taymerProezda.Start();
        }

        // ---------------- шлагбаум ----------------

        private void TaymerProezda_Tick(object otpravitel, EventArgs argumenty)
        {
            ostalosSekund = ostalosSekund - 1;
            labelTaymer.Text = ostalosSekund.ToString();

            if (ostalosSekund <= 0)
            {
                ZakrytShlagbaum();
            }
        }

        /// <summary>
        /// Скрытая кнопка-датчик (левый нижний угол): эмулирует датчик проезда
        /// авто и закрывает шлагбаум досрочно, не дожидаясь 15 секунд.
        /// </summary>
        private void BtnDatchikProezda_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            if (tekusheeSostoyanie == Sostoyanie.Proezd)
            {
                ZakrytShlagbaum();
            }
        }

        private void ZakrytShlagbaum()
        {
            taymerProezda.Stop();
            labelNeZabudte.Visibility = Visibility.Collapsed;
            labelProezzhayte.Visibility = Visibility.Collapsed;
            labelTaymer.Visibility = Visibility.Collapsed;
            tekusheeSostoyanie = Sostoyanie.Gotov;
            btnPoluchit.IsEnabled = true;
        }
    }
}

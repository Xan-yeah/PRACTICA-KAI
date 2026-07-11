// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// interface3.xaml.cs - ТЕРМИНАЛ №3 (выезд).
//
// Правила выпуска машины (проверка по ЗАЩИЩЁННЫМ часам, не по часам ОС!):
//   1) карта оплачена И (тек.время <= время оплаты + N минут)  -> ПРИНЯТО
//   2) карта НЕ оплачена, но стоянка <= M бесплатных минут     -> ПРИНЯТО
//   3) всё остальное (не оплатил / просрочил выезд после оплаты,
//      карта битая/чужая)                                       -> ОТКАЗАНО
// После "ПРИНЯТО" пропуск СТИРАЕТСЯ с карты - карта возвращается в оборот.
// ============================================================================
//
// --- история рассуждений (черновик) ---
// v1: сравнивал с DateTime.Now - перевёл часы назад и выехал бесплатно.
//     Теперь только App.Vremya.Seychas() (NTP + TickCount).
// v2: забыл случай "оплатил и уснул на 2 часа" - выезжал свободно.
//     Переделано дома под все условия: после оплаты на выезд даётся
//     ровно N минут (настройка обладателя), дальше - снова к терминалу 2.
// --------------------------------------

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AvtoParkingKAI.Services;

namespace AvtoParkingKAI
{
    public partial class interface3 : KioskWindow
    {
        private const int SEKUND_NA_PROEZD = 15;  // шлагбаум открыт, как на въезде
        private const int SEKUND_NA_OTKAZ = 6;    // сколько висит "ОТКАЗАНО"

        private DispatcherTimer taymerSbrosa;     // возврат к режиму ожидания
        private int ostalosSekund;
        private bool shlagbaumOtkryt;

        public interface3()
        {
            InitializeComponent();
            App.ZagruzitLogotip(imgLogo); // заглушка: kai.png кладётся рядом с exe
            PodklyuchitStorozhVremeni(overlayVremya);
            // контрастное выделение выбранного элемента (дополняет стили:
            // цвет рамки - противоположный цвету элемента, см. VydelenieService)
            VydelenieService.Podklyuchit(this);

            taymerSbrosa = new DispatcherTimer();
            taymerSbrosa.Interval = TimeSpan.FromSeconds(1);
            taymerSbrosa.Tick += TaymerSbrosa_Tick;

            App.UsbKarta.KartaVstavlena += UsbKarta_KartaVstavlena;
            App.UsbKarta.KartaIzvlechena += UsbKarta_KartaIzvlechena;

            // при КАЖДОМ показе окна (первый запуск или возврат из другого вида
            // работы через EditPanel) перечитываем считыватель - карта могла быть
            // вставлена заранее или пока окно было спрятано. Раньше проверка была
            // разовым вызовом из конструктора, но окна-режимы теперь
            // переиспользуются (App.PokazatOknoRezhima), и разовой проверки мало.
            IsVisibleChanged += (otpravitel, argumenty) =>
            {
                if (IsVisible)
                {
                    UsbKarta_KartaVstavlena();
                }
            };
        }

        private void BtnYazyk_Click(object otpravitel, RoutedEventArgs argumenty)
        {
            LanguageManager.PereklyuchitYazyk();
        }

        // ---------------- проверка карты ----------------

        private void UsbKarta_KartaVstavlena()
        {
            // окно спрятано (обладатель переключил вид работы) - не вмешиваемся
            if (!IsVisible)
            {
                return;
            }

            if (shlagbaumOtkryt)
            {
                return; // уже выпускаем машину, новые карты подождут
            }

            // адресный поиск карты КЛИЕНТА (ключ диспетчера, если он тоже
            // вставлен, картой клиента не считается - см. UsbKartaService)
            DriveInfo karta = App.UsbKarta.NaytiKartuKlienta();
            if (karta == null)
            {
                return;
            }

            PropuskData propusk = App.UsbKarta.ProchitatPropusk(karta);
            if (propusk == null)
            {
                // на флешке нет пропуска / он подделан - от ворот поворот
                PokazatOtkaz();
                return;
            }

            DateTime seychas = App.Vremya.Seychas();

            // сами правила выезда - чистая функция в RaschetService (покрыта
            // unit-тестами), здесь только считаем интервалы по защищённым часам.
            // M и N хранятся в единицах, выбранных обладателем - App переводит в минуты
            double minutStoyanki = (seychas - propusk.GetVremyaZaezda()).TotalMinutes;
            double minutPosleOplaty = propusk.IsPaid
                ? (seychas - propusk.GetVremyaOplaty()).TotalMinutes
                : 0.0;

            if (RaschetService.RazreshenVyezd(propusk.IsPaid, minutPosleOplaty, minutStoyanki,
                                              App.BesplatnoMvMinutah(), App.VyezdNvMinutah()))
            {
                App.UsbKarta.UdalitPropusk(karta); // карта "гасится" и идёт в оборот
                PokazatPrinyato();
            }
            else
            {
                PokazatOtkaz();
            }
        }

        private void UsbKarta_KartaIzvlechena()
        {
            if (!IsVisible)
            {
                return;
            }

            // пока шлагбаум открыт - отсчёт не трогаем (машина едет!)
            if (!shlagbaumOtkryt)
            {
                VernutsyaVOzhidanie();
            }
        }

        // ---------------- вердикты ----------------

        private void PokazatPrinyato()
        {
            labelPodnesite.Visibility = Visibility.Collapsed;

            labelVerdikt.SetResourceReference(TextBlock.TextProperty, "loc_Prinyato");
            labelVerdikt.Foreground = (Brush)FindResource("UspehBrush");
            labelVerdikt.Visibility = Visibility.Visible;

            labelPoyasnenie.SetResourceReference(TextBlock.TextProperty, "loc_Priezzhayte");
            labelPoyasnenie.Foreground = (Brush)FindResource("UspehBrush");
            labelPoyasnenie.Visibility = Visibility.Visible;

            shlagbaumOtkryt = true;
            ostalosSekund = SEKUND_NA_PROEZD;
            labelTaymer.Text = ostalosSekund.ToString();
            labelTaymer.Visibility = Visibility.Visible;
            taymerSbrosa.Start();
        }

        private void PokazatOtkaz()
        {
            labelPodnesite.Visibility = Visibility.Collapsed;

            labelVerdikt.SetResourceReference(TextBlock.TextProperty, "loc_Otkazano");
            labelVerdikt.Foreground = (Brush)FindResource("OshibkaBrush");
            labelVerdikt.Visibility = Visibility.Visible;

            labelPoyasnenie.SetResourceReference(TextBlock.TextProperty, "loc_KTerminalu2");
            labelPoyasnenie.Foreground = (Brush)FindResource("OshibkaBrush");
            labelPoyasnenie.Visibility = Visibility.Visible;

            shlagbaumOtkryt = false;
            ostalosSekund = SEKUND_NA_OTKAZ;
            labelTaymer.Visibility = Visibility.Collapsed;
            taymerSbrosa.Start();
        }

        private void TaymerSbrosa_Tick(object otpravitel, EventArgs argumenty)
        {
            ostalosSekund = ostalosSekund - 1;

            if (shlagbaumOtkryt)
            {
                labelTaymer.Text = ostalosSekund.ToString();
            }

            if (ostalosSekund <= 0)
            {
                VernutsyaVOzhidanie();
            }
        }

        private void VernutsyaVOzhidanie()
        {
            taymerSbrosa.Stop();
            shlagbaumOtkryt = false;
            labelVerdikt.Visibility = Visibility.Collapsed;
            labelPoyasnenie.Visibility = Visibility.Collapsed;
            labelTaymer.Visibility = Visibility.Collapsed;
            labelPodnesite.Visibility = Visibility.Visible;
        }
    }
}

// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// UsbKartaService.cs - эмуляция карты-пропуска через USB-накопитель.
//Программу разработали: Гарафутдинов А.Р.; Зайнабиддинов И.К.
//---------
// По ТЗ у нас НЕТ ни БД, ни сети, ни NFC-считывателя. Карта = флешка.
// Подключение/извлечение флешки ловим через WMI-событие
// Win32_VolumeChangeEvent (ManagementEventWatcher):
//   EventType = 2 -> устройство подключено
//   EventType = 3 -> устройство извлечено
// Событийная модель выбрана вместо опроса дисков таймером: опрос грузит
// диск и даёт задержку, а WMI сообщает о флешке мгновенно. События WMI
// приходят из фонового потока, поэтому пробрасываются в UI-поток через
// Dispatcher.BeginInvoke - иначе WPF кидает InvalidOperationException.
// На флешке лежит СКРЫТЫЙ файл pass.dat: AES(JSON(PropuskData)).
// У диспетчера своя флешка с файлом disp_key.dat (хэш секретной фразы).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Threading;

namespace AvtoParkingKAI.Services
{
    /// <summary>
    /// Какой ролью на флешке сейчас занята данная флешка. Пропуск и ключ диспетчера -
    /// взаимоисключающие роли (см. VydatNovyPropusk/SozdatKlyuchDispetchera): одна и
    /// та же флешка не может одновременно быть и картой клиента, и ключом диспетчера,
    /// иначе терминалы путаются, какую именно роль ей приписывать.
    /// </summary>
    public enum TipKarty
    {
        Neizvestno,
        Propusk,
        KlyuchDispetchera
    }

    /// <summary>
    /// Сервис работы с "картой" (USB-флешкой). Один на всё приложение (App.UsbKarta).
    /// </summary>
    public class UsbKartaService
    {
        private const string IMYA_FAYLA = "pass.dat";       // скрытый файл пропуска
        private const string IMYA_DISP_FAYLA = "disp_key.dat"; // ключ диспетчера

        private ManagementEventWatcher vstavkaWatcher;      // EventType = 2
        private ManagementEventWatcher izvlechenieWatcher;  // EventType = 3

        // резервный опрос дисков на случай, если WMI не поднялся (политики
        // безопасности, урезанный образ Windows): без него терминал навсегда
        // терял бы событие "флешка вставлена"
        private DispatcherTimer rezervnyTaymer;
        private List<string> proshlyeDiski;                 // снимок съёмных дисков для сравнения

        /// <summary>Флешка вставлена (событие уже в UI-потоке).</summary>
        public event Action KartaVstavlena;

        /// <summary>Флешка извлечена (событие уже в UI-потоке).</summary>
        public event Action KartaIzvlechena;

        /// <summary>
        /// Запуск наблюдателей WMI. Вызывается один раз из App.OnStartup.
        /// </summary>
        public void Start()
        {
            try
            {
                // подписка на ПОДКЛЮЧЕНИЕ тома
                vstavkaWatcher = new ManagementEventWatcher(
                    new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2"));
                vstavkaWatcher.EventArrived += (otpravitel, argumenty) => ProbrositVUiPotok(KartaVstavlena);
                vstavkaWatcher.Start();

                // подписка на ИЗВЛЕЧЕНИЕ тома
                izvlechenieWatcher = new ManagementEventWatcher(
                    new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 3"));
                izvlechenieWatcher.EventArrived += (otpravitel, argumenty) => ProbrositVUiPotok(KartaIzvlechena);
                izvlechenieWatcher.Start();
            }
            catch (Exception)
            {
                // WMI отключён политиками / урезанная сборка Windows - терминал не
                // имеет права "ослепнуть": переходим на резервный опрос дисков.
                // Медленнее WMI (флешка ловится с лагом до ~1.5 сек), зато работает везде.
                ZapustitRezervnyOpros();
            }
        }

        /// <summary>Остановка наблюдателей (при завершении программы обладателем).</summary>
        public void Stop()
        {
            try { if (vstavkaWatcher != null) vstavkaWatcher.Stop(); } catch (Exception) { }
            try { if (izvlechenieWatcher != null) izvlechenieWatcher.Stop(); } catch (Exception) { }
            try { if (rezervnyTaymer != null) rezervnyTaymer.Stop(); } catch (Exception) { }
        }

        // ================= резервный опрос (fallback без WMI) =================

        /// <summary>
        /// Опрашиваем диски раз в 1.5 секунды и сравниваем со снимком: диск
        /// появился - генерируем KartaVstavlena, пропал - KartaIzvlechena.
        /// Start() вызывается из UI-потока (App.OnStartup), поэтому DispatcherTimer
        /// тикает сразу в UI-потоке - проброс через Dispatcher не нужен.
        /// </summary>
        private void ZapustitRezervnyOpros()
        {
            proshlyeDiski = SnimokSyemnyhDiskov(); // уже вставленные при старте не считаем "событием"

            rezervnyTaymer = new DispatcherTimer();
            rezervnyTaymer.Interval = TimeSpan.FromSeconds(1.5);
            rezervnyTaymer.Tick += RezervnyTaymer_Tick;
            rezervnyTaymer.Start();
        }

        /// <summary>Имена корней всех готовых съёмных дисков (для сравнения снимков).</summary>
        private static List<string> SnimokSyemnyhDiskov()
        {
            List<string> imena = new List<string>();
            foreach (DriveInfo disk in VseKarty())
            {
                imena.Add(disk.Name);
            }
            return imena;
        }

        private void RezervnyTaymer_Tick(object otpravitel, EventArgs argumenty)
        {
            List<string> tekushieDiski = SnimokSyemnyhDiskov();

            bool poyavilsya = tekushieDiski.Any(imya => !proshlyeDiski.Contains(imya));
            bool ischez = proshlyeDiski.Any(imya => !tekushieDiski.Contains(imya));
            proshlyeDiski = tekushieDiski;

            if (poyavilsya && KartaVstavlena != null)
            {
                KartaVstavlena();
            }
            if (ischez && KartaIzvlechena != null)
            {
                KartaIzvlechena();
            }
        }

        /// <summary>
        /// WMI стучится из фонового потока, а трогать окна можно только из
        /// UI-потока - поэтому аккуратно перекидываем через Dispatcher.
        /// </summary>
        private static void ProbrositVUiPotok(Action sobytie)
        {
            if (sobytie == null) return;
            Application prilozhenie = Application.Current;
            if (prilozhenie != null)
            {
                prilozhenie.Dispatcher.BeginInvoke(sobytie);
            }
        }

        /// <summary>
        /// Все готовые съёмные флешки. Их может быть НЕСКОЛЬКО одновременно
        /// (карта клиента + ключ диспетчера на оплате льготы) - поэтому все
        /// "умные" поиски ниже работают по этому списку, а не по первой попавшейся.
        /// </summary>
        private static DriveInfo[] VseKarty()
        {
            // защита от дурака: IsReady может кинуть IOException если флешку
            // выдернули прямо в момент опроса - глотаем и идём дальше
            try
            {
                return DriveInfo.GetDrives()
                    .Where(disk => disk.DriveType == DriveType.Removable && disk.IsReady)
                    .ToArray();
            }
            catch (Exception)
            {
                return new DriveInfo[0];
            }
        }

        /// <summary>
        /// Ищем первую готовую съёмную флешку. Null = карты нет.
        /// </summary>
        public DriveInfo NaytiKartu()
        {
            return VseKarty().FirstOrDefault();
        }

        /// <summary>
        /// Первая флешка нужной роли. По правилам доработки в устройстве
        /// одновременно может быть максимум ОДНА карта-пропуск и максимум
        /// ОДИН ключ диспетчера, поэтому "первая" = "единственная".
        /// </summary>
        public DriveInfo NaytiKartuTipa(TipKarty tip)
        {
            return VseKarty().FirstOrDefault(karta => OpredelitTipKarty(karta) == tip);
        }

        /// <summary>
        /// Карта КЛИЕНТА для терминалов оплаты/выезда: сначала ищем флешку с
        /// пропуском; если такой нет - берём первую "пустую" (чтобы честно
        /// показать "карта не считана"). Ключ диспетчера картой клиента не
        /// считается НИКОГДА - иначе при двух вставленных флешках (ключ + карта
        /// клиента) терминал хватал первую попавшуюся и падал с ошибкой
        /// (ровно этот баг всплыл при тестировании оплаты со льготой).
        /// </summary>
        public DriveInfo NaytiKartuKlienta()
        {
            DriveInfo[] karty = VseKarty();
            return karty.FirstOrDefault(karta => OpredelitTipKarty(karta) == TipKarty.Propusk)
                ?? karty.FirstOrDefault(karta => OpredelitTipKarty(karta) != TipKarty.KlyuchDispetchera);
        }

        /// <summary>
        /// Флешка для ВЫДАЧИ нового пропуска (терминал №1): сначала пустая,
        /// потом с прежним пропуском (перезапишем), и лишь в самом крайнем
        /// случае - ключ диспетчера (его роль будет заменена на пропуск,
        /// см. VydatNovyPropusk). Такой порядок гарантирует, что при
        /// нескольких флешках мы не испортим ключ диспетчера без нужды.
        /// </summary>
        public DriveInfo NaytiKartuDlyaVydachi()
        {
            DriveInfo[] karty = VseKarty();
            return karty.FirstOrDefault(karta => OpredelitTipKarty(karta) == TipKarty.Neizvestno)
                ?? karty.FirstOrDefault(karta => OpredelitTipKarty(karta) == TipKarty.Propusk)
                ?? karty.FirstOrDefault();
        }

        /// <summary>
        /// Флешка для записи КЛЮЧА ДИСПЕТЧЕРА (EditPanel): сначала уже
        /// существующий ключ (обычная перезапись), потом пустая, и в последнюю
        /// очередь карта-пропуск (её роль будет заменена на ключ,
        /// см. SozdatKlyuchDispetchera).
        /// </summary>
        public DriveInfo NaytiKartuDlyaKlyucha()
        {
            DriveInfo[] karty = VseKarty();
            return karty.FirstOrDefault(karta => OpredelitTipKarty(karta) == TipKarty.KlyuchDispetchera)
                ?? karty.FirstOrDefault(karta => OpredelitTipKarty(karta) == TipKarty.Neizvestno)
                ?? karty.FirstOrDefault();
        }

        /// <summary>Полный путь к pass.dat на конкретной флешке.</summary>
        private static string PutKPropusku(DriveInfo karta)
        {
            return Path.Combine(karta.RootDirectory.FullName, IMYA_FAYLA);
        }

        /// <summary>Полный путь к disp_key.dat на конкретной флешке.</summary>
        private static string PutKDispKlyuchu(DriveInfo karta)
        {
            return Path.Combine(karta.RootDirectory.FullName, IMYA_DISP_FAYLA);
        }

        /// <summary>
        /// Тихо стереть ровно один свой служебный файл (pass.dat ИЛИ disp_key.dat),
        /// если он есть. Флешка не форматируется, посторонние файлы (фото/видео/
        /// документы) не трогаются - используется только для взаимоисключения ролей
        /// флешки в VydatNovyPropusk/SozdatKlyuchDispetchera.
        /// </summary>
        private static void TihoUdalitFayl(string put)
        {
            try
            {
                if (File.Exists(put))
                {
                    File.SetAttributes(put, FileAttributes.Normal);
                    File.Delete(put);
                }
            }
            catch (Exception)
            {
                // не смогли стереть чужую роль - не критично, ниже всё равно
                // попробуем записать свою (в худшем случае перезапишется позже)
            }
        }

        /// <summary>
        /// Выдача СВЕЖЕГО пропуска терминалом №1 (в отличие от ZapisatPropusk, которым
        /// терминал №2 просто помечает уже существующий на карте пропуск оплаченным).
        /// Перед записью тихо стирает disp_key.dat, если он есть на этой же флешке -
        /// одна флешка не может одновременно быть и пропуском клиента, и ключом
        /// диспетчера (иначе ровно тот конфликт, что нашёлся при тестировании: терминал
        /// оплаты путается, какая перед ним карта). Флешка НЕ форматируется - трогаем
        /// только свой файл disp_key.dat, посторонние файлы на флешке не затрагиваются.
        /// </summary>
        public bool VydatNovyPropusk(DriveInfo karta, PropuskData propusk)
        {
            TihoUdalitFayl(PutKDispKlyuchu(karta));
            return ZapisatPropusk(karta, propusk);
        }

        /// <summary>
        /// Записать пропуск на карту: JSON -> AES -> скрытый файл.
        /// Возвращает true при успехе.
        /// </summary>
        public bool ZapisatPropusk(DriveInfo karta, PropuskData propusk)
        {
            try
            {
                string put = PutKPropusku(karta);

                // ВАЖНЫЙ подводный камень: File.WriteAllText не умеет
                // перезаписывать файл с атрибутом Hidden (UnauthorizedAccessException),
                // поэтому перед записью атрибуты снимаем
                if (File.Exists(put))
                {
                    File.SetAttributes(put, FileAttributes.Normal);
                }

                JavaScriptSerializer serializator = new JavaScriptSerializer();
                string json = serializator.Serialize(propusk);
                File.WriteAllText(put, BezopasnostService.Shifrovat(json));

                // прячем файл от любопытных глаз
                File.SetAttributes(put, FileAttributes.Hidden);
                return true;
            }
            catch (Exception)
            {
                return false; // флешка защищена от записи / выдернута и т.п.
            }
        }

        /// <summary>
        /// Прочитать пропуск с карты. Null = файла нет или он битый/подделанный.
        /// </summary>
        public PropuskData ProchitatPropusk(DriveInfo karta)
        {
            try
            {
                string put = PutKPropusku(karta);
                if (!File.Exists(put))
                {
                    return null;
                }

                string json = BezopasnostService.Rasshifrovat(File.ReadAllText(put));
                if (json == null)
                {
                    return null; // расшифровка не удалась - карта подделана?
                }

                JavaScriptSerializer serializator = new JavaScriptSerializer();
                return serializator.Deserialize<PropuskData>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Стереть пропуск с карты (на выезде карта "гасится" и возвращается
        /// в оборот терминала №1).
        /// </summary>
        public void UdalitPropusk(DriveInfo karta)
        {
            try
            {
                string put = PutKPropusku(karta);
                if (File.Exists(put))
                {
                    File.SetAttributes(put, FileAttributes.Normal);
                    File.Delete(put);
                }
            }
            catch (Exception)
            {
                // не смогли стереть - не критично, при следующей записи перезапишется
            }
        }

        /// <summary>
        /// Проверка: это флешка ДИСПЕТЧЕРА? (файл disp_key.dat с верным хэшем).
        /// Нужна для льготного (бесплатного) проезда инвалидов.
        /// </summary>
        public bool EtoKlyuchDispetchera(DriveInfo karta)
        {
            try
            {
                string put = PutKDispKlyuchu(karta);
                if (!File.Exists(put))
                {
                    return false;
                }
                string soderzhimoe = File.ReadAllText(put).Trim();
                return soderzhimoe == BezopasnostService.PoluchitDispHash();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Записать ключ диспетчера на флешку (кнопка в EditPanel, чтобы
        /// обладатель мог изготовить служебную карту для демонстрации).
        /// </summary>
        public bool SozdatKlyuchDispetchera(DriveInfo karta)
        {
            try
            {
                TihoUdalitFayl(PutKPropusku(karta));

                string put = PutKDispKlyuchu(karta);
                if (File.Exists(put))
                {
                    File.SetAttributes(put, FileAttributes.Normal);
                }
                File.WriteAllText(put, BezopasnostService.PoluchitDispHash());
                File.SetAttributes(put, FileAttributes.Hidden);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Явно определяет роль флешки (пропуск клиента / ключ диспетчера / ни то ни
        /// другое) - вместо разрозненных проверок ProchitatPropusk()!=null и
        /// EtoKlyuchDispetchera() в разных местах. Внешнее поведение терминалов не
        /// меняет, просто делает различение флешек явным на будущее.
        /// </summary>
        public TipKarty OpredelitTipKarty(DriveInfo karta)
        {
            if (EtoKlyuchDispetchera(karta))
            {
                return TipKarty.KlyuchDispetchera;
            }
            if (File.Exists(PutKPropusku(karta)))
            {
                return TipKarty.Propusk;
            }
            return TipKarty.Neizvestno;
        }
    }
}

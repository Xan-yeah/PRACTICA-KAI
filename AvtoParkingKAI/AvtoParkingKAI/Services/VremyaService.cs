// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// VremyaService.cs - "защищённые часы" терминала.
//
// ЗАЧЕМ: терминал выезда сравнивает время на карте с системным временем.
// Если у ЭВМ сядет батарейка CMOS (да, время реально сбивается из CMOS -
// предположение верное!) или кто-то переведёт часы ОС, то расчёт стоимости
// сломается. Поэтому:
//   1) при старте синхронизируемся с NTP-сервером time.windows.com;
//   2) дальше время ведём сами: базовая точка + Environment.TickCount
//      (миллисекунды работы ОС - их перевести стрелками НЕЛЬЗЯ);
//   3) раз в секунду сверяем "наши" часы с DateTime.Now: если ОС внезапно
//      прыгнула больше чем на порог - это аномалия, блокируем терминал.
// ============================================================================
//
// --- история рассуждений (черновик) ---
// v1: тупо DateTime.Now везде. Перевёл часы в винде на час назад - парковка
//     стала бесплатной :) не годится.
// v2: хотел хранить "последнее время" в файле и сравнивать при старте:
//     File.WriteAllText("lasttime.txt", DateTime.Now.ToString());
//     но это не ловит перевод времени ВО ВРЕМЯ работы.
// v3 (итог): NTP + TickCount, проверка каждую секунду таймером.
//     Переделано дома под все условия.
// --------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace AvtoParkingKAI.Services
{
    /// <summary>
    /// Защищённые часы терминала. Один экземпляр на всё приложение (App.Vremya).
    /// </summary>
    public class VremyaService
    {
        private const string NTP_SERVER = "time.windows.com";
        // порог аномалии: если системные часы разошлись с TickCount-часами
        // больше чем на 90 секунд - считаем что время перевели/сбилось
        private const double POROG_ANOMALII_SEK = 90.0;

        // базовая точка отсчёта и тик, в который она была взята
        private DateTime bazovoeVremya;
        private int bazovyTick;

        private DispatcherTimer storozhTaymer;   // сторож, тикает раз в секунду

        /// <summary>Удалось ли синхронизироваться с NTP при старте.</summary>
        public bool NtpUspeh { get; private set; }

        /// <summary>Терминал заблокирован из-за скачка времени.</summary>
        public bool Zablokirovan { get; private set; }

        /// <summary>Событие: обнаружен скачок времени, все окна должны показать блокировку.</summary>
        public event Action VremyaSboy;

        /// <summary>Событие: секундный тик (окна подписываются, чтобы рисовать часы).</summary>
        public event Action SekundnyTik;

        /// <summary>
        /// Запуск часов. Вызывается один раз из App.OnStartup.
        /// Сначала берём DateTime.Now (чтобы часы шли сразу), потом в фоне
        /// пытаемся уточниться у NTP - интернет на парковке может тормозить,
        /// и UI ждать этого не должен.
        /// </summary>
        public void Start()
        {
            bazovoeVremya = DateTime.Now;
            bazovyTick = Environment.TickCount;
            NtpUspeh = false;

            // асинхронно тянем точное время (не блокируем окно!)
            Task.Run(() =>
            {
                DateTime? tochnoeVremya = ZaprositNtpVremya();
                if (tochnoeVremya.HasValue)
                {
                    // перебазируем часы на точное время
                    bazovoeVremya = tochnoeVremya.Value;
                    bazovyTick = Environment.TickCount;
                    NtpUspeh = true;
                }
            });

            // сторожевой таймер: сверка часов + рассылка тика окнам
            storozhTaymer = new DispatcherTimer();
            storozhTaymer.Interval = TimeSpan.FromSeconds(1);
            storozhTaymer.Tick += StorozhTaymer_Tick;
            storozhTaymer.Start();
        }

        /// <summary>
        /// Текущее ЗАЩИЩЁННОЕ время: база + сколько миллисекунд натикало с базы.
        /// unchecked-вычитание корректно переживает переполнение TickCount
        /// (он оборачивается раз в ~49.7 суток), а перебазирование в тике
        /// сторожа делает разницу всегда маленькой.
        /// </summary>
        public DateTime Seychas()
        {
            int proshloMs = unchecked(Environment.TickCount - bazovyTick);
            return bazovoeVremya.AddMilliseconds(proshloMs);
        }

        private void StorozhTaymer_Tick(object otpravitel, EventArgs argumenty)
        {
            if (!Zablokirovan)
            {
                // сверяем защищённые часы с часами ОС
                double rashozhdenieSek = Math.Abs((DateTime.Now - Seychas()).TotalSeconds);
                if (rashozhdenieSek > POROG_ANOMALII_SEK)
                {
                    // АНОМАЛИЯ: часы ОС прыгнули (CMOS сброс / перевод стрелок)
                    Zablokirovan = true;
                    if (VremyaSboy != null)
                    {
                        VremyaSboy();
                    }
                }
                else
                {
                    // всё спокойно - перебазируемся, чтобы TickCount не копил
                    // переполнение (терминал может работать месяцами!)
                    bazovoeVremya = Seychas();
                    bazovyTick = Environment.TickCount;
                }
            }

            if (SekundnyTik != null)
            {
                SekundnyTik();
            }
        }

        /// <summary>
        /// Обладатель ЛЕГАЛЬНО поменял системное время из EditPanel -
        /// принимаем новое время ОС как базу и снимаем блокировку.
        /// </summary>
        public void PrinyatNovoeVremyaOs()
        {
            bazovoeVremya = DateTime.Now;
            bazovyTick = Environment.TickCount;
            Zablokirovan = false;
        }

        /// <summary>
        /// Классический SNTP-запрос по UDP (порт 123).
        /// Возвращает локальное время или null, если сети нет.
        /// </summary>
        private static DateTime? ZaprositNtpVremya()
        {
            try
            {
                byte[] paket = new byte[48];
                paket[0] = 0x1B; // LI=0, Version=3, Mode=3 (клиент)

                IPAddress[] adresa = Dns.GetHostEntry(NTP_SERVER).AddressList;
                IPEndPoint tochka = new IPEndPoint(adresa[0], 123);

                using (Socket soket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    soket.ReceiveTimeout = 3000; // 3 секунды и хватит
                    soket.SendTimeout = 3000;
                    soket.Connect(tochka);
                    soket.Send(paket);
                    soket.Receive(paket);
                }

                // время передачи сервером лежит в байтах 40..47 (big-endian)
                ulong sekundyCh = ((ulong)paket[40] << 24) | ((ulong)paket[41] << 16) | ((ulong)paket[42] << 8) | paket[43];
                ulong dolyaCh = ((ulong)paket[44] << 24) | ((ulong)paket[45] << 16) | ((ulong)paket[46] << 8) | paket[47];
                ulong millisek = (sekundyCh * 1000) + ((dolyaCh * 1000) / 0x100000000UL);

                // эпоха NTP = 01.01.1900 UTC
                DateTime epoha = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                DateTime vremyaUtc = epoha.AddMilliseconds((double)millisek);
                return vremyaUtc.ToLocalTime();
            }
            catch (Exception)
            {
                // нет интернета - не страшно, работаем от часов ОС
                return null;
            }
        }
    }
}

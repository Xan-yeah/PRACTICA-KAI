// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// PropuskData.cs - модель карты-пропуска (то, что лежит в pass.dat на флешке)
// ============================================================================
//
// --- история рассуждений (первый черновик, оставил для отчёта) ---
// сначала хотел писать на флешку просто текстовый файл в 3 строки:
//   File.WriteAllLines("E:\\pass.txt", new[] { id, vremya.ToString(), "0" });
//   ...
//   var stroki = File.ReadAllLines("E:\\pass.txt");
//   var vremya = DateTime.Parse(stroki[1]);   // <- падало из-за региональных настроек!
// потом понял что DateTime.Parse зависит от культуры винды и на другом
// терминале время читалось неправильно. Переделано дома под все условия:
// теперь храню тики (long) - они не зависят ни от языка ни от часового
// пояса терминала, а сам файл сериализуется в JSON и шифруется AES,
// чтобы хитрый водитель не подменил время заезда блокнотом.
// ------------------------------------------------------------------

using System;

namespace AvtoParkingKAI.Services
{
    /// <summary>
    /// Содержимое карты-пропуска. Сериализуется в JSON (JavaScriptSerializer),
    /// шифруется и пишется в скрытый файл pass.dat на USB-накопитель.
    /// ВАЖНО: JavaScriptSerializer работает со СВОЙСТВАМИ, поэтому тут
    /// именно свойства, а не поля.
    /// </summary>
    public class PropuskData
    {
        /// <summary>Уникальный ID пропуска (GUID). Выдаётся терминалом №1.</summary>
        public string Id { get; set; }

        /// <summary>Время заезда в тиках (DateTime.Ticks) - не зависит от культуры ОС.</summary>
        public long VremyaZaezdaTicks { get; set; }

        /// <summary>Флаг оплаты. Ставится терминалом №2 (или диспетчером по льготе).</summary>
        public bool IsPaid { get; set; }

        /// <summary>Метка времени оплаты (тики). 0 = ещё не оплачено.</summary>
        public long VremyaOplatyTicks { get; set; }

        // -------- удобные методы-обёртки (в JSON не попадают) --------

        /// <summary>Время заезда как нормальный DateTime.</summary>
        public DateTime GetVremyaZaezda()
        {
            return new DateTime(VremyaZaezdaTicks);
        }

        /// <summary>Время оплаты как DateTime (проверяйте IsPaid перед вызовом!).</summary>
        public DateTime GetVremyaOplaty()
        {
            return new DateTime(VremyaOplatyTicks);
        }

        /// <summary>Фабрика нового пропуска: вызывается на терминале въезда.</summary>
        public static PropuskData NovyPropusk(DateTime vremyaZaezda)
        {
            PropuskData propusk = new PropuskData();
            propusk.Id = Guid.NewGuid().ToString("N");
            propusk.VremyaZaezdaTicks = vremyaZaezda.Ticks;
            propusk.IsPaid = false;
            propusk.VremyaOplatyTicks = 0;
            return propusk;
        }
    }
}

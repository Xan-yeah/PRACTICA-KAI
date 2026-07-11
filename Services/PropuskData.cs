// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// PropuskData.cs - модель карты-пропуска (то, что лежит в pass.dat на флешке)
//Программу разработали: Гарафутдинов А.Р.; Зайнабиддинов И.К.
//---------
// Время храним ТИКАМИ (long), а не строкой: DateTime.Parse зависит от
// региональных настроек Windows, и карта, записанная на одном терминале,
// на другом читалась бы неправильно. Тики от культуры ОС не зависят.
// Сам файл сериализуется в JSON и шифруется AES, чтобы время заезда
// нельзя было подменить обычным текстовым редактором.
// ============================================================================

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

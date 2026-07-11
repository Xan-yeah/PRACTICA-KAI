// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// RaschetService.cs - ВСЯ математика парковки в одном месте, без единой
// ссылки на UI и настройки. Каждый метод - чистая функция: что передали,
// то и посчитала. Именно поэтому этот сервис покрыт unit-тестами
// (Tests/RaschetServiceTests.cs) - окна тестировать сложно, математику легко.
//
// Формула стоимости (демонстрируется проверяющему):
//   если stoyankaMinut <= M -> бесплатно (0 руб)
//   иначе platnyeMinuty = stoyankaMinut - M
//         summa = ОКРУГЛЕНИЕ ВВЕРХ(platnyeMinuty / 60) * cenaZaChas
// Округление вверх - как на реальных парковках: начатый час = целый час.
// ============================================================================

using System;
using System.Text.RegularExpressions;

namespace AvtoParkingKAI.Services
{
    public static class RaschetService
    {
        /// <summary>
        /// Перевод значения M/N из единицы, выбранной обладателем, в минуты.
        /// edinitsa: 0 = минуты (по умолчанию), 1 = часы, 2 = секунды.
        /// Мусор в edinitsa трактуется как минуты - терминал не имеет права упасть.
        /// </summary>
        public static double PerevestiVMinuty(int znachenie, int edinitsa)
        {
            switch (edinitsa)
            {
                case 1:
                    return znachenie * 60.0;  // значение задано в часах
                case 2:
                    return znachenie / 60.0;  // значение задано в секундах
                default:
                    return znachenie;         // минуты
            }
        }

        /// <summary>
        /// Стоимость стоянки на данный момент. Отрицательная стоянка
        /// ("карта из будущего" после перевода часов) считается нулевой.
        /// </summary>
        public static double RasschitatSummu(double stoyankaMinut, double besplatnoMinutM, double cenaZaChas)
        {
            if (stoyankaMinut < 0.0)
            {
                stoyankaMinut = 0.0;
            }

            if (stoyankaMinut <= besplatnoMinutM)
            {
                return 0.0;
            }

            double platnyeMinuty = stoyankaMinut - besplatnoMinutM;
            double chasyKOplate = Math.Ceiling(platnyeMinuty / 60.0); // начатый час = целый час
            return chasyKOplate * cenaZaChas;
        }

        /// <summary>
        /// Сколько минут осталось до СЛЕДУЮЩЕГО повышения стоимости:
        /// в бесплатный период - до его конца (дальше начнётся первый платный час),
        /// в платный - до конца текущего начатого часа. Питает живой счётчик
        /// информационного блока терминала №2.
        /// </summary>
        public static double DoPovysheniyaMinut(double stoyankaMinut, double besplatnoMinutM)
        {
            if (stoyankaMinut < 0.0)
            {
                stoyankaMinut = 0.0;
            }

            if (stoyankaMinut <= besplatnoMinutM)
            {
                return besplatnoMinutM - stoyankaMinut;
            }

            double platnyeMinuty = stoyankaMinut - besplatnoMinutM;
            return 60.0 - (platnyeMinuty % 60.0);
        }

        /// <summary>
        /// Какой станет сумма ПОСЛЕ ближайшего повышения (вторая строка
        /// информационного блока: "затем к оплате: ...").
        /// </summary>
        public static double SleduyushayaSumma(double stoyankaMinut, double besplatnoMinutM, double cenaZaChas)
        {
            if (stoyankaMinut <= besplatnoMinutM)
            {
                return cenaZaChas; // после бесплатного периода начнётся первый час
            }
            return RasschitatSummu(stoyankaMinut, besplatnoMinutM, cenaZaChas) + cenaZaChas;
        }

        /// <summary>
        /// Правила выпуска машины терминалом №3:
        ///   1) оплачено И с момента оплаты прошло не больше N минут -> ПРИНЯТО
        ///   2) не оплачено, но стоянка уложилась в бесплатные M минут -> ПРИНЯТО
        ///   3) всё остальное -> ОТКАЗАНО
        /// Правило 2 - это и есть "просто посмотрел цену и уехал вовремя":
        /// терминал №2 в бесплатный период на карту НИЧЕГО не пишет.
        /// </summary>
        public static bool RazreshenVyezd(bool oplachen, double minutPosleOplaty, double minutStoyanki,
                                          double besplatnoMinutM, double vyezdMinutN)
        {
            if (oplachen)
            {
                return minutPosleOplaty <= vyezdMinutN;
            }
            return minutStoyanki <= besplatnoMinutM;
        }

        // ================= валидация реквизитов онлайн-оплаты =================
        // Проверки простые и предсказуемые (алгоритм Луна намеренно НЕ применяется:
        // страница демонстрационная, случайный "красивый" номер должен проходить).

        /// <summary>Номер карты: ровно 16 цифр, пробелы-разделители игнорируются.</summary>
        public static bool NomerKartyKorrekten(string nomer)
        {
            if (nomer == null)
            {
                return false;
            }
            return Regex.IsMatch(nomer.Replace(" ", ""), "^[0-9]{16}$");
        }

        /// <summary>
        /// Срок действия в формате ММ/ГГ: месяц 01..12, карта действует
        /// ДО КОНЦА указанного месяца (как в реальных платёжных системах).
        /// Сравнивается с переданными "сейчас" - у терминала это защищённые часы.
        /// </summary>
        public static bool SrokKorrekten(string srok, DateTime seychas)
        {
            if (srok == null || !Regex.IsMatch(srok, "^[0-9]{2}/[0-9]{2}$"))
            {
                return false;
            }

            int mesyats = int.Parse(srok.Substring(0, 2));
            int god = 2000 + int.Parse(srok.Substring(3, 2));
            if (mesyats < 1 || mesyats > 12)
            {
                return false;
            }

            // (год, месяц) карты должны быть не раньше текущих
            return god > seychas.Year || (god == seychas.Year && mesyats >= seychas.Month);
        }

        /// <summary>CVC/CVV: ровно 3 цифры.</summary>
        public static bool CvcKorrekten(string cvc)
        {
            return cvc != null && Regex.IsMatch(cvc, "^[0-9]{3}$");
        }
    }
}

// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// LanguageManager.cs - динамическое переключение языка RU/EN.
//
// Принцип: все надписи в XAML привязаны через {DynamicResource loc_...},
// а сами строки лежат в Localization/Lang.ru.xaml и Lang.en.xaml.
// Меняем словарь в Application.Resources.MergedDictionaries - и WPF сам
// мгновенно перерисовывает ВСЕ окна. Никакого ручного обхода контролов!
// ============================================================================

using System;
using System.Linq;
using System.Windows;

namespace AvtoParkingKAI.Services
{
    public static class LanguageManager
    {
        /// <summary>Текущий язык: "ru" или "en".</summary>
        public static string TekushiyYazyk { get; private set; }

        /// <summary>
        /// Установить язык интерфейса и запомнить выбор в настройках.
        /// </summary>
        public static void UstanovitYazyk(string yazyk)
        {
            // защита от дурака: всё что не "en" считаем русским
            if (yazyk != "en")
            {
                yazyk = "ru";
            }

            Uri adresSlovarya = new Uri("Localization/Lang." + yazyk + ".xaml", UriKind.Relative);
            ResourceDictionary novySlovar = new ResourceDictionary { Source = adresSlovarya };

            // ищем в merged-словарях старый языковой словарь (по пути "Localization/Lang.")
            ResourceDictionary starySlovar = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(slovar => slovar.Source != null &&
                                          slovar.Source.OriginalString.Contains("Localization/Lang."));

            if (starySlovar != null)
            {
                int indeks = Application.Current.Resources.MergedDictionaries.IndexOf(starySlovar);
                Application.Current.Resources.MergedDictionaries[indeks] = novySlovar;
            }
            else
            {
                Application.Current.Resources.MergedDictionaries.Add(novySlovar);
            }

            TekushiyYazyk = yazyk;
            Properties.Settings.Default.Yazyk = yazyk;
            Properties.Settings.Default.Save();
        }

        /// <summary>Переключить язык по кругу (кнопка-флаг на каждом окне).</summary>
        public static void PereklyuchitYazyk()
        {
            UstanovitYazyk(TekushiyYazyk == "ru" ? "en" : "ru");
        }

        /// <summary>
        /// Достать локализованную строку из ресурсов в code-behind
        /// (для string.Format с числами попыток, таймерами и т.д.).
        /// </summary>
        public static string Stroka(string klyuch)
        {
            object znachenie = Application.Current.TryFindResource(klyuch);
            return znachenie == null ? klyuch : znachenie.ToString();
        }
    }
}

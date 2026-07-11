// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// VydelenieService.cs - КОНТРАСТНОЕ ВЫДЕЛЕНИЕ ВЫБРАННОГО ЭЛЕМЕНТА.
//
// Проблема: раньше клавиатурный фокус (стрелки заменяют мышь, см.
// KioskWindow.cs) подсвечивался ВСЕГДА одним голубым цветом InfoBrush.
// На голубой кнопке ("ВЫЗОВ ДИСПЕТЧЕРА", "ТЕРМИНАЛ №3") голубая рамка
// сливалась с фоном кнопки - выбранный элемент было не разглядеть.
//
// Решение: цвет рамки выделения подбирается ПРОТИВОПОЛОЖНЫМ цвету самого
// элемента по палитре из правки ТЗ:
//
//   | Цвет элемента     | Противоположный (цвет выделения)                  |
//   | ----------------- | ------------------------------------------------- |
//   | оранжевый #FF6D00 | синий                                             |
//   | голубой   #40C4FF | оранжевый                                         |
//   | зелёный   #00E676 | пурпурный/фиолетовый                              |
//   | тёмно-серый       | белый (максимальный контраст, комплемента нет)    |
//
// Дополнительно к таблице (цвета, которые тоже встречаются в проекте):
//   | жёлтый    #FFD740 | синий (комплемент жёлтого по цветовому кругу)     |
//   | светлые фоны      | тёмно-серый (зеркальное правило "серый -> белый") |
//
// Класс подключается ОДНОЙ строкой в конструкторе каждого окна:
//     VydelenieService.Podklyuchit(this);
// и дальше сам слушает GotKeyboardFocus/LostKeyboardFocus всего окна -
// он ДОПОЛНЯЕТ существующие стили (рост кнопки из HoverScaleButtonStyle
// остаётся), заменяя только цвет рамки. Прежние жёстко зашитые голубые
// Setter-ы из триггеров стилей убраны (CommonStyles.xaml).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AvtoParkingKAI.Services
{
    public static class VydelenieService
    {
        // ---- сама палитра противоположных цветов ----
        // ключ - цвет элемента, значение - цвет его выделения
        private static readonly Dictionary<Color, Color> PALITRA = new Dictionary<Color, Color>
        {
            // оранжевый -> синий
            { Color.FromRgb(0xFF, 0x6D, 0x00), Color.FromRgb(0x29, 0x62, 0xFF) },
            // голубой -> оранжевый
            { Color.FromRgb(0x40, 0xC4, 0xFF), Color.FromRgb(0xFF, 0x6D, 0x00) },
            // ярко-зелёный -> пурпурный/фиолетовый
            { Color.FromRgb(0x00, 0xE6, 0x76), Color.FromRgb(0xD5, 0x00, 0xF9) },
            // тёмно-серые поверхности терминала -> белый
            { Color.FromRgb(0x24, 0x27, 0x2B), Color.FromRgb(0xFF, 0xFF, 0xFF) },
            { Color.FromRgb(0x1A, 0x1D, 0x21), Color.FromRgb(0xFF, 0xFF, 0xFF) },
            // жёлтый (кнопки "ОПЛАТИТЬ ОНЛАЙН", "ТЕРМИНАЛ №2") -> синий
            { Color.FromRgb(0xFF, 0xD7, 0x40), Color.FromRgb(0x29, 0x62, 0xFF) },
        };

        // сохранённая "родная" рамка элемента, чтобы вернуть её при потере
        // фокуса. Attached-свойство вместо словаря - не держит ссылок на
        // умершие элементы и не требует чистки
        private static readonly DependencyProperty IshodnayaRamkaProperty =
            DependencyProperty.RegisterAttached("IshodnayaRamka", typeof(Brush),
                typeof(VydelenieService), new PropertyMetadata(null));

        /// <summary>
        /// Подключить контрастное выделение ко всему окну (вызывается один
        /// раз из конструктора окна). handledEventsToo = true, потому что
        /// TextBox помечает события фокуса обработанными - без этого флага
        /// поля ввода остались бы без выделения.
        /// </summary>
        public static void Podklyuchit(Window okno)
        {
            okno.AddHandler(UIElement.GotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(Element_PoluchilFokus), true);
            okno.AddHandler(UIElement.LostKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(Element_PoteryalFokus), true);
        }

        private static void Element_PoluchilFokus(object otpravitel, KeyboardFocusChangedEventArgs argumenty)
        {
            Control element = argumenty.NewFocus as Control;
            if (!EtoVydelyaemyElement(element))
            {
                return;
            }

            // запоминаем родную рамку ОДИН раз (если фокус пришёл повторно
            // до потери - не затираем оригинал уже подменённой рамкой)
            if (element.GetValue(IshodnayaRamkaProperty) == null)
            {
                element.SetValue(IshodnayaRamkaProperty, element.BorderBrush);
            }

            element.BorderBrush = PodobratKistVydeleniya(element.Background);
        }

        private static void Element_PoteryalFokus(object otpravitel, KeyboardFocusChangedEventArgs argumenty)
        {
            Control element = argumenty.OldFocus as Control;
            if (element == null)
            {
                return;
            }

            Brush ishodnaya = element.GetValue(IshodnayaRamkaProperty) as Brush;
            if (ishodnaya != null)
            {
                element.BorderBrush = ishodnaya;
                element.ClearValue(IshodnayaRamkaProperty);
            }
        }

        /// <summary>
        /// Выделяем те же типы, по которым ходит навигация стрелками
        /// (KioskWindow.SobratNavigiruemyeElementy) - кнопки и поля ввода.
        /// </summary>
        private static bool EtoVydelyaemyElement(Control element)
        {
            return element is Button || element is TextBox || element is PasswordBox ||
                   element is ComboBox || element is CheckBox;
        }

        /// <summary>
        /// Подбор кисти выделения под цвет элемента: сначала точное попадание
        /// в палитру, иначе - правило максимального контраста (тёмный элемент -
        /// белая рамка, светлый - тёмная), чтобы выделение было видно на
        /// ЛЮБОМ элементе, даже не из фирменной палитры.
        /// </summary>
        private static Brush PodobratKistVydeleniya(Brush fonElementa)
        {
            SolidColorBrush odnotsvetny = fonElementa as SolidColorBrush;
            if (odnotsvetny == null)
            {
                // фон не задан/градиентный - считаем элемент тёмным (тема тёмная)
                return SdelatKist(Colors.White);
            }

            Color tsvet = odnotsvetny.Color;
            Color protivopolozhny;
            if (PALITRA.TryGetValue(tsvet, out protivopolozhny))
            {
                return SdelatKist(protivopolozhny);
            }

            // не из палитры: релятивная яркость (формула из sRGB, упрощённая)
            double yarkost = (0.299 * tsvet.R + 0.587 * tsvet.G + 0.114 * tsvet.B) / 255.0;
            return yarkost < 0.5
                ? SdelatKist(Colors.White)                       // тёмный -> белая рамка
                : SdelatKist(Color.FromRgb(0x1A, 0x1D, 0x21));   // светлый -> тёмная рамка
        }

        private static Brush SdelatKist(Color tsvet)
        {
            SolidColorBrush kist = new SolidColorBrush(tsvet);
            kist.Freeze(); // замороженная кисть быстрее и безопасна между потоками
            return kist;
        }
    }
}

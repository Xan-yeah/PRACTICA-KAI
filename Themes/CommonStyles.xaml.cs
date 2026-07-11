// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// CommonStyles.xaml.cs - код-behind общего словаря стилей.
//
// Тут живёт "ответная реакция" на рост HoverScaleButtonStyle: когда КНОПКА
// растёт от наведения мыши/тача или от фокуса (клавиатура/стрелки), её
// соседи по той же Panel одновременно чуть уменьшаются - иначе выросшая
// кнопка накладывается на соседние элементы (баг, замеченный при
// тестировании интерфейса).
//
// Активатор реакции (EventSetter-ы Mouse.MouseEnter/MouseLeave,
// Keyboard.GotKeyboardFocus/LostKeyboardFocus) висит ТОЛЬКО на
// HoverScaleButtonStyle - только кнопка по этому стилю реально растёт,
// значит только она и должна заставлять соседей сжиматься. А вот сжаться
// в ответ (роль соседа) может ЛЮБОЙ FrameworkElement в той же Panel - это
// работает через его СОБСТВЕННЫЙ RenderTransform (а не внутренний
// scaleTransform из ControlTemplate кнопки), поэтому не требует никакой
// отдельной разметки для полей ввода/текста/картинок и т.п.
// ============================================================================

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AvtoParkingKAI.Themes
{
    public partial class CommonStyles : ResourceDictionary
    {
        // во сколько раз сжимается сосед активного элемента (сам активный элемент
        // тем временем растёт до 1.2 - см. Trigger-ы в HoverScaleButtonStyle)
        private const double MASSHTAB_SOSEDA = 0.92;
        private const double DLITELNOST_SEK = 0.2;

        private void Element_Aktivirovan(object otpravitel, RoutedEventArgs argumenty)
        {
            SzhatSosedey(otpravitel as FrameworkElement, true);
        }

        private void Element_Deaktivirovan(object otpravitel, RoutedEventArgs argumenty)
        {
            SzhatSosedey(otpravitel as FrameworkElement, false);
        }

        /// <summary>
        /// Сжимает (или возвращает к обычному размеру) всех соседей источника
        /// по общей Panel. istochnik сам не трогается - он растёт своим триггером.
        ///
        /// Реагируем ТОЛЬКО если родитель - горизонтальный StackPanel (именно так
        /// оформлены все ряды кнопок в проекте: 3 кнопки терминалов на MainWindow,
        /// 2 кнопки оплаты на interface2, ряды кнопок в EditPanel). Именно там рост
        /// кнопки вширь реально накладывается на соседа. Если применять реакцию ко
        /// ВСЕМ Panel подряд, независимо от расположения - например, к большому
        /// вертикальному StackPanel с заголовком/полями/кнопкой на panelVhod -
        /// наведение на одну кнопку внизу неожиданно сжимало бы весь столбец над
        /// ней, что уже не защита от наложения, а лишний, ничем не мотивированный
        /// визуальный эффект.
        /// </summary>
        private static void SzhatSosedey(FrameworkElement istochnik, bool szhat)
        {
            StackPanel roditel = istochnik == null ? null : istochnik.Parent as StackPanel;
            if (roditel == null || roditel.Orientation != Orientation.Horizontal)
            {
                return;
            }

            double tselevoyMasshtab = szhat ? MASSHTAB_SOSEDA : 1.0;

            foreach (object rebenokObj in roditel.Children)
            {
                FrameworkElement sosed = rebenokObj as FrameworkElement;
                if (sosed == null || ReferenceEquals(sosed, istochnik))
                {
                    continue;
                }
                AnimirovatMasshtabSoseda(sosed, tselevoyMasshtab);
            }
        }

        private static void AnimirovatMasshtabSoseda(FrameworkElement sosed, double tselevoyMasshtab)
        {
            ScaleTransform transform = sosed.RenderTransform as ScaleTransform;
            if (transform == null)
            {
                // у соседа ещё нет собственного внешнего трансформа - заводим один раз
                transform = new ScaleTransform(1.0, 1.0);
                sosed.RenderTransform = transform;
                sosed.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            Duration dlitelnost = new Duration(TimeSpan.FromSeconds(DLITELNOST_SEK));
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(tselevoyMasshtab, dlitelnost));
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(tselevoyMasshtab, dlitelnost));
        }
    }
}

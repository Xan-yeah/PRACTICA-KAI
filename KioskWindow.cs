// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// KioskWindow.cs - базовый класс всех окон-терминалов ("режим киоска").
//
// Чтобы не копировать одно и то же в 4 окна, всё общее собрано тут:
//   - полный экран без рамки, поверх всех окон, нет в панели задач;
//   - курсор мыши СКРЫТ (по ТЗ мышь не предполагается, экран сенсорный);
//   - Alt+F4 и любые попытки закрыть окно ОТМЕНЯЮТСЯ (антивандальность),
//     закрыться можно только через EditPanel обладателя;
//   - клавиша "`" ("ё") - вызов панели обладателя;
//   - Ctrl+Enter - активация главной кнопки окна (по ТЗ это равносильно
//     нажатию button);
//   - стрелки переключают "выбранный" элемент (кнопки/поля) - раз мыши на
//     терминале нет, стрелки заменяют наведение, а подсветка/рост элемента
//     (см. HoverScaleButtonStyle в CommonStyles.xaml) показывает, что сейчас
//     выбрано.
// ============================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AvtoParkingKAI
{
    public class KioskWindow : Window
    {
        public KioskWindow()
        {
            // режим киоска: во весь экран, без рамки, поверх всего
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;

            // мышь как устройство ввода не предполагается - прячем курсор
            Cursor = Cursors.None;

            // фон из общей палитры
            object fon = TryFindResource("FonBrush");
            if (fon is Brush)
            {
                Background = (Brush)fon;
            }
        }

        /// <summary>
        /// Каждое окно-наследник говорит, какая у него "главная" кнопка,
        /// чтобы Ctrl+Enter мог её нажать даже без фокуса.
        /// </summary>
        protected virtual Button KnopkaPoUmolchaniyu()
        {
            return null;
        }

        protected override void OnClosing(CancelEventArgs argumenty)
        {
            // закрытие разрешено ТОЛЬКО когда обладатель дал добро из EditPanel
            if (!App.RazreshenoZakrytie)
            {
                argumenty.Cancel = true;
            }
            base.OnClosing(argumenty);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs argumenty)
        {
            // "`" / "ё" - вызов панели обладателя
            if (argumenty.Key == Key.Oem3)
            {
                App.PokazatPanelObladatelya();
                argumenty.Handled = true;
                return;
            }

            // Ctrl+Enter = нажатие кнопки (сначала кнопка в фокусе, иначе главная)
            if (argumenty.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                Button celKnopka = Keyboard.FocusedElement as Button;
                if (celKnopka == null)
                {
                    celKnopka = KnopkaPoUmolchaniyu();
                }
                if (celKnopka != null && celKnopka.IsEnabled && celKnopka.IsVisible)
                {
                    celKnopka.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    argumenty.Handled = true;
                    return;
                }
            }

            // стрелки - навигация без мыши (см. шапку файла)
            if (argumenty.Key == Key.Left || argumenty.Key == Key.Up ||
                argumenty.Key == Key.Right || argumenty.Key == Key.Down)
            {
                if (PereklyuchitStrelkoy(argumenty.Key))
                {
                    argumenty.Handled = true;
                    return;
                }
            }

            base.OnPreviewKeyDown(argumenty);
        }

        // ================= навигация стрелками =================

        /// <summary>
        /// Переключает клавиатурный фокус на следующий/предыдущий пригодный элемент.
        /// Right/Down - вперёд, Left/Up - назад, по кругу. Возвращает false, если
        /// переключаться не на что (пустое окно) - тогда клавиша не перехватывается.
        /// </summary>
        private bool PereklyuchitStrelkoy(Key klavisha)
        {
            FrameworkElement koren = NaytiKorenNavigatsii();
            if (koren == null)
            {
                return false;
            }

            List<FrameworkElement> spisok = new List<FrameworkElement>();
            SobratNavigiruemyeElementy(koren, spisok);
            if (spisok.Count == 0)
            {
                return false;
            }

            bool vpered = klavisha == Key.Right || klavisha == Key.Down;
            FrameworkElement tekushiy = Keyboard.FocusedElement as FrameworkElement;
            int indeks = tekushiy == null ? -1 : spisok.IndexOf(tekushiy);

            int noviyIndeks;
            if (indeks < 0)
            {
                // фокуса ещё нет (только что открылось окно/сменилась панель) -
                // первое нажатие стрелки просто выбирает первый элемент
                noviyIndeks = vpered ? 0 : spisok.Count - 1;
            }
            else
            {
                noviyIndeks = vpered ? indeks + 1 : indeks - 1;
                if (noviyIndeks < 0)
                {
                    noviyIndeks = spisok.Count - 1;
                }
                else if (noviyIndeks >= spisok.Count)
                {
                    noviyIndeks = 0;
                }
            }

            spisok[noviyIndeks].Focus();
            return true;
        }

        /// <summary>
        /// Корень для поиска: если поверх окна сейчас виден модальный оверлей
        /// (по конвенции всех окон - Border с именем, начинающимся на "overlay"),
        /// ищем элементы ТОЛЬКО внутри него - иначе стрелками можно было бы
        /// случайно попасть на кнопку под затемнением фона.
        /// </summary>
        private FrameworkElement NaytiKorenNavigatsii()
        {
            FrameworkElement koren = Content as FrameworkElement;
            if (koren == null)
            {
                return null;
            }
            FrameworkElement aktivnyOverley = NaytiAktivnyOverley(koren);
            return aktivnyOverley != null ? aktivnyOverley : koren;
        }

        private static FrameworkElement NaytiAktivnyOverley(DependencyObject uzel)
        {
            int kolvoDetey = VisualTreeHelper.GetChildrenCount(uzel);
            for (int i = 0; i < kolvoDetey; i++)
            {
                DependencyObject rebenok = VisualTreeHelper.GetChild(uzel, i);
                FrameworkElement rebenokEl = rebenok as FrameworkElement;
                if (rebenokEl != null && rebenokEl.Visibility == Visibility.Visible &&
                    !string.IsNullOrEmpty(rebenokEl.Name) &&
                    rebenokEl.Name.StartsWith("overlay", StringComparison.Ordinal))
                {
                    return rebenokEl;
                }

                FrameworkElement vlozhenny = NaytiAktivnyOverley(rebenok);
                if (vlozhenny != null)
                {
                    return vlozhenny;
                }
            }
            return null;
        }

        /// <summary>Собирает видимые/активные кнопки и поля ввода в порядке обхода дерева.</summary>
        private static void SobratNavigiruemyeElementy(DependencyObject uzel, List<FrameworkElement> spisok)
        {
            int kolvoDetey = VisualTreeHelper.GetChildrenCount(uzel);
            for (int i = 0; i < kolvoDetey; i++)
            {
                DependencyObject rebenok = VisualTreeHelper.GetChild(uzel, i);
                FrameworkElement el = rebenok as FrameworkElement;
                if (el != null && EtoPodhodyashiyElementNavigatsii(el))
                {
                    spisok.Add(el);
                }
                SobratNavigiruemyeElementy(rebenok, spisok);
            }
        }

        private static bool EtoPodhodyashiyElementNavigatsii(FrameworkElement el)
        {
            bool nuzhnyTip = el is Button || el is TextBox || el is PasswordBox ||
                              el is ComboBox || el is CheckBox;
            if (!nuzhnyTip)
            {
                return false;
            }

            Control kontrol = (Control)el;
            return kontrol.IsVisible && kontrol.IsEnabled && kontrol.Focusable && kontrol.IsTabStop;
        }

        /// <summary>
        /// Подключить оверлей блокировки при сбое времени: окно даёт свой Border,
        /// а сторожевые часы сами покажут его при аномалии.
        /// </summary>
        protected void PodklyuchitStorozhVremeni(UIElement overley)
        {
            // если сбой уже случился до открытия окна - показываем сразу
            if (App.Vremya.Zablokirovan)
            {
                overley.Visibility = Visibility.Visible;
            }
            App.Vremya.VremyaSboy += () =>
            {
                overley.Visibility = Visibility.Visible;
            };
        }
    }
}

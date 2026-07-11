// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// AntivandalService.cs - АНТИВАНДАЛЬНАЯ БЛОКИРОВКА СИСТЕМНЫХ КЛАВИШ.
//
// Задача: терминал стоит на улице, клавиатура доступна кому попало. Вандал
// не должен иметь возможности свернуть программу и добраться до рабочего
// стола / Пуска / проводника. KioskWindow уже отменяет Alt+F4 через
// OnClosing, но клавиша Windows и часть системных комбинаций обрабатываются
// САМОЙ ОС раньше, чем WPF получит событие, - обычным PreviewKeyDown их не
// перехватить. Поэтому здесь стоит низкоуровневый клавиатурный хук
// WH_KEYBOARD_LL (user32.dll): он видит нажатие раньше оболочки Windows и
// "проглатывает" опасные клавиши до того, как их увидит Пуск.
//
// Что блокируется (работает и на Windows 11, и на старых версиях - хук
// WH_KEYBOARD_LL существует со времён Windows NT4/2000):
//   - Win (левая/правая) - а вместе с ней ВСЕ комбинации Win+R, Win+D,
//     Win+E, Win+Tab, Win+X, Win+L и т.д.: без самой клавиши Win ОС их
//     просто не соберёт;
//   - Alt+Tab, Alt+Esc              - переключение окон;
//   - Alt+F4                        - второй рубеж (первый - OnClosing);
//   - Alt+Space                     - системное меню окна;
//   - Ctrl+Esc                      - меню Пуск с клавиатуры;
//   - Ctrl+Shift+Esc                - диспетчер задач (ловится тем же
//                                     правилом "Esc при зажатом Ctrl");
//   - клавиша контекстного меню (Apps).
//
// ЧЕСТНОЕ ОГРАНИЧЕНИЕ: Ctrl+Alt+Del заблокировать из пользовательской
// программы НЕЛЬЗЯ в принципе - это Secure Attention Sequence, её ловит
// ядро Windows до всех хуков. Для полного киоска этот пункт закрывается
// политиками ОС (режим Assigned Access / групповые политики), что выходит
// за рамки программы.
// ============================================================================

using System;
using System.Runtime.InteropServices;

namespace AvtoParkingKAI.Services
{
    /// <summary>
    /// Статический сервис-"щит": один низкоуровневый хук на всю программу.
    /// Включается в App.OnStartup, снимается в App.OnExit.
    /// </summary>
    public static class AntivandalService
    {
        // ---- константы WinAPI ----
        private const int WH_KEYBOARD_LL = 13;

        // виртуальные коды клавиш (winuser.h)
        private const uint VK_TAB = 0x09;
        private const uint VK_ESCAPE = 0x1B;
        private const uint VK_SPACE = 0x20;
        private const uint VK_LWIN = 0x5B;
        private const uint VK_RWIN = 0x5C;
        private const uint VK_APPS = 0x5D;   // клавиша контекстного меню
        private const uint VK_F4 = 0x73;
        private const int VK_CONTROL = 0x11;

        // бит "Alt зажат" в flags структуры KBDLLHOOKSTRUCT
        private const uint LLKHF_ALTDOWN = 0x20;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // ссылку на делегат ОБЯЗАТЕЛЬНО держим в поле: если отдать в
        // SetWindowsHookEx временный делегат, сборщик мусора его утилизирует,
        // и программа упадёт при первом же нажатии клавиши (классическая
        // ошибка с P/Invoke callback-ами)
        private static HookProc hukDelegat;
        private static IntPtr hukDeskriptor = IntPtr.Zero;

        /// <summary>
        /// Поставить щит. Вызывается один раз из App.OnStartup.
        /// Под отладчиком хук НЕ ставится: иначе точка останова замораживает
        /// обработчик, Windows перестаёт реагировать на клавиатуру у ВСЕХ
        /// программ разработчика (хук-то глобальный), приходится ждать, пока
        /// ОС сама выкинет зависший хук.
        /// </summary>
        public static void Vklyuchit()
        {
            if (hukDeskriptor != IntPtr.Zero)
            {
                return; // уже включён - второй хук не плодим
            }
            if (System.Diagnostics.Debugger.IsAttached)
            {
                return;
            }

            hukDelegat = ObrabotatKlavishu;
            hukDeskriptor = SetWindowsHookEx(WH_KEYBOARD_LL, hukDelegat,
                                             GetModuleHandle(null), 0);
            // если хук не встал (hukDeskriptor == 0) - не падаем: терминал
            // продолжит работать, просто без этого рубежа защиты
        }

        /// <summary>Снять щит (App.OnExit) - иначе хук переживёт программу.</summary>
        public static void Vyklyuchit()
        {
            if (hukDeskriptor != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hukDeskriptor);
                hukDeskriptor = IntPtr.Zero;
                hukDelegat = null;
            }
        }

        private static IntPtr ObrabotatKlavishu(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // nCode < 0 по контракту WinAPI передаётся дальше без анализа
            if (nCode >= 0)
            {
                KBDLLHOOKSTRUCT dannye = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                    lParam, typeof(KBDLLHOOKSTRUCT));

                if (EtoZapreshchennoe(dannye))
                {
                    // ненулевой результат = "клавишу съели", ОС её не увидит
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(hukDeskriptor, nCode, wParam, lParam);
        }

        /// <summary>Правила блокировки - см. список в шапке файла.</summary>
        private static bool EtoZapreshchennoe(KBDLLHOOKSTRUCT dannye)
        {
            uint klavisha = dannye.vkCode;

            // клавиша Windows целиком (глотаем и нажатие, и отпускание -
            // тогда ни одна комбинация Win+... не соберётся)
            if (klavisha == VK_LWIN || klavisha == VK_RWIN)
            {
                return true;
            }

            // клавиша контекстного меню - в проводнике открывала бы меню,
            // в терминале ей делать нечего
            if (klavisha == VK_APPS)
            {
                return true;
            }

            // комбинации с Alt: Tab (переключение окон), Esc (то же),
            // F4 (закрытие, второй рубеж после OnClosing), Space (сисменю)
            bool altZazhat = (dannye.flags & LLKHF_ALTDOWN) != 0;
            if (altZazhat && (klavisha == VK_TAB || klavisha == VK_ESCAPE ||
                              klavisha == VK_F4 || klavisha == VK_SPACE))
            {
                return true;
            }

            // Esc при зажатом Ctrl: Ctrl+Esc (Пуск) и Ctrl+Shift+Esc
            // (диспетчер задач) отсекаются одним правилом. Обычный Esc
            // НЕ трогаем - он используется интерфейсом (отмена оплаты и т.п.)
            bool ctrlZazhat = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            if (ctrlZazhat && klavisha == VK_ESCAPE)
            {
                return true;
            }

            return false;
        }
    }
}

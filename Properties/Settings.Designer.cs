//------------------------------------------------------------------------------
//Программу разработали: Гарафутдинов А.Р.; Зайнабиддинов И.К.
//---------
// Settings.Designer.cs - класс настроек программы (пара к Settings.settings).
// Здесь живут все параметры обладателя: цена за час, бесплатные минуты M,
// минуты на выезд N, SHA-256 хэш пароля и выбранный язык интерфейса.
// Значения сохраняются между запусками через Properties.Settings.Default
// (файл user.config в профиле пользователя Windows).
//проработка дизайна была сделана в концепции "киоскового" стиля, то есть упрощённого интерфейса для сенсорных экранов
//с учётом ужасных погодных условий эксплуатации (вандализм, грязь, пыль, дождь, снег, мороз, жара и т.д.) была проработана цветовая гамма и оттенки.
//------------------------------------------------------------------------------

namespace AvtoParkingKAI.Properties
{


    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "11.0.0.0")]
    internal sealed partial class Settings : global::System.Configuration.ApplicationSettingsBase
    {

        private static Settings defaultInstance = ((Settings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));

        public static Settings Default
        {
            get
            {
                return defaultInstance;
            }
        }

        /// <summary>
        /// Цена за час парковки (руб). Меняется обладателем в EditPanel.
        /// </summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("200")]
        public double CenaZaChas
        {
            get
            {
                return ((double)(this["CenaZaChas"]));
            }
            set
            {
                this["CenaZaChas"] = value;
            }
        }

        /// <summary>
        /// M минут — время бесплатной стоянки.
        /// </summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("15")]
        public int BesplatnoMinutM
        {
            get
            {
                return ((int)(this["BesplatnoMinutM"]));
            }
            set
            {
                this["BesplatnoMinutM"] = value;
            }
        }

        /// <summary>
        /// N минут — время на выезд после оплаты.
        /// </summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("15")]
        public int VyezdMinutN
        {
            get
            {
                return ((int)(this["VyezdMinutN"]));
            }
            set
            {
                this["VyezdMinutN"] = value;
            }
        }

        /// <summary>
        /// SHA-256 хэш пароля обладателя. Пустая строка = первый запуск (будет записан хэш "qwerty").
        /// </summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string ParolHash
        {
            get
            {
                return ((string)(this["ParolHash"]));
            }
            set
            {
                this["ParolHash"] = value;
            }
        }

        /// <summary>
        /// Текущий язык интерфейса: "ru" или "en".
        /// </summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("ru")]
        public string Yazyk
        {
            get
            {
                return ((string)(this["Yazyk"]));
            }
            set
            {
                this["Yazyk"] = value;
            }
        }

        /// <summary>
        /// Режим работы этого терминала: 0 = не настроен (при старте показывается
        /// окно выбора MainWindow, как раньше), 1/2/3 = interface1/2/3 - при старте
        /// программа сразу открывает нужный терминал, минуя окно выбора.
        /// Настраивается обладателем в EditPanel (комбобокс "вид работы").
        /// </summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("0")]
        public int RezhimTerminala
        {
            get
            {
                return ((int)(this["RezhimTerminala"]));
            }
            set
            {
                this["RezhimTerminala"] = value;
            }
        }

        /// <summary>
        /// Единица измерения значений M и N: 0 = минуты (по умолчанию, как было
        /// всегда), 1 = часы, 2 = секунды. Настраивается обладателем в EditPanel;
        /// перевод в минуты для расчётов - App.BesplatnoMvMinutah/VyezdNvMinutah.
        /// </summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("0")]
        public int EdinitsaVremeniMN
        {
            get
            {
                return ((int)(this["EdinitsaVremeniMN"]));
            }
            set
            {
                this["EdinitsaVremeniMN"] = value;
            }
        }
    }
}

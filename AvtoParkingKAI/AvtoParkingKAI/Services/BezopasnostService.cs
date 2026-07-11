// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// BezopasnostService.cs - всё что касается криптографии:
//   1) SHA-256 хэш пароля обладателя (в настройках пароль в открытую НЕ лежит)
//   2) AES шифрование файла pass.dat на флешке
//   3) эталонный хэш ключа диспетчера (файл disp_key.dat)
// ============================================================================
//
// --- история рассуждений (черновик) ---
// первая версия "шифрования" была смешная - просто XOR каждого байта:
//   for (int i = 0; i < bayty.Length; i++) bayty[i] ^= 42;
// препод такое не примет + любой студент за вечер расковыряет.
// Переделано дома под все условия: нормальный AES-256, ключ и IV
// выводятся из секретной фразы через SHA-256/MD5, наружу торчит base64.
// --------------------------------------

using System;
using System.Security.Cryptography;
using System.Text;

namespace AvtoParkingKAI.Services
{
    /// <summary>
    /// Статический сервис безопасности. Изолирован от UI полностью,
    /// чтобы можно было тестировать отдельно от окон.
    /// </summary>
    public static class BezopasnostService
    {
        // секретная фраза проекта - из неё выводится ключ AES.
        // в реальном продукте её надо прятать лучше, но для демонстрации ок.
        private const string SEKRETNAYA_FRAZA = "KAI-4110-JACA-YUP-AVTOPARKING-SECRET-2026";

        // фраза, из которой сделан эталонный ключ диспетчера (льготы для инвалидов).
        // на флешку диспетчера кладётся файл disp_key.dat с хэшем этой фразы.
        private const string DISP_FRAZA = "DISPETCHER-KAI-4110-LGOTA-KEY";

        /// <summary>
        /// SHA-256 хэш строки в виде hex-строки в нижнем регистре.
        /// Используется для пароля обладателя и для ключа диспетчера.
        /// </summary>
        public static string HashSha256(string tekst)
        {
            // защита от дурака: null превращаем в пустую строку, чтобы не упасть
            if (tekst == null)
            {
                tekst = "";
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] bayty = sha.ComputeHash(Encoding.UTF8.GetBytes(tekst));
                StringBuilder sbornik = new StringBuilder(bayty.Length * 2);
                for (int i = 0; i < bayty.Length; i++)
                {
                    sbornik.Append(bayty[i].ToString("x2"));
                }
                return sbornik.ToString();
            }
        }

        /// <summary>Эталонный хэш ключа диспетчера (сравнивается с содержимым disp_key.dat).</summary>
        public static string PoluchitDispHash()
        {
            return HashSha256(DISP_FRAZA);
        }

        /// <summary>Ключ AES-256: первые 32 байта SHA-256 от секретной фразы.</summary>
        private static byte[] PoluchitKlyuchAes()
        {
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(SEKRETNAYA_FRAZA));
            }
        }

        /// <summary>Вектор инициализации: 16 байт MD5 от той же фразы (для демо достаточно).</summary>
        private static byte[] PoluchitVektorIV()
        {
            using (MD5 md5 = MD5.Create())
            {
                return md5.ComputeHash(Encoding.UTF8.GetBytes(SEKRETNAYA_FRAZA));
            }
        }

        /// <summary>
        /// Шифрует текст (JSON пропуска) в base64-строку.
        /// Именно эта строка лежит внутри pass.dat.
        /// </summary>
        public static string Shifrovat(string otkrytyTekst)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = PoluchitKlyuchAes();
                aes.IV = PoluchitVektorIV();
                using (ICryptoTransform shifrator = aes.CreateEncryptor())
                {
                    byte[] ishodnyeBayty = Encoding.UTF8.GetBytes(otkrytyTekst);
                    byte[] shifroBayty = shifrator.TransformFinalBlock(ishodnyeBayty, 0, ishodnyeBayty.Length);
                    return Convert.ToBase64String(shifroBayty);
                }
            }
        }

        /// <summary>
        /// Расшифровывает base64-строку обратно в JSON.
        /// Если файл битый/подделанный - вернёт null, а НЕ уронит терминал.
        /// </summary>
        public static string Rasshifrovat(string shifroTekst)
        {
            // защита от дурака: битая карта не должна ронять программу,
            // поэтому весь блок в try-catch (аналог while True + try-except из лаб)
            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = PoluchitKlyuchAes();
                    aes.IV = PoluchitVektorIV();
                    using (ICryptoTransform deshifrator = aes.CreateDecryptor())
                    {
                        byte[] shifroBayty = Convert.FromBase64String(shifroTekst);
                        byte[] otkrytyeBayty = deshifrator.TransformFinalBlock(shifroBayty, 0, shifroBayty.Length);
                        return Encoding.UTF8.GetString(otkrytyeBayty);
                    }
                }
            }
            catch (Exception)
            {
                // FormatException (не base64), CryptographicException (не тот ключ) и т.д.
                return null;
            }
        }
    }
}

// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// BezopasnostServiceTests.cs - unit-тесты криптографии (AES + SHA-256).
// Главное свойство: битая/подделанная карта возвращает null и НЕ роняет
// терминал - это проверяется отдельными тестами.
// ============================================================================

using AvtoParkingKAI.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvtoParkingKAI.Tests
{
    [TestClass]
    public class BezopasnostServiceTests
    {
        [TestMethod]
        public void ShifrovanieTudaObratno_TekstNeMenyaetsya()
        {
            string ishodny = "{\"Id\":\"test\",\"VremyaZaezdaTicks\":123456789,\"IsPaid\":false}";
            string shifrovka = BezopasnostService.Shifrovat(ishodny);

            Assert.AreNotEqual(ishodny, shifrovka); // на флешке лежит не открытый текст
            Assert.AreEqual(ishodny, BezopasnostService.Rasshifrovat(shifrovka));
        }

        [TestMethod]
        public void Rasshifrovat_NeBase64Musor_VozvrashaetNull()
        {
            // "хитрый водитель" отредактировал pass.dat блокнотом
            Assert.IsNull(BezopasnostService.Rasshifrovat("это вообще не шифровка"));
        }

        [TestMethod]
        public void Rasshifrovat_ChuzhoyBase64_VozvrashaetNull()
        {
            // валидный base64, но 15 байт - не кратно блоку AES (16),
            // расшифровка гарантированно падает и обязана вернуть null
            Assert.IsNull(BezopasnostService.Rasshifrovat("QUJDREVGR0hJSktMTU5P"));
        }

        [TestMethod]
        public void HashSha256_IzvestnoeZnachenie()
        {
            // эталонный SHA-256 от "qwerty" (пароль по умолчанию из ТЗ)
            Assert.AreEqual(
                "65e84be33532fb784c48129675f9eff3a682b27168c0ea744b2cf58ee02337c5",
                BezopasnostService.HashSha256("qwerty"));
        }

        [TestMethod]
        public void HashSha256_Null_NePadaet()
        {
            // защита от дурака: null == пустая строка, исключения нет
            Assert.AreEqual(BezopasnostService.HashSha256(""), BezopasnostService.HashSha256(null));
        }

        [TestMethod]
        public void DispHash_StabilenMezhduVyzovami()
        {
            // ключ диспетчера пишется на флешку один раз, а проверяется много раз -
            // хэш обязан быть детерминированным
            Assert.AreEqual(BezopasnostService.PoluchitDispHash(), BezopasnostService.PoluchitDispHash());
        }
    }
}

// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// RaschetServiceTests.cs - unit-тесты ВСЕЙ математики парковки.
//
// Каждый тест - маленькая история из жизни терминала: имя говорит, ЧТО
// проверяется, тело - ровно один сценарий. Граничные случаи (ровно M минут,
// начатый час, карта "из будущего") - именно те места, где терминал терял бы
// деньги обладателя или несправедливо наказывал водителя.
// ============================================================================

using System;
using AvtoParkingKAI.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvtoParkingKAI.Tests
{
    [TestClass]
    public class RaschetServiceTests
    {
        private const double CENA = 200.0; // руб/час, как значение по умолчанию
        private const double M = 15.0;     // бесплатные минуты по умолчанию

        // ---------------- RasschitatSummu ----------------

        [TestMethod]
        public void Summa_RovnoNaGranitseBesplatnogo_NulRubley()
        {
            // ровно M минут - это ещё бесплатно (граница включительно)
            Assert.AreEqual(0.0, RaschetService.RasschitatSummu(M, M, CENA));
        }

        [TestMethod]
        public void Summa_MinutaSverhBesplatnogo_OdinPolnyChas()
        {
            // начатый час = целый час: M+1 минута стоит как полный час
            Assert.AreEqual(CENA, RaschetService.RasschitatSummu(M + 1.0, M, CENA));
        }

        [TestMethod]
        public void Summa_ChasPyatdesyatDevyatMinutPlatnogo_DvaChasa()
        {
            // 1ч59м платного времени - водитель платит за 2 начатых часа
            // (ровно тот случай, из-за которого в v1 обладатель терял деньги)
            Assert.AreEqual(2 * CENA, RaschetService.RasschitatSummu(M + 119.0, M, CENA));
        }

        [TestMethod]
        public void Summa_RovnoChasPlatnogo_OdinChas()
        {
            // ровно 60 платных минут - только один час, без округления вверх
            Assert.AreEqual(CENA, RaschetService.RasschitatSummu(M + 60.0, M, CENA));
        }

        [TestMethod]
        public void Summa_KartaIzBudushego_NulRubley()
        {
            // перевод часов назад даёт отрицательную стоянку - защита от дурака
            Assert.AreEqual(0.0, RaschetService.RasschitatSummu(-30.0, M, CENA));
        }

        // ---------------- PerevestiVMinuty ----------------

        [TestMethod]
        public void Perevod_Minuty_BezIzmeneniy()
        {
            Assert.AreEqual(15.0, RaschetService.PerevestiVMinuty(15, 0));
        }

        [TestMethod]
        public void Perevod_Chasy_Umnozhaet()
        {
            Assert.AreEqual(120.0, RaschetService.PerevestiVMinuty(2, 1));
        }

        [TestMethod]
        public void Perevod_Sekundy_Delit()
        {
            Assert.AreEqual(0.5, RaschetService.PerevestiVMinuty(30, 2));
        }

        [TestMethod]
        public void Perevod_MusorVEdinitse_TraktuetsyaKakMinuty()
        {
            // терминал не имеет права упасть из-за мусора в настройках
            Assert.AreEqual(15.0, RaschetService.PerevestiVMinuty(15, 99));
        }

        // ---------------- DoPovysheniyaMinut (живой счётчик) ----------------

        [TestMethod]
        public void Schetchik_VBesplatnomPeriode_DoKontsaBesplatnogo()
        {
            // простоял 5 минут из 15 бесплатных - до платной стоянки 10 минут
            Assert.AreEqual(10.0, RaschetService.DoPovysheniyaMinut(5.0, M));
        }

        [TestMethod]
        public void Schetchik_VPlatnomPeriode_DoKontsaNachatogoChasa()
        {
            // 30 платных минут - до следующего часа (и повышения цены) 30 минут
            Assert.AreEqual(30.0, RaschetService.DoPovysheniyaMinut(M + 30.0, M));
        }

        // ---------------- SleduyushayaSumma (вторая строка инфо-блока) ----------------

        [TestMethod]
        public void SleduyushayaSumma_VBesplatnom_PervyChas()
        {
            // после конца бесплатного периода начнётся первый платный час
            Assert.AreEqual(CENA, RaschetService.SleduyushayaSumma(5.0, M, CENA));
        }

        [TestMethod]
        public void SleduyushayaSumma_VPlatnom_NaChasBolshe()
        {
            // сейчас 1 час (200), после повышения будет 2 часа (400)
            Assert.AreEqual(2 * CENA, RaschetService.SleduyushayaSumma(M + 30.0, M, CENA));
        }

        // ---------------- RazreshenVyezd (правила терминала №3) ----------------

        [TestMethod]
        public void Vyezd_OplachenoIVovremya_Prinyato()
        {
            Assert.IsTrue(RaschetService.RazreshenVyezd(true, 10.0, 500.0, M, 15.0));
        }

        [TestMethod]
        public void Vyezd_OplachenoNoProspal_Otkazano()
        {
            // оплатил и "уснул на 2 часа" - N минут на выезд истекли
            Assert.IsFalse(RaschetService.RazreshenVyezd(true, 120.0, 500.0, M, 15.0));
        }

        [TestMethod]
        public void Vyezd_NeOplachenoVnutriBesplatnogo_Prinyato()
        {
            // "просто посмотрел цену на терминале 2 и уехал вовремя":
            // терминал 2 на карту ничего не писал, выезд по правилу 2 бесплатный
            Assert.IsTrue(RaschetService.RazreshenVyezd(false, 0.0, 10.0, M, 15.0));
        }

        [TestMethod]
        public void Vyezd_NeOplachenoPosleBesplatnogo_Otkazano()
        {
            // погулял дольше бесплатного и не оплатил - к терминалу №2
            Assert.IsFalse(RaschetService.RazreshenVyezd(false, 0.0, M + 1.0, M, 15.0));
        }

        // ---------------- валидация реквизитов онлайн-оплаты ----------------

        [TestMethod]
        public void NomerKarty_16TsifrSProbelami_Korrekten()
        {
            // автоформат страницы вставляет пробелы-разделители - они не мешают
            Assert.IsTrue(RaschetService.NomerKartyKorrekten("1234 5678 9012 3456"));
        }

        [TestMethod]
        public void NomerKarty_15Tsifr_Nekorrekten()
        {
            Assert.IsFalse(RaschetService.NomerKartyKorrekten("123456789012345"));
        }

        [TestMethod]
        public void NomerKarty_SBukvami_Nekorrekten()
        {
            Assert.IsFalse(RaschetService.NomerKartyKorrekten("1234abcd90123456"));
        }

        [TestMethod]
        public void NomerKarty_Null_Nekorrekten()
        {
            Assert.IsFalse(RaschetService.NomerKartyKorrekten(null));
        }

        [TestMethod]
        public void Srok_BudushiyGod_Korrekten()
        {
            Assert.IsTrue(RaschetService.SrokKorrekten("05/30", new DateTime(2026, 7, 1)));
        }

        [TestMethod]
        public void Srok_TekushiyMesyats_Korrekten()
        {
            // карта действует ДО КОНЦА указанного месяца - как в реальных системах
            Assert.IsTrue(RaschetService.SrokKorrekten("07/26", new DateTime(2026, 7, 31)));
        }

        [TestMethod]
        public void Srok_ProshlyMesyats_Nekorrekten()
        {
            Assert.IsFalse(RaschetService.SrokKorrekten("06/26", new DateTime(2026, 7, 1)));
        }

        [TestMethod]
        public void Srok_TrinadtsatyMesyats_Nekorrekten()
        {
            Assert.IsFalse(RaschetService.SrokKorrekten("13/30", new DateTime(2026, 7, 1)));
        }

        [TestMethod]
        public void Srok_MusorVmestoFormata_Nekorrekten()
        {
            Assert.IsFalse(RaschetService.SrokKorrekten("7/26", new DateTime(2026, 7, 1)));
            Assert.IsFalse(RaschetService.SrokKorrekten("0726", new DateTime(2026, 7, 1)));
            Assert.IsFalse(RaschetService.SrokKorrekten(null, new DateTime(2026, 7, 1)));
        }

        [TestMethod]
        public void Cvc_TriTsifry_Korrekten()
        {
            Assert.IsTrue(RaschetService.CvcKorrekten("123"));
        }

        [TestMethod]
        public void Cvc_DveTsifryIliBukvy_Nekorrekten()
        {
            Assert.IsFalse(RaschetService.CvcKorrekten("12"));
            Assert.IsFalse(RaschetService.CvcKorrekten("12a"));
            Assert.IsFalse(RaschetService.CvcKorrekten(null));
        }
    }
}

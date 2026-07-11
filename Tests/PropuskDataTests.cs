// ============================================================================
// Курсовой проект "АвтоПарковка КАИ" | Группа 4110 /JACA/ /YUP/
// PropuskDataTests.cs - unit-тесты модели карты-пропуска, включая полный
// круг "объект -> JSON -> AES -> расшифровка -> объект" (именно этот круг
// проходит каждая флешка между терминалами 1 -> 2 -> 3).
// ============================================================================

using System;
using System.Web.Script.Serialization;
using AvtoParkingKAI.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AvtoParkingKAI.Tests
{
    [TestClass]
    public class PropuskDataTests
    {
        [TestMethod]
        public void NovyPropusk_ZapolnyaetVsePolya()
        {
            DateTime zaezd = new DateTime(2026, 7, 4, 12, 30, 0);
            PropuskData propusk = PropuskData.NovyPropusk(zaezd);

            Assert.IsFalse(string.IsNullOrEmpty(propusk.Id));
            Assert.AreEqual(zaezd, propusk.GetVremyaZaezda());
            Assert.IsFalse(propusk.IsPaid);
            Assert.AreEqual(0L, propusk.VremyaOplatyTicks);
        }

        [TestMethod]
        public void VremyaCherezTiki_NeZavisitOtKultury()
        {
            // тики выбраны вместо DateTime.Parse именно из-за региональных
            // настроек (см. историю в PropuskData.cs) - проверяем круг
            DateTime oplata = new DateTime(2026, 12, 31, 23, 59, 59);
            PropuskData propusk = PropuskData.NovyPropusk(DateTime.MinValue);
            propusk.VremyaOplatyTicks = oplata.Ticks;

            Assert.AreEqual(oplata, propusk.GetVremyaOplaty());
        }

        [TestMethod]
        public void PolnyKrugFleshki_JsonAesJson_PropuskTotZhe()
        {
            // ровно то, что делает UsbKartaService при записи/чтении pass.dat
            PropuskData ishodny = PropuskData.NovyPropusk(new DateTime(2026, 7, 4, 8, 0, 0));
            ishodny.IsPaid = true;
            ishodny.VremyaOplatyTicks = new DateTime(2026, 7, 4, 10, 15, 0).Ticks;

            JavaScriptSerializer serializator = new JavaScriptSerializer();
            string shifrovka = BezopasnostService.Shifrovat(serializator.Serialize(ishodny));
            PropuskData prochitanny = serializator.Deserialize<PropuskData>(
                BezopasnostService.Rasshifrovat(shifrovka));

            Assert.AreEqual(ishodny.Id, prochitanny.Id);
            Assert.AreEqual(ishodny.VremyaZaezdaTicks, prochitanny.VremyaZaezdaTicks);
            Assert.AreEqual(ishodny.IsPaid, prochitanny.IsPaid);
            Assert.AreEqual(ishodny.VremyaOplatyTicks, prochitanny.VremyaOplatyTicks);
        }
    }
}

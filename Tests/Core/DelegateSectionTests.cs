using System;
using NUnit.Framework;

namespace SaveSystem.Tests
{
    /** <summary>A section made from a key and two functions: saving, restoring and its default.</summary> */
    public sealed class DelegateSectionTests
    {
        private sealed class Counter
        {
            public int Value;
        }

        [Test]
        public void ItCapturesUnderItsKey_AndRestoresWhatItSaved()
        {
            var live = new Counter { Value = 7 };
            var service = new SaveService();
            service.Register(new DelegateSection<CounterData>("counter", () => new CounterData { Value = live.Value }, data => live.Value = data.Value));

            var saved = service.Capture();
            live.Value = 0;
            service.Restore(saved);

            Assert.That(saved.Sections["counter"].Data["Value"].ToObject<int>(), Is.EqualTo(7));
            Assert.That(live.Value, Is.EqualTo(7));
        }

        [Test]
        public void ANewGame_StartsFromItsDefault_OrANewDataWithNone()
        {
            var plain = new Counter { Value = 5 };
            var seeded = new Counter { Value = 5 };
            var service = new SaveService();
            service.Register(new DelegateSection<CounterData>("plain", () => new CounterData(), data => plain.Value = data.Value));
            service.Register(new DelegateSection<CounterData>("seeded", () => new CounterData(), data => seeded.Value = data.Value, () => new CounterData { Value = 3 }));

            service.NewGame();

            Assert.That((plain.Value, seeded.Value), Is.EqualTo((0, 3)));
        }

        [Test]
        public void ASaveWithoutIt_RestoresItsDefault_EvenANullOne()
        {
            var restored = new CounterData { Value = 5 };
            var service = new SaveService();
            var saved = service.Capture();
            service.Register(new DelegateSection<CounterData>("nothing", () => new CounterData(), data => restored = data, () => null));

            service.Restore(saved);

            Assert.That(restored, Is.Null);
        }

        [Test]
        public void ItNeedsAKey_ACapture_AndARestore()
        {
            Assert.Throws<ArgumentException>(() => new DelegateSection<CounterData>("", () => new CounterData(), _ => { }));
            Assert.Throws<ArgumentNullException>(() => new DelegateSection<CounterData>("k", null, _ => { }));
            Assert.Throws<ArgumentNullException>(() => new DelegateSection<CounterData>("k", () => new CounterData(), null));
        }
    }
}

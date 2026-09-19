using System;
using System.Collections.Generic;
using GuildMaster.Core;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>EventBus delivery rules, including (un)subscribing while an event is being delivered.</summary>
    public class EventBusTests
    {
        struct PingEvent { public int Value; }

        [SetUp]
        public void SetUp() => EventBus.Clear();

        [TearDown]
        public void TearDown() => EventBus.Clear();

        [Test]
        public void Subscribe_IsIdempotent_AndUnsubscribeStopsDelivery()
        {
            int calls = 0;
            Action<PingEvent> handler = _ => calls++;
            EventBus.Subscribe(handler);
            EventBus.Subscribe(handler);
            EventBus.Publish(new PingEvent());
            Assert.That(calls, Is.EqualTo(1), "subscribing twice still delivers once");

            EventBus.Unsubscribe(handler);
            EventBus.Publish(new PingEvent());
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void ChangesDuringDelivery_ApplyFromTheNextEvent()
        {
            var log = new List<string>();
            Action<PingEvent> late = e => log.Add("late" + e.Value);
            Action<PingEvent> second = e => log.Add("second" + e.Value);
            Action<PingEvent> first = null;
            first = e =>
            {
                log.Add("first" + e.Value);
                EventBus.Unsubscribe(second);   // still gets this event
                EventBus.Subscribe(late);       // only from the next one
                EventBus.Unsubscribe(first);
            };
            EventBus.Subscribe(first);
            EventBus.Subscribe(second);

            EventBus.Publish(new PingEvent { Value = 1 });
            EventBus.Publish(new PingEvent { Value = 2 });
            Assert.That(log, Is.EqualTo(new[] { "first1", "second1", "late2" }));
        }

        [Test]
        public void AThrowingHandler_DoesNotStopTheOthers_AndClearRemovesAll()
        {
            int calls = 0;
            EventBus.Subscribe<PingEvent>(_ => throw new InvalidOperationException("test"));
            EventBus.Subscribe<PingEvent>(_ => calls++);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("InvalidOperationException: test"));
            EventBus.Publish(new PingEvent());
            Assert.That(calls, Is.EqualTo(1));

            EventBus.Clear();
            EventBus.Publish(new PingEvent());
            Assert.That(calls, Is.EqualTo(1));
        }
    }
}

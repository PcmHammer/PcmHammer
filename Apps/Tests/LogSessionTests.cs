// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// Unit tests for LogSession, which composes the parameter sources into rows. The sources here
    /// are fakes: what is being tested is the composition - routing, ordering, and what happens when
    /// one source does not deliver - none of which needs a PCM or a bus.
    /// </summary>
    [TestClass]
    public class LogSessionTests
    {
        /// <summary>A source that claims named parameters and returns whatever it is told to.</summary>
        private class FakeSource : IParameterSource
        {
            private readonly string[] claimed;

            public FakeSource(string name, string[] claimed, params string[] columnNames)
            {
                this.Name = name;
                this.claimed = claimed;
                this.ColumnNames = columnNames.ToList();
                this.Values = columnNames.Select(x => Element(x)).ToList();
            }

            public string Name { get; }
            public IReadOnlyList<BusProtocol> SupportedBuses => Array.Empty<BusProtocol>();
            public List<string> ColumnNames { get; set; }
            public List<LogRowElement> Values { get; set; }
            public bool OpenResult { get; set; } = true;
            public IReadOnlyList<LogColumn>? OpenedWith { get; private set; }
            public bool Started { get; private set; }
            public bool Closed { get; private set; }

            public bool CanProvide(Parameter parameter) => this.claimed.Contains(parameter.Id);

            public Task<IReadOnlyList<ParameterAvailability>> Probe(
                IReadOnlyList<Parameter> candidates, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<ParameterAvailability>>(Array.Empty<ParameterAvailability>());

            public Task<bool> Open(IReadOnlyList<LogColumn> columns, CancellationToken cancellationToken)
            {
                this.OpenedWith = columns;
                return Task.FromResult(this.OpenResult);
            }

            public Task Start(CancellationToken cancellationToken)
            {
                this.Started = true;
                return Task.CompletedTask;
            }

            public bool Suspended { get; private set; }

            public Task Suspend()
            {
                this.Suspended = true;
                return Task.CompletedTask;
            }

            public Task Resume()
            {
                this.Suspended = false;
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<LogRowElement>> Read(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<LogRowElement>>(this.Values);

            public IEnumerable<string> GetColumnNames() => this.ColumnNames;

            public Task Close()
            {
                this.Closed = true;
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }
        }

        private static LogRowElement Element(string id) => new LogRowElement(id, id, "units", "1", 1);

        private static LogColumn Column(string id)
        {
            Conversion conversion = new Conversion("units", "x", "0.00");
            Parameter parameter = new RamParameter(
                id, id, "", "uint8", false, new[] { conversion }, new Dictionary<uint, uint>());
            return new LogColumn(parameter, conversion, false);
        }

        private static async Task<LogSession> Opened(params IParameterSource[] sources)
        {
            LogSession session = new LogSession(sources);
            await session.Open(new[] { Column("a"), Column("b") }, CancellationToken.None);
            return session;
        }

        [TestMethod]
        public async Task Open_GivesEachSourceOnlyTheColumnsItClaims()
        {
            FakeSource first = new FakeSource("first", new[] { "a" }, "A");
            FakeSource second = new FakeSource("second", new[] { "b" }, "B");

            await Opened(first, second);

            Assert.AreEqual("a", first.OpenedWith!.Single().Parameter.Id);
            Assert.AreEqual("b", second.OpenedWith!.Single().Parameter.Id);
        }

        [TestMethod]
        public async Task ColumnNames_AreInSourceOrder()
        {
            FakeSource first = new FakeSource("first", new[] { "a" }, "A1", "A2");
            FakeSource second = new FakeSource("second", new[] { "b" }, "B1");

            LogSession session = await Opened(first, second);

            CollectionAssert.AreEqual(
                new[] { "A1", "A2", "B1" }, session.GetColumnNames().ToArray());
        }

        [TestMethod]
        public async Task Read_ConcatenatesInSourceOrder()
        {
            FakeSource first = new FakeSource("first", new[] { "a" }, "A1", "A2");
            FakeSource second = new FakeSource("second", new[] { "b" }, "B1");

            LogSession session = await Opened(first, second);
            IReadOnlyList<LogRowElement> row = await session.Read(CancellationToken.None);

            CollectionAssert.AreEqual(
                new[] { "A1", "A2", "B1" }, row.Select(x => x.ParameterId).ToArray());
        }

        [TestMethod]
        public async Task Read_AbandonsTheRowWhenASourceDeliversTooFew()
        {
            // A short row would put every later source's values under the wrong columns, so the
            // whole row is dropped and the caller asks again.
            FakeSource first = new FakeSource("first", new[] { "a" }, "A1", "A2");
            FakeSource second = new FakeSource("second", new[] { "b" }, "B1");

            LogSession session = await Opened(first, second);
            first.Values = new List<LogRowElement>();

            Assert.AreEqual(0, (await session.Read(CancellationToken.None)).Count);
        }

        [TestMethod]
        public async Task Open_SkipsASourceThatContributesNoColumns()
        {
            // An auxiliary bus with no parameters defined for it. Keeping it would make every row
            // look incomplete, because it would owe nothing and deliver nothing.
            FakeSource first = new FakeSource("first", new[] { "a" }, "A1");
            FakeSource empty = new FakeSource("empty", new[] { "b" });

            LogSession session = await Opened(first, empty);
            IReadOnlyList<LogRowElement> row = await session.Read(CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "A1" }, row.Select(x => x.ParameterId).ToArray());
        }

        [TestMethod]
        public async Task Open_SkipsASourceThatRefusesToOpen()
        {
            FakeSource first = new FakeSource("first", new[] { "a" }, "A1");
            FakeSource refused = new FakeSource("refused", new[] { "b" }, "B1") { OpenResult = false };

            LogSession session = await Opened(first, refused);

            CollectionAssert.AreEqual(new[] { "A1" }, session.GetColumnNames().ToArray());
        }

        [TestMethod]
        public async Task Open_IsFalseWhenNoSourceContributes()
        {
            FakeSource refused = new FakeSource("refused", new[] { "a" }, "A1") { OpenResult = false };

            LogSession session = new LogSession(new IParameterSource[] { refused });

            Assert.IsFalse(await session.Open(new[] { Column("a") }, CancellationToken.None));
        }

        [TestMethod]
        public async Task SuspendAndResume_ReachOnlyTheOpenSources()
        {
            FakeSource open = new FakeSource("open", new[] { "a" }, "A1");
            FakeSource refused = new FakeSource("refused", new[] { "b" }, "B1") { OpenResult = false };

            LogSession session = await Opened(open, refused);

            await session.Suspend();
            Assert.IsTrue(open.Suspended);
            Assert.IsFalse(refused.Suspended);

            await session.Resume();
            Assert.IsFalse(open.Suspended);
        }

        [TestMethod]
        public async Task StartAndClose_ReachOnlyTheOpenSources()
        {
            FakeSource open = new FakeSource("open", new[] { "a" }, "A1");
            FakeSource refused = new FakeSource("refused", new[] { "b" }, "B1") { OpenResult = false };

            LogSession session = await Opened(open, refused);
            await session.Start(CancellationToken.None);
            await session.Close();

            Assert.IsTrue(open.Started);
            Assert.IsTrue(open.Closed);
            Assert.IsFalse(refused.Started);
            Assert.IsFalse(refused.Closed);
        }
    }
}

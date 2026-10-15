// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// Unit tests for routing parameters to the source that supplies them, which is what gives each
    /// connection its own list.
    /// </summary>
    [TestClass]
    public class ParameterSourceTests
    {
        private static readonly Conversion Units = new Conversion("units", "x", "0.00");

        private static PidParameter Pid(string id, string source, params uint[] osids)
        {
            PidParameter parameter = new PidParameter(
                id, id, "", "uint8", false, new[] { Units }, 0x1234, osids);
            parameter.Source = source;
            return parameter;
        }

        private static ParameterDatabase Database(params Parameter[] parameters)
        {
            ParameterDatabase database = new ParameterDatabase(
                new Dictionary<uint, IEnumerable<BusParameter>>());

            foreach (Parameter parameter in parameters)
            {
                database.AddParameter(parameter);
            }

            return database;
        }

        private static BusParameter Broadcast(string id)
        {
            BusParameter parameter = new BusParameter(
                0x100, 0, 1, true, id, id, "", new[] { Units }, Aggregation.Last);
            parameter.Source = ParameterSources.Broadcast;
            return parameter;
        }

        [TestMethod]
        public void AuxiliaryList_IncludesBroadcastParametersFromTheirOwnCollection()
        {
            // Bus parameters are stored apart from the rest, so the per-connection query has to span
            // both or the auxiliary list comes back empty.
            ParameterDatabase database = new ParameterDatabase(
                new Dictionary<uint, IEnumerable<BusParameter>>
                {
                    { 0x100, new[] { Broadcast("aem") } },
                });

            database.AddParameter(Pid("pid", ParameterSources.GmEnhancedObd));

            CollectionAssert.AreEquivalent(
                new[] { "aem" },
                database.ListParametersForConnection(0, ParameterSources.AuxiliaryConnection)
                    .Select(x => x.Id).ToArray());

            CollectionAssert.AreEquivalent(
                new[] { "pid" },
                database.ListParametersForConnection(0, ParameterSources.PcmConnection)
                    .Select(x => x.Id).ToArray());
        }

        [TestMethod]
        public void Connection_SeesOnlyItsOwnSources()
        {
            ParameterDatabase database = Database(
                Pid("pid", ParameterSources.GmEnhancedObd),
                Pid("math", ParameterSources.Math),
                Pid("broadcast", ParameterSources.Broadcast));

            CollectionAssert.AreEquivalent(
                new[] { "pid", "math" },
                database.ListParametersForConnection(0, ParameterSources.PcmConnection)
                    .Select(x => x.Id).ToArray());

            CollectionAssert.AreEquivalent(
                new[] { "broadcast" },
                database.ListParametersForConnection(0, ParameterSources.AuxiliaryConnection)
                    .Select(x => x.Id).ToArray());
        }

        [TestMethod]
        public void Connection_StillHonoursOperatingSystemSupport()
        {
            ParameterDatabase database = Database(
                Pid("everyOs", ParameterSources.GmEnhancedObd),
                Pid("otherOsOnly", ParameterSources.GmEnhancedObd, 12345678));

            CollectionAssert.AreEquivalent(
                new[] { "everyOs" },
                database.ListParametersForConnection(999, ParameterSources.PcmConnection)
                    .Select(x => x.Id).ToArray());
        }

        [TestMethod]
        public void PcmSource_ProvidesItsOwnSourceAndMath()
        {
            PcmParameterSource source = new PcmParameterSource(null!, 0, new MockLogger());

            Assert.IsTrue(source.CanProvide(Pid("a", ParameterSources.GmEnhancedObd)));
            Assert.IsTrue(source.CanProvide(Pid("b", ParameterSources.Math)));
            Assert.IsFalse(source.CanProvide(Pid("c", ParameterSources.Broadcast)));
        }

        [TestMethod]
        public void AuxiliarySource_ProvidesBroadcastOnly()
        {
            AuxiliaryBusParameterSource source = new AuxiliaryBusParameterSource(
                new AuxiliaryBusLogger(
                    new ParameterDatabase(new Dictionary<uint, IEnumerable<BusParameter>>()),
                    new MockLogger()),
                enabled: true);

            Assert.IsTrue(source.CanProvide(Pid("a", ParameterSources.Broadcast)));
            Assert.IsFalse(source.CanProvide(Pid("b", ParameterSources.GmEnhancedObd)));
            Assert.IsFalse(source.CanProvide(Pid("c", ParameterSources.Math)));
        }

        [TestMethod]
        public void BusConstraint_IsPerParameterNotPerSource()
        {
            // AEM and Plex build CAN products and frame the data themselves, so those parameters
            // exist nowhere else. A VPW broadcast read by the same source is not constrained.
            BusParameter vendorGauge = Broadcast("aem");
            vendorGauge.RequiredBus = BusProtocol.Can500k;

            BusParameter clusterMessage = Broadcast("cluster");
            clusterMessage.RequiredBus = null;

            Assert.AreEqual(BusProtocol.Can500k, vendorGauge.RequiredBus);
            Assert.IsNull(clusterMessage.RequiredBus);

            // Both are still the same source, and both still belong to the auxiliary list: the
            // constraint is stated against the row, not used to filter it out.
            AuxiliaryBusParameterSource source = new AuxiliaryBusParameterSource(
                new AuxiliaryBusLogger(
                    new ParameterDatabase(new Dictionary<uint, IEnumerable<BusParameter>>()),
                    new MockLogger()),
                enabled: true);

            Assert.IsTrue(source.CanProvide(vendorGauge));
            Assert.IsTrue(source.CanProvide(clusterMessage));
        }

        [TestMethod]
        public async System.Threading.Tasks.Task AuxiliarySource_ContributesNothingWhenNoBusIsConfigured()
        {
            ParameterDatabase database = new ParameterDatabase(
                new Dictionary<uint, IEnumerable<BusParameter>> { { 0x100, new[] { Broadcast("aem") } } });

            AuxiliaryBusLogger busLogger = new AuxiliaryBusLogger(database, new MockLogger());

            Assert.IsTrue(await new AuxiliaryBusParameterSource(busLogger, enabled: true)
                .Open(new LogColumn[0], System.Threading.CancellationToken.None));

            Assert.IsFalse(await new AuxiliaryBusParameterSource(busLogger, enabled: false)
                .Open(new LogColumn[0], System.Threading.CancellationToken.None));
        }

        [TestMethod]
        public void UnknownSource_BelongsToNoConnection()
        {
            // A definition naming a source nothing implements. It must not quietly land in the PCM
            // list, where it would be requested as a PID and refused.
            ParameterDatabase database = Database(Pid("odd", "Some Future Device"));

            Assert.AreEqual(
                0, database.ListParametersForConnection(0, ParameterSources.PcmConnection).Count());
            Assert.AreEqual(
                0, database.ListParametersForConnection(0, ParameterSources.AuxiliaryConnection).Count());
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace Tests
{
    [TestClass]
    public class LiveDataRecordingTests
    {
        private static LogRowElement Value(string id="rpm", string name="Engine Speed", double value=800) =>
            new LogRowElement(id,name,"RPM",value.ToString(CultureInfo.CurrentCulture),value);
        private static LogColumn[] Columns() => new[] {new LogColumn(new TestParameter(),new Conversion("RPM","x","0"),false)};
        private sealed class TestParameter : Parameter {public TestParameter(){Id="rpm";Name="Engine Speed";} public override bool IsSupported(uint osid)=>true;}
        private sealed class Source : IParameterSource
        {
            public Func<int,IReadOnlyList<LogRowElement>> Rows {get;set;} = _ => new[] {Value()};
            public bool Opened, Suspended, Closed;
            public bool OpenResult=true;
            public int Reads;
            public string Name=>"Test";
            public IReadOnlyList<BusProtocol> SupportedBuses=>Array.Empty<BusProtocol>();
            public bool CanProvide(Parameter p)=>true;
            public Task<IReadOnlyList<ParameterAvailability>> Probe(IReadOnlyList<Parameter> p,CancellationToken c)=>Task.FromResult<IReadOnlyList<ParameterAvailability>>(Array.Empty<ParameterAvailability>());
            public Task<bool> Open(IReadOnlyList<LogColumn> columns,CancellationToken token){Opened=true;return Task.FromResult(OpenResult);}
            public Task Start(CancellationToken c)=>Task.CompletedTask;
            public Task Suspend(){Suspended=true;return Task.CompletedTask;}
            public Task Resume()=>Task.CompletedTask;
            public IEnumerable<string> GetColumnNames()=>new[]{"Engine Speed (RPM)"};
            public Task<IReadOnlyList<LogRowElement>> Read(CancellationToken c)=>Task.FromResult(Rows(++Reads));
            public Task Close(){Closed=true;return Task.CompletedTask;}
            public void Dispose(){}
        }

        [TestMethod]
        public async Task SavesCompleteRowBeforeCancellationAndLabelsDemo()
        {
            var source=new Source();var output=new StringWriter();using(var stop=new CancellationTokenSource())
            {
                await Assert.ThrowsExceptionAsync<TaskCanceledException>(()=>LiveDataRecording.Run(source,Columns(),output,"DEMO",row=>stop.Cancel(),stop.Token));
            }
            string[] lines=output.ToString().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(2,lines.Length);
            StringAssert.StartsWith(lines[1],"\"DEMO\",");
            StringAssert.EndsWith(lines[1],",800");
            Assert.IsTrue(source.Suspended && source.Closed);
        }

        [TestMethod]
        public async Task IncompleteSampleIsNotWrittenOrPublished()
        {
            var source=new Source{Rows=i=>i==1 ? Array.Empty<LogRowElement>() : new[]{Value()}};
            var output=new StringWriter();int callbacks=0;
            using(var stop=new CancellationTokenSource())
                await Assert.ThrowsExceptionAsync<TaskCanceledException>(()=>LiveDataRecording.Run(source,Columns(),output,"Vehicle",row=>{callbacks++;stop.Cancel();},stop.Token));
            Assert.AreEqual(2,source.Reads);Assert.AreEqual(1,callbacks);
            Assert.AreEqual(2,output.ToString().Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries).Length);
        }

        [TestMethod]
        public async Task ChangingColumnsStopsWithoutWritingMisalignedRow()
        {
            var source=new Source{Rows=i=>new[]{Value(i==1?"rpm":"temperature")}};
            var output=new StringWriter();
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(()=>LiveDataRecording.Run(source,Columns(),output,"Vehicle",_=>{},CancellationToken.None));
            Assert.AreEqual(2,output.ToString().Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.IsTrue(source.Suspended && source.Closed);
        }

        [TestMethod]
        public async Task UsesInvariantNumbersAndQuotesHeaders()
        {
            CultureInfo previous=CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
                var source=new Source{Rows=_=>new[]{Value(name:"Speed, \"test\"",value:12.5)}};
                var output=new StringWriter();using(var stop=new CancellationTokenSource())
                    await Assert.ThrowsExceptionAsync<TaskCanceledException>(()=>LiveDataRecording.Run(source,Columns(),output,"Vehicle",_=>stop.Cancel(),stop.Token));
                StringAssert.Contains(output.ToString(),"\"Speed, \"\"test\"\" (RPM)\"");
                StringAssert.Contains(output.ToString(),",12.5");
            }
            finally {CultureInfo.CurrentCulture=previous;}
        }

        [TestMethod]
        public async Task FailedStartClosesSourceWithoutWritingData()
        {
            var source=new Source{OpenResult=false};var output=new StringWriter();
            await Assert.ThrowsExceptionAsync<LogStartFailedException>(()=>LiveDataRecording.Run(source,Columns(),output,"Vehicle",_=>{},CancellationToken.None));
            Assert.IsTrue(source.Suspended && source.Closed);Assert.AreEqual(0,source.Reads);Assert.AreEqual("",output.ToString());
        }

        [TestMethod]
        public async Task PreCancelledSessionDoesNotOpenSource()
        {
            var source=new Source();using(var stop=new CancellationTokenSource())
            {
                stop.Cancel();
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(()=>LiveDataRecording.Run(source,Columns(),new StringWriter(),"Vehicle",_=>{},stop.Token));
            }
            Assert.IsFalse(source.Opened);Assert.AreEqual(0,source.Reads);
        }

        [TestMethod]
        public async Task ReadFailureReleasesSourceAndPreservesEarlierRows()
        {
            var source=new Source{Rows=i=>i==1 ? new[]{Value()} : throw new IOException("connection lost")};
            var output=new StringWriter();
            await Assert.ThrowsExceptionAsync<IOException>(()=>LiveDataRecording.Run(source,Columns(),output,"Vehicle",_=>{},CancellationToken.None));
            Assert.IsTrue(source.Suspended && source.Closed);StringAssert.Contains(output.ToString(),",800");
        }
    }
}

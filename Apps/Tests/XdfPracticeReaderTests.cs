// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace Tests
{
    [TestClass]
    public class XdfPracticeReaderTests
    {
        private static byte[] Image()=>File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"TestData","E38_2048KiB_12607218.bin"));
        private static int Address(byte[] bytes)=>FileValidator.GetE38MasterSegments(bytes).First(s=>s.BlockType==BlockType.Calibration).Start+0x40;
        private static string Entry(int address,string math="X/2",string flags="0x05",string extra="")=>$"<XDFTABLE><title>Test mapping</title><XDFAXIS id='x'><LABEL index='0' value='1000'/><LABEL index='1' value='2000'/></XDFAXIS><XDFAXIS id='y'><LABEL index='0' value='0.1'/><LABEL index='1' value='0.2'/></XDFAXIS><XDFAXIS id='z'><EMBEDDEDDATA mmedaddress='0x{address:X}' mmedelementsizebits='8' mmedtypeflags='{flags}' mmedrowcount='2' mmedcolcount='2' {extra}/><MATH equation='{math}'><VAR id='X'/></MATH></XDFAXIS></XDFTABLE>";
        private static string Doc(string entries,string os="12607218",string defaults="lsbfirst='0'")=>$"<XDFFORMAT><XDFHEADER><deftitle>{os}</deftitle><BASEOFFSET offset='0' subtract='0'/><DEFAULTS {defaults}/></XDFHEADER>{entries}</XDFFORMAT>";
        private static XdfPracticeImport Read(byte[] bytes,string text)=>XdfPracticeReader.Read(bytes,new StringReader(text),new MockLogger());
        [TestMethod]
        public void MatchedPairOpensSandboxAndNeverEnablesBinExport()
        {
            var bytes=Image();var original=(byte[])bytes.Clone();var result=Read(bytes,Doc(Entry(Address(bytes))));
            Assert.AreEqual(1,result.Definition.Fields.Count);Assert.IsTrue(result.Definition.DemoOnly);
            var editor=new CalibrationEditor(bytes,result.Definition,new MockLogger(),true);var field=editor.Definition.Fields[0];
            Assert.AreEqual("s8",field.Storage);Assert.AreEqual(-64,field.Minimum);Assert.AreEqual(63.5,field.Maximum);
            CollectionAssert.AreEqual(new[]{"1000","2000"},field.ColumnLabels);
            editor.Edit(field,new[]{-1.0,0,1,2});CollectionAssert.AreEqual(new[]{-1.0,0,1,2},editor.Read(field));
            Assert.ThrowsException<InvalidOperationException>(()=>editor.Export());CollectionAssert.AreEqual(original,bytes);
        }
        [DataTestMethod]
        [DataRow("12614088")][DataRow("12607218 and 12614088")][DataRow("E38")]
        public void MissingAmbiguousAndWrongOsAreRejected(string title)
        {var bytes=Image();Assert.ThrowsException<InvalidDataException>(()=>Read(bytes,Doc(Entry(Address(bytes)),title)));}
        [TestMethod]
        public void InvalidBinAndExternalEntitiesAreRejected()
        {
            Assert.ThrowsException<InvalidDataException>(()=>Read(new byte[2097152],Doc("")));
            var bytes=Image();Assert.ThrowsException<System.Xml.XmlException>(()=>Read(bytes,"<!DOCTYPE XDFFORMAT [<!ENTITY x SYSTEM 'file:///should-not-be-read'>]>"+Doc(Entry(Address(bytes)))));
        }
        [TestMethod]
        public void OsHeadersAndOverlapsAreSkippedWithoutReplacingGoodTable()
        {
            var bytes=Image();int addr=Address(bytes);var result=Read(bytes,Doc(Entry(0x10040)+Entry(addr-0x40)+Entry(addr)+Entry(addr+1)));
            Assert.AreEqual(1,result.Definition.Fields.Count);Assert.AreEqual(addr,result.Definition.Fields[0].Address);
            Assert.AreEqual(3,result.Notes.Count(n=>n.Contains("skipped")));
        }
        [DataTestMethod]
        [DataRow("X/0","0x05","")][DataRow("System.IO.File.ReadAllText(X)","0x05","")]
        [DataRow("X/2","0x07","")][DataRow("X/2","0x01","")]
        [DataRow("X/2","0x10005","")][DataRow("X/2","0x05","mmedmajorstridebits='16'")]
        public void UnsupportedMathLayoutAndStorageAreNeverGuessed(string math,string flags,string extra)
        {var bytes=Image();Assert.ThrowsException<InvalidDataException>(()=>Read(bytes,Doc(Entry(Address(bytes),math,flags,extra))));}
        [TestMethod]
        public void WrongAxisCountAndLinkedExpressionsAreRejected()
        {
            var bytes=Image();string text=Entry(Address(bytes));
            Assert.ThrowsException<InvalidDataException>(()=>Read(bytes,Doc(text.Replace("<LABEL index='1' value='2000'/>",""))));
            Assert.ThrowsException<InvalidDataException>(()=>Read(bytes,Doc(text.Replace("<VAR id='X'/>","<VAR id='X'><link/></VAR>"))));
        }
        [TestMethod]
        public void NonzeroBaseOffsetsAndLittleEndianDefaultsAreRejected()
        {
            var bytes=Image();string text=Doc(Entry(Address(bytes)));
            Assert.ThrowsException<InvalidDataException>(()=>Read(bytes,text.Replace("offset='0'","offset='1'")));
            Assert.ThrowsException<InvalidDataException>(()=>Read(bytes,Doc(Entry(Address(bytes)),defaults:"lsbfirst='1'")));
        }
        [TestMethod]
        public void ExplicitFloatMarkersDecodeRatherThanDisplayingIntegerBitPatterns()
        {
            var bytes=Image();int address=Address(bytes);var os=FileValidator.GetE38MasterSegments(bytes).First(s=>s.BlockType==BlockType.OperatingSystem);
            var def=new CalibrationDefinition{Osid=12607218,ImageSize=bytes.Length,OsSha256=CalibrationEditor.Hash(bytes,os.Start,os.Length),Fields={new CalibrationField{Id="float-test",Address=address,Rows=2,Columns=2,Storage="f32be",Minimum=-10,Maximum=10000}}};
            var source=new CalibrationEditor(bytes,def,new MockLogger());source.Edit(source.Definition.Fields[0],new[]{4000.0,3900.0,0.75,-1.25});bytes=source.Export();
            foreach(string marker in new[]{"standard","attribute","element"})
            {
                string entry=Entry(address,"X",marker=="standard"?"0x10004":"0x04",marker=="attribute"?"claudetool_float='1'":"").Replace("mmedelementsizebits='8'","mmedelementsizebits='32'");
                if(marker=="element")entry=entry.Replace("</XDFTABLE>","<claudetool_isfloat>1</claudetool_isfloat></XDFTABLE>");
                var result=Read(bytes,Doc(entry));Assert.AreEqual("f32be",result.Definition.Fields.Single().Storage);
                var editor=new CalibrationEditor(bytes,result.Definition,new MockLogger(),true);
                CollectionAssert.AreEqual(new[]{4000.0,3900.0,0.75,-1.25},editor.Read(editor.Definition.Fields[0]));
                Assert.ThrowsException<InvalidOperationException>(()=>editor.Export());
            }
        }
        [TestMethod]
        public void NonFiniteFloatAndUnknownExtensionsAreRejected()
        {
            var bytes=Image();int address=Address(bytes);var os=FileValidator.GetE38MasterSegments(bytes).First(s=>s.BlockType==BlockType.OperatingSystem);
            var def=new CalibrationDefinition{Osid=12607218,ImageSize=bytes.Length,OsSha256=CalibrationEditor.Hash(bytes,os.Start,os.Length),Fields={new CalibrationField{Id="float-bits",Address=address,Rows=2,Columns=2,Storage="u32be",Minimum=0,Maximum=uint.MaxValue}}};
            var source=new CalibrationEditor(bytes,def,new MockLogger());source.Edit(source.Definition.Fields[0],new[]{2143289344.0,0,0,0});bytes=source.Export();
            string entry=Entry(address,"X","0x04","claudetool_float='1'").Replace("mmedelementsizebits='8'","mmedelementsizebits='32'");
            Assert.ThrowsException<InvalidDataException>(()=>Read(bytes,Doc(entry)));
            Assert.ThrowsException<InvalidDataException>(()=>Read(bytes,Doc(entry.Replace("claudetool_float='1'","claudetool_float='unknown'"))));
        }
        [TestMethod]
        public void XdfCategoryMembershipSurvivesImport()
        {
            var bytes=Image();string xml=Doc(Entry(Address(bytes)).Replace("<title>Test mapping</title>","<title>Test mapping</title><CATEGORYMEM index='0' category='1'/>"));
            xml=xml.Replace("<deftitle>","<CATEGORY index='0x0' name='spark'/><deftitle>");
            Assert.AreEqual("spark",Read(bytes,xml).Definition.Fields.Single().Category);
        }
        [TestMethod]
        public void NumericOffsetAndNegativeFactorsUseCorrectEncodingRange()
        {
            var bytes=Image();var result=Read(bytes,Doc(Entry(Address(bytes),"X*-0.5+10")));
            var field=result.Definition.Fields[0];Assert.AreEqual(-0.5,field.Factor);Assert.AreEqual(10,field.Offset);
            Assert.AreEqual(-53.5,field.Minimum);Assert.AreEqual(74,field.Maximum);
        }
    }
}

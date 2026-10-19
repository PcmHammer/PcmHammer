// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace Tests
{
    [TestClass]
    public class CalibrationEditorTests
    {
        private static byte[] Image()=>File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"TestData","E38_2048KiB_12607218.bin"));
        private static CalibrationDefinition Definition(byte[] image)
        {
            var segments=FileValidator.GetE38MasterSegments(image);
            var os=segments.First(s=>s.BlockType==BlockType.OperatingSystem);
            var cal=segments.First(s=>s.BlockType==BlockType.Calibration);
            return new CalibrationDefinition {Name="Synthetic test mapping, never for a vehicle",Osid=12607218,ImageSize=image.Length,
                OsSha256=CalibrationEditor.Hash(image,os.Start,os.Length),
                Fields={new CalibrationField{Id="test",Name="Synthetic test bytes",Address=cal.Start+0x40,Rows=1,Columns=2,Storage="u16be",Minimum=0,Maximum=65535}}};
        }
        [TestMethod]
        public void ExportRecomputesChecksumsAndPreservesAllNonCalibrationBytes()
        {
            var original=Image();var copy=(byte[])original.Clone();var editor=new CalibrationEditor(original,Definition(original),new MockLogger());
            var field=editor.Definition.Fields[0];var before=editor.Read(field);editor.Edit(field,new[]{(before[0]+1)%65536,before[1]});
            Assert.IsTrue(editor.HasChanges);var exported=editor.Export();
            CollectionAssert.AreEqual(copy,original,"The supplied stock file is never edited.");
            Assert.IsTrue(new FileValidator(exported,new MockLogger()).IdentifyAndValidate());
            foreach(var segment in FileValidator.GetE38MasterSegments(original).Where(s=>s.BlockType!=BlockType.Calibration))
                CollectionAssert.AreEqual(original.Skip(segment.Start).Take(segment.Length).ToArray(),exported.Skip(segment.Start).Take(segment.Length).ToArray());
            var reopened=new CalibrationEditor(exported,Definition(original),new MockLogger());
            Assert.AreEqual((before[0]+1)%65536,reopened.Read(reopened.Definition.Fields[0])[0]);
            editor.Undo();Assert.IsFalse(editor.HasChanges);editor.Redo();Assert.IsTrue(editor.HasChanges);
        }
        [TestMethod]
        public void WrongOsAndFingerprintRemainLocked()
        {
            var image=Image();var definition=Definition(image);definition.Osid++;
            Assert.ThrowsException<InvalidDataException>(()=>new CalibrationEditor(image,definition,new MockLogger()));
            definition=Definition(image);definition.OsSha256=new string('0',64);
            Assert.ThrowsException<InvalidDataException>(()=>new CalibrationEditor(image,definition,new MockLogger()));
        }
        [TestMethod]
        public void OsAndSegmentHeaderAndOverlappingFieldsAreRejected()
        {
            var image=Image();var definition=Definition(image);definition.Fields[0].Address=0x10040;
            Assert.ThrowsException<InvalidDataException>(()=>new CalibrationEditor(image,definition,new MockLogger()));
            definition=Definition(image);definition.Fields[0].Address-=0x40;
            Assert.ThrowsException<InvalidDataException>(()=>new CalibrationEditor(image,definition,new MockLogger()));
            definition=Definition(image);definition.Fields.Add(new CalibrationField {Id="overlap",Address=definition.Fields[0].Address,Minimum=0,Maximum=65535});
            Assert.ThrowsException<InvalidDataException>(()=>new CalibrationEditor(image,definition,new MockLogger()));
        }
        [TestMethod]
        public void FailedEditIsAtomicAndDemoCannotExport()
        {
            var definition=new CalibrationDefinition {DemoOnly=true,ImageSize=16,Fields={new CalibrationField{Id="demo",Columns=2,Minimum=0,Maximum=100}}};
            Assert.ThrowsException<InvalidDataException>(()=>new CalibrationEditor(new byte[16],definition,new MockLogger()));
            var editor=new CalibrationEditor(new byte[16],definition,new MockLogger(),true);var field=editor.Definition.Fields[0];
            Assert.ThrowsException<ArgumentOutOfRangeException>(()=>editor.Edit(field,new[]{10.0,101.0}));
            Assert.IsFalse(editor.HasChanges);Assert.IsFalse(editor.CanUndo);
            Assert.ThrowsException<ArgumentOutOfRangeException>(()=>editor.Edit(field,new[]{double.NaN,20.0}));
            Assert.ThrowsException<InvalidOperationException>(()=>editor.Export());
        }
        [DataTestMethod]
        [DataRow("u32be",4294967295.0)] [DataRow("s32be",-2147483648.0)]
        [DataRow("s16be",-32768.0)] [DataRow("s8",-128.0)] [DataRow("f32be",12.5)]
        public void StorageRoundTripsWithoutUnsignedPrecisionLoss(string storage,double value)
        {
            var definition=new CalibrationDefinition{DemoOnly=true,ImageSize=16,Fields={new CalibrationField{Id="demo",Storage=storage,Minimum=-2147483648.0,Maximum=4294967295.0}}};
            var editor=new CalibrationEditor(new byte[16],definition,new MockLogger(),true);var field=editor.Definition.Fields[0];
            editor.Edit(field,new[]{value});Assert.AreEqual(value,editor.Read(field)[0]);
        }
        [TestMethod]
        public void MutatingDefinitionCannotBypassValidatedAddresses()
        {
            var image=Image();var definition=Definition(image);var editor=new CalibrationEditor(image,definition,new MockLogger());
            definition.Fields[0].Address=0; // caller model is separate
            Assert.AreNotEqual(0,editor.Definition.Fields[0].Address);
            editor.Definition.Fields[0].Address=0;
            Assert.ThrowsException<InvalidDataException>(()=>editor.Edit(editor.Definition.Fields[0],new[]{0.0,0.0}));
            Assert.ThrowsException<InvalidDataException>(()=>editor.Export());
        }
        [TestMethod]
        public void AxisMetadataMustMatchDimensionsAndSurvivesDefinitionCopy()
        {
            var image=Image();var definition=Definition(image);var field=definition.Fields[0];field.RowAxisLabel="MAP (kPa)";field.ColumnAxisLabel="RPM";field.RowLabels=new[]{"20"};field.ColumnLabels=new[]{"400","800"};
            var editor=new CalibrationEditor(image,definition,new MockLogger());
            Assert.AreEqual("RPM",editor.Definition.Fields[0].ColumnAxisLabel);
            CollectionAssert.AreEqual(new[]{"400","800"},editor.Definition.Fields[0].ColumnLabels);
            field.ColumnLabels=new[]{"400"};
            Assert.ThrowsException<InvalidDataException>(()=>new CalibrationEditor(image,definition,new MockLogger()));
        }
        [TestMethod]
        public void XdfInspectionListsTablesButRejectsExternalEntities()
        {
            var items=XdfInspector.Read(new StringReader("<XDFFORMAT><XDFTABLE><title>Example</title></XDFTABLE></XDFFORMAT>"));
            Assert.AreEqual("Example",items.Single().Name);
            Assert.ThrowsException<System.Xml.XmlException>(()=>XdfInspector.Read(new StringReader("<!DOCTYPE XDFFORMAT [<!ENTITY x SYSTEM 'file:///secret'>]><XDFFORMAT>&x;</XDFFORMAT>")));
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;
namespace Tests
{
    [TestClass] public class TableSelectionToolsTests
    {
        [DataTestMethod]
        [DataRow(TableTool.Fill,7,7)] [DataRow(TableTool.Offset,-2,8)] [DataRow(TableTool.Multiply,1.5,15)]
        [DataRow(TableTool.Divide,2,5)] [DataRow(TableTool.Percent,10,11)] [DataRow(TableTool.Percent,-10,9)]
        public void ArithmeticTouchesOnlySelectedCells(TableTool tool,double amount,double expected)
        {
            var source=new[]{10.0,20.0,30.0,40.0};var result=TableSelectionTools.Transform(source,2,2,new[]{0,3},tool,amount);
            Assert.AreEqual(expected,result[0],0.000001);Assert.AreEqual(20.0,result[1]);Assert.AreEqual(30.0,result[2]);Assert.AreEqual(10.0,source[0]);
        }
        [TestMethod] public void InterpolationRespectsNonUniformNumericAxesAndKeepsEndpoints()
        {
            var result=TableSelectionTools.Transform(new[]{0.0,99,40},1,3,new[]{0,1,2},TableTool.InterpolateRows,0,columnAxis:new[]{0.0,100,400});
            CollectionAssert.AreEqual(new[]{0.0,10,40},result);
            result=TableSelectionTools.Transform(new[]{10.0,99,30},3,1,new[]{0,1,2},TableTool.InterpolateColumns,0);
            CollectionAssert.AreEqual(new[]{10.0,20,30},result);
        }
        [TestMethod] public void SurfaceInterpolationUsesFourCorners()
        {
            var result=TableSelectionTools.Transform(new[]{0.0,99,20,99,99,99,20,99,40},3,3,Enumerable.Range(0,9).ToArray(),TableTool.InterpolateSurface,0);
            CollectionAssert.AreEqual(new[]{0.0,10,20,10,20,30,20,30,40},result);
        }
        [TestMethod] public void SmoothUsesSnapshotAndSelectedNeighborsOnly()
        {
            var result=TableSelectionTools.Transform(new[]{0.0,10,20,30},2,2,new[]{0,1,2,3},TableTool.Smooth,50);
            CollectionAssert.AreEqual(new[]{7.5,12.5,17.5,22.5},result);
            result=TableSelectionTools.Transform(new[]{0.0,1000,10,1000},2,2,new[]{0,2},TableTool.Smooth,100);
            CollectionAssert.AreEqual(new[]{10.0,1000,0,1000},result);
        }
        [TestMethod] public void RestoreUsesTheOriginalSelectedValues()
        {
            var result=TableSelectionTools.Transform(new[]{1.0,2,3,4},2,2,new[]{1},TableTool.RestoreOriginal,0,new[]{9.0,8,7,6});
            CollectionAssert.AreEqual(new[]{1.0,8,3,4},result);
        }
        [TestMethod] public void BadOperationsRejectWithoutChangingInput()
        {
            var source=new[]{1.0,2,3,4};
            Assert.ThrowsException<ArgumentException>(()=>TableSelectionTools.Transform(source,2,2,new[]{0},TableTool.Divide,0));
            Assert.ThrowsException<ArgumentException>(()=>TableSelectionTools.Transform(source,2,2,new[]{0,3},TableTool.InterpolateSurface,0));
            Assert.ThrowsException<ArgumentException>(()=>TableSelectionTools.Transform(source,2,2,new[]{0,1},TableTool.Smooth,101));
            Assert.ThrowsException<ArgumentException>(()=>TableSelectionTools.Transform(source,2,2,new[]{0},TableTool.Offset,double.NaN));
            Assert.ThrowsException<ArgumentException>(()=>TableSelectionTools.Transform(source,2,2,new[]{4},TableTool.Fill,0));
            Assert.ThrowsException<ArgumentException>(()=>TableSelectionTools.Transform(source,2,2,Array.Empty<int>(),TableTool.Fill,0));
            CollectionAssert.AreEqual(new[]{1.0,2,3,4},source);
        }
        [TestMethod] public void WholeSelectionIsOneAtomicUndoableEditAndRangeFailureLeavesHistoryIntact()
        {
            var definition=new CalibrationDefinition {DemoOnly=true,ImageSize=32,Fields={new CalibrationField{Id="test",Rows=2,Columns=2,Storage="u8",Minimum=0,Maximum=100}}};
            var editor=new CalibrationEditor(new byte[]{10,20,30,40,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},definition,new MockLogger(),true);var field=editor.Definition.Fields[0];
            var before=editor.Read(field);var changed=TableSelectionTools.Transform(before,2,2,new[]{0,1,2,3},TableTool.Offset,5);
            editor.Edit(field,changed,"Offset +5 on four cells");Assert.AreEqual(1,editor.Changes.Count);
            var bad=TableSelectionTools.Transform(changed,2,2,new[]{0,1},TableTool.Fill,101);
            Assert.ThrowsException<ArgumentOutOfRangeException>(()=>editor.Edit(field,bad));
            CollectionAssert.AreEqual(changed,editor.Read(field));Assert.AreEqual(1,editor.Changes.Count);
            editor.Undo();CollectionAssert.AreEqual(before,editor.Read(field));Assert.IsFalse(editor.CanUndo);
            editor.Redo();CollectionAssert.AreEqual(changed,editor.Read(field));
            CollectionAssert.AreEqual(before,editor.ReadOriginal(field));
        }
    }
}

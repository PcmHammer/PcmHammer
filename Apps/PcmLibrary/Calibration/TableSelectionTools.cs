// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
namespace PcmHacking
{
    public enum TableTool { Fill, Offset, Multiply, Divide, Percent, InterpolateRows, InterpolateColumns, InterpolateSurface, Smooth, RestoreOriginal }

    /// <summary>Pure selection transforms. The editor validates/encodes the complete candidate atomically.</summary>
    public static class TableSelectionTools
    {
        public static double[] Transform(IReadOnlyList<double> source,int rows,int columns,IReadOnlyCollection<int> selection,
            TableTool tool,double amount,IReadOnlyList<double>? original=null,IReadOnlyList<double>? rowAxis=null,IReadOnlyList<double>? columnAxis=null)
        {
            if(rows<1||columns<1||(long)rows*columns!=source.Count)throw new ArgumentException("Invalid table dimensions.");
            if(selection==null||selection.Count==0)throw new ArgumentException("Select one or more cells first.");
            var chosen=new HashSet<int>(selection);
            if(chosen.Any(i=>i<0||i>=source.Count))throw new ArgumentException("The selection is outside the table.");
            if(source.Any(v=>!Finite(v)))throw new ArgumentException("The table contains invalid numbers.");
            bool needsAmount=tool==TableTool.Fill||tool==TableTool.Offset||tool==TableTool.Multiply||tool==TableTool.Divide||tool==TableTool.Percent||tool==TableTool.Smooth;
            if(needsAmount&&!Finite(amount))throw new ArgumentException("Enter a finite amount.");
            if(tool==TableTool.Divide&&amount==0)throw new ArgumentException("Cannot divide by zero.");
            if(tool==TableTool.Smooth&&(amount<0||amount>100))throw new ArgumentException("Smoothing strength must be between 0 and 100 percent.");
            if(tool==TableTool.RestoreOriginal&&(original==null||original.Count!=source.Count))throw new ArgumentException("The original table is unavailable.");
            int top=chosen.Min(i=>i/columns),bottom=chosen.Max(i=>i/columns),left=chosen.Min(i=>i%columns),right=chosen.Max(i=>i%columns);
            bool interpolation=tool==TableTool.InterpolateRows||tool==TableTool.InterpolateColumns||tool==TableTool.InterpolateSurface;
            if(interpolation&&chosen.Count!=(bottom-top+1)*(right-left+1))throw new ArgumentException("Interpolation needs a solid rectangular selection.");
            if(tool==TableTool.InterpolateRows&&left==right)throw new ArgumentException("Select at least two columns.");
            if(tool==TableTool.InterpolateColumns&&top==bottom)throw new ArgumentException("Select at least two rows.");
            if(tool==TableTool.InterpolateSurface&&(top==bottom||left==right))throw new ArgumentException("Surface interpolation needs at least two rows and two columns.");
            var result=source.ToArray();
            foreach(int i in chosen)
            {
                int r=i/columns,c=i%columns;double value=source[i];
                switch(tool)
                {
                    case TableTool.Fill:value=amount;break;
                    case TableTool.Offset:value+=amount;break;
                    case TableTool.Multiply:value*=amount;break;
                    case TableTool.Divide:value/=amount;break;
                    case TableTool.Percent:value*=1+amount/100;break;
                    case TableTool.RestoreOriginal:value=original![i];break;
                    case TableTool.InterpolateRows:value=Lerp(source[r*columns+left],source[r*columns+right],Position(c,left,right,columnAxis,columns));break;
                    case TableTool.InterpolateColumns:value=Lerp(source[top*columns+c],source[bottom*columns+c],Position(r,top,bottom,rowAxis,rows));break;
                    case TableTool.InterpolateSurface:
                        double x=Position(c,left,right,columnAxis,columns),y=Position(r,top,bottom,rowAxis,rows);
                        value=Lerp(Lerp(source[top*columns+left],source[top*columns+right],x),Lerp(source[bottom*columns+left],source[bottom*columns+right],x),y);break;
                    case TableTool.Smooth:
                        var neighbors=new List<int>();
                        if(r>0&&chosen.Contains(i-columns))neighbors.Add(i-columns);
                        if(r<rows-1&&chosen.Contains(i+columns))neighbors.Add(i+columns);
                        if(c>0&&chosen.Contains(i-1))neighbors.Add(i-1);
                        if(c<columns-1&&chosen.Contains(i+1))neighbors.Add(i+1);
                        if(neighbors.Count>0)value=Lerp(source[i],neighbors.Average(n=>source[n]),amount/100);break;
                    default:throw new ArgumentException("Unknown table tool.");
                }
                if(!Finite(value))throw new ArgumentException("The operation produced an invalid number.");
                result[i]=value;
            }
            return result;
        }
        private static double Position(int index,int first,int last,IReadOnlyList<double>? axis,int expected)
        {
            if(axis==null)return(double)(index-first)/(last-first);
            if(axis.Count!=expected||axis.Any(v=>!Finite(v)))throw new ArgumentException("Invalid axis values.");
            bool increasing=axis[last]>axis[first];
            for(int i=first+1;i<=last;i++)if(increasing?axis[i]<=axis[i-1]:axis[i]>=axis[i-1])throw new ArgumentException("Interpolation axis must be strictly ordered.");
            return(axis[index]-axis[first])/(axis[last]-axis[first]);
        }
        private static double Lerp(double a,double b,double t)=>a+(b-a)*t;
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    }
}

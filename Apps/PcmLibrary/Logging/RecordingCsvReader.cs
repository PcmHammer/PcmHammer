// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
namespace PcmHacking
{
    public sealed class RecordingData
    {
        public string[] Headings {get;set;}=Array.Empty<string>();
        public List<double> Seconds {get;}=new List<double>();
        public List<double[]> Samples {get;}=new List<double[]>();
        public bool HasDemoData {get;set;}
    }
    public static class RecordingCsvReader
    {
        public static RecordingData Read(TextReader reader)
        {
            string first=reader.ReadLine()??throw new InvalidDataException("The CSV is empty.");
            string[] header=Fields(first);
            if(header.Length<4||header[0]!="Source"||header[1]!="TimestampUTC"||header[2]!="ElapsedSeconds")
                throw new InvalidDataException("Open a PCM Hammer preview recording CSV with Source, TimestampUTC and ElapsedSeconds columns.");
            var result=new RecordingData {Headings=new List<string>(header).GetRange(3,header.Length-3).ToArray()};
            string? line;double previous=double.NegativeInfinity;
            while((line=reader.ReadLine())!=null)
            {
                if(string.IsNullOrWhiteSpace(line))continue;
                string[] fields=Fields(line);
                if(fields.Length!=header.Length)throw new InvalidDataException("A CSV row has a different number of columns.");
                if(!double.TryParse(fields[2],NumberStyles.Float,CultureInfo.InvariantCulture,out double seconds)
                    || double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<previous)
                    throw new InvalidDataException("Elapsed time is invalid or not ordered.");
                previous=seconds;
                var values=new double[result.Headings.Length];
                for(int i=0;i<values.Length;i++)values[i]=double.TryParse(fields[i+3],NumberStyles.Float,CultureInfo.InvariantCulture,out double value)?value:double.NaN;
                result.HasDemoData|=string.Equals(fields[0],"DEMO",StringComparison.OrdinalIgnoreCase);
                result.Seconds.Add(seconds);result.Samples.Add(values);
                if(result.Samples.Count>100000)throw new InvalidDataException("This viewer accepts at most 100,000 samples per file. Split larger logs first.");
            }
            if(result.Samples.Count==0)throw new InvalidDataException("This recording has no samples.");
            return result;
        }
        private static string[] Fields(string text)
        {
            var result=new List<string>();var field=new StringBuilder();bool quoted=false;
            for(int i=0;i<text.Length;i++)
            {
                char c=text[i];
                if(c=='"'){if(quoted&&i+1<text.Length&&text[i+1]=='"'){field.Append('"');i++;}else quoted=!quoted;}
                else if(c==','&&!quoted){result.Add(field.ToString().Trim());field.Clear();}
                else field.Append(c);
            }
            if(quoted)throw new InvalidDataException("A CSV field has an unclosed quote.");
            result.Add(field.ToString().Trim());return result.ToArray();
        }
    }
}

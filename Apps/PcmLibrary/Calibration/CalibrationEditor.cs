// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Newtonsoft.Json;

namespace PcmHacking
{
    public sealed class CalibrationDefinition
    {
        public int FormatVersion { get; set; } = 1;
        public string Name { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public uint Osid { get; set; }
        public string OsSha256 { get; set; } = string.Empty;
        public int ImageSize { get; set; }
        public bool DemoOnly { get; set; }
        public List<CalibrationField> Fields { get; set; } = new List<CalibrationField>();
        public static CalibrationDefinition Load(string json) => JsonConvert.DeserializeObject<CalibrationDefinition>(json)
            ?? throw new InvalidDataException("The definition is empty.");
    }

    public sealed class CalibrationField
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "Calibration";
        public string Description { get; set; } = string.Empty;
        public string Units { get; set; } = string.Empty;
        public int Address { get; set; }
        public int Rows { get; set; } = 1;
        public int Columns { get; set; } = 1;
        public string Storage { get; set; } = "u16be";
        public double Factor { get; set; } = 1;
        public double Offset { get; set; }
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public string[] RowLabels { get; set; } = Array.Empty<string>();
        public string[] ColumnLabels { get; set; } = Array.Empty<string>();
        public string RowAxisLabel { get; set; } = "Row";
        public string ColumnAxisLabel { get; set; } = "Column";
        [JsonIgnore] public int Width => Storage == "u8" || Storage == "s8" ? 1
            : Storage == "u16be" || Storage == "s16be" ? 2
            : Storage == "u32be" || Storage == "s32be" || Storage == "f32be" ? 4
            : throw new InvalidDataException("Unsupported storage type: " + Storage);
        public override string ToString() => Name;
    }

    public sealed class CalibrationChange
    {
        public DateTime TimeUtc { get; set; }
        public string Field { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>Edits a private copy. A supplied definition must match both the OSID and OS bytes.</summary>
    public sealed class CalibrationEditor
    {
        private readonly byte[] original;
        private byte[] working;
        private readonly Stack<byte[]> undo = new Stack<byte[]>();
        private readonly Stack<byte[]> redo = new Stack<byte[]>();
        private readonly ILogger logger;
        private readonly string definitionSnapshot;
        public CalibrationDefinition Definition { get; }
        public List<CalibrationChange> Changes { get; } = new List<CalibrationChange>();
        public bool HasChanges => !this.original.SequenceEqual(this.working);
        public string WorkingHash { get; private set; }
        public bool CanUndo => this.undo.Count > 0;
        public bool CanRedo => this.redo.Count > 0;

        public CalibrationEditor(byte[] image, CalibrationDefinition definition, ILogger logger, bool allowDemo = false)
        {
            this.logger = logger;
            // Freeze the caller's definition by copying it; reject later mutation of the exposed model.
            definition = CalibrationDefinition.Load(JsonConvert.SerializeObject(definition));
            Definition = definition;
            if (definition.FormatVersion != 1 || definition.ImageSize != image.Length || definition.Fields == null
                || definition.Fields.Count == 0 || definition.Fields.Count > 10000)
                throw new InvalidDataException("Definition version, image size, or table count is invalid.");
            if (definition.DemoOnly && !allowDemo) throw new InvalidDataException("Demo definitions cannot edit a vehicle tune.");
            var ranges = new List<FlashSegment>();
            if (!definition.DemoOnly)
            {
                var validator = new FileValidator(image,logger);
                if (!validator.IdentifyAndValidate() || validator.GetFileType() != PcmType.E38 || validator.GetOsidFromImage() != definition.Osid)
                    throw new InvalidDataException("This definition does not match a valid E38 image and OSID.");
                ranges = FileValidator.GetE38MasterSegments(image);
                FlashSegment os = ranges.First(s => s.BlockType == BlockType.OperatingSystem);
                string hash = Hash(image,os.Start,os.Length);
                if (!string.Equals(hash,definition.OsSha256,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("OS fingerprint mismatch. Editing remains locked.");
            }
            var occupied = new HashSet<int>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (CalibrationField field in definition.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Id) || !ids.Add(field.Id) || field.Rows < 1 || field.Columns < 1
                    || field.Rows > 128 || field.Columns > 128 || !Finite(field.Factor) || field.Factor == 0
                    || !Finite(field.Offset) || !Finite(field.Minimum) || !Finite(field.Maximum) || field.Minimum > field.Maximum)
                    throw new InvalidDataException("Invalid field or scaling: " + field.Name);
                if(field.RowLabels==null||field.ColumnLabels==null
                    ||(field.RowLabels.Length!=0&&field.RowLabels.Length!=field.Rows)
                    ||(field.ColumnLabels.Length!=0&&field.ColumnLabels.Length!=field.Columns))
                    throw new InvalidDataException("Axis label counts must match table dimensions: "+field.Name);
                long end = (long)field.Address + (long)field.Rows * field.Columns * field.Width;
                if (field.Address < 0 || end > image.Length)
                    throw new InvalidDataException("Field is outside the image: " + field.Name);
                if (!definition.DemoOnly && !ranges.Any(s => s.BlockType == BlockType.Calibration
                    && field.Address >= s.Start + 0x40 && end <= (long)s.End + 1))
                    throw new InvalidDataException("Fields must be inside calibration data, outside segment headers: " + field.Name);
                for (int address=field.Address; address<end; address++)
                    if (!occupied.Add(address)) throw new InvalidDataException("Overlapping fields are not supported.");
            }
            original = (byte[])image.Clone();
            working = (byte[])image.Clone();
            WorkingHash = Hash(working,0,working.Length);
            definitionSnapshot = JsonConvert.SerializeObject(Definition);
        }

        public double[] Read(CalibrationField field)
        {
            RequireField(field);
            return Enumerable.Range(0,field.Rows*field.Columns).Select(i => Decode(working,field,field.Address+i*field.Width)).ToArray();
        }
        public double[] ReadOriginal(CalibrationField field)
        {
            RequireField(field);
            return Enumerable.Range(0,field.Rows*field.Columns).Select(i=>Decode(original,field,field.Address+i*field.Width)).ToArray();
        }

        public void Edit(CalibrationField field,IReadOnlyList<double> values,string? description=null)
        {
            RequireField(field);
            if (values.Count != field.Rows*field.Columns) throw new ArgumentException("The table dimensions do not match.");
            byte[] candidate=(byte[])working.Clone();
            for(int i=0;i<values.Count;i++) Encode(candidate,field,field.Address+i*field.Width,values[i]);
            if(candidate.SequenceEqual(working)) return;
            undo.Push(working);
            if(undo.Count>32){var keep=undo.Take(32).Reverse().ToArray();undo.Clear();foreach(var snapshot in keep)undo.Push(snapshot);}
            working=candidate; redo.Clear();
            WorkingHash=Hash(working,0,working.Length);
            Changes.Add(new CalibrationChange {TimeUtc=DateTime.UtcNow,Field=field.Name,Summary=description??("Updated " + values.Count + " cell(s).")});
        }
        public void Undo(){if(undo.Count==0)return;redo.Push(working);working=undo.Pop();WorkingHash=Hash(working,0,working.Length);Journal("Undo");}
        public void Redo(){if(redo.Count==0)return;undo.Push(working);working=redo.Pop();WorkingHash=Hash(working,0,working.Length);Journal("Redo");}
        private void Journal(string action)=>Changes.Add(new CalibrationChange {TimeUtc=DateTime.UtcNow,Field=action,Summary=action+" table edit"});
        public byte[] Export()
        {
            RequireUnchangedDefinition();
            if(Definition.DemoOnly)throw new InvalidOperationException("Demo data cannot be exported or flashed as a vehicle tune.");
            byte[] result=(byte[])working.Clone();
            // Each calibration has its own CVN and word sum. Never change OS/boot/slave bytes.
            foreach(var segment in FileValidator.GetE38MasterSegments(result).Where(s=>s.BlockType==BlockType.Calibration))
            {
                ushort crc=Crc(result,0,segment.Start+2,segment.Start+0x1D);
                crc=Crc(result,crc,segment.Start+0x20,segment.End);
                result[segment.Start+0x1E]=(byte)crc;result[segment.Start+0x1F]=(byte)(crc>>8);
                uint sum=0;for(int i=segment.Start+2;i<=segment.End;i+=2)sum+=(uint)((result[i]<<8)|result[i+1]);
                ushort check=unchecked((ushort)(0u-sum));result[segment.Start]=(byte)(check>>8);result[segment.Start+1]=(byte)check;
            }
            var validator=new FileValidator(result,logger);
            if(!validator.IdentifyAndValidate())throw new InvalidDataException("Edited image failed checksum verification.");
            return result;
        }
        private void RequireUnchangedDefinition(){if(JsonConvert.SerializeObject(Definition)!=definitionSnapshot)throw new InvalidDataException("The definition changed after validation. Reload it before editing.");}
        private void RequireField(CalibrationField f){RequireUnchangedDefinition();if(!Definition.Fields.Contains(f))throw new ArgumentException("Field is not part of the loaded definition.");}
        public static string Hash(byte[] image,int start,int count)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(image,start,count)).Replace("-",string.Empty);}
        private static bool Finite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
        private static double Decode(byte[] image,CalibrationField f,int address)
        {
            uint raw=0;for(int i=0;i<f.Width;i++)raw=(raw<<8)|image[address+i];
            double value=raw;
            if(f.Storage=="s8")value=unchecked((sbyte)raw);
            else if(f.Storage=="s16be")value=unchecked((short)raw);
            else if(f.Storage=="s32be")value=unchecked((int)raw);
            else if(f.Storage=="f32be")value=BitConverter.ToSingle(BitConverter.GetBytes(raw),0);
            return value*f.Factor+f.Offset;
        }
        private static void Encode(byte[] image,CalibrationField f,int address,double value)
        {
            if(!Finite(value)||value<f.Minimum||value>f.Maximum)throw new ArgumentOutOfRangeException(nameof(value),f.Name+": value outside definition limits.");
            double raw=(value-f.Offset)/f.Factor;
            uint bits;
            if(f.Storage=="f32be")
            {float v=(float)raw;if(float.IsNaN(v)||float.IsInfinity(v))throw new ArgumentOutOfRangeException(nameof(value));bits=BitConverter.ToUInt32(BitConverter.GetBytes(v),0);}
            else
            {
                double min=f.Storage=="s8"?sbyte.MinValue:f.Storage=="s16be"?short.MinValue:f.Storage=="s32be"?int.MinValue:0;
                double max=f.Storage=="u8"?byte.MaxValue:f.Storage=="s8"?sbyte.MaxValue:f.Storage=="u16be"?ushort.MaxValue:f.Storage=="s16be"?short.MaxValue:f.Storage=="s32be"?int.MaxValue:uint.MaxValue;
                raw=Math.Round(raw,MidpointRounding.AwayFromZero);
                if(raw<min||raw>max)throw new ArgumentOutOfRangeException(nameof(value),"Value cannot be represented in the field storage.");
                bits=unchecked((uint)(long)raw);
            }
            for(int i=0;i<f.Width;i++)image[address+i]=(byte)(bits>>(8*(f.Width-1-i)));
        }
        private static ushort Crc(byte[] data,ushort initial,int start,int end)
        {int crc=initial;for(int i=start;i<=end;i++){crc^=data[i];for(int j=0;j<8;j++)crc=(crc&1)!=0?(crc>>1)^0xA001:crc>>1;}return(ushort)crc;}
    }

    public sealed class XdfItemInfo
    {
        public string Name {get;set;}=string.Empty;
        public string Kind {get;set;}=string.Empty;
        public string Description {get;set;}=string.Empty;
        public override string ToString()=>Kind+": "+Name;
    }
    /// <summary>Inspection only: XDF math, flags and third-party checksum plug-ins are not executed.</summary>
    public static class XdfInspector
    {
        public static List<XdfItemInfo> Read(TextReader input)
        {
            var settings=new System.Xml.XmlReaderSettings {DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=16000000};
            using(var xml=System.Xml.XmlReader.Create(input,settings))
            {
                var doc=XDocument.Load(xml);
                if(doc.Root?.Name.LocalName!="XDFFORMAT")throw new InvalidDataException("This is not an XML XDF definition.");
                return doc.Root.Elements().Where(e=>e.Name.LocalName=="XDFTABLE"||e.Name.LocalName=="XDFCONSTANT"||e.Name.LocalName=="XDFFLAG")
                    .Select(e=>new XdfItemInfo {Name=e.Element("title")?.Value??"Untitled",Kind=e.Name.LocalName,Description=e.Element("description")?.Value??string.Empty}).ToList();
            }
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
// 2026-10-18: Bounded, data-only XDF viewing/practice. Never produces a vehicle-editable definition.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PcmHacking
{
    public sealed class XdfPracticeImport
    {
        public CalibrationDefinition Definition { get; internal set; } = new CalibrationDefinition();
        public List<string> Notes { get; } = new List<string>();
    }

    public static class XdfPracticeReader
    {
        public static XdfPracticeImport Read(byte[] image,TextReader input,ILogger logger)
        {
            if(image==null||image.Length!=2097152)throw new InvalidDataException("Open a complete 2 MB E38 BIN.");
            var validator=new FileValidator(image,logger);
            if(!validator.IdentifyAndValidate()||validator.GetFileType()!=PcmType.E38)throw new InvalidDataException("The BIN is not a valid E38 image.");
            uint osid=validator.GetOsidFromImage();
            var settings=new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024};
            using(var reader=XmlReader.Create(input,settings))
            {
                var document=XDocument.Load(reader);var root=document.Root;
                if(root==null||root.Name!="XDFFORMAT")throw new InvalidDataException("Unsupported XDF document.");
                var header=root.Element("XDFHEADER")??throw new InvalidDataException("XDF header is missing.");
                var ids=Regex.Matches(Text(header,"deftitle"),@"\b[0-9]{8}\b").Cast<Match>().Select(m=>m.Value).Distinct().ToArray();
                if(ids.Length!=1||ids[0]!=osid.ToString(CultureInfo.InvariantCulture))throw new InvalidDataException($"XDF must identify exactly OS {osid}. A similar OS number does not match.");
                var baseOffset=header.Element("BASEOFFSET");
                if(baseOffset!=null&&(Number((string?)baseOffset.Attribute("offset")??"0")!=0||Number((string?)baseOffset.Attribute("subtract")??"0")!=0))throw new InvalidDataException("Nonzero XDF base offsets are not supported.");
                var defaults=header.Element("DEFAULTS");
                if((string?)defaults?.Attribute("lsbfirst")=="1")throw new InvalidDataException("Little-endian XDF defaults are not supported.");
                var entries=root.Elements().Where(e=>e.Name=="XDFTABLE"||e.Name=="XDFCONSTANT"||e.Name=="XDFFLAG").OrderBy(e=>e.Name=="XDFTABLE"?0:1).ToArray();
                if(entries.Length>2000)throw new InvalidDataException("Too many XDF entries.");
                var result=new XdfPracticeImport {Definition=new CalibrationDefinition {Name="XDF practice — unverified",Osid=osid,DemoOnly=true,ImageSize=image.Length}};
                var segments=FileValidator.GetE38MasterSegments(image).Where(s=>s.BlockType==BlockType.Calibration).ToArray();
                var occupied=new HashSet<int>();
                foreach(var entry in entries)
                {
                    string name=Text(entry,"title");if(name.Length>180)name=name.Substring(0,180);
                    try
                    {
                        if(entry.Name=="XDFFLAG")throw new InvalidDataException("bit flags are unsupported");
                        var field=ParseField(entry,defaults,name,result.Definition.Fields.Count);
                        var membership=entry.Elements("CATEGORYMEM").FirstOrDefault();
                        if(membership?.Attribute("category")!=null)
                        {
                            int category=Number((string)membership.Attribute("category")!)-1;
                            var definition=header.Elements("CATEGORY").FirstOrDefault(c=>Number((string?)c.Attribute("index")??"-1")==category);
                            field.Category=(string?)definition?.Attribute("name")??field.Category;
                        }
                        long end=(long)field.Address+(long)field.Rows*field.Columns*field.Width;
                        if(field.Address<0||end>image.Length||!segments.Any(s=>field.Address>=s.Start+0x40&&end<=s.Start+s.Length))throw new InvalidDataException("outside calibration payload");
                        if(field.Storage=="f32be")
                            for(int address=field.Address;address<end;address+=4)
                            {
                                var raw=new byte[4];Array.Copy(image,address,raw,0,4);if(BitConverter.IsLittleEndian)Array.Reverse(raw);
                                float decoded=BitConverter.ToSingle(raw,0);
                                if(float.IsNaN(decoded)||float.IsInfinity(decoded))throw new InvalidDataException("non-finite float data");
                            }
                        for(int i=field.Address;i<end;i++)if(occupied.Contains(i))throw new InvalidDataException("overlaps a previously imported item");
                        // Supported storage, finite data and scaling endpoints were validated above.
                        for(int i=field.Address;i<end;i++)occupied.Add(i);
                        if(field.Storage=="f32be")result.Notes.Add(name+": explicit float32 storage decoded for practice; meaning and physical axes remain unverified.");
                        result.Definition.Fields.Add(field);
                    }
                    catch(Exception error)when(error is InvalidDataException||error is FormatException||error is OverflowException||error is ArgumentException)
                    {result.Notes.Add((name.Length==0?"Unnamed entry":name)+": skipped — "+error.Message);}
                }
                if(result.Definition.Fields.Count==0)throw new InvalidDataException("No supported calibration tables were found. "+string.Join("; ",result.Notes.Take(3)));
                if(root.Element("XDFCHECKSUM")!=null)result.Notes.Add("Checksum plug-in references were not executed. Practice BIN export and vehicle Apply are disabled.");
                result.Notes.Add("Names, axes and equations come from the XDF and remain unverified. Numeric limits describe encoding, not safe engine settings.");
                return result;
            }
        }

        private static CalibrationField ParseField(XElement entry,XElement? defaults,string name,int id)
        {
            bool table=entry.Name=="XDFTABLE";
            var z=table?entry.Elements("XDFAXIS").SingleOrDefault(e=>(string?)e.Attribute("id")=="z"):entry;
            if(z==null)throw new InvalidDataException("value axis is missing");
            var data=z.Element("EMBEDDEDDATA")??throw new InvalidDataException("storage is missing");
            foreach(string stride in new[]{"mmedmajorstridebits","mmedminorstridebits"})
                if(Number((string?)data.Attribute(stride)??"0")!=0)throw new InvalidDataException("non-contiguous storage is unsupported");
            int rows=table?DecimalAttribute(data,"mmedrowcount",1):1,cols=table?DecimalAttribute(data,"mmedcolcount",1):1;
            if(rows<1||rows>128||cols<1||cols>128)throw new InvalidDataException("dimensions must be between 1 and 128");
            int bits=DecimalAttribute(data,"mmedelementsizebits",0);
            int flags=Number((string?)data.Attribute("mmedtypeflags")??"0");
            if((flags&~0x10007)!=0||(flags&2)!=0)throw new InvalidDataException("unsupported storage flags or little-endian values");
            if(rows>1&&cols>1&&(flags&4)==0)throw new InvalidDataException("only explicit row-major 2D storage is supported");
            bool signed=data.Attribute("mmedtypeflags")!=null?(flags&1)!=0:(string?)defaults?.Attribute("signed")=="1";
            string customFloat=(string?)data.Attribute("claudetool_float")??"0";
            string customScalar=Text(entry,"claudetool_isfloat");
            if(customFloat!="0"&&customFloat!="1")throw new InvalidDataException("unknown float extension value");
            if(customScalar.Length!=0&&customScalar!="0"&&customScalar!="1")throw new InvalidDataException("unknown scalar float extension value");
            bool floating=(flags&0x10000)!=0||(string?)defaults?.Attribute("float")=="1"||customFloat=="1"||customScalar=="1";
            if(floating&&(bits!=32||signed))throw new InvalidDataException("float32 requires 32-bit storage without integer signed flags");
            string storage=floating?"f32be":bits==8?(signed?"s8":"u8"):bits==16?(signed?"s16be":"u16be"):bits==32?(signed?"s32be":"u32be"):throw new InvalidDataException("unsupported element width");
            var math=z.Elements("MATH").ToArray();
            if(math.Length!=1||math[0].Elements("VAR").Any(v=>v.Elements().Any()||(string?)v.Attribute("id")!="X"))throw new InvalidDataException("linked or multiple equations are unsupported");
            string equation=((string?)math[0].Attribute("equation")??"").Replace(" ","");
            var match=Regex.Match(equation,@"^[Xx](?:([*/])([+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?))?(?:([+-](?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?))?$");
            if(!match.Success)throw new InvalidDataException("only X, X*factor, X/divisor and a numeric offset are supported");
            double factor=1,offset=0;
            if(match.Groups[1].Success){double n=double.Parse(match.Groups[2].Value,CultureInfo.InvariantCulture);if(n==0)throw new InvalidDataException("zero scaling is unsupported");factor=match.Groups[1].Value=="/"?1/n:n;}
            if(match.Groups[3].Success)offset=double.Parse(match.Groups[3].Value,CultureInfo.InvariantCulture);
            if(double.IsNaN(factor)||double.IsInfinity(factor)||factor==0||double.IsInfinity(offset)||double.IsNaN(offset))throw new InvalidDataException("invalid scaling");
            double rawMin=floating?float.MinValue:signed?-Math.Pow(2,bits-1):0,rawMax=floating?float.MaxValue:signed?Math.Pow(2,bits-1)-1:Math.Pow(2,bits)-1;
            double a=rawMin*factor+offset,b=rawMax*factor+offset;
            if(double.IsInfinity(a)||double.IsInfinity(b))throw new InvalidDataException("scaling exceeds numeric bounds");
            var x=entry.Elements("XDFAXIS").SingleOrDefault(e=>(string?)e.Attribute("id")=="x");
            var y=entry.Elements("XDFAXIS").SingleOrDefault(e=>(string?)e.Attribute("id")=="y");
            string description=Text(entry,"description");if(description.Length>1600)description=description.Substring(0,1600);
            return new CalibrationField {Id="xdf-"+id,Name=string.IsNullOrWhiteSpace(name)?"Unnamed XDF item":name,Category="XDF practice",Address=HexAddress((string?)data.Attribute("mmedaddress")??""),Rows=rows,Columns=cols,Storage=storage,Factor=factor,Offset=offset,Minimum=Math.Min(a,b),Maximum=Math.Max(a,b),
                Units=string.IsNullOrWhiteSpace(Text(z,"units"))?"XDF values":Short(Text(z,"units")),RowLabels=Labels(y,rows),ColumnLabels=Labels(x,cols),RowAxisLabel=AxisName(y,"Row label"),ColumnAxisLabel=AxisName(x,"Column label"),
                Description="REFERENCE PRACTICE: edits cannot be applied or exported as a BIN. XDF meanings and axes are unverified. "+description};
        }
        private static string[] Labels(XElement? axis,int count)
        {
            if(axis==null||!axis.Elements("LABEL").Any())return Array.Empty<string>();
            var labels=axis.Elements("LABEL").ToArray();
            if(labels.Length!=count)throw new InvalidDataException("axis label count does not match dimensions");
            var result=new string[count];
            foreach(var label in labels){int index=int.Parse((string?)label.Attribute("index")??"-1",CultureInfo.InvariantCulture);string value=(string?)label.Attribute("value")??"";if(index<0||index>=count||result[index]!=null||value.Length>80)throw new InvalidDataException("invalid axis labels");result[index]=value;}
            return result;
        }
        private static string AxisName(XElement? axis,string fallback)=>string.IsNullOrWhiteSpace(Text(axis,"units"))?fallback:Short(Text(axis,"units"));
        private static string Short(string value)=>value.Length>128?value.Substring(0,128):value;
        private static string Text(XElement? item,string name)=>item?.Element(name)?.Value??"";
        private static int DecimalAttribute(XElement e,string name,int fallback)=>e.Attribute(name)==null?fallback:int.Parse((string)e.Attribute(name)!,CultureInfo.InvariantCulture);
        private static int Number(string value)=>value.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?Convert.ToInt32(value.Substring(2),16):int.Parse(value,CultureInfo.InvariantCulture);
        private static int HexAddress(string value)=>Convert.ToInt32(value.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?value.Substring(2):value,16);
    }
}

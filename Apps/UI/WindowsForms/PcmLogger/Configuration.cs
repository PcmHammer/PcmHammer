using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking
{
    public class Configuration
    {
        public static PcmLogger.Properties.Settings Settings = PcmLogger.Properties.Settings.Default;
    }

    // Shamelessly copied from Stackoverflow because it's such a great idea.
    // Posted by https://stackoverflow.com/users/350188/stephen-turner 
    // Posted at https://stackoverflow.com/questions/922047/store-dictionarystring-string-in-application-settings
    public class SerializableStringDictionary : System.Collections.Specialized.StringDictionary, System.Xml.Serialization.IXmlSerializable
    {
        public System.Xml.Schema.XmlSchema GetSchema()
        {
            return null;
        }

        public void ReadXml(System.Xml.XmlReader reader)
        {
            this.Clear();
            while (reader.Read() &&
                !(reader.NodeType == System.Xml.XmlNodeType.EndElement && reader.LocalName == this.GetType().Name))
            {
                var name = reader["Name"];
                if (name == null)
                    throw new FormatException();

                var value = reader["Value"];
                this[name] = value;
            }
        }

        public void WriteXml(System.Xml.XmlWriter writer)
        {
            foreach (System.Collections.DictionaryEntry entry in this)
            {
                writer.WriteStartElement("Pair");
                writer.WriteAttributeString("Name", (string)entry.Key);
                writer.WriteAttributeString("Value", (string)entry.Value);
                writer.WriteEndElement();
            }
        }
    }
}

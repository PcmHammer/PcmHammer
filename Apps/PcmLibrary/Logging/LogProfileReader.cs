﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace PcmHacking
{
    /// <summary>
    /// Reads a LogProfile from an XML file.
    /// </summary>
    public class LogProfileReader
    {
        private readonly ParameterDatabase database;
        private readonly uint osid;
        private readonly ILogger logger;

        private LogProfile profile;

        public LogProfileReader(ParameterDatabase database, uint osid, ILogger logger)
        {
            this.database = database;
            this.osid = osid;            
            this.logger = logger;

            this.profile = new LogProfile();
        }

        public LogProfile Read(string path)
        {
            try
            {
                XDocument xml = XDocument.Load(path);

                this.LoadParameters<PidParameter>(xml);
                this.LoadParameters<RamParameter>(xml);
                this.LoadParameters<MathParameter>(xml);
            }
            catch(Exception exception)
            {
                this.logger.AddUserMessage("Unable to load profile " + Path.GetFileName(path));
                this.logger.AddDebugMessage(exception.ToString());
                this.profile = new LogProfile();
            }

            return profile;
        }

        private void LoadParameters<T>(XDocument xml) where T : Parameter
        {
            string parameterType = typeof(T).Name;

            XElement container = xml.Root.Elements(string.Format("{0}s", parameterType)).FirstOrDefault();

            if (container != null)
            {
                foreach (XElement parameterElement in container.Elements(parameterType))
                {
                    string id = parameterElement.Attribute("id").Value;
                    string units = parameterElement.Attribute("units").Value;
                    string zoomAttributeValue = parameterElement.Attribute("zoom")?.Value;
                    bool zoom = string.Equals(zoomAttributeValue, "true", StringComparison.OrdinalIgnoreCase);
                    this.AddParameterToProfile<T>(id, units, zoom);
                }
            }
        }

        private void AddParameterToProfile<T>(string id, string units, bool zoom) where T : Parameter
        {
            T parameter;
            if (!this.database.TryGetParameter<T>(id, out parameter))
            {
                this.logger.AddUserMessage($"Parameter {id} is not supported by this version of PCM Hammer.");
                return;
            }

            if (!parameter.IsSupported(this.osid))
            {
                this.logger.AddUserMessage($"Parameter {id} is not supported by this operating system.");
                return;
            }

            Conversion conversion = parameter.GetConversion(units);

            if (conversion == null)
            {
                throw new Exception(String.Format("Conversion {0} for parameter {1} not found when loading profile.", units, id));
            }

            LogColumn column = new LogColumn(parameter, conversion, zoom);

            this.profile.AddColumn(column);
        }
    }
}

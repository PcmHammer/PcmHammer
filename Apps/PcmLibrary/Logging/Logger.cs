// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

// I'm a little surprised that this workaround is still needed.
// https://stackoverflow.com/questions/62648189/testing-c-sharp-9-0-in-vs2019-cs0518-isexternalinit-is-not-defined-or-imported
namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal class IsExternalInit { }
}

namespace PcmHacking
{
    public record LogRowElement(string ParameterId, string ParameterName, string Units, string ValueAsString, double ValueAsNumber);

    /// <summary>
    /// Thrown when the PCM does not support a requested parameter.
    /// </summary>
    public class ParameterNotSupportedException : Exception
    {
        public ParameterNotSupportedException(string message) : base(message)
        { }

        public ParameterNotSupportedException(Parameter parameter) : base(
            $"Parameter \"{parameter.Name}\" is not supported by this PCM.")
        { }
    }

    /// <summary>
    /// Thrown when the interface device requires more data in order to log reliably.
    /// </summary>
    /// <remarks>
    /// ELM devices are unreliable when only one DPID is configured.
    /// </remarks>
    public class NeedMoreParametersException : Exception
    {
        public NeedMoreParametersException(string message) : base(message)
        { }
    }

    /// <summary>
    /// Requests log data from the Vehicle.
    /// </summary>
    /// <remarks>
    /// There are two derived classes:
    /// 
    /// SlowLogger - sends one request for each row of log data. 
    /// Works, but it's slow.
    /// 
    /// FastLogger - sends one request, receives many rows of log data.
    /// Preferable, but doesn't work for all devices.
    /// </remarks>
    public abstract class Logger
    {
        private readonly Vehicle vehicle;
        private readonly uint osid;
        private readonly DpidConfiguration dpidConfiguration;
        private readonly MathValueProcessor mathValueProcessor;
        private DpidCollection dpids;
        private CanLogger canLogger;
        private ILogger uiLogger;

        public DpidConfiguration DpidConfiguration {  get { return this.dpidConfiguration; } }

        public MathValueProcessor MathValueProcessor {  get { return this.mathValueProcessor; } }

        public CanLogger CanLogger { get { return this.canLogger; } }

        protected Vehicle Vehicle { get { return this.vehicle; } }

        protected DpidCollection Dpids {  get { return this.dpids; } }

        protected ILogger UILogger { get { return this.uiLogger; } }

        /// <summary>
        /// Constructor.
        /// </summary>
        protected Logger(
            Vehicle vehicle, 
            CanLogger canLogger,
            uint osid, 
            DpidConfiguration dpidConfiguration, 
            MathValueProcessor mathValueProcessor, 
            ILogger uiLogger)
        {
            this.vehicle = vehicle;
            this.osid = osid;
            this.dpidConfiguration = dpidConfiguration;
            this.mathValueProcessor = mathValueProcessor;
            this.uiLogger = uiLogger;
            this.canLogger = canLogger;
        }

        /// <summary>
        /// The factory method converts the list of columns to a DPID configuration and math-value processor.
        /// </summary>
        public static Logger Create(
            Vehicle vehicle, 
            uint osid, 
            IEnumerable<LogColumn> columns, 
            bool deviceSupportsSingleDpid,
            bool deviceSupportsStreaming,
            CanLogger canLogger,
            ILogger uiLogger)
        {
            DpidConfiguration dpidConfiguration = new DpidConfiguration();

            List<LogColumn> singleByteColumns = new List<LogColumn>();
            List<LogColumn> mathColumns = new List<LogColumn>();
            List<LogColumn> pcmColumns = new List<LogColumn>();
            List<MathColumnAndDependencies> dependencies = new List<MathColumnAndDependencies>();

            // Separate PCM columns from Math columns
            foreach (LogColumn column in columns)
            {
                PcmParameter pcmParameter = column.Parameter as PcmParameter;
                if (pcmParameter != null)
                {
                    pcmColumns.Add(column);
                    continue;
                }

                MathParameter mathParameter = column.Parameter as MathParameter;
                if (mathParameter != null)
                {
                    mathColumns.Add(column);
                    continue;
                }                
            }

            // Ensure all math columns have their dependencies
            foreach (LogColumn column in mathColumns)
            {
                MathParameter mathParameter = (MathParameter)column.Parameter;

                LogColumn xColumn = pcmColumns.Where(x => x.Parameter == mathParameter.XColumn.Parameter).FirstOrDefault();

                if (xColumn == null)
                {
                    xColumn = new LogColumn(mathParameter.XColumn.Parameter, mathParameter.XColumn.Conversion, false);
                    pcmColumns.Add(xColumn);
                }

                LogColumn yColumn = pcmColumns.Where(y => y.Parameter == mathParameter.YColumn.Parameter).FirstOrDefault();
                if (yColumn == null)
                {
                    yColumn = new LogColumn(mathParameter.YColumn.Parameter, mathParameter.YColumn.Conversion, false);
                    pcmColumns.Add(yColumn);
                }

                MathColumnAndDependencies map = new MathColumnAndDependencies(column, xColumn, yColumn);
                dependencies.Add(map);
            }

            // Populate DPIDs with two-byte values
            byte groupId = 0xFE;
            ParameterGroup group = new ParameterGroup(groupId);
            foreach (LogColumn column in pcmColumns)
            {
                PcmParameter pcmParameter = column.Parameter as PcmParameter;
                if (pcmParameter == null)
                {
                    continue;
                }

                if (pcmParameter.ByteCount == 1)
                {
                    singleByteColumns.Add(column);
                    continue;
                }

                if (!group.TryAddLogColumn(column))
                {
                    throw new ParameterNotSupportedException(
                        $"Parameter \"{column.Parameter.Name}\" is not supported by this PCM.");
                }

                if (group.TotalBytes == ParameterGroup.MaxBytes)
                {
                    dpidConfiguration.ParameterGroups.Add(group);
                    groupId--;

                    if (groupId < 0xF9)
                    {
                        throw new ParameterNotSupportedException(
                            $"The PCM cannot send this much data.{System.Environment.NewLine}Please un-select some parameters.");
                    }

                    group = new ParameterGroup(groupId);
                }
            }

            // Add the remaining one-byte values
            foreach (LogColumn column in singleByteColumns)
            {
                if (!group.TryAddLogColumn(column))
                {
                    throw new ParameterNotSupportedException(
                        $"Parameter \"{column.Parameter.Name}\" is not supported by this PCM.");
                }

                if (group.TotalBytes == ParameterGroup.MaxBytes)
                {
                    dpidConfiguration.ParameterGroups.Add(group);
                    groupId--;

                    if (groupId < 0xFB)
                    {
                        throw new ParameterNotSupportedException(
                            $"The PCM cannot send this much data.{System.Environment.NewLine}Please un-select some parameters.");
                    }

                    group = new ParameterGroup(groupId);
                }
            }

            // Add the last DPID group
            if (group.LogColumns.Count > 0)
            {
                dpidConfiguration.ParameterGroups.Add(group);
                group = null;
            }

            if (!deviceSupportsSingleDpid && dpidConfiguration.ParameterGroups.Count == 1)
            {
                throw new NeedMoreParametersException("Add more parameters to begin logging.");
            }

            // In theory we could also create a "mixed logger" that gets the 
            // FE, FD, FC DPIDs at 10hz and the FB & FA DPIDs at 5hz.
            //
            // This would require the user to specify, or the app to just know,
            // which parameters to poll at 5hz rather than 10hz. A list of
            // 5hz-friendly parameters is not out of the question. Some day.
            if (deviceSupportsStreaming)
            {
                return new FastLogger(
                    vehicle,
                    canLogger,
                    osid,
                    dpidConfiguration,
                    new MathValueProcessor(
                        dpidConfiguration,
                        dependencies),
                    uiLogger);
            }
            else
            {
                return new SlowLogger(
                    vehicle,
                    canLogger,
                    osid,
                    dpidConfiguration,
                    new MathValueProcessor(
                        dpidConfiguration,
                        dependencies),
                    uiLogger);
            }
        }

        public IEnumerable<string> GetColumnNames()
        {
            IEnumerable<string> columns = this.dpidConfiguration.GetParameterNames();
            columns = columns.Concat(this.mathValueProcessor.GetHeaderNames());
            columns = columns.Concat(this.canLogger.GetParameterNames());
            return columns;
        }

        /// <summary>
        /// Invoke this once to begin a logging session.
        /// </summary>
        public async Task<bool> StartLogging()
        {
            try
            {
                this.dpids = await this.vehicle.ConfigureDpids(this.dpidConfiguration, this.osid);

                if (this.dpids == null)
                {
                    return false;
                }

                // This part differs for the fast and slow loggers.
                await this.StartLoggingInternal();
                return true;
            }
            catch (Exception exception)
            {
                this.uiLogger.AddUserMessage("Unable to start logging: " + exception.Message);
                this.uiLogger.AddDebugMessage(exception.ToString());
                return false;
            }            
        }

        protected abstract Task<bool> StartLoggingInternal();

        /// <summary>
        /// Invoke this repeatedly to get each row of data from the PCM.
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<string>> GetNextRow()
        {
            LogRowParser row = new LogRowParser(this.dpidConfiguration);

            // This part differs for the fast and slow loggers.
            await this.GetNextRowInternal(row);

            if (row.IsComplete)
            {
                PcmParameterValues dpidValues = row.Evaluate();

                IEnumerable<string> mathValues = this.mathValueProcessor.GetMathValues(dpidValues);
                IEnumerable<string> canValues = this.canLogger.GetParameterValues().Select(x => x.ValueAsString);

                return dpidValues
                        .Select(x => x.Value.ValueAsString)
                        .Concat(mathValues)
                        .Concat(canValues)
                        .ToArray();
            }
            else
            {
                return null;
            }
        }

        public async Task<IEnumerable<LogRowElement>> GetNextRowV2()
        {
            LogRowParser row = new LogRowParser(this.dpidConfiguration);

            // This part differs for the fast and slow loggers.
            await this.GetNextRowInternal(row);

            if (row.IsComplete)
            {
                PcmParameterValues dpidValues = row.Evaluate();
                IEnumerable<LogRowElement> pcmValues = dpidValues.Select(
                    x => new LogRowElement(
                        x.Key.Parameter.Id,
                        x.Key.Parameter.Name,
                        x.Key.Conversion.Units,
                        x.Value.ValueAsString,
                        x.Value.ValueAsDouble));

                IEnumerable<LogRowElement> mathValues = this.mathValueProcessor.GetMathValuesV2(dpidValues);
                IEnumerable<LogRowElement> canValues = this.canLogger.GetParameterValuesV2();

                return pcmValues
                    .Concat(mathValues)
                    .Concat(canValues)
                    .ToArray();
            }
            else
            {
                return new LogRowElement[0];
            }
        }

        protected abstract Task GetNextRowInternal(LogRowParser row);
    }
}

﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.CompilerServices;
using System.Data;
using System.Data.Common;

namespace PcmHacking
{
    public class ZoomedParameter
    {
        public LogColumn LogColumn { get; private set; }
        public string Value { get; private set; }
        public string Name { get; private set; }
        public string Units { get; private set; }

        public ZoomedParameter(LogColumn logColumn, string value, string name, string units)
        {
            LogColumn = logColumn;
            Value = value;
            Name = name;
            Units = units;
        }
    }

    partial class MainForm
    {
        private ConcurrentQueue<Tuple<Logger, LogFileWriter, IEnumerable<string>>> logRowQueue = new ConcurrentQueue<Tuple<Logger, LogFileWriter, IEnumerable<string>>>();

        private AutoResetEvent endWriterThread = new AutoResetEvent(false);
        private AutoResetEvent rowAvailable = new AutoResetEvent(false);

        private static DateTime lastLogTime;

        private ManualResetEvent loggerThreadEnded = new ManualResetEvent(false);
        private ManualResetEvent writerThreadEnded = new ManualResetEvent(false);
        
        enum LogState
        {
            Nothing,
            DisplayOnly,
            StartSaving,
            Saving,
            StopSaving
        }

        private LogState logState = LogState.Nothing;

        CanLogger canLogger;

        /// <summary>
        /// Create a string that will look reasonable in the UI's main text box, and
        /// build a list of parameters to show in a larger format.
        /// 
        /// TODO: Instead of a string for a text box, make a list of objects, and render
        /// them as a grid.
        /// </summary>
        private Tuple<string, List<ZoomedParameter>> FormatValuesForDisplay(Logger logger, IEnumerable<string> rowValues)
        {
            List<ZoomedParameter> zoomedParameters = new List<ZoomedParameter>();

            StringBuilder builder = new StringBuilder();
            IEnumerator<string> rowValueEnumerator = rowValues.GetEnumerator();
            foreach (ParameterGroup group in logger.DpidConfiguration.ParameterGroups)
            {
                foreach (LogColumn column in group.LogColumns)
                {
                    rowValueEnumerator.MoveNext();
                    builder.Append(rowValueEnumerator.Current);
                    builder.Append('\t');
                    builder.Append(column.Conversion.Units);
                    builder.Append('\t');
                    builder.AppendLine(column.Parameter.Name);

                    if (column.Zoom)
                    {
                        zoomedParameters.Add(
                            new ZoomedParameter(
                                column,
                                rowValueEnumerator.Current,
                                column.Parameter.Name,
                                column.Conversion.Units));
                    }
                }
            }

            foreach (LogColumn mathColumn in logger.MathValueProcessor.GetMathColumns())
            {
                rowValueEnumerator.MoveNext();
                builder.Append(rowValueEnumerator.Current);
                builder.Append('\t');
                builder.Append(mathColumn.Conversion.Units);
                builder.Append('\t');
                builder.AppendLine(mathColumn.Parameter.Name);

                if (mathColumn.Zoom)
                {
                    zoomedParameters.Add(
                    new ZoomedParameter(
                            mathColumn,
                            rowValueEnumerator.Current,
                            mathColumn.Parameter.Name,
                            mathColumn.Conversion.Units));
                }
            }



            foreach (CanLogger.ParameterValue pv in logger.CanLogger.GetParameterValues())
            {
                builder.Append(pv.Value);
                builder.Append('\t');
                builder.Append(pv.Units);
                builder.Append('\t');
                builder.AppendLine(pv.Name);


                /* TODO: How to make this work for CAN?
                 * 
                 * if (column.Zoom)
                {
                    zoomedParameters.Add(
                        new ZoomedParameter(
                            column,
                            rowValueEnumerator.Current,
                            column.Parameter.Name,
                            column.Conversion.Units));
                }*/
            }

            DateTime now = DateTime.Now;
            builder.AppendLine((now - lastLogTime).TotalMilliseconds.ToString("0.00") + "\tms\tQuery time");
            lastLogTime = now;

            return new Tuple<string, List<ZoomedParameter>>(builder.ToString(), zoomedParameters);
        }

        private async Task<Logger> RecreateLogger(ParameterDatabase parameterDatabase)
        {
            this.canLogger?.Dispose();
            this.canLogger = new CanLogger(parameterDatabase);

            if (string.IsNullOrEmpty(this.canPortName))
            {
                await this.canLogger.SetPort(null);
            }
            else
            {
                await this.canLogger.SetPort(new StandardPort(canPortName));

            }

            Logger logger = this.Vehicle.CreateLogger(this.osid, canLogger, this.currentProfile.Columns, this);

            if (!await logger.StartLogging())
            {
                throw new LogStartFailedException();
            }

            return logger;
        }

        private async Task<Tuple<LogFileWriter,StreamWriter>> StartSaving(Logger logger)
        {
            string logFilePath = GenerateLogFilePath();
            StreamWriter streamWriter = new StreamWriter(logFilePath);
            LogFileWriter logFileWriter = new LogFileWriter(streamWriter);

            IEnumerable<string> columnNames = logger.GetColumnNames();
            await logFileWriter.WriteHeader(columnNames);

            return new Tuple<LogFileWriter, StreamWriter>(logFileWriter, streamWriter);
        }

        private void StopSaving(ref StreamWriter streamWriter)
        {
            if (streamWriter != null)
            {
                streamWriter.Dispose();
                streamWriter = null;
            }
        }

        private async Task ProcessRow(Logger logger, LogFileWriter logFileWriter)
        {
            IEnumerable<string> rowValues = await logger.GetNextRow();
            if (rowValues != null)
            {
                this.loggerProgress.Invoke(
                    (MethodInvoker)
                    delegate ()
                    {
                        this.AddDebugMessage("Row received.");
                    });

                // Hand this data off to be written to disk and displayed in the UI.
                this.logRowQueue.Enqueue(
                    new Tuple<Logger, LogFileWriter, IEnumerable<string>>(
                        logger,
                        logFileWriter,
                        rowValues));

                this.rowAvailable.Set();
            }
        }

        /// <summary>
        /// The loop that reads data from the PCM.
        /// </summary>
        private async void LoggingThread(object threadContext)
        {
            using (AwayMode lockScreenSuppressor = new AwayMode())
            {
                try
                {
                    // Start the write/display thread.
                    ThreadPool.QueueUserWorkItem(LogFileWriterThread, null);

#if Vpw4x
                    if (!await this.Vehicle.VehicleSetVPW4x(VpwSpeed.FourX))
                    {
                        this.AddUserMessage("Unable to switch to 4x.");
                        return;
                    }
#endif

                    StreamWriter streamWriter = null;
                    try
                    {
                        LogProfile lastProfile = null;
                        Logger logger = null;
                        LogFileWriter logFileWriter = null;

                        while (!this.logStopRequested)
                        {
                            // Re-create the logger with an updated profile if necessary.
                            if (this.currentProfile != lastProfile)
                            {
                                this.StopSaving(ref streamWriter);

                                if ((this.currentProfile == null) || this.currentProfile.IsEmpty)
                                {
                                    this.logState = LogState.Nothing;
                                    lastProfile = this.currentProfile;
                                    logger = null;

                                    this.loggerProgress.Invoke(
                                        (MethodInvoker)
                                        delegate ()
                                        {
                                            this.startStopSaving.Enabled = false;
                                            this.logValues.Text = "Please select some parameters, or open a log profile.";
                                        });
                                }
                                else
                                {
                                    Exception exception = null;

                                    try
                                    {
                                        // It may be counterintuitive that we update lastProfile here, but that 
                                        // prevents the invalid parameter exception from being thrown repeatedly.
                                        lastProfile = this.currentProfile;
                                        logger = await this.RecreateLogger(this.database);

                                        // If this was the first profile to load...
                                        if (this.logState == LogState.Nothing)
                                        {
                                            this.logState = LogState.DisplayOnly;
                                        }

                                        switch (logState)
                                        {
                                            case LogState.Nothing:
                                            case LogState.DisplayOnly:
                                            case LogState.StopSaving:
                                                break;

                                            default:
                                                var tuple = await this.StartSaving(logger);
                                                logFileWriter = tuple.Item1;
                                                streamWriter = tuple.Item2;
                                                logState = LogState.Saving;
                                                break;
                                        }
                                    }
                                    catch (NeedMoreParametersException ex)
                                    {
                                        exception = ex;
                                    }
                                    catch (ParameterNotSupportedException ex)
                                    {
                                        exception = ex;
                                    }
                                    catch (LogStartFailedException ex)
                                    {
                                        // This will cause the logger to be recreated on the next iteration.
                                        lastProfile = null;

                                        exception = ex;
                                    }
                                    finally
                                    {
                                        if (exception != null)
                                        {
                                            logState = LogState.Nothing;

                                            this.loggerProgress.Invoke(
                                                (MethodInvoker)
                                                delegate ()
                                                {
                                                    this.AddUserMessage(exception.Message);
                                                    this.startStopSaving.Enabled = false;
                                                    this.logValues.Text = exception.Message;
                                                });
                                        }
                                    }
                                }
                            }

                            switch (logState)
                            {
                                case LogState.Nothing:
                                    Thread.Sleep(100);
                                    break;

                                case LogState.DisplayOnly:
                                    if (logger != null)
                                    {
                                        await this.ProcessRow(logger, null);
                                    }
                                    break;

                                case LogState.StartSaving:
                                    if (logger != null)
                                    {
                                        var tuple = await this.StartSaving(logger);
                                        logFileWriter = tuple.Item1;
                                        streamWriter = tuple.Item2;
                                        logState = LogState.Saving;
                                    }
                                    break;

                                case LogState.Saving:
                                    if (logger != null)
                                    {
                                        await this.ProcessRow(logger, logFileWriter);
                                    }
                                    break;

                                case LogState.StopSaving:
                                    this.StopSaving(ref streamWriter);
                                    this.logState = LogState.DisplayOnly;
                                    break;
                            }
                        }

                        this.logStopRequested = false;
                    }
                    finally
                    {
                        if (streamWriter != null)
                        {
                            streamWriter.Dispose();
                            streamWriter = null;
                        }

                        endWriterThread.Set();
                    }
                }
                catch (Exception exception)
                {
                    if (!logStopRequested)
                    {
                        this.AddUserMessage("Logging halted. " + exception.Message);
                        this.AddDebugMessage(exception.ToString());
                        this.logValues.Invoke(
                            (MethodInvoker)
                            delegate ()
                            {
                                this.logValues.Text = "Logging halted. " + exception.Message;
                                this.startStopSaving.Focus();
                            });
                    }
                }
                finally
                {
                    this.loggerThreadEnded.Set();
#if Vpw4x
                    if (!await this.Vehicle.VehicleSetVPW4x(VpwSpeed.Standard))
                    {
                        // Try twice...
                        await this.Vehicle.VehicleSetVPW4x(VpwSpeed.Standard);
                    }
#endif
                }
            }
        }

        /// <summary>
        /// Background thread to write to disk and send updates to the UI.
        /// This minimizes the amount code that executes between requests for new rows of log data.
        /// </summary>
        private void LogFileWriterThread(object threadContext)
        {
            WaitHandle[] writerHandles = new WaitHandle[] { endWriterThread, rowAvailable };

            try
            {
                while (!logStopRequested)
                {
                    int index = WaitHandle.WaitAny(writerHandles);
                    if (index == 0)
                    {
                        this.BeginInvoke((MethodInvoker)
                        delegate ()
                        {
                            this.logValues.Text = "Logging halted.";
                        });

                        return;
                    }

                    Tuple<Logger, LogFileWriter, IEnumerable<string>> row;
                    if (logRowQueue.TryDequeue(out row))
                    {
                        if (row.Item2 != null)
                        {
                            row.Item2.WriteLine(row.Item3);
                        }

                        Tuple<string, List<ZoomedParameter>> values = FormatValuesForDisplay(
                            row.Item1, // logger
                            row.Item3); // row values

                        this.BeginInvoke((MethodInvoker)
                        delegate ()
                        {
                            this.logValues.Text = values.Item1;
                            this.DrawZoomedParameters(values.Item2);
                        });
                    }
                }
            }
            catch (Exception exception)
            {
                if (!logStopRequested)
                {
                    this.AddUserMessage("Log writing halted. " + exception.Message);
                    this.AddDebugMessage(exception.ToString());
                    this.logValues.Invoke(
                            (MethodInvoker)
                            delegate ()
                            {
                                this.logValues.Text = "Log writing halted. " + exception.Message;
                                this.startStopSaving.Focus();
                            });
                }
            }
            finally
            {
                this.writerThreadEnded.Set();
            }
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-only
using DynamicExpresso;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PcmHacking
{
    /// <summary>
    /// Combines a math column with the columns that it depends on.
    /// </summary>
    public class MathColumnAndDependencies
    {
        public LogColumn MathColumn { get; private set; }
        public LogColumn XColumn { get; private set; }
        public LogColumn YColumn { get; private set; }
        
        public MathColumnAndDependencies(
            LogColumn mathColumn,
            LogColumn xColumn,
            LogColumn yColumn)
        {
            this.MathColumn = mathColumn;
            this.XColumn = xColumn;
            this.YColumn = yColumn;
        }
    }

    /// <summary>
    /// Computes the values for math columns, based on data read from the PCM.
    /// </summary>
    public class MathValueProcessor
    {
        private readonly DpidConfiguration dpidConfiguration;
        private IEnumerable<MathColumnAndDependencies> mathColumns;

        /// <summary>
        /// Constructor
        /// </summary>
        public MathValueProcessor(DpidConfiguration dpidConfiguration, IEnumerable<MathColumnAndDependencies> mathColumns)
        {
            this.dpidConfiguration = dpidConfiguration;
            this.mathColumns = mathColumns;
        }

        /// <summary>
        /// Drop the math columns whose inputs are not being logged, and return what was dropped.
        /// </summary>
        /// <remarks>
        /// A math value is computed from two other columns, and the module gets to refuse those:
        /// anything it will not supply is taken out of its DPID group as logging starts. The math
        /// column is then left asking for a value nobody is reading, which surfaced as a bare
        /// "the given key was not present in the dictionary" against the parameter.
        ///
        /// Called once, after the module has had its say and before the headings are read, so the
        /// columns and the values still agree on how many there are.
        /// </remarks>
        public IReadOnlyList<Parameter> RemoveColumnsWithMissingDependencies()
        {
            HashSet<LogColumn> logged = new HashSet<LogColumn>(this.dpidConfiguration.AllLogColumns);

            List<MathColumnAndDependencies> kept = new List<MathColumnAndDependencies>();
            List<Parameter> dropped = new List<Parameter>();

            foreach (MathColumnAndDependencies math in this.mathColumns)
            {
                if (logged.Contains(math.XColumn) && logged.Contains(math.YColumn))
                {
                    kept.Add(math);
                }
                else
                {
                    dropped.Add(math.MathColumn.Parameter);
                }
            }

            this.mathColumns = kept;
            return dropped;
        }

        /// <summary>
        /// Returns the names of the math columns.
        /// </summary>
        public IEnumerable<string> GetHeaderNames()
        {
            return this.mathColumns.Select(
                x => LogColumnHeading.For(x.MathColumn.Parameter, x.MathColumn.Conversion.Units));
        }

        /// <summary>
        /// Gets the math columns - the logger will concatenate these with the PCM columns.
        /// </summary>
        public IEnumerable<LogColumn> GetMathColumns()
        {
            return this.mathColumns.Select(x => x.MathColumn);
        }

        /// <summary>
        /// The two values a math column is computed from, or false when either is not in this row.
        /// </summary>
        /// <remarks>
        /// <see cref="RemoveColumnsWithMissingDependencies"/> is what normally keeps this from
        /// happening. This is the guard behind it, because the alternative is a dictionary lookup
        /// that throws and puts the exception's own wording where a reading belongs.
        /// </remarks>
        private static bool TryGetInputs(
            PcmParameterValues dpidValues, MathColumnAndDependencies math, out double x, out double y)
        {
            x = 0;
            y = 0;

            if (!dpidValues.TryGetValue(math.XColumn, out PcmParameterValue? xValue)
                || !dpidValues.TryGetValue(math.YColumn, out PcmParameterValue? yValue))
            {
                return false;
            }

            x = xValue.ValueAsDouble;
            y = yValue.ValueAsDouble;
            return true;
        }

        /// <summary>
        /// Get the values of the math columns as strings, suitable for display or writing to a log file.
        /// </summary>
        public IEnumerable<string> GetMathValues(PcmParameterValues dpidValues)
        {
            List<string> result = new List<string>();
            foreach (MathColumnAndDependencies value in this.mathColumns)
            {
                try
                {
                    if (!TryGetInputs(dpidValues, value, out double x, out double y))
                    {
                        result.Add(string.Empty);
                        continue;
                    }

                    Interpreter finalConverter = new Interpreter();
                    finalConverter.SetVariable("x", x);
                    finalConverter.SetVariable("y", y);
                    double valueAsNumber = finalConverter.Eval<double>(value.MathColumn.Conversion.Expression);
                    if (double.IsNaN(valueAsNumber))
                    {
                        valueAsNumber = 0;
                    }

                    result.Add(valueAsNumber.ToString(value.MathColumn.Conversion.Format));
                }
                catch (Exception exception)
                {
                    result.Add("Error: " + exception.Message);
                }
            }

            return result;
        }

        public IEnumerable<LogRowElement> GetMathValuesV2(PcmParameterValues dpidValues)
        {
            List<LogRowElement> result = new List<LogRowElement>();
            foreach (MathColumnAndDependencies value in this.mathColumns)
            {
                try
                {
                    if (!TryGetInputs(dpidValues, value, out double x, out double y))
                    {
                        result.Add(new LogRowElement(
                            value.MathColumn.Parameter.Id,
                            value.MathColumn.Parameter.Name,
                            value.MathColumn.Conversion.Units,
                            string.Empty,
                            double.NaN));

                        continue;
                    }

                    Interpreter finalConverter = new Interpreter();
                    finalConverter.SetVariable("x", x);
                    finalConverter.SetVariable("y", y);
                    double valueAsNumber = finalConverter.Eval<double>(value.MathColumn.Conversion.Expression);
                    if (double.IsNaN(valueAsNumber))
                    {
                        valueAsNumber = 0;
                    }
                    var asString = valueAsNumber.ToString(value.MathColumn.Conversion.Format);

                    var item = new LogRowElement(
                        value.MathColumn.Parameter.Id,
                        value.MathColumn.Parameter.Name,
                        value.MathColumn.Conversion.Units,
                        asString,
                        valueAsNumber);

                    result.Add(item);
                        
                }
                catch (Exception exception)
                {
                    string valueAsString = "Error: " + exception.Message;
                    var item = new LogRowElement(
                        value.MathColumn.Parameter.Id,
                        value.MathColumn.Parameter.Name,
                        value.MathColumn.Conversion.Units,
                        valueAsString,
                        0);

                    result.Add(item);
                }
            }

            return result;
        }
    }
}


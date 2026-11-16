// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace PcmHacking
{
    /// <summary>
    /// Where a new gauge can go on a dashboard, in the layout's fractional coordinates.
    /// </summary>
    /// <remarks>
    /// Kept out of the view so the geometry can be tested without a window. A dashboard is usually
    /// tiled edge to edge, so there is no spare room to drop a gauge into; adding one has to take
    /// space from a neighbour (<see cref="Split"/>) or reuse the gap left by a deleted gauge
    /// (<see cref="FillFreeSpace"/>).
    /// </remarks>
    public static class GaugePlacement
    {
        /// <summary>
        /// Smallest width or height, as a fraction of the dashboard, that a gauge may be given. Below
        /// this the face and its title are unreadable.
        /// </summary>
        public const double MinimumSize = 0.06;

        private const double Tolerance = 1e-6;

        /// <summary>
        /// Halve a gauge along its longer side and put a new, unbound gauge in the other half.
        /// </summary>
        /// <param name="gauge">The gauge to take space from; it keeps the first half.</param>
        /// <param name="aspect">
        /// Width divided by height of the dashboard area, so "longer" is judged on screen rather
        /// than in fractions, which are not square.
        /// </param>
        /// <returns>The new gauge, or null if either half would be too small.</returns>
        public static GaugeLayout? Split(GaugeLayout gauge, double aspect)
        {
            if (gauge == null)
            {
                throw new ArgumentNullException(nameof(gauge));
            }

            bool sideBySide = gauge.Width * aspect >= gauge.Height;

            GaugeLayout added = CreateLike(gauge);

            if (sideBySide)
            {
                if (gauge.Width / 2 < MinimumSize)
                {
                    return null;
                }

                double half = gauge.Width / 2;
                added.Left = gauge.Left + half;
                added.Top = gauge.Top;
                added.Width = half;
                added.Height = gauge.Height;
                gauge.Width = half;
            }
            else
            {
                if (gauge.Height / 2 < MinimumSize)
                {
                    return null;
                }

                double half = gauge.Height / 2;
                added.Left = gauge.Left;
                added.Top = gauge.Top + half;
                added.Width = gauge.Width;
                added.Height = half;
                gauge.Height = half;
            }

            return added;
        }

        /// <summary>
        /// Give the space of a gauge that is about to be deleted to a neighbour, so deleting leaves
        /// no hole where that is possible.
        /// </summary>
        /// <remarks>
        /// Only a neighbour that shares the deleted gauge's whole edge qualifies, because growing
        /// it then gives a rectangle again. The smallest such neighbour is chosen: a gauge made by
        /// <see cref="Split"/> is half the size of the cell it came from, so deleting either half
        /// gives the space to the other, and growing the smaller gauge evens the layout out. Where
        /// they are all the same size, as in an unedited grid, the gauge before it in the list wins.
        /// </remarks>
        /// <returns>The gauge that grew, or null if none could and a gap is left.</returns>
        public static GaugeLayout? Absorb(DashboardLayout layout, GaugeLayout removed)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            int index = layout.Gauges.IndexOf(removed);
            if (index < 0)
            {
                return null;
            }

            // Before it, nearest first, then after it, nearest first. The smallest candidate wins,
            // and strictly smaller is required to displace an earlier one, so ties go to the
            // nearer gauge before it.
            GaugeLayout? best = null;
            double bestArea = double.MaxValue;
            for (int offset = 1; offset < layout.Gauges.Count; offset++)
            {
                foreach (int candidateIndex in new[] { index - offset, index + offset })
                {
                    if (candidateIndex < 0 || candidateIndex >= layout.Gauges.Count)
                    {
                        continue;
                    }

                    GaugeLayout candidate = layout.Gauges[candidateIndex];
                    double area = candidate.Width * candidate.Height;
                    if (area < bestArea - Tolerance && Grow(candidate, removed, apply: false))
                    {
                        best = candidate;
                        bestArea = area;
                    }
                }
            }

            if (best != null)
            {
                Grow(best, removed, apply: true);
            }

            return best;
        }

        /// <summary>
        /// Whether a gauge can be extended over a neighbour's space - they share a whole edge - and,
        /// when <paramref name="apply"/> is set, does it.
        /// </summary>
        private static bool Grow(GaugeLayout gauge, GaugeLayout neighbour, bool apply)
        {
            bool sameRows = Near(gauge.Top, neighbour.Top) && Near(gauge.Height, neighbour.Height);
            bool sameColumns = Near(gauge.Left, neighbour.Left) && Near(gauge.Width, neighbour.Width);

            if (sameRows && Near(gauge.Left + gauge.Width, neighbour.Left))
            {
                if (apply)
                {
                    gauge.Width += neighbour.Width;
                }

                return true;
            }

            if (sameRows && Near(neighbour.Left + neighbour.Width, gauge.Left))
            {
                if (apply)
                {
                    gauge.Left = neighbour.Left;
                    gauge.Width += neighbour.Width;
                }

                return true;
            }

            if (sameColumns && Near(gauge.Top + gauge.Height, neighbour.Top))
            {
                if (apply)
                {
                    gauge.Height += neighbour.Height;
                }

                return true;
            }

            if (sameColumns && Near(neighbour.Top + neighbour.Height, gauge.Top))
            {
                if (apply)
                {
                    gauge.Top = neighbour.Top;
                    gauge.Height += neighbour.Height;
                }

                return true;
            }

            return false;
        }

        private static bool Near(double a, double b) => Math.Abs(a - b) <= Tolerance;

        /// <summary>
        /// A new, unbound gauge filling the empty space around a point, or null if the point is on a
        /// gauge or the space is too small.
        /// </summary>
        /// <remarks>
        /// Grows a rectangle from the point: first along the row until it meets a gauge on either
        /// side, then up and down while that whole width stays clear. For the gap left by deleting
        /// a gauge in a tiled layout this gives back the same cell.
        /// </remarks>
        public static GaugeLayout? FillFreeSpace(DashboardLayout layout, double x, double y)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            foreach (GaugeLayout gauge in layout.Gauges)
            {
                if (Contains(gauge, x, y))
                {
                    return null;
                }
            }

            double left = 0;
            double right = 1;
            foreach (GaugeLayout gauge in layout.Gauges)
            {
                if (gauge.Top - Tolerance <= y && y < gauge.Top + gauge.Height + Tolerance)
                {
                    if (gauge.Left + gauge.Width <= x + Tolerance)
                    {
                        left = Math.Max(left, gauge.Left + gauge.Width);
                    }
                    else if (gauge.Left >= x - Tolerance)
                    {
                        right = Math.Min(right, gauge.Left);
                    }
                }
            }

            double top = 0;
            double bottom = 1;
            foreach (GaugeLayout gauge in layout.Gauges)
            {
                bool overlapsColumn = gauge.Left < right - Tolerance && gauge.Left + gauge.Width > left + Tolerance;
                if (!overlapsColumn)
                {
                    continue;
                }

                if (gauge.Top + gauge.Height <= y + Tolerance)
                {
                    top = Math.Max(top, gauge.Top + gauge.Height);
                }
                else if (gauge.Top >= y - Tolerance)
                {
                    bottom = Math.Min(bottom, gauge.Top);
                }
            }

            if (right - left < MinimumSize || bottom - top < MinimumSize)
            {
                return null;
            }

            GaugeLayout added = CreateLike(null);
            added.Left = left;
            added.Top = top;
            added.Width = right - left;
            added.Height = bottom - top;
            return added;
        }

        /// <summary>
        /// A blank gauge, taking the look of another so a split reads as more of the same.
        /// </summary>
        private static GaugeLayout CreateLike(GaugeLayout? model)
        {
            GaugeLayout created = new GaugeLayout
            {
                Kind = model?.Kind ?? GaugeKind.Round,
                Title = "New gauge",
                RangeLow = 0,
                RangeHigh = 100,
            };

            if (model != null)
            {
                created.NormalColor = model.NormalColor;
                created.AlarmColor = model.AlarmColor;
                created.Digits = model.Digits;
                created.ArcDegrees = model.ArcDegrees;
            }

            return created;
        }

        private static bool Contains(GaugeLayout gauge, double x, double y) =>
            x >= gauge.Left - Tolerance && x < gauge.Left + gauge.Width - Tolerance
            && y >= gauge.Top - Tolerance && y < gauge.Top + gauge.Height - Tolerance;
    }
}

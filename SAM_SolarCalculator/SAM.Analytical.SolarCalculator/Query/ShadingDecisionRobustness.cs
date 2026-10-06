// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>A break-even weighting, with its two special states kept distinct.</summary>
    public class BreakEvenResult
    {
        /// <summary>The weighting at which the two scores tie. NaN when <see cref="Never"/>.</summary>
        public double Value { get; set; } = double.NaN;

        /// <summary>True when the weighting cannot flip the pair at any value (zero denominator).</summary>
        public bool Never { get; set; }

        /// <summary>True when the only tie lies outside the valid penalty domain (negative).</summary>
        public bool NotReachable { get; set; }

        public BreakEvenResult()
        {
        }

        public BreakEvenResult(double value, bool never, bool notReachable)
        {
            Value = value;
            Never = never;
            NotReachable = notReachable;
        }
    }

    public static partial class Query
    {
        /// <summary>
        /// Decision robustness: how far each weighting would have to move before the ranking
        /// changes, solved from the row fields alone.
        ///
        ///   break-even lambda = ( (B_c - B_s) - mu     x (C_c - C_s) ) / (H_c - H_s)
        ///   break-even mu     = ( (B_c - B_s) - lambda x (H_c - H_s) ) / (C_c - C_s)
        ///
        /// THIS IS ALGEBRA ON MEASURED ROWS, NOT A RE-OPTIMISATION. It says exactly "how sensitive
        /// is the choice between these measured designs" — at the break-even value the challenger
        /// would also have been sized differently, and is not re-searched here.
        /// </summary>
        public static BreakEvenResult BreakEvenWantedSolarPenalty(ShadingComparisonRow top, ShadingComparisonRow challenger, double materialPenalty)
        {
            if (top == null || challenger == null)
            {
                return new BreakEvenResult(double.NaN, false, false);
            }

            double dB = challenger.Benefit - top.Benefit;
            double dH = challenger.Harm - top.Harm;
            double dC = challenger.Cost - top.Cost;
            double denominator = dH;

            if (Math.Abs(denominator) <= 1e-12)
            {
                return new BreakEvenResult(double.NaN, true, false);
            }

            double value = (dB - materialPenalty * dC) / denominator;
            return Classify(value);
        }

        /// <summary>See <see cref="BreakEvenWantedSolarPenalty"/>.</summary>
        public static BreakEvenResult BreakEvenMaterialPenalty(ShadingComparisonRow top, ShadingComparisonRow challenger, double wantedSolarPenalty)
        {
            if (top == null || challenger == null)
            {
                return new BreakEvenResult(double.NaN, false, false);
            }

            double dB = challenger.Benefit - top.Benefit;
            double dH = challenger.Harm - top.Harm;
            double dC = challenger.Cost - top.Cost;
            double denominator = dC;

            if (Math.Abs(denominator) <= 1e-12)
            {
                return new BreakEvenResult(double.NaN, true, false);
            }

            double value = (dB - wantedSolarPenalty * dH) / denominator;
            return Classify(value);
        }

        private static BreakEvenResult Classify(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return new BreakEvenResult(double.NaN, true, false);
            }

            if (value < 0)
            {
                return new BreakEvenResult(value, false, true);
            }

            return new BreakEvenResult(value, false, false);
        }

        /// <summary>The robustness verdict thresholds: SENSITIVE &lt; 0.25, MARGINAL &lt; 1.00, STABLE above or no reachable flip.</summary>
        public static string RobustnessVerdict(BreakEvenResult nearest, double current, double minimumWorkingRange, double maximumWorkingRange)
        {
            if (nearest == null || nearest.Never || nearest.NotReachable || double.IsNaN(current) || current == 0)
            {
                return "STABLE";
            }

            double ratio = Math.Abs(nearest.Value - current) / Math.Abs(current);

            // Lambda additionally requires no flip inside the recommended working range.
            if (!double.IsNaN(minimumWorkingRange) && !double.IsNaN(maximumWorkingRange)
                && nearest.Value >= minimumWorkingRange && nearest.Value <= maximumWorkingRange)
            {
                return "MARGINAL";
            }

            if (ratio < 0.25)
            {
                return "SENSITIVE";
            }

            if (ratio < 1.00)
            {
                return "MARGINAL";
            }

            return "STABLE";
        }

        /// <summary>Smallest value &gt;= the argument from the per-decade ladder 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10.</summary>
        public static double NiceCeiling(double value)
        {
            if (double.IsNaN(value) || value <= 0)
            {
                return double.NaN;
            }

            double[] ladder = new double[] { 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 };
            double decade = 1e-9;
            for (int i = 0; i < 20; i++)
            {
                foreach (double step in ladder)
                {
                    double candidate = step * decade;
                    if (candidate >= value - 1e-12 * Math.Max(1.0, value))
                    {
                        return candidate;
                    }
                }

                decade *= 10;
            }

            return value;
        }

        /// <summary>
        /// Renders ONE robustness axis as fixed-width ASCII lines. Deterministic by contract —
        /// the output is byte-asserted by test.
        ///
        ///   line 0  the axis title:  "lambda  (wanted-solar penalty)   domain 0 - 10"
        ///   line 1  tick value labels, left-aligned at each tick column
        ///   line 2  the "|" tick row
        ///   line 3  the glyph row (61 columns, indices 0-60)
        ///   lines 4+ value labels, greedy first-fit across at most three lines
        ///   then    the note lines, when anything was omitted
        /// </summary>
        /// <param name="title">The axis name and weighting description.</param>
        /// <param name="current">The weighting in use.</param>
        /// <param name="flipValues">Reachable, positive break-even values (already filtered).</param>
        /// <param name="neverValues">Break-evens that can never flip the pair (denominator zero).</param>
        /// <param name="offScaleValues">Reachable values above the axis maximum (excluded from drawing but named).</param>
        /// <param name="noShadeThreshold">The "no device worth its material" threshold; NaN when none.</param>
        /// <param name="domainMaximum">The drawn axis maximum (ExtremePenalty-clamped domainMax).</param>
        /// <param name="notes">The rendered note lines (empty when nothing was omitted).</param>
        public static List<string> RobustnessAxis(
            string title,
            double current,
            List<double> flipValues,
            List<double> neverValues,
            List<double> offScaleValues,
            double noShadeThreshold,
            double domainMaximum,
            out List<string> notes)
        {
            notes = new List<string>();

            double domainMax = domainMaximum;
            if (double.IsNaN(domainMax) || domainMax <= 0)
            {
                domainMax = 10.0;
            }

            // Crowding: at most the three break-evens nearest the current value are drawn per axis;
            // the No Shade threshold is always drawn when it exists.
            List<double> drawn = new List<double>();
            List<double> candidates = new List<double>(flipValues ?? new List<double>());
            candidates.Sort((a, b) =>
            {
                double da = Math.Abs(a - current);
                double db = Math.Abs(b - current);
                int order = da.CompareTo(db);
                return order != 0 ? order : a.CompareTo(b);
            });

            foreach (double value in candidates)
            {
                if (value < 0 || value > domainMax || drawn.Count >= 3)
                {
                    continue;
                }

                drawn.Add(value);
            }

            int omittedFlips = (flipValues?.Count ?? 0) - drawn.Count;

            List<string> lines = new List<string>();
            lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}  domain 0 - {1}", title, Format(domainMax)));

            // Ticks: five or six evenly spaced ticks at whole divisions of the domain maximum.
            double tickStep = TickStep(domainMax);
            int tickCount = (int)Math.Round(domainMax / tickStep);

            char[] tickRow = new char[61];
            char[] glyphRow = new char[61];
            for (int i = 0; i < 61; i++)
            {
                tickRow[i] = ' ';
                glyphRow[i] = ' ';
            }

            // The "-" span covers everything from the first flip onward; "=" covers the rest.
            drawn.Sort();
            double firstFlip = drawn.Count == 0 ? double.NaN : drawn[0];
            for (int i = 0; i < 61; i++)
            {
                double value = domainMax * i / 60.0;
                glyphRow[i] = !double.IsNaN(firstFlip) && value >= firstFlip - 1e-12 ? '-' : '=';
            }

            List<Tuple<int, string>> labels = new List<Tuple<int, string>>();

            // Glyph placement, with the fixed precedence so a collision is never ambiguous.
            if (!double.IsNaN(noShadeThreshold) && noShadeThreshold >= 0 && noShadeThreshold <= domainMax)
            {
                int column = Column(noShadeThreshold, domainMax);
                glyphRow[column] = '!';
                labels.Add(new Tuple<int, string>(column, Format(noShadeThreshold)));
            }

            foreach (double value in drawn)
            {
                int column = Column(value, domainMax);
                glyphRow[column] = 'X';
                labels.Add(new Tuple<int, string>(column, Format(value)));
            }

            if (!double.IsNaN(current) && current >= 0 && current <= domainMax)
            {
                int column = Column(current, domainMax);
                glyphRow[column] = 'o';
                labels.Add(new Tuple<int, string>(column, current.ToString("0.0##", CultureInfo.InvariantCulture) + " now"));
            }

            // Ticks.
            for (int t = 0; t <= tickCount; t++)
            {
                double value = tickStep * t;
                int column = Column(value, domainMax);
                if (column < 0 || column > 60)
                {
                    continue;
                }

                tickRow[column] = '|';
            }

            lines.Add(TickLabelRow(tickStep, tickCount, domainMax));
            lines.Add(new string(tickRow));
            lines.Add(new string(glyphRow));

            // Value labels: greedy first-fit, left-to-right, at most three lines.
            List<int> labelStarts = new List<int> { -1, -1, -1 }; // previous label end per line
            List<List<string>> labelLines = new List<List<string>> { new List<string>(), new List<string>(), new List<string>() };
            List<List<int>> labelColumns = new List<List<int>> { new List<int>(), new List<int>(), new List<int>() };

            List<Tuple<int, string>> unplaced = new List<Tuple<int, string>>();
            labels.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            foreach (Tuple<int, string> label in labels)
            {
                bool placed = false;
                for (int line = 0; line < 3; line++)
                {
                    if (label.Item1 > labelStarts[line] + 1)
                    {
                        labelLines[line].Add(label.Item2);
                        labelColumns[line].Add(label.Item1);
                        labelStarts[line] = label.Item1 + label.Item2.Length - 1;
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                {
                    unplaced.Add(label);
                }
            }

            for (int line = 0; line < 3; line++)
            {
                if (labelLines[line].Count == 0)
                {
                    continue;
                }

                // One overflow character beyond the axis end is admitted so the final value label
                // fits whole; the row is otherwise the fixed inner width.
                char[] labelRow = new char[62];
                for (int i = 0; i < 62; i++)
                {
                    labelRow[i] = ' ';
                }

                for (int k = 0; k < labelLines[line].Count; k++)
                {
                    string text = labelLines[line][k];
                    int start = labelColumns[line][k];
                    for (int j = 0; j < text.Length && start + j < 62; j++)
                    {
                        labelRow[start + j] = text[j];
                    }
                }

                lines.Add(new string(labelRow).TrimEnd());
            }

            // Notes: off-scale and never values, omitted flips, unplaced labels.
            List<string> offScaleNames = new List<string>();
            foreach (double value in offScaleValues ?? new List<double>())
            {
                offScaleNames.Add(Format(value));
            }

            foreach (double value in neverValues ?? new List<double>())
            {
                offScaleNames.Add("never");
            }

            if (offScaleNames.Count != 0)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} break-even value{1} {2} off this scale ({3}) - see table above.",
                    offScaleNames.Count, offScaleNames.Count == 1 ? "is" : "s are", offScaleNames.Count == 1 ? "is" : "are",
                    string.Join(", ", offScaleNames)));
            }

            if (omittedFlips > 0)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} break-even value{1} {2} omitted from this axis ({3} are drawn); see the table above.",
                    omittedFlips, omittedFlips == 1 ? "was" : "s were", omittedFlips == 1 ? "was" : "were", drawn.Count));
            }

            if (unplaced.Count != 0)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} label{1} could not be placed on the axis ({2}).",
                    unplaced.Count, unplaced.Count == 1 ? string.Empty : "s", string.Join(", ", unplaced.ConvertAll(x => x.Item2))));
            }

            return lines;
        }

        private static int Column(double value, double domainMax)
        {
            int column = (int)Math.Round(60.0 * value / domainMax);
            return Math.Max(0, Math.Min(60, column));
        }

        private static string Format(double value)
        {
            if (double.IsNaN(value))
            {
                return "n/a";
            }

            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static double TickStep(double domainMax)
        {
            double[] ladder = new double[] { 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 };

            // Prefer five divisions, then six, then four — the specimen's axes use whole divisions
            // of the domain maximum (step 2 over a 0-10 domain, step 0.1 over a 0-0.4 domain).
            foreach (int divisions in new int[] { 5, 6, 4 })
            {
                double candidate = domainMax / divisions;
                for (int decade = -6; decade <= 6; decade++)
                {
                    foreach (double step in ladder)
                    {
                        double scaled = step * Math.Pow(10, decade);
                        if (scaled <= 0)
                        {
                            continue;
                        }

                        if (Math.Abs(scaled - candidate) <= 1e-9 * Math.Max(1.0, Math.Abs(candidate)))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return domainMax / 5.0;
        }

        private static string TickLabelRow(double tickStep, int tickCount, double domainMax)
        {
            // Tick labels are RIGHT-ALIGNED to their tick column, so the last label ends exactly on
            // the axis end.
            StringBuilder stringBuilder = new StringBuilder(new string(' ', 61));
            for (int t = 0; t <= tickCount; t++)
            {
                double value = tickStep * t;
                int column = Column(value, domainMax);
                string label = Format(value);
                int start = column - label.Length + 1;
                for (int j = 0; j < label.Length; j++)
                {
                    int index = start + j;
                    if (index >= 0 && index < 61)
                    {
                        stringBuilder[index] = label[j];
                    }
                }
            }

            return stringBuilder.ToString().TrimEnd();
        }
    }
}

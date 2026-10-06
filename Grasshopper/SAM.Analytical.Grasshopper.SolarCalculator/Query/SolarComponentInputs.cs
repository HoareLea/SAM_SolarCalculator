// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.Grasshopper.SolarCalculator
{
    /// <summary>
    /// Input plumbing shared by the Solar components. Kept in one place so every component reads a
    /// wire the same way: an enum accepts its own value, its name or its index; an object input
    /// accepts the SAM object whether it arrived bare, inside a Goo, or inside a GH_ObjectWrapper.
    /// </summary>
    public static class Query
    {
        /// <summary>Unwraps a Grasshopper value down to the SAM/system object inside it.</summary>
        public static object Unwrap(object @object)
        {
            if (@object == null)
            {
                return null;
            }

            if (@object is GH_ObjectWrapper objectWrapper)
            {
                return Unwrap(objectWrapper.Value);
            }

            if (@object is Core.Grasshopper.IGooJSAMObject gooJSAMObject)
            {
                return gooJSAMObject.GetJSAMObject();
            }

            if (@object is IGH_Goo)
            {
                try
                {
                    object value = (@object as dynamic).Value;
                    if (value != null && !ReferenceEquals(value, @object))
                    {
                        return Unwrap(value);
                    }
                }
                catch
                {
                }
            }

            return @object;
        }

        /// <summary>The wrapped object as T, or the type's default when it is something else.</summary>
        public static T Value<T>(object @object) where T : class
        {
            return Unwrap(@object) as T;
        }

        /// <summary>
        /// An enum from whatever a user plugged in: the enum value, its name (case-insensitive) or
        /// its integer index. False when the input is absent or names nothing in the enum, so the
        /// caller can say which values ARE accepted rather than substituting a default silently.
        /// </summary>
        public static bool TryGetEnum<T>(object @object, out T value) where T : struct
        {
            value = default;

            object unwrapped = Unwrap(@object);
            if (unwrapped == null)
            {
                return false;
            }

            if (unwrapped is T typed)
            {
                value = typed;
                return true;
            }

            if (unwrapped is string text)
            {
                return Enum.TryParse(text.Replace(" ", string.Empty), true, out value) && Enum.IsDefined(typeof(T), value);
            }

            if (Core.Query.IsNumeric(unwrapped))
            {
                int index = System.Convert.ToInt32(unwrapped);
                if (Enum.IsDefined(typeof(T), index))
                {
                    value = (T)Enum.ToObject(typeof(T), index);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Hours of the year from a list of numbers, dropping anything that is not one.</summary>
        public static List<int> HoursOfYear(IEnumerable<GH_ObjectWrapper> objectWrappers)
        {
            List<int> result = new List<int>();
            if (objectWrappers == null)
            {
                return result;
            }

            foreach (GH_ObjectWrapper objectWrapper in objectWrappers)
            {
                object @object = Unwrap(objectWrapper);
                if (@object == null)
                {
                    continue;
                }

                if (@object is DateTime dateTime)
                {
                    result.Add((int)(dateTime - new DateTime(dateTime.Year, 1, 1)).TotalHours);
                    continue;
                }

                if (Core.Query.IsNumeric(@object))
                {
                    result.Add(System.Convert.ToInt32(@object));
                }
            }

            return result;
        }

        /// <summary>
        /// A percentage for reporting: the ratio as a percentage, or NaN when the ratio is NaN.
        /// A NaN ratio means its denominator was zero and the metric is genuinely unavailable — it
        /// must never be reported as 0 % or 100 %.
        /// </summary>
        public static double Percentage(double ratio)
        {
            return double.IsNaN(ratio) ? double.NaN : 100.0 * ratio;
        }
    }
}

// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Linq;
using System.Reflection;
using Xunit;
using SAM.Analytical;
using SolarCreate = SAM.Analytical.SolarCalculator.Create;
using SAM.Analytical.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// §14.1: the CLR-signature guard for the new API. An optional parameter appended to an
    /// existing method changes its metadata signature even though the same source call still
    /// compiles — so this PR adds capability only as NEW overloads, and this pins the shapes a
    /// downstream caller could bind to, following the pattern PR #17 established.
    /// </summary>
    public class SchemeApiCompatibilityTests
    {
        [Fact]
        public void VerifiedShadingSchemeResult_Has_A_Model_Level_And_A_Context_Level_Overload()
        {
            // The model-level entry point (extension on AnalyticalModel): a distinct overload, not
            // an optional parameter appended to an existing method.
            MethodInfo modelLevel = typeof(SolarCreate)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .SingleOrDefault(method => method.Name == "VerifiedShadingSchemeResult"
                    && method.GetParameters().FirstOrDefault()?.ParameterType == typeof(AnalyticalModel));

            Assert.NotNull(modelLevel);
            Assert.Equal(typeof(ShadingScheme), modelLevel.GetParameters()[1].ParameterType);
            Assert.Equal(typeof(int), modelLevel.GetParameters()[3].ParameterType);

            // The prepared-context overload (extension on ShadingScheme): the one the comparison
            // calls so the solar context is built once, not once per scheme.
            MethodInfo contextLevel = typeof(SolarCreate)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .SingleOrDefault(method => method.Name == "VerifiedShadingSchemeResult"
                    && method.GetParameters().FirstOrDefault()?.ParameterType == typeof(ShadingScheme));

            Assert.NotNull(contextLevel);
            Assert.Equal(typeof(ApertureSolarContext), contextLevel.GetParameters()[1].ParameterType);
        }

        [Fact]
        public void ShadingObjective_Existing_Overloads_Are_Untouched()
        {
            // The pre-existing ShadingPerformance and GroupedShadingPerformance scoring overloads
            // keep their parameter counts; the scheme set is ADDITIVE.
            int performanceOverloads = typeof(ShadingObjective)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Count(method => method.Name == "Score" && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == typeof(ShadingPerformance));
            Assert.Equal(1, performanceOverloads);

            int groupedOverloads = typeof(ShadingObjective)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Count(method => method.Name == "Score" && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == typeof(GroupedShadingPerformance));
            Assert.Equal(1, groupedOverloads);

            int schemeOverloads = typeof(ShadingObjective)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Count(method => method.Name == "Score" && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == typeof(ShadingSchemePerformance));
            Assert.Equal(1, schemeOverloads);
        }

        [Fact]
        public void ShadingTypology_ElementGuid_Is_Untouched()
        {
            // P1: the element-GUID collision is fixed at scheme-assembly time; the typology's own
            // identity method is not changed, or every cached attribution identity would move.
            MethodInfo elementGuid = typeof(ShadingTypology)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .SingleOrDefault(method => method.Name == "ElementGuid" && method.GetParameters().Length == 1);

            Assert.NotNull(elementGuid);
            Assert.Equal(typeof(int), elementGuid.GetParameters()[0].ParameterType);
        }

        [Fact]
        public void ShadingScheme_Is_Not_A_ShadingDevice_And_Never_Passes_As_One()
        {
            // §5.2: the scheme is a container. A grouped device must never pass as a per-window
            // device, and the scheme must never implement the single-device contract.
            Assert.False(typeof(ShadingDevice).IsAssignableFrom(typeof(ShadingScheme)));
            Assert.False(typeof(IShadingTypology).IsAssignableFrom(typeof(GroupedShadingDevice)));
            Assert.False(typeof(IShadingTypology).IsAssignableFrom(typeof(ShadingScheme)));
        }

        [Fact]
        public void Existing_Public_Entry_Points_Keep_Their_Arities()
        {
            // The frozen arities of the entry points PR #17 pinned, extended to the new API: no
            // public method gained a parameter in this PR. The two overloads are identified by their
            // trailing parameter TYPE — the original twenty-two-parameter signature ends in the int
            // evaluation budget, the mounting-offset overload ends in the double offset — and the
            // arity is asserted independently, so a signature change is caught rather than silently
            // re-selected by its own parameter count.
            MethodInfo[] overloads = typeof(SolarCreate)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == "AwningGroupResults")
                .ToArray();

            MethodInfo original = overloads.SingleOrDefault(method => method.GetParameters().Last().ParameterType == typeof(int));
            MethodInfo mountingOffset = overloads.SingleOrDefault(method => method.GetParameters().Last().ParameterType == typeof(double));

            Assert.NotNull(original);
            Assert.NotNull(mountingOffset);

            Assert.Equal(22, original.GetParameters().Length);
            Assert.Equal(23, mountingOffset.GetParameters().Length);
        }
    }
}

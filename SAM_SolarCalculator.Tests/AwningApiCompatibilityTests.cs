// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Linq;
using System.Reflection;
using Xunit;
using SAM.Analytical.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The mounting-offset feature was added as NEW overloads; the original public signatures must
    /// remain in the assembly unchanged so already-compiled callers keep resolving. Source-level
    /// tests alone cannot see a CLR signature change — an optional parameter appended to an existing
    /// method changes its metadata signature even though the same source call still compiles — so
    /// this pins the exact old parameter counts and trailing types by reflection.
    /// </summary>
    public class AwningApiCompatibilityTests
    {
        [Fact]
        public void RetractableAwning_Retains_The_Original_Five_Double_Constructor()
        {
            // The pre-mounting-offset constructor: exactly five double parameters.
            ConstructorInfo oldConstructor = typeof(RetractableAwning).GetConstructor(new[]
            {
                typeof(double), typeof(double), typeof(double), typeof(double), typeof(double),
            });
            Assert.NotNull(oldConstructor);

            // The new constructor: six double parameters, with MountingOffset appended.
            ConstructorInfo newConstructor = typeof(RetractableAwning).GetConstructor(new[]
            {
                typeof(double), typeof(double), typeof(double), typeof(double), typeof(double), typeof(double),
            });
            Assert.NotNull(newConstructor);
        }

        [Fact]
        public void AwningGroupResults_Retains_The_Original_Twenty_Two_Parameter_Overload()
        {
            MethodInfo[] overloads = typeof(Create)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == "AwningGroupResults")
                .ToArray();

            MethodInfo old = overloads.SingleOrDefault(method => method.GetParameters().Length == 22);
            MethodInfo @new = overloads.SingleOrDefault(method => method.GetParameters().Length == 23);

            Assert.NotNull(old);
            Assert.NotNull(@new);

            // The old trailing parameter is the int maximumEvaluations budget, never the double offset.
            Assert.Equal(typeof(int), old.GetParameters().Last().ParameterType);
            Assert.Equal(typeof(double), @new.GetParameters().Last().ParameterType);
        }

        [Fact]
        public void RetractableAwningGroup_Retains_The_Original_Twelve_Parameter_Overload()
        {
            MethodInfo[] overloads = typeof(Optimise)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == "RetractableAwningGroup")
                .ToArray();

            MethodInfo old = overloads.SingleOrDefault(method => method.GetParameters().Length == 12);
            MethodInfo @new = overloads.SingleOrDefault(method => method.GetParameters().Length == 13);

            Assert.NotNull(old);
            Assert.NotNull(@new);

            Assert.Equal(typeof(int), old.GetParameters().Last().ParameterType);
            Assert.Equal(typeof(double), @new.GetParameters().Last().ParameterType);
        }
    }
}

// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SAM.Analytical.Grasshopper.SolarCalculator.Properties;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Core.SolarCalculator;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.Grasshopper.SolarCalculator
{
    public class SAMAnalyticalAnalysisPeriod : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7f3c9a10-5b28-4e63-9a41-6c0d2e7b1101");

        /// <summary>
        /// The latest version of this component
        /// </summary>
        public override string LatestComponentVersion => "1.0.0";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        public override GH_Exposure Exposure => GH_Exposure.primary;

        public SAMAnalyticalAnalysisPeriod()
          : base("SAMAnalytical.AnalysisPeriod", "SAMAnalytical.AnalysisPeriod",
              "SUMMARY\nDefines WHICH HOURS of the year the solar and shading calculations run over. Feed the result into ApertureIrradiance, ShadingPotentialField, RationaliseShading and VerifyShading so every step reports the same hours.\n\nINPUTS\n  _year — calendar year the hours belong to. Downstream nodes re-root the period onto the weather file's own year when they differ, keeping the hour-of-year structure.\n  _preset_ — a named period: Full Year, Summer, Winter, Equinox, Cooling Season, Heating Season, Peak Summer Day, Peak Winter Day. Seasonal presets FLIP in the southern hemisphere when _location_ is supplied. Accepts the name, or the value from a SAM enum node.\n  _startMonth_ / _startDay_ / _endMonth_ / _endDay_ — a custom date range, inclusive of the whole end day. A range that runs backwards (e.g. 1 Nov to 28 Feb) wraps the year end, which is what a heating season needs.\n  _startHour_ / _endHour_ — hours of the day to keep, 0-23 inclusive. A backwards pair (e.g. 22 to 6) is an overnight window.\n  _HOYs_ — explicit hours of the year, 0 = 1 Jan 00:00.\n  _location_ — only used to decide the hemisphere for the seasonal presets.\n\nOUTPUTS\n  analysisPeriod — the period object to wire downstream.\n  HOYs — every hour of the year selected, so the selection can be inspected or plotted.\n  dateTimes — the same hours as date-times.\n  hourCount — how many hours were selected.\n  description — a one-line summary of what was built.\n\nNOTES\nPRECEDENCE, and nothing is discarded quietly: explicit _HOYs_ beat a custom date range, which beats _preset_, which defaults to the full year. Whenever a lower-priority input is overridden the node says so in its message balloon.\nHours are on the weather timeline. The sun-position offset (interval start, on the hour, interval end) is applied by the calculation nodes, never here, so the two timelines cannot be mixed silently.\n\nEXAMPLE\nAnalysisPeriod.analysisPeriod → ShadingPotentialField._unwantedPeriod_ (Summer) and ShadingPotentialField._wantedPeriod_ (Winter) — and the same pair on RationaliseShading, RationaliseAwningGroup, VerifyShading and CompareShading, so every step reports the same hours.\nAnalysisPeriod.analysisPeriod → ApertureIrradiance._analysisPeriod_ for the irradiance run.\nSet _year = 2018 and _preset_ = Summer for a June-August summer study; or leave everything empty for a full year.",
              "SAM", "Solar")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                global::Grasshopper.Kernel.Parameters.Param_Integer integer = new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "_year", NickName = "_year", Description = "Calendar year the analysis hours belong to", Access = GH_ParamAccess.item };
                integer.SetPersistentData(2018);
                result.Add(new GH_SAMParam(integer, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_GenericObject preset = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_preset_", NickName = "_preset_", Description = "Named period: Full Year, Summer, Winter, Equinox, Cooling Season, Heating Season, Peak Summer Day, Peak Winter Day.\nDefault: Full Year", Access = GH_ParamAccess.item, Optional = true };
                result.Add(new GH_SAMParam(preset, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(Integer("_startMonth_", "First month of a custom date range, 1-12"), ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(Integer("_startDay_", "First day of a custom date range"), ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(Integer("_endMonth_", "Last month of a custom date range, 1-12 (inclusive)"), ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(Integer("_endDay_", "Last day of a custom date range (inclusive)"), ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(Integer("_startHour_", "First hour of the day to keep, 0-23"), ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(Integer("_endHour_", "Last hour of the day to keep, 0-23 (inclusive). Lower than _startHour_ makes an overnight window"), ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_GenericObject hoursOfYear = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_HOYs_", NickName = "_HOYs_", Description = "Explicit hours of the year (0 = 1 Jan 00:00). These OVERRIDE a preset or custom range", Access = GH_ParamAccess.list, Optional = true };
                result.Add(new GH_SAMParam(hoursOfYear, ParamVisibility.Voluntary));

                result.Add(new GH_SAMParam(new GooLocationParam() { Name = "_location_", NickName = "_location_", Description = "Site location. Only used to flip the seasonal presets in the southern hemisphere", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));

                return result.ToArray();
            }
        }

        protected override GH_SAMParam[] Outputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "analysisPeriod", NickName = "analysisPeriod", Description = "The analysis period to feed the solar and shading nodes", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "HOYs", NickName = "HOYs", Description = "Every hour of the year selected", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Time() { Name = "dateTimes", NickName = "dateTimes", Description = "The selected hours as date-times", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "hourCount", NickName = "hourCount", Description = "Number of hours selected", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "description", NickName = "description", Description = "One-line summary of the period that was built", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                return result.ToArray();
            }
        }

        private static global::Grasshopper.Kernel.Parameters.Param_Integer Integer(string name, string description)
        {
            global::Grasshopper.Kernel.Parameters.Param_Integer result = new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = name, NickName = name, Description = description, Access = GH_ParamAccess.item, Optional = true };
            result.SetPersistentData(-1);
            return result;
        }

        private int Integer(IGH_DataAccess dataAccess, string name)
        {
            int index = Params.IndexOfInputParam(name);
            if (index == -1)
            {
                return -1;
            }

            int value = -1;
            return dataAccess.GetData(index, ref value) ? value : -1;
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            int index = Params.IndexOfInputParam("_year");
            int year = -1;
            if (index == -1 || !dataAccess.GetData(index, ref year) || year < 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply a valid year.");
                return;
            }

            Location location = null;
            index = Params.IndexOfInputParam("_location_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref location);
            }

            List<int> hoursOfYear = new List<int>();
            index = Params.IndexOfInputParam("_HOYs_");
            if (index != -1)
            {
                List<GH_ObjectWrapper> objectWrappers = new List<GH_ObjectWrapper>();
                if (dataAccess.GetDataList(index, objectWrappers))
                {
                    hoursOfYear = Query.HoursOfYear(objectWrappers);
                }
            }

            int startMonth = Integer(dataAccess, "_startMonth_");
            int startDay = Integer(dataAccess, "_startDay_");
            int endMonth = Integer(dataAccess, "_endMonth_");
            int endDay = Integer(dataAccess, "_endDay_");
            int startHour = Integer(dataAccess, "_startHour_");
            int endHour = Integer(dataAccess, "_endHour_");

            bool customDates = startMonth >= 1 || startDay >= 1 || endMonth >= 1 || endDay >= 1;
            bool customHours = startHour >= 0 || endHour >= 0;

            AnalysisPeriodPreset preset = AnalysisPeriodPreset.Undefined;
            bool presetSupplied = false;
            index = Params.IndexOfInputParam("_preset_");
            if (index != -1)
            {
                GH_ObjectWrapper objectWrapper = null;
                if (dataAccess.GetData(index, ref objectWrapper) && objectWrapper?.Value != null)
                {
                    if (!Query.TryGetEnum(objectWrapper, out preset) || preset == AnalysisPeriodPreset.Undefined)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_preset_ was not recognised. Use one of: Full Year, Summer, Winter, Equinox, Cooling Season, Heating Season, Peak Summer Day, Peak Winter Day.");
                        return;
                    }

                    presetSupplied = preset != AnalysisPeriodPreset.Custom;
                }
            }

            AnalysisPeriod analysisPeriod;
            string description;

            if (hoursOfYear.Count != 0)
            {
                analysisPeriod = new AnalysisPeriod(year, hoursOfYear);
                description = string.Format("{0} explicit hours in {1}", analysisPeriod.HoursOfYear().Count, year);

                if (presetSupplied || customDates || customHours)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Explicit HOYs override the connected preset and custom range.");
                }
            }
            else if (customDates || customHours)
            {
                int resolvedStartMonth = startMonth >= 1 ? startMonth : 1;
                int resolvedStartDay = startDay >= 1 ? startDay : 1;
                int resolvedEndMonth = endMonth >= 1 ? endMonth : 12;
                int resolvedEndDay = endDay >= 1 ? endDay : 31;
                int resolvedStartHour = startHour >= 0 ? startHour : 0;
                int resolvedEndHour = endHour >= 0 ? endHour : 23;

                if (resolvedStartMonth > 12 || resolvedEndMonth > 12)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Months must be within 1-12.");
                    return;
                }

                analysisPeriod = new AnalysisPeriod(year, resolvedStartMonth, resolvedStartDay, resolvedEndMonth, resolvedEndDay, resolvedStartHour, resolvedEndHour);
                description = analysisPeriod.ToString();

                if (presetSupplied)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The custom date range overrides the connected preset.");
                }

                if (analysisPeriod.WrapsYear)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The date range wraps the year end, so both ends of the year are included.");
                }
            }
            else
            {
                AnalysisPeriodPreset resolved = presetSupplied ? preset : AnalysisPeriodPreset.FullYear;
                analysisPeriod = Core.SolarCalculator.Create.AnalysisPeriod(resolved, year, location);
                if (analysisPeriod == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "That preset does not define a period on its own. Supply a custom date range or explicit HOYs instead.");
                    return;
                }

                description = string.Format("{0} {1}", resolved, year);

                if (!presetSupplied)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No period supplied: using the full year.");
                }
                else if (location == null && (resolved == AnalysisPeriodPreset.Summer || resolved == AnalysisPeriodPreset.Winter || resolved == AnalysisPeriodPreset.CoolingSeason || resolved == AnalysisPeriodPreset.HeatingSeason || resolved == AnalysisPeriodPreset.PeakSummerDay || resolved == AnalysisPeriodPreset.PeakWinterDay))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No location supplied: seasonal presets assume the northern hemisphere.");
                }
            }

            List<int> resolvedHours = analysisPeriod.HoursOfYear();
            if (resolvedHours.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The period contains no hours. Check the dates, the hours of the day and the year.");
            }

            index = Params.IndexOfOutputParam("analysisPeriod");
            if (index != -1)
            {
                dataAccess.SetData(index, new GooSAMObject(analysisPeriod));
            }

            index = Params.IndexOfOutputParam("HOYs");
            if (index != -1)
            {
                dataAccess.SetDataList(index, resolvedHours);
            }

            index = Params.IndexOfOutputParam("dateTimes");
            if (index != -1)
            {
                dataAccess.SetDataList(index, analysisPeriod.DateTimes());
            }

            index = Params.IndexOfOutputParam("hourCount");
            if (index != -1)
            {
                dataAccess.SetData(index, resolvedHours.Count);
            }

            index = Params.IndexOfOutputParam("description");
            if (index != -1)
            {
                dataAccess.SetData(index, description);
            }
        }
    }
}

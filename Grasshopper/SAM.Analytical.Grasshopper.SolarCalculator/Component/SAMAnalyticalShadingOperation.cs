// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SAM.Analytical.Grasshopper.SolarCalculator.Properties;
using SAM.Analytical.SolarCalculator;
using SAM.Core.Grasshopper;
using SAM.Core.SolarCalculator;
using SAM.Weather;
using System;
using System.Collections.Generic;
using System.Linq;
using SolarCreate = SAM.Analytical.SolarCalculator.Create;

namespace SAM.Analytical.Grasshopper.SolarCalculator
{
    public class SAMAnalyticalShadingOperation : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7f3c9a10-5b28-4e63-9a41-6c0d2e7b1109");

        /// <summary>
        /// The latest version of this component.
        ///
        /// 1.1.0 — the GROUPED DEVICE hand-off: _shadingDevice now also accepts a
        /// GroupedShadingDevice from SAMAnalytical.RationaliseAwningGroup.groupedShadingDevices.
        /// The device fixes the member scope: the wired targets must be exactly its member
        /// apertures (matched by aperture Guid, in any order), and the group the device was
        /// designed for is re-established under the device's OWN recorded grouping criteria
        /// (maximum gap and head tolerance included) and checked against the device's GroupGuid —
        /// never re-guessed from the wired targets. A shading scheme is still refused, now with a
        /// message saying where it DOES go (SAMAnalytical.VerifyShading).
        /// </summary>
        public override string LatestComponentVersion => "1.1.0";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Resources.SAM_SolarCalculator;

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        public SAMAnalyticalShadingOperation()
          : base("SAMAnalytical.ShadingOperation", "SAMAnalytical.ShadingOperation",
              "SUMMARY\nReports what a RETRACTABLE shading device actually does over one weather year: the hours shading was REQUESTED, the hours the device was actually DEPLOYED, the hours the wind RETRACTED it, and the energy it intercepted.\n\nTHREE DISTINCT THINGS, KEPT APART:\n  REQUESTED - the control rule (from SAMAnalytical.SolarControlProfile) says this window's solar is unwanted.\n  DEPLOYED - the device was actually out: requested AND under the wind limit.\n  WIND RETRACTED - requested but refused, because the recorded wind speed exceeded the limit.\nSite daylight and sun on the window are different numbers too - the control profile already separates them; this component measures the DEVICE.\n\nTWO KINDS OF ENERGY, KEPT APART:\n  controlledUnwantedSolarIntercepted - unwanted beam the device stopped WHILE DEPLOYED. This is the operational answer and it responds to the wind limit.\n  controlledDirectSolarIntercepted / canopyAttributedEnergy / valanceAttributedEnergy - the FULL-YEAR direct-beam interception and first-hit attribution of the device geometry. These do NOT change with the wind limit; they describe the geometry, not the deployment schedule.\n\nTHE WIND SPEED IS THE WEATHER FILE'S OWN HOURLY VALUE. This is an ANNUAL DESIGN PROFILE - an operating assumption for sizing and estimating, NOT a live safety controller and NOT a structural verification. The limit (set on the control profile) is fully configurable; no value is hard-coded here.\n\nONE DEVICE, SEVERAL WINDOWS. Wire several targets and their control profiles to measure ONE awning spanning them: the device headline is the UNION of the member schedules (the motor moved because ANY window asked), and each member's own schedule is kept on memberProfiles. Every member profile must share the same year, sun-position shift and control rule - a disagreement is refused, never averaged.\n\nINPUTS\n  _analyticalModel - the SAM Analytical Model.\n  _apertureSolarTargets - the windows, from SAMAnalytical.ApertureSolarTargets. Several targets form one shared device when they make a valid group.\n  _controlProfiles - one SAMAnalytical.SolarControlProfile per target, from SAMAnalytical.SolarControlProfile.\n  _shadingDevice - the device, from a shading node: a ShadingDevice from RationaliseShading, a GroupedShadingDevice from RationaliseAwningGroup, or a typology such as RetractableAwning.\n  _gridSize_ - the analysis grid size [m]. Keep it the same as the targets.\n  _sunAngleStep_ - how finely similar sun positions are grouped [°].\n  _run - nothing is calculated until this is true.\n\nEXAMPLE\nApertureSolarTargets.apertureSolarTargets → ShadingOperation._apertureSolarTargets and SolarControlProfile.controlProfile → ShadingOperation._controlProfiles - one of each per window; several windows forming one valid group are measured as ONE shared awning.\nRationaliseShading.shadingDevice → ShadingOperation._shadingDevice - the retractable device, e.g. the Retractable Awning family winner for the window.\nOR RationaliseAwningGroup.groupedShadingDevices → ShadingOperation._shadingDevice - the grouped awning designed for those SAME windows: the device fixes the member group, so the wired targets must be exactly its member windows, matched by aperture in any order.\nDo not wire a scheme from AssembleShadingSchemes or SelectShadingScheme into _shadingDevice: a scheme is a whole-scope proposal, not one device, and it is refused.",
              "SAM", "Solar")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooAnalyticalModelParam() { Name = "_analyticalModel", NickName = "_analyticalModel", Description = "SAM Analytical Model", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooApertureSolarTargetParam() { Name = "_apertureSolarTargets", NickName = "_apertureSolarTargets", Description = "The windows, from SAMAnalytical.ApertureSolarTargets.\nOne device may span several adjacent windows when they form a valid group", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_controlProfiles", NickName = "_controlProfiles", Description = "One SAMAnalytical.SolarControlProfile per window, from SAMAnalytical.SolarControlProfile.\nIts shadeOn hours ARE the deployment schedule", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_shadingDevice", NickName = "_shadingDevice", Description = "The device, from a shading node: a ShadingDevice (SAMAnalytical.RationaliseShading), a GroupedShadingDevice (SAMAnalytical.RationaliseAwningGroup.groupedShadingDevices) or a shading typology such as RetractableAwning.\nA GroupedShadingDevice fixes the member group: the wired targets must be exactly its member windows, matched by aperture in any order.\nA shading scheme is refused - verify a scheme with SAMAnalytical.VerifyShading", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(Number("_gridSize_", "The analysis grid size [m]. Keep it the same as the targets.\nDefault 0.5 m", 0.5), ParamVisibility.Binding));
                result.Add(new GH_SAMParam(Number("_sunAngleStep_", "How finely similar sun positions are grouped [°].\nDefault 2°", 2.0), ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean run = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_run", NickName = "_run", Description = "Nothing is calculated until this is true", Access = GH_ParamAccess.item };
                run.SetPersistentData(false);
                result.Add(new GH_SAMParam(run, ParamVisibility.Binding));

                return result.ToArray();
            }
        }

        protected override GH_SAMParam[] Outputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "operationProfile", NickName = "operationProfile", Description = "The whole answer as one object: the device schedule, the effective hours, the energies and the per-window diagnostics", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "deviceRequestedHOYs", NickName = "deviceRequestedHOYs", Description = "Hours SHADING IS REQUESTED at the device (0-based hour of the year): the union of the member requests.\nOne physical awning is requested whenever ANY of its windows is", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "deviceDeployedHOYs", NickName = "deviceDeployedHOYs", Description = "Hours the device is ACTUALLY DEPLOYED (0-based): requested AND under the wind limit", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "windRetractedHOYs", NickName = "windRetractedHOYs", Description = "Requested hours the wind limit REFUSED (0-based).\ndeviceRequested = deviceDeployed + windRetracted, exactly", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "shadeUse", NickName = "shadeUse", Description = "Deployed hours as a percentage of requested hours [%]. NaN when nothing was requested", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "canopyEffectiveHOYs", NickName = "canopyEffectiveHOYs", Description = "Deployed hours in which the CANOPY actually intercepted sun that would otherwise reach a window (0-based)", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "valanceEffectiveHOYs", NickName = "valanceEffectiveHOYs", Description = "Deployed hours in which the VALANCE actually intercepted sun (0-based).\nAn hour can be effective for both the canopy and the valance: different parts of the window are hit by different parts of the device", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "deviceRequestedHours", NickName = "deviceRequestedHours", Description = "How many hours shading is requested at the device", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "deviceDeployedHours", NickName = "deviceDeployedHours", Description = "How many hours the device is actually deployed", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "windRetractedHours", NickName = "windRetractedHours", Description = "How many requested hours the wind limit refused", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "canopyEffectiveHours", NickName = "canopyEffectiveHours", Description = "How many deployed hours the canopy is effective in", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "valanceEffectiveHours", NickName = "valanceEffectiveHours", Description = "How many deployed hours the valance is effective in", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "controlledUnwantedSolarIntercepted", NickName = "controlledUnwantedSolarIntercepted", Description = "Unwanted solar the device intercepted WHILE DEPLOYED [kWh] - what the control actually achieved", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "controlledDirectSolarIntercepted", NickName = "controlledDirectSolarIntercepted", Description = "Full-year direct solar the device geometry intercepts [kWh]. NOT weighted by the deployment schedule - use controlledUnwantedSolarIntercepted for the deployed-hour effect", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "canopyAttributedEnergy", NickName = "canopyAttributedEnergy", Description = "Full-year direct beam attributed to the CANOPY as the first element hit [kWh]. A geometric attribution metric - not limited to deployed hours", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "valanceAttributedEnergy", NickName = "valanceAttributedEnergy", Description = "Full-year direct beam attributed to the VALANCE as the first element hit [kWh]. A geometric attribution metric - not limited to deployed hours.\nATTRIBUTED energy in the current geometry - NOT a claim about the same device with the valance removed", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMObjectParam() { Name = "memberProfiles", NickName = "memberProfiles", Description = "The per-window diagnostics: each window's own deployed hours, effective hours and energies", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "description", NickName = "description", Description = "The whole answer in one readable line", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "successful", NickName = "successful", Description = "Successful?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                return result.ToArray();
            }
        }

        private static global::Grasshopper.Kernel.Parameters.Param_Number Number(string name, string description, double defaultValue)
        {
            global::Grasshopper.Kernel.Parameters.Param_Number result = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = name, NickName = name, Description = description, Access = GH_ParamAccess.item };
            result.SetPersistentData(defaultValue);
            return result;
        }

        private double Number(IGH_DataAccess dataAccess, string name, double defaultValue)
        {
            int index = Params.IndexOfInputParam(name);
            if (index == -1)
            {
                return defaultValue;
            }

            double value = defaultValue;
            if (dataAccess.GetData(index, ref value) && !double.IsNaN(value))
            {
                return value;
            }

            return defaultValue;
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            int index_Successful = Params.IndexOfOutputParam("successful");
            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, false);
            }

            int index = Params.IndexOfInputParam("_run");
            bool run = false;
            if (index == -1 || !dataAccess.GetData(index, ref run))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply a value for _run.");
                return;
            }

            if (!run)
            {
                return;
            }

            index = Params.IndexOfInputParam("_analyticalModel");
            AnalyticalModel analyticalModel = null;
            if (index == -1 || !dataAccess.GetData(index, ref analyticalModel) || analyticalModel == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply a valid SAM AnalyticalModel.");
                return;
            }

            index = Params.IndexOfInputParam("_apertureSolarTargets");
            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget>();
            if (index == -1 || !dataAccess.GetDataList(index, targets) || targets.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply at least one aperture solar target from SAMAnalytical.ApertureSolarTargets.");
                return;
            }

            index = Params.IndexOfInputParam("_controlProfiles");
            List<GH_ObjectWrapper> profileWrappers = new List<GH_ObjectWrapper>();
            if (index == -1 || !dataAccess.GetDataList(index, profileWrappers) || profileWrappers.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply one control profile per window, from SAMAnalytical.SolarControlProfile.");
                return;
            }

            List<SolarControlProfile> profiles = new List<SolarControlProfile>();
            foreach (GH_ObjectWrapper wrapper in profileWrappers)
            {
                SolarControlProfile profile = Query.Value<SolarControlProfile>(wrapper);
                if (profile == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_controlProfiles must be SAMAnalytical.SolarControlProfile objects. Its shadeOn hours ARE the deployment schedule this component measures.");
                    return;
                }

                profiles.Add(profile);
            }

            index = Params.IndexOfInputParam("_shadingDevice");
            GH_ObjectWrapper deviceWrapper = null;
            IShadingTypology typology = null;
            if (index == -1 || !dataAccess.GetData(index, ref deviceWrapper) || deviceWrapper?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please supply a shading device: a ShadingDevice, a GroupedShadingDevice from SAMAnalytical.RationaliseAwningGroup, or a shading typology such as RetractableAwning.");
                return;
            }

            ShadingDevice shadingDevice = Query.Value<ShadingDevice>(deviceWrapper);
            GroupedShadingDevice groupedShadingDevice = null;
            if (shadingDevice != null)
            {
                typology = shadingDevice.Typology;
            }
            else
            {
                groupedShadingDevice = Query.Value<GroupedShadingDevice>(deviceWrapper);
                if (groupedShadingDevice != null)
                {
                    typology = groupedShadingDevice.Typology;
                }
                else
                {
                    typology = Query.Value<IShadingTypology>(deviceWrapper);
                }
            }

            if (typology == null && groupedShadingDevice == null)
            {
                if (Query.Value<ShadingScheme>(deviceWrapper) != null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_shadingDevice carries a shading scheme. ShadingOperation measures ONE physical device and a scheme is a whole-scope proposal, not one device: verify a scheme with SAMAnalytical.VerifyShading instead, or wire one grouped device from SAMAnalytical.RationaliseAwningGroup.groupedShadingDevices.");
                    return;
                }

                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "_shadingDevice was not recognised. Supply a ShadingDevice, a GroupedShadingDevice from SAMAnalytical.RationaliseAwningGroup, or a shading typology such as RetractableAwning.");
                return;
            }

            double gridSize = Number(dataAccess, "_gridSize_", 0.5);
            double sunAngleStep = Number(dataAccess, "_sunAngleStep_", 2.0);

            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            if (weatherData == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "WeatherData is required. Attach hourly weather to the AnalyticalModel.");
                return;
            }

            int year = profiles[0].Year;

            List<Guid> apertureGuids = new List<Guid>();
            foreach (ApertureSolarTarget target in targets)
            {
                if (target != null && !apertureGuids.Contains(target.ApertureGuid))
                {
                    apertureGuids.Add(target.ApertureGuid);
                }
            }

            // A grouped device fixes its own member scope: the solar calculation runs over the
            // device's members, and the wired targets are validated against that scope by the
            // grouped path itself — never the other way round.
            List<Guid> contextScope = apertureGuids;
            if (groupedShadingDevice != null && groupedShadingDevice.ApertureGuids.Count != 0)
            {
                contextScope = groupedShadingDevice.ApertureGuids;
            }

            ApertureSolarContext context = SolarCreate.ApertureSolarContext(analyticalModel, year, weatherData, contextScope, gridSize, sunAngleStep);
            if (context == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The solar calculation could not be set up for this model: no analysable apertures, no weather, or an unresolvable site.");
                return;
            }

            // The schedule (the control profile) and the energy accounting (the visibility cache)
            // must sit on one timeline: the profile shift must match the cache's. Mismatches are
            // refused with the reason, never approximated.
            SolarControlProfile firstProfile = profiles[0];
            if (Math.Abs(firstProfile.TimeShiftInMinutes - context.TimeShiftInMinutes) > 1e-6)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The control profiles use a sun-position shift of {0} minutes but this model's solar calculation uses {1}. Rebuild the profiles and this component on the same timeline.",
                    firstProfile.TimeShiftInMinutes, context.TimeShiftInMinutes));
                return;
            }

            List<ShadingOperationProfile> memberProfiles = new List<ShadingOperationProfile>();
            GroupedShadingOperationProfile device = null;
            ShadingOperationProfile single = null;
            string description = null;

            if (groupedShadingDevice != null)
            {
                // The grouped device hands over its own member group: the wired targets are
                // scope-checked against the device's members (by Guid, any order), the group is
                // re-established and identity-checked against the device's GroupGuid, and the
                // measurement runs through the existing grouped operation. Nothing is re-guessed
                // from the wired targets.
                device = SolarCreate.GroupedShadingOperationProfile(
                    groupedShadingDevice, targets, profiles, context, out string groupedMessage);

                if (device == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, groupedMessage ?? "The grouped operation could not be measured.");
                    return;
                }

                memberProfiles = device.Members;
                description = device.ToString();
            }
            else if (apertureGuids.Count == 1)
            {
                ApertureSolarTarget target = context.Target(apertureGuids[0]);
                if (target == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The aperture could not be analysed: it is not one of the sun-exposed external apertures of this model.");
                    return;
                }

                SolarControlProfile singleProfile = profiles.FirstOrDefault(x => x.ApertureGuid == target.ApertureGuid);
                if (singleProfile == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The control profile does not belong to this window. Supply the profile built for THIS aperture, from SAMAnalytical.SolarControlProfile.");
                    return;
                }

                single = SolarCreate.ShadingOperationProfile(
                    target, singleProfile, context.SolarVisibilityCache, context.ContextOccluders,
                    typology, weatherData, context.CellIndexOffset(apertureGuids[0]));

                if (single == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The operation could not be measured: the control profile, the window and the solar calculation do not describe one timeline. Check that the profile was built for THIS window, this weather year and this sun-position shift.");
                    return;
                }

                memberProfiles.Add(single);
                description = single.ToString();
            }
            else
            {
                double extension = typology is RetractableAwning awning ? awning.GetParameter("ExtensionBeyondJambs") : 0.15;

                List<ApertureSolarTarget> members = new List<ApertureSolarTarget>();
                foreach (Guid guid in apertureGuids)
                {
                    ApertureSolarTarget member = context.Target(guid);
                    if (member == null)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "Aperture {0} is not one of the sun-exposed external apertures of this model.", guid));
                        return;
                    }

                    members.Add(member);
                }

                List<ApertureShadingGroup> groups = members.ApertureShadingGroups(AwningSpecification.Dakar, extension);
                if (groups == null || groups.Count != 1)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The selected windows do not form ONE shared device. A grouped shading operation needs one group: adjacent windows on one wall with aligned heads. Split the selection or run one window at a time.");
                    return;
                }

                ApertureShadingGroup group = groups[0];
                List<int> offsets = new List<int>();
                foreach (Guid guid in group.ApertureGuids)
                {
                    offsets.Add(context.CellIndexOffset(guid));
                }

                device = SolarCreate.GroupedShadingOperationProfile(
                    group, profiles, context.SolarVisibilityCache, context.ContextOccluders,
                    typology, weatherData, out string message, offsets);

                if (device == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message ?? "The grouped operation could not be measured.");
                    return;
                }

                memberProfiles = device.Members;
                description = device.ToString();
            }

            List<int> requested = device?.DeviceDemandHoursOfYear ?? Union(single.DeployedHoursOfYear, single.WindRetractedHoursOfYear);
            List<int> deployed = device?.DeviceDeployedHoursOfYear ?? single.DeployedHoursOfYear;
            List<int> windRetracted = device?.DeviceWindRetractedHoursOfYear ?? single.WindRetractedHoursOfYear;
            List<int> canopyEffective = device?.CanopyEffectiveHoursOfYear ?? single.CanopyEffectiveHoursOfYear;
            List<int> valanceEffective = device?.ValanceEffectiveHoursOfYear ?? single.ValanceEffectiveHoursOfYear;

            double shadeUseFraction = device != null ? device.DeviceShadeUseFraction : single.ShadeUseFraction;
            double controlledUnwanted = device != null ? device.ControlledUnwantedSolarIntercepted : single.ControlledUnwantedSolarIntercepted;
            double controlledDirect = device != null ? device.ControlledDirectSolarIntercepted : single.ControlledDirectSolarIntercepted;
            double canopyEnergy = device != null ? device.CanopyAttributedEnergy : single.CanopyAttributedEnergy;
            double valanceEnergy = device != null ? device.ValanceAttributedEnergy : single.ValanceAttributedEnergy;

            if (device != null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "One device spanning {0} windows. The headline hours are the union of the member schedules: the motor moved because ANY window asked. The wind speed is the weather file's own hourly value - this is an annual design profile, not a live safety controller and not a structural verification.",
                    device.Members.Count));
            }

            SetData(dataAccess, "operationProfile", new GooSAMObject(device != null ? (SAM.Core.IJSAMObject)device : single));
            SetDataList(dataAccess, "deviceRequestedHOYs", requested);
            SetDataList(dataAccess, "deviceDeployedHOYs", deployed);
            SetDataList(dataAccess, "windRetractedHOYs", windRetracted);
            SetData(dataAccess, "shadeUse", Query.Percentage(shadeUseFraction));
            SetDataList(dataAccess, "canopyEffectiveHOYs", canopyEffective);
            SetDataList(dataAccess, "valanceEffectiveHOYs", valanceEffective);
            SetData(dataAccess, "deviceRequestedHours", requested?.Count ?? 0);
            SetData(dataAccess, "deviceDeployedHours", deployed?.Count ?? 0);
            SetData(dataAccess, "windRetractedHours", windRetracted?.Count ?? 0);
            SetData(dataAccess, "canopyEffectiveHours", canopyEffective?.Count ?? 0);
            SetData(dataAccess, "valanceEffectiveHours", valanceEffective?.Count ?? 0);
            SetData(dataAccess, "controlledUnwantedSolarIntercepted", controlledUnwanted);
            SetData(dataAccess, "controlledDirectSolarIntercepted", controlledDirect);
            SetData(dataAccess, "canopyAttributedEnergy", canopyEnergy);
            SetData(dataAccess, "valanceAttributedEnergy", valanceEnergy);
            SetDataList(dataAccess, "memberProfiles", memberProfiles.ConvertAll(x => new GooSAMObject(x)));
            SetData(dataAccess, "description", description);

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, true);
            }
        }

        private static List<int> Union(IEnumerable<int> first, IEnumerable<int> second)
        {
            List<int> result = new List<int>(first ?? new List<int>());
            foreach (int value in second ?? new List<int>())
            {
                if (!result.Contains(value))
                {
                    result.Add(value);
                }
            }

            result.Sort();
            return result;
        }

        private void SetData(IGH_DataAccess dataAccess, string name, object value)
        {
            int index = Params.IndexOfOutputParam(name);
            if (index != -1)
            {
                dataAccess.SetData(index, value);
            }
        }

        private void SetDataList(IGH_DataAccess dataAccess, string name, System.Collections.IEnumerable values)
        {
            int index = Params.IndexOfOutputParam(name);
            if (index != -1)
            {
                dataAccess.SetDataList(index, values);
            }
        }
    }
}

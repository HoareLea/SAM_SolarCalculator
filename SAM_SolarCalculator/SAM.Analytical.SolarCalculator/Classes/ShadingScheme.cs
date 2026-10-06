// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// The complete physical shading proposal over ONE exact aperture set: zero or more
    /// per-aperture devices plus zero or more grouped devices, carrying identity but never
    /// geometry. Geometry is regenerated from the targets exactly as <see cref="ShadingDevice"/>
    /// and <see cref="GroupedShadingDevice"/> already do.
    ///
    /// WHY THIS TYPE EXISTS. <see cref="RationaliseShading"/> scores each window independently,
    /// so a per-window family "total" is a sum of separate optimisations, not one physical
    /// proposal; <see cref="RationaliseAwningGroup"/> scores a group correctly but on a different
    /// footing. A scheme is the object that makes "one proposal over the whole aperture set" a
    /// first-class thing a verification can measure, a comparison can rank and a report can name.
    ///
    /// IDENTITY RULES (unchanged from the single-device types, and enforced by design):
    ///   - <see cref="ShadingDevice"/> = one device, one real aperture. Unchanged.
    ///   - <see cref="GroupedShadingDevice"/> = one device, one deterministic aperture group. Unchanged.
    ///   - The two device kinds live in TWO separate typed lists: no abstract device base type and
    ///     no marker interface, so a grouped device can never pass as a per-window device, the
    ///     JSON stays typed, and the wrong-aperture refusal in VerifyShading keeps its meaning.
    ///
    /// ELEMENT IDENTITY (the scheme-scoped fix). <see cref="ShadingTypology"/> derives element
    /// Guids from family + parameters + ordinal only, so three identical overhangs on three
    /// windows are three physical objects with ONE Guid — which would silently merge three devices
    /// into one attribution row and under-count material by 3x. The scheme re-identifies every
    /// element at build time with <see cref="SchemeElementGuid"/>; the typology's own identity is
    /// untouched. See <see cref="SchemeElements"/>.
    /// </summary>
    public class ShadingScheme : IJSAMObject, ISolarObject
    {
        private string name;
        private string designMethod;
        private List<Guid> apertureGuids = new List<Guid>();
        private List<Guid> panelGuids = new List<Guid>();
        private List<ShadingDevice> devices = new List<ShadingDevice>();
        private List<GroupedShadingDevice> groupedDevices = new List<GroupedShadingDevice>();
        private List<ApertureShadingGroup> groups = new List<ApertureShadingGroup>();
        private ShadingDesignStatus status = ShadingDesignStatus.Undefined;
        private List<string> warnings = new List<string>();
        private ShadingObjective designObjective;
        private List<string> designDiagnostics = new List<string>();
        private double designTimeScore = double.NaN;
        private double designTimeBenefit = double.NaN;
        private double designTimeHarm = double.NaN;
        private double designTimeCost = double.NaN;
        private int designEvaluations;
        private ShadingOptimisationTermination designTermination = ShadingOptimisationTermination.Undefined;
        private Guid schemeGuid;

        public ShadingScheme(
            string name,
            string designMethod,
            IEnumerable<Guid> apertureGuids,
            IEnumerable<Guid> panelGuids,
            IEnumerable<ShadingDevice> devices,
            IEnumerable<GroupedShadingDevice> groupedDevices,
            IEnumerable<ApertureShadingGroup> groups,
            ShadingDesignStatus status,
            IEnumerable<string> warnings,
            ShadingObjective designObjective,
            IEnumerable<string> designDiagnostics)
        {
            this.name = name;
            this.designMethod = designMethod;
            this.apertureGuids = new List<Guid>(apertureGuids ?? new List<Guid>());
            this.panelGuids = new List<Guid>(panelGuids ?? new List<Guid>());
            this.devices = new List<ShadingDevice>(devices ?? new List<ShadingDevice>());
            this.groupedDevices = new List<GroupedShadingDevice>(groupedDevices ?? new List<GroupedShadingDevice>());
            this.groups = new List<ApertureShadingGroup>(groups ?? new List<ApertureShadingGroup>());
            Canonicalise();

            this.status = status;
            this.warnings = new List<string>(warnings ?? new List<string>());
            this.designObjective = designObjective == null ? null : new ShadingObjective(designObjective);
            this.designDiagnostics = new List<string>(designDiagnostics ?? new List<string>());
            schemeGuid = ComputeSchemeGuid();
        }

        public ShadingScheme(ShadingScheme shadingScheme)
        {
            if (shadingScheme != null)
            {
                name = shadingScheme.name;
                designMethod = shadingScheme.designMethod;
                apertureGuids = new List<Guid>(shadingScheme.apertureGuids);
                panelGuids = new List<Guid>(shadingScheme.panelGuids);
                devices = shadingScheme.devices.ConvertAll(x => x == null ? null : new ShadingDevice(x));
                groupedDevices = shadingScheme.groupedDevices.ConvertAll(x => x == null ? null : new GroupedShadingDevice(x));
                groups = shadingScheme.groups.ConvertAll(x => x == null ? null : new ApertureShadingGroup(x));
                status = shadingScheme.status;
                warnings = new List<string>(shadingScheme.warnings);
                designObjective = shadingScheme.designObjective == null ? null : new ShadingObjective(shadingScheme.designObjective);
                designDiagnostics = new List<string>(shadingScheme.designDiagnostics);
                designTimeScore = shadingScheme.designTimeScore;
                designTimeBenefit = shadingScheme.designTimeBenefit;
                designTimeHarm = shadingScheme.designTimeHarm;
                designTimeCost = shadingScheme.designTimeCost;
                designEvaluations = shadingScheme.designEvaluations;
                designTermination = shadingScheme.designTermination;
                schemeGuid = shadingScheme.schemeGuid;
            }
        }

        public ShadingScheme(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>Deterministic scheme identity — see <see cref="ComputeSchemeGuid"/>. Independent of input order and of any runtime data.</summary>
        public Guid SchemeGuid { get { return schemeGuid; } }

        /// <summary>The option name this scheme competes under, e.g. "Overhang", "Grouped Dakar Retractable Awning", "No Shade".</summary>
        public string Name { get { return name; } }

        /// <summary>Which workflow produced the geometry: "RationaliseShading" | "RationaliseAwningGroup" | "Baseline".</summary>
        public string DesignMethod { get { return designMethod; } }

        /// <summary>The aperture scope, sorted ordinal ascending. Every scheme in one comparison must cover the same set.</summary>
        public List<Guid> ApertureGuids { get { return new List<Guid>(apertureGuids); } }

        /// <summary>Host panel GUIDs, sorted ordinal ascending.</summary>
        public List<Guid> PanelGuids { get { return new List<Guid>(panelGuids); } }

        /// <summary>Per-aperture devices, sorted by ApertureGuid. A NoShading member means "build nothing here" and is a legitimate answer.</summary>
        public List<ShadingDevice> Devices { get { return devices.ConvertAll(x => x == null ? null : new ShadingDevice(x)); } }

        /// <summary>Grouped devices, sorted by GroupGuid. One device may span several apertures.</summary>
        public List<GroupedShadingDevice> GroupedDevices { get { return groupedDevices.ConvertAll(x => x == null ? null : new GroupedShadingDevice(x)); } }

        /// <summary>The deterministic aperture group of each grouped device, aligned index-for-index with <see cref="GroupedDevices"/>. Carried because a grouped device's geometry is rebuilt from its group frame.</summary>
        public List<ApertureShadingGroup> Groups { get { return groups.ConvertAll(x => x == null ? null : new ApertureShadingGroup(x)); } }

        /// <summary>Number of physical buildable units: non-null per-aperture devices plus non-null grouped devices.</summary>
        public int PhysicalDeviceCount
        {
            get
            {
                int count = 0;
                foreach (ShadingDevice device in devices)
                {
                    if (device != null && !device.IsNoShading)
                    {
                        count++;
                    }
                }

                foreach (GroupedShadingDevice device in groupedDevices)
                {
                    if (device != null && !device.IsNoShading)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Distinct built typology names, sorted, excluding "NoShading".</summary>
        public List<string> TypologyNames
        {
            get
            {
                List<string> result = new List<string>();
                foreach (ShadingDevice device in devices)
                {
                    string typologyName = device?.TypologyName;
                    if (typologyName != null && typologyName != "NoShading" && !result.Contains(typologyName))
                    {
                        result.Add(typologyName);
                    }
                }

                foreach (GroupedShadingDevice device in groupedDevices)
                {
                    string typologyName = device?.TypologyName;
                    if (typologyName != null && typologyName != "NoShading" && !result.Contains(typologyName))
                    {
                        result.Add(typologyName);
                    }
                }

                result.Sort(StringComparer.Ordinal);
                return result;
            }
        }

        /// <summary>The single product preset name across all grouped devices; null when there are none.</summary>
        public string ProductPreset
        {
            get
            {
                string result = null;
                foreach (GroupedShadingDevice device in groupedDevices)
                {
                    if (device == null || device.IsNoShading)
                    {
                        continue;
                    }

                    string specificationName = device.SpecificationName;
                    if (result == null)
                    {
                        result = specificationName;
                    }
                }

                return result;
            }
        }

        /// <summary>Ok | NoShading | Warning | NotEvaluated — the worst contribution across the scheme.</summary>
        public ShadingDesignStatus Status { get { return status; } }

        public List<string> Warnings { get { return new List<string>(warnings); } }

        /// <summary>The objective the geometry was optimised under; null for the Baseline method.</summary>
        public ShadingObjective DesignObjective { get { return designObjective == null ? null : new ShadingObjective(designObjective); } }

        /// <summary>Evaluations, termination and per-aperture notes from the design run.</summary>
        public List<string> DesignDiagnostics { get { return new List<string>(designDiagnostics); } }

        /// <summary>
        /// The objective value recorded while the geometry was being searched. Never used for
        /// ranking. For a conventional family this is the SUM of the per-window design scores — a
        /// sum of independent optimisations, which is exactly the blind spot whole-scheme
        /// verification exists to expose. NaN when not recorded (grouped and baseline schemes).
        /// </summary>
        public double DesignTimeScore { get { return designTimeScore; } internal set { designTimeScore = value; } }

        /// <summary>The per-window design benefit total (sum of the members' recorded benefits). NaN when not recorded.</summary>
        public double DesignTimeBenefit { get { return designTimeBenefit; } internal set { designTimeBenefit = value; } }

        /// <summary>The per-window design harm total. NaN when not recorded.</summary>
        public double DesignTimeHarm { get { return designTimeHarm; } internal set { designTimeHarm = value; } }

        /// <summary>The per-window design cost total. NaN when not recorded.</summary>
        public double DesignTimeCost { get { return designTimeCost; } internal set { designTimeCost = value; } }

        /// <summary>Distinct candidate geometries measured across the design run (summed over members/groups).</summary>
        public int DesignEvaluations { get { return designEvaluations; } internal set { designEvaluations = value; } }

        /// <summary>Why the design search stopped — the most consequential termination across the run.</summary>
        public ShadingOptimisationTermination DesignTermination { get { return designTermination; } internal set { designTermination = value; } }

        /// <summary>True when any member family exhausted its evaluation budget: the geometry is the best SEEN, not a converged one.</summary>
        public bool DesignBudgetExhausted { get { return designTermination == ShadingOptimisationTermination.EvaluationBudgetExhausted; } }

        /// <summary>True when this is the zero-device baseline scheme.</summary>
        public bool IsNoShade { get { return PhysicalDeviceCount == 0 && groupedDevices.Count == 0 && devices.Count == 0; } }

        // ------------------------------------------------------- scheme element identity ----

        /// <summary>
        /// Scheme-scoped physical identity: the typology element Guid re-keyed by the owning
        /// placement. placementKey = ApertureGuid for a ShadingDevice, GroupGuid for a
        /// GroupedShadingDevice. Deterministic, collision-free within a scheme, and stable across
        /// runs and JSON round-trips — the P1 fix. <see cref="ShadingTypology"/>'s own element
        /// Guid is never modified.
        /// </summary>
        public static Guid SchemeElementGuid(Guid placementKey, Guid typologyElementGuid)
        {
            string text = "SchemeElement|" + placementKey + "|" + typologyElementGuid;
            using (MD5 md5 = MD5.Create())
            {
                return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(text)));
            }
        }

        /// <summary>The aperture group a grouped device belongs to, or null.</summary>
        public ApertureShadingGroup Group(GroupedShadingDevice groupedShadingDevice)
        {
            if (groupedShadingDevice == null)
            {
                return null;
            }

            for (int i = 0; i < groupedDevices.Count; i++)
            {
                if (groupedDevices[i] != null && groupedDevices[i].GroupGuid == groupedShadingDevice.GroupGuid && i < groups.Count)
                {
                    return groups[i] == null ? null : new ApertureShadingGroup(groups[i]);
                }
            }

            return null;
        }

        /// <summary>
        /// Every element of every placement, in placement order then element order, re-identified
        /// with scheme-scoped GUIDs. This is the ONE place scheme geometry is built. A grouped
        /// device contributes its shared canopy once because it has one placement key, which is
        /// what makes the material accounting count each physical element exactly once.
        /// </summary>
        public List<ShadingElement> SchemeElements(IEnumerable<ApertureSolarTarget> targets)
        {
            List<ShadingElement> result = new List<ShadingElement>();

            foreach (ShadingDevice device in devices)
            {
                ApertureSolarTarget target = Find(targets, device.ApertureGuid);
                if (target == null || device.Typology == null)
                {
                    continue;
                }

                List<ShadingElement> elements = device.Typology.ShadingElements(target);
                AppendReidentified(result, elements, device.ApertureGuid);
            }

            for (int i = 0; i < groupedDevices.Count; i++)
            {
                GroupedShadingDevice device = groupedDevices[i];
                if (device == null)
                {
                    continue;
                }

                ApertureShadingGroup group = i < groups.Count ? groups[i] : null;
                List<ShadingElement> elements = device.ShadingElements(group);
                AppendReidentified(result, elements, device.GroupGuid);
            }

            return result;
        }

        /// <summary>elementGuid → the placement key that owns it. Backs per-device attribution in the report.</summary>
        public Dictionary<Guid, Guid> ElementOwners(IEnumerable<ApertureSolarTarget> targets)
        {
            Dictionary<Guid, Guid> result = new Dictionary<Guid, Guid>();

            foreach (ShadingDevice device in devices)
            {
                ApertureSolarTarget target = Find(targets, device.ApertureGuid);
                if (target == null || device.Typology == null)
                {
                    continue;
                }

                foreach (ShadingElement element in device.Typology.ShadingElements(target) ?? new List<ShadingElement>())
                {
                    if (element != null)
                    {
                        result[SchemeElementGuid(device.ApertureGuid, element.Guid)] = device.ApertureGuid;
                    }
                }
            }

            for (int i = 0; i < groupedDevices.Count; i++)
            {
                GroupedShadingDevice device = groupedDevices[i];
                if (device == null)
                {
                    continue;
                }

                ApertureShadingGroup group = i < groups.Count ? groups[i] : null;
                foreach (ShadingElement element in device.ShadingElements(group) ?? new List<ShadingElement>())
                {
                    if (element != null)
                    {
                        result[SchemeElementGuid(device.GroupGuid, element.Guid)] = device.GroupGuid;
                    }
                }
            }

            return result;
        }

        /// <summary>placementKey → human label, e.g. "Overhang @ 344d86d7", "Grouped Dakar Retractable Awning (3 apertures)".</summary>
        public Dictionary<Guid, string> PlacementLabels
        {
            get
            {
                Dictionary<Guid, string> result = new Dictionary<Guid, string>();
                foreach (ShadingDevice device in devices)
                {
                    result[device.ApertureGuid] = device.TypologyName + " @ " + device.ApertureGuid;
                }

                foreach (GroupedShadingDevice device in groupedDevices)
                {
                    int count = device.ApertureGuids.Count;
                    string specificationName = device.SpecificationName;
                    string label = "Grouped "
                        + (specificationName == null ? string.Empty : specificationName + " ")
                        + DisplayName(device.TypologyName)
                        + " (" + count.ToString(CultureInfo.InvariantCulture) + " apertures)";
                    result[device.GroupGuid] = label;
                }

                return result;
            }
        }

        /// <summary>"RetractableAwning" → "Retractable Awning", "EggCrate" → "Egg Crate"; other names pass through.</summary>
        public static string DisplayName(string typologyName)
        {
            switch (typologyName)
            {
                case "RetractableAwning": return "Retractable Awning";
                case "EggCrate": return "Egg Crate";
                case "HorizontalLouvres": return "Horizontal Louvres";
                case "VerticalFins": return "Vertical Fins";
                default: return typologyName;
            }
        }

        private static void AppendReidentified(List<ShadingElement> result, List<ShadingElement> elements, Guid placementKey)
        {
            foreach (ShadingElement element in elements ?? new List<ShadingElement>())
            {
                if (element == null)
                {
                    continue;
                }

                result.Add(new ShadingElement(SchemeElementGuid(placementKey, element.Guid), element.Name, element.Face3D));
            }
        }

        private static ApertureSolarTarget Find(IEnumerable<ApertureSolarTarget> targets, Guid apertureGuid)
        {
            if (targets == null)
            {
                return null;
            }

            foreach (ApertureSolarTarget target in targets)
            {
                if (target != null && target.ApertureGuid == apertureGuid)
                {
                    return target;
                }
            }

            return null;
        }

        private static List<Guid> Sorted(IEnumerable<Guid> guids)
        {
            List<Guid> result = new List<Guid>(guids ?? new List<Guid>());
            result.RemoveAll(x => x == Guid.Empty);
            result.Sort();
            return result;
        }

        /// <summary>
        /// Restores every field to the inert state of a freshly constructed, unusable scheme. Called
        /// before a JSON reconstruction (so a re-read can never inherit stale state) and again on
        /// every reconstruction failure (so a failed read can never leave partial or stale identity
        /// behind). A scheme in this state has no name, no devices, no scope and an empty
        /// <see cref="SchemeGuid"/>, so it can never masquerade as a valid comparison option.
        /// </summary>
        private void Reset()
        {
            name = null;
            designMethod = null;
            apertureGuids = new List<Guid>();
            panelGuids = new List<Guid>();
            devices = new List<ShadingDevice>();
            groupedDevices = new List<GroupedShadingDevice>();
            groups = new List<ApertureShadingGroup>();
            status = ShadingDesignStatus.Undefined;
            warnings = new List<string>();
            designObjective = null;
            designDiagnostics = new List<string>();
            designTimeScore = double.NaN;
            designTimeBenefit = double.NaN;
            designTimeHarm = double.NaN;
            designTimeCost = double.NaN;
            designEvaluations = 0;
            designTermination = ShadingOptimisationTermination.Undefined;
            schemeGuid = Guid.Empty;
        }

        /// <summary>
        /// The ONE canonical ordering a scheme ever carries, applied by both the constructor and the
        /// JSON reader so two representations of the same logical scheme reconstruct the same
        /// <see cref="SchemeGuid"/> whatever their input order was:
        ///   - aperture and panel scopes sorted ordinal ascending;
        ///   - per-aperture devices deep-copied and sorted by <see cref="ShadingDevice.ApertureGuid"/>;
        ///   - grouped devices deep-copied and sorted by <see cref="GroupedShadingDevice.GroupGuid"/>;
        ///   - each group paired to the grouped device carrying the SAME GroupGuid, the PAIR sorted,
        ///     so later index-based pairing (SchemeElements, ElementOwners) can never rebuild a device
        ///     against another device's frame. A group whose Guid matches no device is dropped.
        /// Null devices are skipped, never carried into a collection that assumes a valid device.
        /// </summary>
        private void Canonicalise()
        {
            apertureGuids = Sorted(apertureGuids);
            panelGuids = Sorted(panelGuids);

            List<ShadingDevice> canonicalDevices = new List<ShadingDevice>();
            foreach (ShadingDevice device in devices)
            {
                if (device != null)
                {
                    canonicalDevices.Add(new ShadingDevice(device));
                }
            }
            canonicalDevices.Sort((a, b) => a.ApertureGuid.CompareTo(b.ApertureGuid));
            devices = canonicalDevices;

            List<GroupedShadingDevice> canonicalGrouped = new List<GroupedShadingDevice>();
            foreach (GroupedShadingDevice device in groupedDevices)
            {
                if (device != null)
                {
                    canonicalGrouped.Add(new GroupedShadingDevice(device));
                }
            }
            canonicalGrouped.Sort((a, b) => a.GroupGuid.CompareTo(b.GroupGuid));

            List<Tuple<GroupedShadingDevice, ApertureShadingGroup>> pairs = new List<Tuple<GroupedShadingDevice, ApertureShadingGroup>>();
            foreach (GroupedShadingDevice device in canonicalGrouped)
            {
                pairs.Add(new Tuple<GroupedShadingDevice, ApertureShadingGroup>(device, null));
            }

            foreach (ApertureShadingGroup group in groups)
            {
                if (group == null)
                {
                    continue;
                }

                int index = pairs.FindIndex(x => x.Item1 != null && x.Item1.GroupGuid == group.GroupGuid);
                if (index != -1)
                {
                    pairs[index] = new Tuple<GroupedShadingDevice, ApertureShadingGroup>(pairs[index].Item1, new ApertureShadingGroup(group));
                }
            }

            pairs.Sort((a, b) => a.Item1.GroupGuid.CompareTo(b.Item1.GroupGuid));

            List<GroupedShadingDevice> finalGrouped = new List<GroupedShadingDevice>();
            List<ApertureShadingGroup> finalGroups = new List<ApertureShadingGroup>();
            foreach (Tuple<GroupedShadingDevice, ApertureShadingGroup> pair in pairs)
            {
                if (pair.Item1 != null)
                {
                    finalGrouped.Add(pair.Item1);
                    finalGroups.Add(pair.Item2);
                }
            }

            groupedDevices = finalGrouped;
            groups = finalGroups;
        }

        // --------------------------------------------------------------- identity ----

        /// <summary>
        /// SchemeGuid = MD5 over the name, design method, the sorted aperture scope, and every
        /// placement in fixed order (Devices by ApertureGuid, then GroupedDevices by GroupGuid)
        /// with its typology name and parameters iterated in the typology's own ParameterNames
        /// order. Contains no runtime data: two runs of the same design produce the same Guid.
        /// </summary>
        private Guid ComputeSchemeGuid()
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("ShadingScheme|");
            stringBuilder.Append(name);
            stringBuilder.Append('|');
            stringBuilder.Append(designMethod);
            stringBuilder.Append("|Apertures:");
            stringBuilder.Append(string.Join(",", apertureGuids));

            stringBuilder.Append("|Devices:");
            bool first = true;
            foreach (ShadingDevice device in devices)
            {
                if (!first)
                {
                    stringBuilder.Append(';');
                }
                first = false;

                AppendPlacement(stringBuilder, device.ApertureGuid, device.Typology, null, null);
            }

            foreach (GroupedShadingDevice device in groupedDevices)
            {
                if (!first)
                {
                    stringBuilder.Append(';');
                }
                first = false;

                List<Guid> members = new List<Guid>(device.ApertureGuids);
                members.Sort();
                AppendPlacement(stringBuilder, device.GroupGuid, device.Typology, device.SpecificationName, members);
            }

            using (MD5 md5 = MD5.Create())
            {
                return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(stringBuilder.ToString())));
            }
        }

        private static void AppendPlacement(StringBuilder stringBuilder, Guid placementKey, IShadingTypology typology, string specificationName, List<Guid> memberApertureGuids)
        {
            stringBuilder.Append(placementKey);
            stringBuilder.Append(':');
            stringBuilder.Append(typology?.Name);

            if (typology != null)
            {
                stringBuilder.Append('(');
                bool firstParameter = true;
                // ParameterNames, never a dictionary's enumeration order, so the identity is
                // byte-stable between runs (the OptimisedShadingResult rule).
                foreach (string parameterName in typology.ParameterNames ?? new List<string>())
                {
                    if (!firstParameter)
                    {
                        stringBuilder.Append(',');
                    }
                    firstParameter = false;

                    stringBuilder.Append(parameterName);
                    stringBuilder.Append('=');
                    stringBuilder.Append(typology.GetParameter(parameterName).ToString("R", CultureInfo.InvariantCulture));
                }

                stringBuilder.Append(')');
            }

            if (specificationName != null)
            {
                stringBuilder.Append(':');
                stringBuilder.Append(specificationName);
            }

            if (memberApertureGuids != null)
            {
                stringBuilder.Append(":apertures=");
                stringBuilder.Append(string.Join(",", memberApertureGuids));
            }
        }

        // --------------------------------------------------------------- JSON ----

        public bool FromJsonObject(JsonObject jObject)
        {
            // Start from the inert state so a re-read of a live object can never inherit the old
            // identity, scope or devices behind a later failure.
            Reset();

            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("Name")) { name = jObject["Name"]?.GetValue<string>(); }
            if (jObject.ContainsKey("DesignMethod")) { designMethod = jObject["DesignMethod"]?.GetValue<string>(); }

            apertureGuids = ReadGuids(jObject, "ApertureGuids");
            panelGuids = ReadGuids(jObject, "PanelGuids");

            devices = new List<ShadingDevice>();
            if (jObject.ContainsKey("Devices") && jObject["Devices"] is JsonArray devicesArray)
            {
                foreach (JsonNode node in devicesArray)
                {
                    if (!(node is JsonObject deviceObject))
                    {
                        Reset();
                        return false;
                    }

                    ShadingDevice device = Core.Create.IJSAMObject<ShadingDevice>(deviceObject);
                    if (device == null)
                    {
                        Reset();
                        return false;
                    }

                    devices.Add(device);
                }
            }

            groupedDevices = new List<GroupedShadingDevice>();
            if (jObject.ContainsKey("GroupedDevices") && jObject["GroupedDevices"] is JsonArray groupedArray)
            {
                foreach (JsonNode node in groupedArray)
                {
                    if (!(node is JsonObject deviceObject))
                    {
                        Reset();
                        return false;
                    }

                    GroupedShadingDevice device = Core.Create.IJSAMObject<GroupedShadingDevice>(deviceObject);
                    if (device == null)
                    {
                        Reset();
                        return false;
                    }

                    groupedDevices.Add(device);
                }
            }

            groups = new List<ApertureShadingGroup>();
            if (jObject.ContainsKey("Groups") && jObject["Groups"] is JsonArray groupsArray)
            {
                foreach (JsonNode node in groupsArray)
                {
                    // A null group is a legitimate aligned entry: a grouped device without a group
                    // travels with a null group, index-aligned with GroupedDevices.
                    if (node == null || (node is JsonValue jsonValue && jsonValue.GetValueKind() == System.Text.Json.JsonValueKind.Null))
                    {
                        groups.Add(null);
                        continue;
                    }

                    if (!(node is JsonObject groupObject))
                    {
                        Reset();
                        return false;
                    }

                    ApertureShadingGroup group = Core.Create.IJSAMObject<ApertureShadingGroup>(groupObject);
                    if (group == null)
                    {
                        Reset();
                        return false;
                    }

                    groups.Add(group);
                }
            }

            if (jObject.ContainsKey("Status")) { Enum.TryParse(jObject["Status"]?.GetValue<string>(), out status); }

            warnings = new List<string>();
            if (jObject.ContainsKey("Warnings") && jObject["Warnings"] is JsonArray warningsArray)
            {
                foreach (JsonNode node in warningsArray)
                {
                    warnings.Add(node?.GetValue<string>());
                }
            }

            designObjective = jObject.ContainsKey("DesignObjective") ? new ShadingObjective(jObject["DesignObjective"] as JsonObject) : null;

            designDiagnostics = new List<string>();
            if (jObject.ContainsKey("DesignDiagnostics") && jObject["DesignDiagnostics"] is JsonArray diagnosticsArray)
            {
                foreach (JsonNode node in diagnosticsArray)
                {
                    designDiagnostics.Add(node?.GetValue<string>());
                }
            }

            if (jObject.ContainsKey("DesignTimeScore")) { designTimeScore = jObject["DesignTimeScore"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("DesignTimeBenefit")) { designTimeBenefit = jObject["DesignTimeBenefit"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("DesignTimeHarm")) { designTimeHarm = jObject["DesignTimeHarm"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("DesignTimeCost")) { designTimeCost = jObject["DesignTimeCost"]?.GetValue<double>() ?? double.NaN; }
            if (jObject.ContainsKey("DesignEvaluations")) { designEvaluations = jObject["DesignEvaluations"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("DesignTermination")) { Enum.TryParse(jObject["DesignTermination"]?.GetValue<string>(), out designTermination); }

            // The stored Guid is carried for reference, but the identity is always RE-DERIVED so a
            // tampered or re-imported object cannot disagree with its own contents. The lists are
            // canonicalised to the SAME ordering the constructor enforces, so two documents with the
            // same logical scheme but different array orders reconstruct the same identity.
            Canonicalise();
            schemeGuid = ComputeSchemeGuid();
            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            if (name != null) { jObject.Add("Name", name); }
            if (designMethod != null) { jObject.Add("DesignMethod", designMethod); }

            AddGuids(jObject, "ApertureGuids", apertureGuids);
            AddGuids(jObject, "PanelGuids", panelGuids);

            JsonArray devicesArray = new JsonArray();
            foreach (ShadingDevice device in devices)
            {
                devicesArray.Add(device?.ToJsonObject());
            }
            jObject.Add("Devices", devicesArray);

            JsonArray groupedArray = new JsonArray();
            foreach (GroupedShadingDevice device in groupedDevices)
            {
                groupedArray.Add(device?.ToJsonObject());
            }
            jObject.Add("GroupedDevices", groupedArray);

            JsonArray groupsArray = new JsonArray();
            foreach (ApertureShadingGroup group in groups)
            {
                groupsArray.Add(group?.ToJsonObject());
            }
            jObject.Add("Groups", groupsArray);

            jObject.Add("Status", status.ToString());

            JsonArray warningsArray = new JsonArray();
            foreach (string warning in warnings)
            {
                warningsArray.Add(warning);
            }
            jObject.Add("Warnings", warningsArray);

            if (designObjective != null)
            {
                jObject.Add("DesignObjective", designObjective.ToJsonObject());
            }

            JsonArray diagnosticsArray = new JsonArray();
            foreach (string diagnostic in designDiagnostics)
            {
                diagnosticsArray.Add(diagnostic);
            }
            jObject.Add("DesignDiagnostics", diagnosticsArray);

            AddFinite(jObject, "DesignTimeScore", designTimeScore);
            AddFinite(jObject, "DesignTimeBenefit", designTimeBenefit);
            AddFinite(jObject, "DesignTimeHarm", designTimeHarm);
            AddFinite(jObject, "DesignTimeCost", designTimeCost);
            jObject.Add("DesignEvaluations", designEvaluations);            jObject.Add("DesignTermination", designTermination.ToString());

            return jObject;
        }

        private static List<Guid> ReadGuids(JsonObject jObject, string name)
        {
            List<Guid> result = new List<Guid>();
            if (jObject.ContainsKey(name) && jObject[name] is JsonArray jArray)
            {
                foreach (JsonNode node in jArray)
                {
                    if (Guid.TryParse(node?.GetValue<string>(), out Guid guid))
                    {
                        result.Add(guid);
                    }
                }
            }

            return result;
        }

        private static void AddGuids(JsonObject jObject, string name, List<Guid> guids)
        {
            JsonArray jArray = new JsonArray();
            foreach (Guid guid in guids)
            {
                jArray.Add(guid.ToString());
            }
            jObject.Add(name, jArray);
        }

        /// <summary>Non-finite doubles are OMITTED from the JSON (absent = NaN on read), so the serialised form is always writable and byte-stable.</summary>
        private static void AddFinite(JsonObject jObject, string name, double value)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value))
            {
                jObject.Add(name, value);
            }
        }
    }
}


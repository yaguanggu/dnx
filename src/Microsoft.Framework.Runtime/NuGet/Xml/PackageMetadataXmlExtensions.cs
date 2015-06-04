﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace NuGet.Xml
{
    public static class PackageMetadataXmlExtensions
    {
        private const string References = "references";
        private const string Reference = "reference";
        private const string Group = "group";
        private const string File = "file";
        private const string TargetFramework = "targetFramework";
        private const string FrameworkAssemblies = "frameworkAssemblies";
        private const string FrameworkAssembly = "frameworkAssembly";
        private const string AssemblyName = "assemblyName";
        private const string Dependencies = "dependencies";

        public static XElement ToXElement(this ManifestMetadata metadata, XNamespace ns)
        {
            var elem = new XElement(ns + "metadata",
                new XAttribute("minClientVersion", metadata.MinClientVersionString));

            elem.Add(new XElement(ns + "id", metadata.Id));
            elem.Add(new XElement(ns + "version", metadata.Version.OriginalString));
            AddElementIfNotNull(elem, ns, "title", metadata.Title);
            elem.Add(new XElement(ns + "requireLicenseAcceptance", metadata.RequireLicenseAcceptance));
            AddElementIfNotNull(elem, ns, "authors", metadata.Authors, authors => string.Join(",", authors));
            AddElementIfNotNull(elem, ns, "owners", metadata.Owners, owners => string.Join(",", owners));
            AddElementIfNotNull(elem, ns, "licenseUrl", metadata.LicenseUrl);
            AddElementIfNotNull(elem, ns, "projectUrl", metadata.ProjectUrl);
            AddElementIfNotNull(elem, ns, "iconUrl", metadata.IconUrl);
            AddElementIfNotNull(elem, ns, "description", metadata.Description);
            AddElementIfNotNull(elem, ns, "summary", metadata.Summary);
            AddElementIfNotNull(elem, ns, "releaseNotes", metadata.ReleaseNotes);
            AddElementIfNotNull(elem, ns, "copyright", metadata.Copyright);
            AddElementIfNotNull(elem, ns, "language", metadata.Language);
            AddElementIfNotNull(elem, ns, "tags", metadata.Tags);

            elem.Add(GetXElementFromGroupableItemSets(
                ns,
                metadata.DependencySets,
                set => set.TargetFramework != null,
                set => VersionUtility.GetFrameworkString(set.TargetFramework),
                set => set.Dependencies,
                GetXElementFromPackageDependency,
                Dependencies,
                TargetFramework));

            elem.Add(GetXElementFromGroupableItemSets(
                ns,
                metadata.PackageAssemblyReferences,
                set => set.TargetFramework != null,
                set => VersionUtility.GetFrameworkString(set.TargetFramework),
                set => set.References,
                GetXElementFromPackageReference,
                References,
                TargetFramework));

            elem.Add(GetXElementFromFrameworkAssemblies(ns, metadata.FrameworkAssemblies));

            return elem;
        }

        private static XElement GetXElementFromGroupableItemSets<TSet, TItem>(
            XNamespace ns,
            IEnumerable<TSet> objectSets,
            Func<TSet, bool> isGroupable,
            Func<TSet, string> getGroupIdentifer,
            Func<TSet, IEnumerable<TItem>> getItems,
            Func<XNamespace, TItem, XElement> getXElementFromItem,
            string parentName,
            string identiferAttributeName)
        {
            if (objectSets == null || objectSets.IsEmpty())
            {
                return null;
            }

            var groupableSets = new List<TSet>();
            var ungroupableSets = new List<TSet>();

            foreach (var set in objectSets)
            {
                if (isGroupable(set))
                {
                    groupableSets.Add(set);
                }
                else
                {
                    ungroupableSets.Add(set);
                }
            }

            var childElements = new List<XElement>();
            if (groupableSets.IsEmpty())
            {
                // none of the item sets are groupable, then flatten the items
                childElements.AddRange(objectSets.SelectMany(getItems).Select(item => getXElementFromItem(ns, item)));
            }
            else
            {
                // move the group with null target framework (if any) to the front just for nicer display in UI
                foreach (var set in ungroupableSets.Concat(groupableSets))
                {
                    var groupElem = new XElement(
                        ns + Group,
                        getItems(set).Select(item => getXElementFromItem(ns, item)).ToArray());

                    if (isGroupable(set))
                    {
                        groupElem.SetAttributeValue(identiferAttributeName, getGroupIdentifer(set));
                    }

                    childElements.Add(groupElem);
                }
            }

            return new XElement(ns + parentName, childElements.ToArray());
        }

        private static XElement GetXElementFromPackageReference(XNamespace ns, string reference)
        {
            return new XElement(ns + Reference, new XAttribute(File, reference));
        }

        private static XElement GetXElementFromPackageDependency(XNamespace ns, PackageDependency dependency)
        {
            return new XElement(ns + "dependency",
                new XAttribute("id", dependency.Id),
                dependency.VersionSpec != null ? new XAttribute("version", dependency.VersionSpec.ToString()) : null);
        }

        private static XElement GetXElementFromFrameworkAssemblies(XNamespace ns, IEnumerable<FrameworkAssemblyReference> references)
        {
            if (references == null || references.IsEmpty())
            {
                return null;
            }

            return new XElement(
                ns + FrameworkAssemblies,
                references.Select(reference =>
                    new XElement(ns + FrameworkAssembly,
                        new XAttribute(AssemblyName, reference.AssemblyName),
                        reference.SupportedFrameworks != null && reference.SupportedFrameworks.Any() ?
                            new XAttribute("targetFramework", string.Join(", ", reference.SupportedFrameworks.Select(VersionUtility.GetFrameworkString))) :
                            null)));
        }

        private static void AddElementIfNotNull<T>(XElement parent, XNamespace ns, string name, T value)
            where T : class
        {
            if (value != null)
            {
                parent.Add(new XElement(ns + name, value));
            }
        }

        private static void AddElementIfNotNull<T>(XElement parent, XNamespace ns, string name, T value, Func<T, object> process)
            where T : class
        {
            if (value != null)
            {
                var processed = process(value);
                if (processed != null)
                {
                    parent.Add(new XElement(ns + name, processed));
                }
            }
        }
    }
}

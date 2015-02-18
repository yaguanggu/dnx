// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.Framework.Runtime
{
    // TODO: Figure out what project object model we want to expose then expose that
    // and move this to the Interfaces assembly
    public interface IProjectReferenceProvider
    {
        IMetadataProjectReference GetProjectReference(
            Project project,
            ILibraryKey target,
            IProjectDependencyProvider dependencyProvider);
    }
}
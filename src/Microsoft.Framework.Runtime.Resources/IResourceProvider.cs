// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Framework.Runtime.Compilation;

namespace Microsoft.Framework.Runtime.Resources
{
    public interface IResourceProvider
    { 
        IList<ResourceDescriptor> GetResources(ICompilationProject project);
    }
}

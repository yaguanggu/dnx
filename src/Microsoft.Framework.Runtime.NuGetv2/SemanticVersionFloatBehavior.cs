using System;

namespace NuGet
{
    public enum SemanticVersionFloatBehavior
    {
        None,
        Prerelease,
        Revision,
        Build,
        Minor,
        Major
    }

}
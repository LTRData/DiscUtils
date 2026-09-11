using System;

namespace DiscUtils.MountDokan;

[Flags]
public enum DokanDiscUtilsOptions
{
    None = 0x00,
    ForceReadOnly = 0x01,
    AccessCheck = 0x02,
    HiddenAsNormal = 0x04,
    LeaveFSOpen = 0x08,
    BlockExecute = 0x10
}


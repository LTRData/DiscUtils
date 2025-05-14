using System;

namespace DiscUtils.MountFuse;

[Flags]
public enum FuseDiscUtilsOptions
{
    None = 0x00,
    AccessCheck = 0x01,
    LeaveFSOpen = 0x02,
    BlockExecute = 0x04
}


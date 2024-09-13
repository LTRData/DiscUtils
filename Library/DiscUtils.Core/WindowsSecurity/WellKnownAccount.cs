using System;

namespace DiscUtils.Core.WindowsSecurity;

internal class WellKnownAccount
{
    public WellKnownSidType WellKnownValue { get; private set; }
    public bool IsAbsolute { get; private set; }
    public SecurityIdentifier Sid { get; private set; }
    public string SidStr
    {
        get => sidStr;
        private set
        {
            sidStr = value;
            Sid = new(sidStr);
        }
    }
    public uint Rid { get; private set; }
    public string RidStr
    {
        get => ridStr;
        private set
        {
            ridStr = value;
            Rid = uint.Parse(ridStr);
        }
    }
    public string Name { get; private set; }
    public string SddlForm { get; private set; }

    public static WellKnownAccount LookupByType(WellKnownSidType sidType)
    {
        foreach (var acct in accounts)
        {
            if (acct.WellKnownValue == sidType)
            {
                return acct;
            }
        }

        return null;
    }

    public static WellKnownAccount LookupBySid(string s)
    {
        foreach (var acct in accounts)
        {
            if (acct.SidStr == s)
            {
                return acct;
            }
        }

        return null;
    }

    public static WellKnownAccount LookupByName(string s)
    {
        foreach (var acct in accounts)
        {
            if (acct.Name == s)
            {
                return acct;
            }
        }

        return null;
    }

    public static WellKnownAccount LookupBySddlForm(string s)
    {
        foreach (var acct in accounts)
        {
            if (acct.SddlForm == s)
            {
                return acct;
            }
        }

        return null;
    }

    public static WellKnownAccount LookupBySddlForm(ReadOnlySpan<char> s)
    {
        foreach (var acct in accounts)
        {
            if (acct.SddlForm.AsSpan().Equals(s, StringComparison.Ordinal))
            {
                return acct;
            }
        }

        return null;
    }

    private static readonly WellKnownAccount[] accounts =
    [
        new() { WellKnownValue = WellKnownSidType.NullSid, IsAbsolute = true, SidStr = "S-1-0-0", Name = @"NULL SID" },
        new() { WellKnownValue = WellKnownSidType.WorldSid, IsAbsolute = true, SidStr = "S-1-1-0", Name = @"Everyone", SddlForm = "WD" },
        new() { WellKnownValue = WellKnownSidType.LocalSid, IsAbsolute = true, SidStr = "S-1-2-0", Name = @"LOCAL" },
        new() { WellKnownValue = WellKnownSidType.CreatorOwnerSid, IsAbsolute = true, SidStr = "S-1-3-0", Name = @"CREATOR OWNER", SddlForm = "CO" },
        new() { WellKnownValue = WellKnownSidType.CreatorGroupSid, IsAbsolute = true, SidStr = "S-1-3-1", Name = @"CREATOR GROUP", SddlForm = "CG" },
        new() { WellKnownValue = WellKnownSidType.CreatorOwnerServerSid, IsAbsolute = true, SidStr = "S-1-3-2", Name = @"CREATOR OWNER SERVER" },
        new() { WellKnownValue = WellKnownSidType.CreatorGroupServerSid, IsAbsolute = true, SidStr = "S-1-3-3", Name = @"CREATOR GROUP SERVER" },
        new() { WellKnownValue = WellKnownSidType.NTAuthoritySid, IsAbsolute = true, SidStr = "S-1-5", Name = null },
        new() { WellKnownValue = WellKnownSidType.DialupSid, IsAbsolute = true, SidStr = "S-1-5-1", Name = @"NT AUTHORITY\DIALUP" },
        new() { WellKnownValue = WellKnownSidType.NetworkSid, IsAbsolute = true, SidStr = "S-1-5-2", Name = @"NT AUTHORITY\NETWORK", SddlForm = "NU" },
        new() { WellKnownValue = WellKnownSidType.BatchSid, IsAbsolute = true, SidStr = "S-1-5-3", Name = @"NT AUTHORITY\BATCH" },
        new() { WellKnownValue = WellKnownSidType.InteractiveSid, IsAbsolute = true, SidStr = "S-1-5-4", Name = @"NT AUTHORITY\INTERACTIVE", SddlForm = "IU" },
        new() { WellKnownValue = WellKnownSidType.ServiceSid, IsAbsolute = true, SidStr = "S-1-5-6", Name = @"NT AUTHORITY\SERVICE", SddlForm = "SU" },
        new() { WellKnownValue = WellKnownSidType.AnonymousSid, IsAbsolute = true, SidStr = "S-1-5-7", Name = @"NT AUTHORITY\ANONYMOUS LOGON", SddlForm = "AN" },
        new() { WellKnownValue = WellKnownSidType.ProxySid, IsAbsolute = true, SidStr = "S-1-5-8", Name = @"NT AUTHORITY\PROXY" },
        new() { WellKnownValue = WellKnownSidType.EnterpriseControllersSid, IsAbsolute = true, SidStr = "S-1-5-9", Name = @"NT AUTHORITY\ENTERPRISE DOMAIN CONTROLLERS", SddlForm = "ED" },
        new() { WellKnownValue = WellKnownSidType.SelfSid, IsAbsolute = true, SidStr = "S-1-5-10", Name = @"NT AUTHORITY\SELF", SddlForm = "PS" },
        new() { WellKnownValue = WellKnownSidType.AuthenticatedUserSid, IsAbsolute = true, SidStr = "S-1-5-11", Name = @"NT AUTHORITY\Authenticated Users", SddlForm = "AU" },
        new() { WellKnownValue = WellKnownSidType.RestrictedCodeSid, IsAbsolute = true, SidStr = "S-1-5-12", Name = @"NT AUTHORITY\RESTRICTED", SddlForm = "RC" },
        new() { WellKnownValue = WellKnownSidType.TerminalServerSid, IsAbsolute = true, SidStr = "S-1-5-13", Name = @"NT AUTHORITY\TERMINAL SERVER USER" },
        new() { WellKnownValue = WellKnownSidType.RemoteLogonIdSid, IsAbsolute = true, SidStr = "S-1-5-14", Name = @"NT AUTHORITY\REMOTE INTERACTIVE LOGON" },
        new() { WellKnownValue = WellKnownSidType.LocalSystemSid, IsAbsolute = true, SidStr = "S-1-5-18", Name = @"NT AUTHORITY\SYSTEM", SddlForm = "SY" },
        new() { WellKnownValue = WellKnownSidType.LocalServiceSid, IsAbsolute = true, SidStr = "S-1-5-19", Name = @"NT AUTHORITY\LOCAL SERVICE", SddlForm = "LS" },
        new() { WellKnownValue = WellKnownSidType.NetworkServiceSid, IsAbsolute = true, SidStr = "S-1-5-20", Name = @"NT AUTHORITY\NETWORK SERVICE", SddlForm = "NS" },
        new() { WellKnownValue = WellKnownSidType.BuiltinDomainSid, IsAbsolute = true, SidStr = "S-1-5-32", Name = null },
        new() { WellKnownValue = WellKnownSidType.BuiltinAdministratorsSid, IsAbsolute = true, SidStr = "S-1-5-32-544", Name = @"BUILTIN\Administrators", SddlForm = "BA" },
        new() { WellKnownValue = WellKnownSidType.BuiltinUsersSid, IsAbsolute = true, SidStr = "S-1-5-32-545", Name = @"BUILTIN\Users", SddlForm = "BU" },
        new() { WellKnownValue = WellKnownSidType.BuiltinGuestsSid, IsAbsolute = true, SidStr = "S-1-5-32-546", Name = @"BUILTIN\Guests", SddlForm = "BG" },
        new() { WellKnownValue = WellKnownSidType.BuiltinPowerUsersSid, IsAbsolute = true, SidStr = "S-1-5-32-547", Name = null, SddlForm = "PU" },
        new() { WellKnownValue = WellKnownSidType.BuiltinAccountOperatorsSid, IsAbsolute = true, SidStr = "S-1-5-32-548", Name = null, SddlForm = "AO" },
        new() { WellKnownValue = WellKnownSidType.BuiltinSystemOperatorsSid, IsAbsolute = true, SidStr = "S-1-5-32-549", Name = null, SddlForm = "SO" },
        new() { WellKnownValue = WellKnownSidType.BuiltinPrintOperatorsSid, IsAbsolute = true, SidStr = "S-1-5-32-550", Name = null, SddlForm = "PO" },
        new() { WellKnownValue = WellKnownSidType.BuiltinBackupOperatorsSid, IsAbsolute = true, SidStr = "S-1-5-32-551", Name = null, SddlForm = "BO" },
        new() { WellKnownValue = WellKnownSidType.BuiltinReplicatorSid, IsAbsolute = true, SidStr = "S-1-5-32-552", Name = null, SddlForm = "RE" },
        new() { WellKnownValue = WellKnownSidType.BuiltinPreWindows2000CompatibleAccessSid, IsAbsolute = true, SidStr = "S-1-5-32-554", Name = null, SddlForm = "RU" },
        new() { WellKnownValue = WellKnownSidType.BuiltinRemoteDesktopUsersSid, IsAbsolute = true, SidStr = "S-1-5-32-555", Name = null, SddlForm = "RD" },
        new() { WellKnownValue = WellKnownSidType.BuiltinNetworkConfigurationOperatorsSid, IsAbsolute = true, SidStr = "S-1-5-32-556", Name = null, SddlForm = "NO" },
        new() { WellKnownValue = WellKnownSidType.AccountAdministratorSid, IsAbsolute = true, SidStr = "S-1-5-21-1234567890-1234567890-1234567890-500", SddlForm = "LA" },
        new() { WellKnownValue = WellKnownSidType.AccountGuestSid, IsAbsolute = false, RidStr = "501", SddlForm = "LG" },
        new() { WellKnownValue = WellKnownSidType.AccountKrbtgtSid, IsAbsolute = false, RidStr = "502" },
        new() { WellKnownValue = WellKnownSidType.AccountDomainAdminsSid, IsAbsolute = false, RidStr = "512", SddlForm = "DA" },
        new() { WellKnownValue = WellKnownSidType.AccountDomainUsersSid, IsAbsolute = false, RidStr = "513", SddlForm = "DU" },
        new() { WellKnownValue = WellKnownSidType.AccountDomainGuestsSid, IsAbsolute = false, RidStr = "514", SddlForm = "DG" },
        new() { WellKnownValue = WellKnownSidType.AccountComputersSid, IsAbsolute = false, RidStr = "515", SddlForm = "DC" },
        new() { WellKnownValue = WellKnownSidType.AccountControllersSid, IsAbsolute = false, RidStr = "516", SddlForm = "DD" },
        new() { WellKnownValue = WellKnownSidType.AccountCertAdminsSid, IsAbsolute = false, RidStr = "517", SddlForm = "CA" },
        new() { WellKnownValue = WellKnownSidType.AccountSchemaAdminsSid, IsAbsolute = false, RidStr = "518", SddlForm = "SA" },
        new() { WellKnownValue = WellKnownSidType.AccountEnterpriseAdminsSid, IsAbsolute = false, RidStr = "519", SddlForm = "EA" },
        new() { WellKnownValue = WellKnownSidType.AccountPolicyAdminsSid, IsAbsolute = false, RidStr = "520", SddlForm = "PA" },
        new() { WellKnownValue = WellKnownSidType.AccountRasAndIasServersSid, IsAbsolute = false, RidStr = "553", SddlForm = "RS" },
        new() { WellKnownValue = WellKnownSidType.NtlmAuthenticationSid, IsAbsolute = true, SidStr = "S-1-5-64-10", Name = @"NT AUTHORITY\NTLM Authentication" },
        new() { WellKnownValue = WellKnownSidType.DigestAuthenticationSid, IsAbsolute = true, SidStr = "S-1-5-64-21", Name = @"NT AUTHORITY\Digest Authentication" },
        new() { WellKnownValue = WellKnownSidType.SChannelAuthenticationSid, IsAbsolute = true, SidStr = "S-1-5-64-14", Name = @"NT AUTHORITY\SChannel Authentication" },
        new() { WellKnownValue = WellKnownSidType.ThisOrganizationSid, IsAbsolute = true, SidStr = "S-1-5-15", Name = @"NT AUTHORITY\This Organization" },
        new() { WellKnownValue = WellKnownSidType.OtherOrganizationSid, IsAbsolute = true, SidStr = "S-1-5-1000", Name = @"NT AUTHORITY\Other Organization" },
        new() { WellKnownValue = WellKnownSidType.BuiltinIncomingForestTrustBuildersSid, IsAbsolute = true, SidStr = "S-1-5-32-557", Name = null },
        new() { WellKnownValue = WellKnownSidType.BuiltinPerformanceMonitoringUsersSid, IsAbsolute = true, SidStr = "S-1-5-32-558", Name = @"BUILTIN\Performance Monitor Users", SddlForm = "MU" },
        new() { WellKnownValue = WellKnownSidType.BuiltinPerformanceLoggingUsersSid, IsAbsolute = true, SidStr = "S-1-5-32-559", Name = @"BUILTIN\Performance Log Users" },
        new() { WellKnownValue = WellKnownSidType.BuiltinAuthorizationAccessSid, IsAbsolute = true, SidStr = "S-1-5-32-560", Name = null },
        new() { WellKnownValue = WellKnownSidType.WinBuiltinTerminalServerLicenseServersSid, IsAbsolute = true, SidStr = "S-1-5-32-561", Name = null },
        new() { WellKnownValue = (WellKnownSidType)66, IsAbsolute = false, RidStr = "4096", SddlForm = "LW" },
        new() { WellKnownValue = (WellKnownSidType)67, IsAbsolute = false, RidStr = "8192", SddlForm = "ME" },
        new() { WellKnownValue = (WellKnownSidType)68, IsAbsolute = false, RidStr = "12288", SddlForm = "HI" },
        new() { WellKnownValue = (WellKnownSidType)69, IsAbsolute = false, RidStr = "16384", SddlForm = "SI" },
        new() { WellKnownValue = (WellKnownSidType)74, IsAbsolute = false, RidStr = "521", SddlForm = "RO" },
        new() { WellKnownValue = (WellKnownSidType)78, IsAbsolute = false, RidStr = "574", SddlForm = "CD" },
    ];
    private string sidStr;
    private string ridStr;
}
namespace DiscUtils.ReFs.Validation;

/// <summary>The part of <c>Validate</c> that is running.</summary>
public enum ValidationPhase
{
    /// <summary>Checkpoint roots, the object table, the upcase and security tables.</summary>
    Tables,

    /// <summary>The directory walk: every directory listed, every entry checked and, when asked, every stream read.</summary>
    Directories,

    /// <summary>The trees of objects the directory walk did not read.</summary>
    Objects
}

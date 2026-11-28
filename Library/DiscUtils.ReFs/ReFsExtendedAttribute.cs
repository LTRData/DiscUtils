namespace DiscUtils.ReFs;

/// <summary>A Windows extended attribute (EA) of a file or directory.</summary>
public sealed class ReFsExtendedAttribute
{
    internal ReFsExtendedAttribute(string name, bool needEa, byte[] value)
    {
        Name = name;
        NeedEa = needEa;
        Value = value;
    }

    /// <summary>The attribute name.</summary>
    public string Name { get; }

    /// <summary>True when the NEED_EA flag is set: an application that does not understand the attribute must not use the file.</summary>
    public bool NeedEa { get; }

    /// <summary>The attribute value.</summary>
    public byte[] Value { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}

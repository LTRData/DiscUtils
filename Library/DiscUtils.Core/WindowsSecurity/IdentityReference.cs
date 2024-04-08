using System;

namespace DiscUtils.Core.WindowsSecurity;

public abstract class IdentityReference
{
    internal IdentityReference() { }

    public abstract string Value { get; }

    public abstract override bool Equals(object o);

    public abstract override int GetHashCode();

    public abstract bool IsValidTargetType(Type targetType);

    public abstract override string ToString();

    public abstract IdentityReference Translate(Type targetType);

    public static bool operator ==(IdentityReference left, IdentityReference right)
    {
        if (((object)left) == null)
        {
            return (((object)right) == null);
        }

        if (((object)right) == null)
        {
            return false;
        }

        return (left.Value == right.Value);
    }

    public static bool operator !=(IdentityReference left, IdentityReference right)
    {
        if (((object)left) == null)
        {
            return (((object)right) != null);
        }

        if (((object)right) == null)
        {
            return true;
        }

        return (left.Value != right.Value);
    }
}
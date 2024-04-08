using System;
using System.Collections;

namespace DiscUtils.Core.WindowsSecurity.AccessControl;

public abstract class GenericAcl : ICollection
{
    public static readonly byte AclRevision;
    public static readonly byte AclRevisionDS;
    public static readonly int MaxBinaryLength;

    static GenericAcl()
    {
        // FIXME: they are likely platform dependent (on windows)
        AclRevision = 2;
        AclRevisionDS = 4;
        MaxBinaryLength = 0x10000;
    }

    public abstract int BinaryLength { get; }

    public abstract int Count { get; }

    public bool IsSynchronized => false;

    public abstract GenericAce this[int index] { get; set; }

    public abstract byte Revision { get; }

    public virtual object SyncRoot => this;

    public void CopyTo(GenericAce[] array, int index)
    {
        if (array == null)
        {
            throw new ArgumentNullException(nameof(array));
        }

        if (index < 0 || array.Length - index < Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be non-negative integer and must not exceed array length - count");
        }

        for (var i = 0; i < Count; i++)
        {
            array[i + index] = this[i];
        }
    }

    void ICollection.CopyTo(Array array, int index)
    {
        CopyTo((GenericAce[])array, index);
    }

    public void GetBinaryForm(byte[] binaryForm, int offset) => GetBinaryForm(binaryForm.AsSpan(offset));

    public abstract void GetBinaryForm(Span<byte> binaryForm);

    public AceEnumerator GetEnumerator()
    {
        return new AceEnumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    internal abstract string GetSddlForm(ControlFlags sdFlags, bool isDacl);
}
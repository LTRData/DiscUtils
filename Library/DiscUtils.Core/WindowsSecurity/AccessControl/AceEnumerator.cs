using System.Collections;

namespace DiscUtils.Core.WindowsSecurity.AccessControl;

public sealed class AceEnumerator : IEnumerator
{
    private int _current = -1;
    private readonly GenericAcl _owner;

    internal AceEnumerator(GenericAcl owner)
    {
        _owner = owner;
    }

    public GenericAce Current => _current < 0 ? null : _owner[_current];
    object IEnumerator.Current => Current;

    public bool MoveNext()
    {
        if (_current + 1 == _owner.Count)
        {
            return false;
        }

        _current++;
        return true;
    }

    public void Reset()
    {
        _current = -1;
    }
}
/*
Bijection.cs
Obtained from:
https://github.com/cyriaca/Cyriaca.Common/blob/main/net.cyriaca.common/Cyriaca.Common/Collections/Bijection.cs
@646770b

Changes:
-namespace
 */

using System.Collections;

namespace norco;

/// <summary>
/// Stores pairs of elements where values are unique.
/// </summary>
/// <typeparam name="TA">First element type.</typeparam>
/// <typeparam name="TB">Second element type.</typeparam>
/// <remarks>
/// All of the first elements are unique, and all of the second elements are unique.
/// Attempts to add a value that already exists results in a <see cref="DuplicateKeyException"/>.
/// If <typeparamref name="TA"/> and <typeparamref name="TB"/> are the same type, the same value can occur
/// once in both the set of first elements and once in the set of second elements.
/// </remarks>
public class Bijection<TA, TB> : ICollection<KeyValuePair<TA, TB>>, IReadOnlyCollection<KeyValuePair<TA, TB>>
{
    #region Fields / properties

    /// <summary>
    /// The number of pairs in this collection.
    /// </summary>
    public int Count => _aToB.Count;

    /// <summary>
    /// The first elements of each pair.
    /// </summary>
    public IEnumerable<TA> A => _aToB.Keys;

    /// <summary>
    /// The second elements of each pair.
    /// </summary>
    public IEnumerable<TB> B => _bToA.Keys;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    private readonly Dictionary<TA, TB> _aToB = new Dictionary<TA, TB>();
    private readonly Dictionary<TB, TA> _bToA = new Dictionary<TB, TA>();

    #endregion

    #region Public API

    /// <summary>
    /// Adds a pair to this collection.
    /// </summary>
    /// <param name="a">First element.</param>
    /// <param name="b">Second element.</param>
    public void Add(TA a, TB b)
    {
        EnsureNotExists(a, b);
        AddInternal(a, b);
    }

    /// <summary>
    /// Attempts to add a pair to this collection.
    /// </summary>
    /// <param name="a">First element.</param>
    /// <param name="b">Second element.</param>
    /// <returns>True if successfully added.</returns>
    public bool TryAdd(TA a, TB b)
    {
        if (_aToB.ContainsKey(a) || _bToA.ContainsKey(b)) return false;
        AddInternal(a, b);
        return true;
    }

    /// <summary>
    /// Attempts to get the second element corresponding to a first element.
    /// </summary>
    /// <param name="a">First element.</param>
    /// <param name="b">Second element.</param>
    /// <returns>True if found.</returns>
    public bool TryGetB(TA a, out TB b)
    {
        return _aToB.TryGetValue(a, out b);
    }

    /// <summary>
    /// Attempts to get the second element corresponding to a second element.
    /// </summary>
    /// <param name="a">First element.</param>
    /// <param name="b">Second element.</param>
    /// <returns>True if found.</returns>
    public bool TryGetA(TB b, out TA a)
    {
        return _bToA.TryGetValue(b, out a);
    }

    /// <summary>
    /// Checks if collection contains the specified value as the first element in a pair.
    /// </summary>
    /// <param name="a">First element.</param>
    /// <returns>True if the value is the first element in a pair.</returns>
    public bool ContainsA(TA a) => _aToB.ContainsKey(a);

    /// <summary>
    /// Checks if collection contains the specified value as the second element in a pair.
    /// </summary>
    /// <param name="b">Second element.</param>
    /// <returns>True if the value is the second element in a pair.</returns>
    public bool ContainsB(TB b) => _bToA.ContainsKey(b);

    /// <summary>
    /// Removes the pair with the specified first element.
    /// </summary>
    /// <param name="a">First element.</param>
    /// <returns>True if the pair existed and was removed.</returns>
    public bool RemoveA(TA a)
    {
        if (!_aToB.TryGetValue(a, out TB b)) return false;
        RemoveInternal(a, b);
        return true;
    }

    /// <summary>
    /// Removes the pair with the specified second element.
    /// </summary>
    /// <param name="b">Second element.</param>
    /// <returns>True if the pair existed and was removed.</returns>
    public bool RemoveB(TB b)
    {
        if (!_bToA.TryGetValue(b, out TA a)) return false;
        RemoveInternal(a, b);
        return true;
    }


    /// <inheritdoc />
    public void Add(KeyValuePair<TA, TB> item)
    {
        Add(item.Key, item.Value);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _aToB.Clear();
        _bToA.Clear();
    }

    /// <inheritdoc />
    public bool Contains(KeyValuePair<TA, TB> item)
    {
        return _aToB.TryGetValue(item.Key, out TB b) && Equals(b, item);
    }

    /// <inheritdoc />
    public void CopyTo(KeyValuePair<TA, TB>[] array, int arrayIndex)
    {
        if (array == null)
            throw new ArgumentNullException(nameof(array));
        if (arrayIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (array.Rank != 1)
            throw new ArgumentException($"{nameof(array)} is not single-dimensional");
        if (array.Length - arrayIndex < Count)
            throw new ArgumentException("Insufficient space to copy collection to array");
        foreach ((KeyValuePair<TA, TB> kvp, int i) in _aToB.Select((v, i) => (v, arrayIndex + i)))
            array[i] = kvp;
    }

    /// <inheritdoc />
    public bool Remove(KeyValuePair<TA, TB> item)
    {
        if (!_aToB.TryGetValue(item.Key, out TB b) || !Equals(b, item.Value)) return false;
        _aToB.Remove(item.Key);
        _bToA.Remove(b);
        return true;
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<TA, TB>> GetEnumerator() => _aToB.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #endregion

    #region Internal API

    private void AddInternal(TA a, TB b)
    {
        _aToB.Add(a, b);
        _bToA.Add(b, a);
    }

    private void RemoveInternal(TA a, TB b)
    {
        _aToB.Remove(a);
        _bToA.Remove(b);
    }

    private void EnsureNotExists(TA a, TB b)
    {
        if (_aToB.ContainsKey(a)) throw new DuplicateKeyException(a);
        if (_bToA.ContainsKey(b)) throw new DuplicateKeyException(b);
    }

    #endregion
}

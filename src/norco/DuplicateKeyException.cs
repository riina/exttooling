/*
DuplicateKeyException.cs
Obtained from:
https://github.com/cyriaca/Cyriaca.Common/blob/main/net.cyriaca.common/Cyriaca.Common/Collections/DuplicateKeyException.cs
@646770b

Changes:
-namespace
 */

namespace norco;

/// <summary>
/// Thrown when trying to add an existing key to a collection.
/// </summary>
public class DuplicateKeyException : Exception
{
    /// <summary>
    /// Key that was being added to the collection.
    /// </summary>
    public readonly object Key;

    /// <summary>
    /// Creates a new instance of <see cref="DuplicateKeyException"/>.
    /// </summary>
    /// <param name="key">Key that was being added to the collection.</param>
    public DuplicateKeyException(object key) => Key = key;

    /// <inheritdoc />
    public override string Message => $"The key \"{Key}\" already exists in the collection.";
}

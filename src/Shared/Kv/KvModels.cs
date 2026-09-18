namespace Shared;

/// <summary>Request body of the KV sample: one of the fixed keys and its new value.</summary>
public sealed record KvWriteRequest(string Key, string Value);

/// <summary>A single KV entry. <see cref="HasValue"/> is false for keys never written.</summary>
public sealed record KvItem(string Key, string Value, bool HasValue);

/// <summary>The three fixed keys with their values, plus the namespace and the length limit.</summary>
public sealed record KvSnapshot(IReadOnlyList<KvItem> Items, int MaxValueLength, string NamespaceName);

/// <summary>Result of writing a KV value, together with the values after the write.</summary>
public sealed record KvWriteResult(bool Ok, string Message, KvSnapshot Snapshot);

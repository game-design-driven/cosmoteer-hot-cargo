using System.Collections;
using System.Reflection;
using Halfling.Serialization;

namespace HotCargo.Core;

/// <summary>
/// Keeps a saved member of the run state out of the co-op hash (RunHash). The reason says why co-op machines may differ in it, or
/// which hashed member stands for it.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class NotInRunHashAttribute(string reason) : Attribute
{
    public string Reason => reason;
}

/// <summary>
/// Spec 58: the hash of the Hot Cargo run state in the game's integrity hash. It reads every saved member ([Serialize]) of RunState
/// and of the state objects below it, so a new field is in the hash with no other change. A member with [NotInRunHash] stays out.
/// Lists hash in order. Sets and dictionaries hash without order, so the hash does not depend on their add order.
/// Limit: reflection (FieldInfo.GetValue) on each call. Revisit with compiled getters if "selfprof" shows it in a profile.
/// </summary>
public static class RunHash
{
    static readonly Dictionary<Type, Func<object, object?>[]> Members = [];

    public static int Of(RunState state) => Value(state);

    /// <summary>The saved members of a state type that the hash reads, in name order. Empty for a type with no [Serialize] member.</summary>
    static Func<object, object?>[] MembersOf(Type type)
    {
        lock (Members)
        {
            if (Members.TryGetValue(type, out var known)) return known;
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var found = new List<(string Name, Func<object, object?> Get)>();
            for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType)
                foreach (var m in t.GetMembers(all | BindingFlags.DeclaredOnly))
                {
                    if (!m.IsDefined(typeof(SerializeAttribute), inherit: true) || m.IsDefined(typeof(NotInRunHashAttribute))) continue;
                    found.Add(m switch
                    {
                        FieldInfo f => (f.Name, f.GetValue),
                        PropertyInfo p => (p.Name, p.GetValue),
                        _ => throw new NotSupportedException($"{type.Name}.{m.Name} is a {m.MemberType}, not a field or property"),
                    });
                }
            return Members[type] = [.. found.OrderBy(m => m.Name, StringComparer.Ordinal).Select(m => m.Get)];
        }
    }

    static int Value(object? value)
    {
        switch (value)
        {
            case null: return 0;
            case bool b: return b ? 1 : 2;
            case int i: return i;
            case uint u: return unchecked((int)u);
            case long l: return unchecked((int)l ^ (int)(l >> 32));
            case ulong l: return unchecked((int)l ^ (int)(l >> 32));
            case float f: return BitConverter.SingleToInt32Bits(f);
            case double d: return Value(BitConverter.DoubleToInt64Bits(d));
            case byte or sbyte or short or ushort or char: return Convert.ToInt32(value);
            case Enum e: return Value(Convert.ToInt64(e));
            // string.GetHashCode differs per process.
            case string s: { int h = 17; foreach (char c in s) h = unchecked(h * 31 + c); return h; }
            case IDictionary map:
            {
                int sum = map.Count;
                foreach (DictionaryEntry e in map) sum = unchecked(sum + Value(e.Key) * 31 + Value(e.Value));
                return sum;
            }
            case IEnumerable items:
            {
                bool ordered = !value.GetType().GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISet<>));
                int h = 17;
                foreach (object? item in items) h = ordered ? unchecked(h * 31 + Value(item)) : unchecked(h + Value(item));
                return h;
            }
        }
        var type = value.GetType();
        var members = MembersOf(type);
        // A value type without saved members (ObjectID, a vector, a tuple) hashes by its fields.
        if (members.Length == 0)
        {
            if (!type.IsValueType) throw new NotSupportedException($"RunHash: {type.FullName} has no [Serialize] member. Hash it by hand or mark its member [NotInRunHash].");
            int h = 17;
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) h = unchecked(h * 31 + Value(f.GetValue(value)));
            return h;
        }
        int hash = 17;
        foreach (var get in members) hash = unchecked(hash * 31 + Value(get(value)));
        return hash;
    }
}

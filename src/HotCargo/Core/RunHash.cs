using System.Collections;
using System.Linq.Expressions;
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
/// Each type gets one compiled function that reads its members directly: the sim phase check (SimPhase) takes this hash several
/// times in each frame, and reflection on each call cost about 18 to 40 µs.
/// </summary>
public static class RunHash
{
    /// <summary>What the hash reads of one type: the function over its members (null for a type with none), and for a collection whether its order counts.</summary>
    sealed record Shape(Func<object, int>? Members, bool Ordered);

    static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Shape> Shapes = new();

    public static int Of(RunState state) => Value(state);

    /// <summary>Test only: the cost of each run state member in the hash, as "name=time", most costly first.</summary>
    internal static IEnumerable<string> MemberCosts(RunState state, Func<Action, double> micros) =>
        typeof(RunState).GetFields().Where(f => f.IsDefined(typeof(SerializeAttribute), true) && !f.IsDefined(typeof(NotInRunHashAttribute)))
            .Select(f => (f.Name, Cost: micros(() => Value(f.GetValue(state))))).OrderByDescending(m => m.Cost).Take(12).Select(m => $"{m.Name}={m.Cost:0.0}");

    /// <summary>
    /// The members of a type that the hash reads, in name order: its saved members; for a value type with none (ObjectID, a vector,
    /// a tuple) every field. A set has no order.
    /// </summary>
    static Shape ShapeOf(Type type) => Shapes.GetOrAdd(type, static type =>
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var members = new List<MemberInfo>();
        for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType)
            members.AddRange(t.GetMembers(all).Where(m => m.IsDefined(typeof(SerializeAttribute), inherit: true) && !m.IsDefined(typeof(NotInRunHashAttribute))));
        if (members.Count == 0 && type.IsValueType) members.AddRange(type.GetFields(all));
        bool set = type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISet<>));
        if (members.Count == 0) return new(null, Ordered: !set);

        // h = 17; for each member: h = h * 31 + hash(member).
        var boxed = Expression.Parameter(typeof(object));
        var self = Expression.Convert(boxed, type);
        Expression hash = Expression.Constant(17);
        foreach (var m in members.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            if (m is not (FieldInfo or PropertyInfo)) throw new NotSupportedException($"{type.Name}.{m.Name} is a {m.MemberType}, not a field or property");
            hash = Expression.Add(Expression.Multiply(hash, Expression.Constant(31)), HashOf(Expression.MakeMemberAccess(self, m)));
        }
        return new(Expression.Lambda<Func<object, int>>(hash, boxed).Compile(), Ordered: !set);
    });

    /// <summary>The hash of a member value. Numbers, bool and text hash without a box; every other type goes through Value.</summary>
    static Expression HashOf(Expression value)
    {
        var type = value.Type;
        if (type == typeof(int)) return value;
        string? direct = type == typeof(bool) ? nameof(Bool) : type == typeof(float) ? nameof(Float) : type == typeof(double) ? nameof(Double)
            : type == typeof(long) ? nameof(Long) : type == typeof(string) ? nameof(Text) : null;
        if (direct is not null) return Expression.Call(typeof(RunHash), direct, null, value);
        if (type.IsEnum) return Expression.Call(typeof(RunHash), nameof(Long), null, Expression.Convert(value, typeof(long)));
        return Expression.Call(typeof(RunHash), nameof(Value), null, Expression.Convert(value, typeof(object)));
    }

    static int Bool(bool b) => b ? 1 : 2;
    static int Float(float f) => BitConverter.SingleToInt32Bits(f);
    static int Long(long l) => unchecked((int)l ^ (int)(l >> 32));
    static int Double(double d) => Long(BitConverter.DoubleToInt64Bits(d));
    /// <summary>string.GetHashCode differs per process.</summary>
    static int Text(string? s)
    {
        if (s is null) return 0;
        int h = 17;
        foreach (char c in s) h = unchecked(h * 31 + c);
        return h;
    }

    static int Value(object? value)
    {
        switch (value)
        {
            case null: return 0;
            case bool b: return Bool(b);
            case int i: return i;
            case uint u: return unchecked((int)u);
            case long l: return Long(l);
            case ulong l: return Long(unchecked((long)l));
            case float f: return Float(f);
            case double d: return Double(d);
            case byte or sbyte or short or ushort or char: return Convert.ToInt32(value);
            case Enum e: return Long(Convert.ToInt64(e));
            case string s: return Text(s);
            case IDictionary map:
            {
                int sum = map.Count;
                foreach (DictionaryEntry e in map) sum = unchecked(sum + Value(e.Key) * 31 + Value(e.Value));
                return sum;
            }
            case IEnumerable items:
            {
                bool ordered = ShapeOf(value.GetType()).Ordered;
                int h = 17;
                foreach (object? item in items) h = ordered ? unchecked(h * 31 + Value(item)) : unchecked(h + Value(item));
                return h;
            }
        }
        return ShapeOf(value.GetType()).Members is { } members ? members(value)
            : throw new NotSupportedException($"RunHash: {value.GetType().FullName} has no [Serialize] member. Mark the member that holds it [NotInRunHash] with a reason.");
    }
}

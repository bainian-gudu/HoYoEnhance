namespace HoYoEnhance.Contracts;

/// <summary>TypeScript 里使用的类型名；不写时沿用 C# 类型名。</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum)]
public sealed class TsNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>覆盖生成器推断出的 TypeScript 类型。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TsTypeAttribute(string type) : Attribute
{
    public string Type { get; } = type;
}

/// <summary>枚举成员在 JSON / TypeScript 字符串联合里的取值。</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class TsValueAttribute(string value) : Attribute
{
    public string Value { get; } = value;
}

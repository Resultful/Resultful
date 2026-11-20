#nullable enable
#if !NET
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Parameter)]
    internal sealed class NotNullWhenAttribute : Attribute { public NotNullWhenAttribute(bool returnValue) { } }
    [AttributeUsage(AttributeTargets.Parameter)]
    internal sealed class MaybeNullWhenAttribute : Attribute { public MaybeNullWhenAttribute(bool returnValue) { } }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
    internal sealed class MemberNotNullWhenAttribute : Attribute { public MemberNotNullWhenAttribute(bool returnValue, params string[] members) { } }
}
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif

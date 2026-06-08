// The IL2CPP corelib references (Il2Cppmscorlib) don't define the nullable-metadata attributes the
// Roslyn compiler emits when consuming nullable-annotated BCL APIs (e.g. HttpClient / async state
// machines), causing CS0656. Defining them here as internal stubs satisfies the compiler. CS0436
// (duplicate-if-present) is already suppressed in the csproj.
namespace System.Runtime.CompilerServices
{
    [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = false, Inherited = false)]
    internal sealed class NullableAttribute : System.Attribute
    {
        public NullableAttribute(byte b) { }
        public NullableAttribute(byte[] b) { }
    }

    [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = false, Inherited = false)]
    internal sealed class NullableContextAttribute : System.Attribute
    {
        public NullableContextAttribute(byte b) { }
    }
}

// Unity targets .NET Standard 2.0 which lacks this type required by C# 9 init-only setters.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

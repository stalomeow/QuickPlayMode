using System;
using System.Diagnostics;

namespace EasyTypeReload
{
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class ReloadOnEnterPlayModeAttribute : Attribute { }
}

using System;
using System.Diagnostics;

namespace EasyTypeReload
{
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Event, AllowMultiple = false)]
    public sealed class NeverReloadAttribute : Attribute { }
}

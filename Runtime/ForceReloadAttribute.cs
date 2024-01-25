using System;
using System.Diagnostics;

namespace EasyTypeReload
{
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class ForceReloadAttribute : Attribute { }
}

using System;
using System.Diagnostics;

namespace EasyTypeReload
{
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RunBeforeReloadAttribute : Attribute
    {
        public int OrderInType { get; set; }
    }
}

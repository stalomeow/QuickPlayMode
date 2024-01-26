using System;
using System.Diagnostics;

namespace QuickPlayMode
{
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class ReloadOnEnterPlayModeAttribute : Attribute
    {
        /// <summary>
        /// Preserve the readonly keyword on all fields. This can lead to unpredictable results.
        /// </summary>
        public bool PreserveReadonly { get; set; }
    }
}

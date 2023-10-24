using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace EasyTypeReload.Editor
{
    [NeverReload]
    public static class TypeReloader
    {
        private static bool s_Initialized = false;
        private static Action s_UnloadTypesAction;
        private static Action s_LoadTypesAction;

        public static void ReloadDirtyTypes()
        {
            try
            {
                InitializeIfNot();

                s_UnloadTypesAction?.Invoke();

                GC.Collect();
                GC.WaitForPendingFinalizers();

                s_LoadTypesAction?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("Failed to reload dirty types!");
            }
        }

        [InitializeOnEnterPlayMode]
        private static void OnEnterPlayModeInEditor(EnterPlayModeOptions options)
        {
            if ((options & EnterPlayModeOptions.DisableDomainReload) == 0)
            {
                return;
            }

            ReloadDirtyTypes();
        }

        [MenuItem("Reload Tools/Reload Domain")]
        private static void ForceReloadDomain()
        {
            EditorUtility.RequestScriptReload();
        }

        [MenuItem("Reload Tools/Reload Dirty Types")]
        private static void ForceReloadDirtyTypes()
        {
            ReloadDirtyTypes();
        }

        private static void InitializeIfNot()
        {
            if (s_Initialized)
            {
                return;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type reloaderType = assembly.GetType(AssemblyTypeReloaderConsts.TypeName, false);

                if (reloaderType == null)
                {
                    continue;
                }

                s_UnloadTypesAction += CreateStaticMethodDelegate(reloaderType, AssemblyTypeReloaderConsts.UnloadMethodName);
                s_LoadTypesAction += CreateStaticMethodDelegate(reloaderType, AssemblyTypeReloaderConsts.LoadMethodName);
            }

            s_Initialized = true;
        }

        private static Action CreateStaticMethodDelegate(Type type, string methodName)
        {
            return (Action)type.GetMethod(methodName).CreateDelegate(typeof(Action));
        }
    }
}

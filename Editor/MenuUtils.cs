using UnityEditor;

namespace QuickPlayMode.Editor
{
    internal static class MenuUtils
    {
        public const string ToggleDomainReloadingPath = "Reloads/Enter Play Mode Options/Reload Domain";
        public const string ToggleSceneReloadingPath = "Reloads/Enter Play Mode Options/Reload Scene";
        public const string ForceReloadDomainPath = "Reloads/Reload Domain";
        public const string ForceReloadDirtyTypesPath = "Reloads/Reload Dirty Types";

        private static bool HasEnterPlayModeOptions(EnterPlayModeOptions options)
        {
            return EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & options) != 0;
        }

        private static void ToggleEnterPlayModeOptions(EnterPlayModeOptions options)
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled)
            {
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = options;
                return;
            }

            EditorSettings.enterPlayModeOptions ^= options;
        }

        [MenuItem(ToggleDomainReloadingPath, validate = true)]
        private static bool ToggleDomainReloadingValidator()
        {
            Menu.SetChecked(ToggleDomainReloadingPath, !HasEnterPlayModeOptions(EnterPlayModeOptions.DisableDomainReload));
            return true;
        }

        [MenuItem(ToggleDomainReloadingPath, priority = 110)]
        private static void ToggleDomainReloading()
        {
            ToggleEnterPlayModeOptions(EnterPlayModeOptions.DisableDomainReload);
        }

        [MenuItem(ToggleSceneReloadingPath, validate = true)]
        private static bool ToggleSceneReloadingValidator()
        {
            Menu.SetChecked(ToggleSceneReloadingPath, !HasEnterPlayModeOptions(EnterPlayModeOptions.DisableSceneReload));
            return true;
        }

        [MenuItem(ToggleSceneReloadingPath, priority = 120)]
        private static void ToggleSceneReloading()
        {
            ToggleEnterPlayModeOptions(EnterPlayModeOptions.DisableSceneReload);
        }

        [MenuItem(ForceReloadDomainPath)]
        private static void ForceReloadDomain()
        {
            EditorUtility.RequestScriptReload();
        }

        [MenuItem(ForceReloadDirtyTypesPath, validate = true)]
        private static bool ForceReloadDirtyTypesValidator()
        {
            return HasEnterPlayModeOptions(EnterPlayModeOptions.DisableDomainReload);
        }

        [MenuItem(ForceReloadDirtyTypesPath)]
        private static void ForceReloadDirtyTypes()
        {
            TypeReloader.ReloadDirtyTypes();
        }
    }
}

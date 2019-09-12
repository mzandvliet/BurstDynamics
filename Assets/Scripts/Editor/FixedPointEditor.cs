using System.Collections.Generic;
using System.Linq;
using UnityEditor;

/*
    I haven't found a way to get fixed point code to be aware of when
    it is being compiled as part of a Burst job, which would be great
    for automatically removing overflow exception throws.

    Being able to quickly toggle a custom compiler flag is the next
    best option, I guess!

    Code adapted from:
    https://forum.unity.com/threads/scripting-define-symbols-access-in-code.174390/
    https://answers.unity.com/questions/775869/editor-how-to-add-checkmarks-to-menuitems.html
 */

[InitializeOnLoad]
public static class FixedPointEditor {

    private const string MENU_NAME = "Fixed Point/Enable Safety Checks";

    private static bool _toggledActive;

    /// Called on load thanks to the InitializeOnLoad attribute
    static FixedPointEditor() {
        FixedPointEditor._toggledActive = EditorPrefs.GetBool(FixedPointEditor.MENU_NAME, false);

        /// Delaying until first editor tick so that the menu
        /// will be populated before setting check state, and
        /// re-apply correct action
        EditorApplication.delayCall += () => {
            PerformAction(FixedPointEditor._toggledActive);
        };
    }

    [MenuItem(FixedPointEditor.MENU_NAME)]
    private static void ToggleAction() {

        /// Toggling action
        PerformAction(!FixedPointEditor._toggledActive);
    }

    public static void PerformAction(bool enabled) {
        /// Set checkmark on menu item
        Menu.SetChecked(FixedPointEditor.MENU_NAME, enabled);
        /// Saving editor state
        EditorPrefs.SetBool(FixedPointEditor.MENU_NAME, enabled);

        FixedPointEditor._toggledActive = enabled;

        if (enabled) {
            AddDefineSymbols();
        } else {
            RemoveDefineSymbols();
        }
    }

    /// <summary>
    /// Symbols that will be added to the editor
    /// </summary>
    public static readonly string[] Symbols = new string[] {
        "FIXED_POINT_SAFETY_CHECKS",
    };

    /// <summary>
    /// Add define symbols as soon as Unity gets done compiling.
    /// </summary>
    private static void AddDefineSymbols() {
        string definesString = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
        List<string> allDefines = definesString.Split(';').ToList();
        allDefines.AddRange(Symbols.Except(allDefines));
        PlayerSettings.SetScriptingDefineSymbolsForGroup(
            EditorUserBuildSettings.selectedBuildTargetGroup,
            string.Join(";", allDefines.ToArray()));
    }

    /// <summary>
    /// Remove define symbols as soon as Unity gets done compiling.
    /// </summary>
    private static void RemoveDefineSymbols() {
        string definesString = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
        List<string> allDefines = definesString.Split(';').Except(Symbols).ToList();
        PlayerSettings.SetScriptingDefineSymbolsForGroup(
            EditorUserBuildSettings.selectedBuildTargetGroup,
            string.Join(";", allDefines.ToArray()));
    }
}
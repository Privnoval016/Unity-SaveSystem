using Extensions.Menus;

namespace SaveSystem
{
    /** <summary>Where the save system's entries sit: <i>Save System</i> under the Privnoval heading.</summary> */
    public static class SaveMenus
    {
        #region Public API

        /** <summary>The tool's name in every menu.</summary> */
        public const string Name = "Save System/";

        /** <summary>Its windows and commands.</summary> */
        public const string Tools = ToolMenus.Tools + Name;

        /** <summary>Its components.</summary> */
        public const string Component = ToolMenus.Component + Name;

        #endregion
    }
}

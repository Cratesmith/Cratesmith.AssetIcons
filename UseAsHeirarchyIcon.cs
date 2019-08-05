using UnityEditor;
using UnityEngine;

namespace Cratesmith.AssetIcons
{
    /// <summary>
    /// Interface for runtime component types that should be used to set their GameObject's icon
    /// </summary>
    public interface IUseAsHeirarchyIcon
    {
    }

    public static class UseAsHeirarchyIcon
    {
#if UNITY_EDITOR
        public static void Editor_Apply_Icon(Component component)
        {
            Texture2D icon = IconUtility.Editor_GetIcon(component);
            Texture2D objectIcon = IconUtility.Editor_GetIcon(component.gameObject);

            if (objectIcon != icon)
            {
                //Debug.LogFormat("Icon changed for actor:{0} from:{1} to:{2}", name, objectIcon?.name, icon?.name);
                IconUtility.Editor_SetIcon(component.gameObject, icon);
            }
        }
#endif

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoad]
        class HeirachyDrawer
        {
            private static HeirachyDrawer m_instance;

            static HeirachyDrawer()
            {
                m_instance = new HeirachyDrawer();
                EditorApplication.hierarchyWindowItemOnGUI += m_instance.HierarchyWindowListElementOnGUI;
                EditorApplication.RepaintHierarchyWindow();
            }

            private void HierarchyWindowListElementOnGUI(int instanceid, Rect rect)
            {
                var obj = EditorUtility.InstanceIDToObject(instanceid);
                if (obj == null)
                {
                    return;
                }

                var go = obj as GameObject;
                if (go == null)
                {
                    return;
                }

                var source = go.GetComponent<IUseAsHeirarchyIcon>();
                if (source is Component comp)
                {
                    Editor_Apply_Icon(comp);
                }
            }
        }
#endif
    }
}
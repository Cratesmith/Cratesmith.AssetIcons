//#define AUTOICONS_LOGGING
#define AUTOICONS_ENABLED

// TODO: AutoIcons not updating correctly if auto-refresh is not turned on. Can take several refreshes of the changed file to take effect.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.VersionControl;
using UnityEngine;

using Object = UnityEngine.Object;

namespace Cratesmith.AssetIcons
{
    public class IconUtility : UnityEditor.AssetPostprocessor
    {
#if AUTOICONS_ENABLED
        private static Queue<string> s_prefabGuids;
        private static HashSet<string> s_iconScriptPaths = new HashSet<string>();
        private static MonoScript[] s_prefabIconTypes;

        private static MonoScript[] PrefabIconTypes
        {
            get
            {
                if (s_prefabIconTypes == null)
                {
                    s_prefabIconTypes = MonoImporter.GetAllRuntimeMonoScripts()
                        .Select(x => new { script=x, type=x!=null?x.GetClass():null })
                        .Where(x => x.script!=null && x.type != null && typeof(Component).IsAssignableFrom(x.type) && typeof(IUseAsPrefabIcon).IsAssignableFrom(x.type))
                        .Select(x=>x.script)
                        .ToArray();
                }
                return s_prefabIconTypes;
            }
        }

        public override int GetPostprocessOrder()
        {
            return 100;
        }

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (BuildPipeline.isBuildingPlayer)
            {
                return;
            }

            var allAssets = importedAssets
                .Concat(movedAssets)
                .Concat(deletedAssets)
                .Concat(movedFromAssetPaths)
                .Where(x=>!string.IsNullOrEmpty(x))
                .Distinct()
                .ToArray();

            bool iconsChanged = allAssets.Any(x => 
                File.Exists(x)
                && new DirectoryInfo(Path.GetDirectoryName(x)).Name.Equals("Gizmos", StringComparison.CurrentCultureIgnoreCase)
                && x.EndsWith(" Icon.png", StringComparison.CurrentCultureIgnoreCase));

            bool scriptsChanged = allAssets.Any(x => x.EndsWith(".cs", StringComparison.CurrentCultureIgnoreCase) || x.EndsWith(".dll", StringComparison.CurrentCultureIgnoreCase));

            bool prefabsChanged = allAssets.Any(x => x.EndsWith(".prefab", StringComparison.CurrentCultureIgnoreCase));

            // save if scripts have changed. We'll update them when they reload (as we might not know their types yet)
            EditorPrefs.SetBool("IconUtility.scriptsChanged", scriptsChanged);

            // if icons have changed we may need to update scripts
            if (iconsChanged)
            {
                AssignIconsToScripts();   
            }	

            // if icons OR prefabs have changed, check the prefabs
            if (iconsChanged || prefabsChanged)
            {
                AssignIconsToPrefabs();		    
            }
        }

        [InitializeOnLoadMethod]
        private static void DoStartup()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !BuildPipeline.isBuildingPlayer)
            {
                EditorPrefs.SetBool("IconUtility.scriptsChanged", EditorApplication.isCompiling);
                if (!EditorApplication.isCompiling)
                {
                    AssignIconsToScripts();				
                }
            }
        }

        [DidReloadScripts]
        private static void UpdateIfDirty()
        {
            if (BuildPipeline.isBuildingPlayer)
            {
                return;
            }

            var scriptsChanged = EditorPrefs.GetBool("IconUtility.scriptsChanged", false);
            if (scriptsChanged)
            {
                AssignIconsToScripts();
            }
        }

        private static void AssignIconsToPrefabs()
        {
#if AUTOICONS_LOGGING					
		Debug.Log("IconUtility: Assigning Icons to Prefabs");
#endif 		
            s_prefabGuids = new Queue<string>(AssetDatabase.FindAssets("t:prefab"));
            EditorApplication.delayCall += AssignIconsToPrefabs_Step;		
        }

        private static void AssignIconsToPrefabs_Step()
        {
            const int PREFABS_PER_STEP = 1000;
            for (int i = 0; i < PREFABS_PER_STEP && s_prefabGuids.Count > 0; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(s_prefabGuids.Dequeue());
                var deps = AssetDatabase.GetDependencies(path, false);
                if (string.IsNullOrEmpty(path) || !s_iconScriptPaths.Overlaps(deps))
                {
                    continue;
                }
                //Debug.Log(" about to load asset at path " + path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                AssignIconsToPrefab_Step_ApplyToPrefab(prefab);			
            }

            if (s_prefabGuids.Count > 0)
            {
                EditorApplication.delayCall += AssignIconsToPrefabs_Step;
            } 
        }

        private static void AssignIconsToPrefab_Step_ApplyToPrefab(GameObject prefab)
        {
            if (prefab == null) return;
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(prefab))) return;
            if (!AssetDatabase.IsMainAsset(prefab)) return;
            var iconComponent = PrefabIconTypes.Select(y=>prefab.GetComponent(y.GetClass())).FirstOrDefault(y=>y!=null);
            if (iconComponent == null) return;
	
            var icon = Editor_GetIcon(iconComponent);
            var prevIcon = Editor_GetIcon(prefab);
            if (icon != prevIcon)
            {
#if AUTOICONS_LOGGING
			Debug.LogFormat("IconUtility: Icon changed for prefab:{0} from:{1} to:{2}", prefab.name, prevIcon != null ? prevIcon.name : "", icon != null ? icon.name : "");
#endif
                Editor_SetIcon(prefab, icon);
            }
            else
            {
#if AUTOICONS_LOGGING
			Debug.LogFormat("IconUtility: Icon prefab:{0} icon:{1} iconType:{2} UP TO DATE", 
				prefab.name,
				prevIcon != null ? prevIcon.name : "",
				iconComponent.name);
#endif
            }
        }

        private static void AssignIconsToScripts()
        {
#if AUTOICONS_LOGGING					
		Debug.Log("IconUtility: Assigning Icons to Scripts");
#endif
            var lookup = new Dictionary<string,string>();
            var suffix = " Icon.png";

            var iconFiles = AssetDatabase.FindAssets("t:texture").Select(AssetDatabase.GUIDToAssetPath)
                .Where(x => x.EndsWith(suffix))
                .ToArray();

            foreach (var file in iconFiles)
            {
                var di = new DirectoryInfo(Path.GetDirectoryName(file));
                if (!di.Name.Equals("Gizmos", StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                var filename = Path.GetFileName(file);
                var name = filename.Substring(0, filename.Length - suffix.Length).ToLower();
                lookup[name] = file;
            }

            var projectPath = new DirectoryInfo(Application.dataPath).Parent.FullName.Replace('\\','/');
            var allScripts = Resources.FindObjectsOfTypeAll<MonoScript>();
            foreach (var script in allScripts.Where(x=>x.GetClass()!=null && typeof(UnityEngine.Object).IsAssignableFrom(x.GetClass())))
            {
                var type = script.GetClass();
                Texture2D prevIcon = Editor_GetIcon(script);
                var prevIconPath = AssetDatabase.GetAssetPath(prevIcon);

                var fullScriptPath = Path.GetFullPath(AssetDatabase.GetAssetPath(script)).Replace('\\','/');
                if(!fullScriptPath.StartsWith(projectPath, StringComparison.InvariantCultureIgnoreCase))
                {
#if AUTOICONS_LOGGING					
		        Debug.LogFormat("IconUtility: Skipping script {0} as it's not under path {1}", script.name, projectPath);
#endif 		
                    continue;
                }

                // ignore icons we haven't set up (except unity defaults)
                if(prevIcon && !new DirectoryInfo(Path.GetDirectoryName(prevIconPath)).Name.Equals("Gizmos", StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                while (type!=null)
                {
                    if (string.IsNullOrEmpty(type.FullName))
                    {
                        break;
                    }

                    string iconPath = "";
                
                    if (!lookup.TryGetValue( TrimGenericsFromType(type.FullName.ToLower()), out iconPath) &&
                        !lookup.TryGetValue( TrimGenericsFromType(type.Name.ToLower()), out iconPath))
                    {
                        type = type.BaseType;
                        continue;
                    }
	           
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
                    if (texture == null)
                    {
                        type = type.BaseType;
                        continue;
                    }

                    s_iconScriptPaths.Add(AssetDatabase.GetAssetPath(script));
                    if (texture != prevIcon)
                    {
#if AUTOICONS_LOGGING					
	                Debug.LogFormat("IconUtility: Icon changed for script:{0} from:{1} to:{2}", script.name, prevIcon!=null ? prevIcon.name:"", texture!=null ? texture.name:"");
#endif
                        Editor_SetIcon(script,texture);
                    }
                    break;
                }

                // no icon for this type (ours or otherwise)
                if (type == null)
                {
                    Editor_SetIcon(script, null);		        
                }
            }

            // we may need to update prefabs
            AssignIconsToPrefabs();
        }    

        static string TrimGenericsFromType(string name)
        {
            int index = name.IndexOf('`');
            if (index == -1)
            {
                return name;
            }
            return name.Substring(0, index);
        }

        [UnityEditor.InitializeOnLoad]
        class HeirachyDrawer
        {
            private static HeirachyDrawer m_instance;

            static HeirachyDrawer()
            {
                m_instance = new HeirachyDrawer();
                EditorApplication.hierarchyWindowItemOnGUI += m_instance.HeirachyWindowItemOnGUI;
                EditorApplication.projectWindowItemOnGUI += m_instance.ProjectWindowItemOnGUI;
                EditorApplication.RepaintHierarchyWindow();
            }

            private void HeirachyWindowItemOnGUI(int instanceid, Rect rect)
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
                        
                var texture = IconUtility.Editor_GetIcon(go);
                if(texture==null)
                {
                    return;
                }
                
                Rect iconRect = new Rect(rect.x+(EditorGUI.indentLevel-1)*35, rect.y, 30, rect.height);
                GUI.DrawTexture(iconRect, texture, ScaleMode.ScaleToFit, true, (float)texture.width/texture.height);
            }

            private void ProjectWindowItemOnGUI(string guid, Rect rect)
            {
                var assetName = AssetDatabase.GUIDToAssetPath(guid);
                ProjectWindowItemOnGUI_Scripts(rect, assetName);
                ProjectWindowItemOnGUI_Prefabs(rect, assetName);
                ProjectWindowItemOnGUI_Assets(rect, assetName);
            }

            private static void ProjectWindowItemOnGUI_Scripts(Rect rect, string assetName)
            {
                if (!assetName.EndsWith(".cs", StringComparison.CurrentCultureIgnoreCase))
                {
                    return;
                }

                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetName);
                if (script == null)
                {
                    return;
                }

                var mainIcon = Editor_GetIcon(script);
                var miniIcon = EditorGUIUtility.FindTexture("cs Script Icon");
                if (mainIcon == null || miniIcon == mainIcon || miniIcon == null)
                {
                    return;
                }

                DrawProjectWindowMiniIcon(rect, miniIcon);
            }

            private static void DrawProjectWindowMiniIcon(Rect rect, Texture2D miniIcon)
            {
                Rect mainIconRect = rect.height < 32
                    ? new Rect(Mathf.Round(rect.x / 5f) * 5f + (Provider.isActive ? 8:0), rect.y, rect.height, rect.height)
                    : new Rect(rect.x, rect.y, rect.height - 14, rect.height - 14);
			
                var subIconScale = 0.75f;
                var aspect = (float) miniIcon.width / miniIcon.height;
                var iconRect = new Rect(mainIconRect.x + mainIconRect.width / 2f,
                    mainIconRect.y + mainIconRect.height * (1f - subIconScale), mainIconRect.width * subIconScale / aspect,
                    mainIconRect.height * subIconScale);
                GUI.DrawTexture(iconRect, miniIcon, ScaleMode.ScaleToFit, true, aspect);
            }

            private static void ProjectWindowItemOnGUI_Assets(Rect rect, string assetName)
            {
                if (!assetName.EndsWith(".asset", StringComparison.CurrentCultureIgnoreCase))
                {
                    return;
                }

                var asset = AssetDatabase.LoadAssetAtPath<Object>(assetName);
                if (asset == null)
                {
                    return;
                }

                var assetIcon = EditorGUIUtility.FindTexture("ScriptableObject Icon");
                var texture = IconUtility.Editor_GetIcon(asset);
                if (texture == null || assetIcon == texture || assetIcon == null)
                {
                    return;
                }

                DrawProjectWindowMiniIcon(rect, assetIcon);
            }

            private static void ProjectWindowItemOnGUI_Prefabs(Rect rect, string assetName)
            {
                if (!assetName.EndsWith(".prefab", StringComparison.CurrentCultureIgnoreCase))
                {
                    return;
                }

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetName);
                if (go == null)
                {
                    return;
                }

                var scriptIcon = IconUtility.Editor_GetIcon(go);
                if (scriptIcon == null)
                {
                    return;
                }

                DrawProjectWindowMiniIcon(rect, scriptIcon);
            }
        }
#endif 

        public static void Editor_SetIcon(Object forObject, Texture2D iconTexture)
        {
            var ty = typeof(EditorGUIUtility);
            var mi2 = ty.GetMethod("SetIconForObject", BindingFlags.NonPublic | BindingFlags.Static);
            mi2.Invoke(null, new object[] { forObject, iconTexture });               
        }

        public static Texture2D Editor_GetIcon(Object forObject)
        {
            var ty = typeof(EditorGUIUtility);
            if (forObject == null)
            {						
                return null;
            }

            if (forObject is ScriptableObject || forObject is MonoBehaviour || forObject is GameObject || forObject is MonoScript)
            {
                var mi = ty.GetMethod("GetIconForObject", BindingFlags.NonPublic | BindingFlags.Static);
                return mi.Invoke(null, new object[] { forObject }) as Texture2D;			
            }

            return (Texture2D)EditorGUIUtility.ObjectContent(forObject, typeof(Mesh)).image;
        }

        public static string GetIconPath(MonoScript scriptAsset, bool fullClassName=false)
        {
            if (scriptAsset == null)
            {
                return string.Empty;
            }

            var assetPath = AssetDatabase.GetAssetPath(scriptAsset);
            if (string.IsNullOrEmpty(assetPath))
            {
                return string.Empty;
            }
        
            var scriptClass = scriptAsset.GetClass();
            var scriptClassName = scriptClass != null
                ? (fullClassName ? scriptClass.FullName : scriptClass.Name)
                : Path.GetFileNameWithoutExtension(assetPath);

            var assetDir = Path.GetDirectoryName(assetPath).Replace('\\','/');
            var gizmoDir = assetDir + "/Gizmos";
            return string.Format("{0}/{1} Icon.png", gizmoDir, scriptClassName);
        }
    }
}
#endif
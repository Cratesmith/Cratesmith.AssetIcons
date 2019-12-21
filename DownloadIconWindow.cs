#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

#pragma warning disable 618

namespace Cratesmith.AssetIcons
{
    public class DownloadIconWindow : EditorWindow 
    {
        [Serializable]
        public struct IconEntry
        {
            public int     width;
            public int     height;
            public string  link;
        }

        private static readonly string[] s_iconCategories = new[] {"ios7", "win8", "win10", "android", "androidL", "color", "office"};
     

        static MonoScript currentScript { get { return Selection.activeObject as MonoScript; }}

        private const string MENUITEM_WINDOW_STRING = "Window/Download Icons...";
        private const string MENUITEM_ASSETS_STRING = "Assets/Download Script Icon...";
        private const string MENUITEM_CONTEXT_STRING = "CONTEXT/MonoScript/Download Script Icon...";

        [SerializeField] private string m_searchName = "";
        [SerializeField] private SearchField         m_searchField;
        [SerializeField] private Vector2             m_scrollPosition;
        [SerializeField] private EditorWWW           m_searchQuery;
        [SerializeField] private List<EditorWWW>     m_iconQueries = new List<EditorWWW>();
        [SerializeField] private List<IconEntry>     m_downloadQueue = new List<IconEntry>();
        [SerializeField] private List<Texture2D>     m_textures = new List<Texture2D>();
        [SerializeField] private List<string>        m_categories = new List<string>(  new[]{"color"});
        [SerializeField] bool m_showCategories;
        private string m_errorString;

        [MenuItem(MENUITEM_CONTEXT_STRING, false)]
        static void ContextShowAcquireIconWindow(MenuCommand command)
        {
            Selection.activeObject = command.context;
            ShowAcquireIconWindow();
        }

        [MenuItem(MENUITEM_ASSETS_STRING, true)]
        static bool _ShowAcquireIconWindow()
        {
            return currentScript;
        }
        
        [MenuItem(MENUITEM_ASSETS_STRING, false)]
        [MenuItem(MENUITEM_WINDOW_STRING)]
        public static void ShowAcquireIconWindow()
        {
            var window = GetWindow<DownloadIconWindow>();
            window.Init(Selection.activeObject as MonoScript);
            window.name = "Download Icons";
            window.Show();
            window.minSize = new Vector2(400, 200);
        }

        private void Init(MonoScript monoScript)
        {
            m_searchName = monoScript.name;      
            DoSearch(m_searchName, m_categories.ToArray());
        }

        void OnEnable()
        {
            m_searchField = new SearchField();
        }

        private void OnGUI()
        {               
            using (new GUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("For Script", "GUIEditor.BreadcrumbLeft");
                GUI.enabled = false;
                EditorGUILayout.ObjectField(currentScript, typeof(MonoScript), false);
                GUI.enabled = true;
            }

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Icon Search", "GUIEditor.BreadcrumbMid");
                var newName = m_searchField.OnGUI(m_searchName);
                if (newName != m_searchName)
                {
                    m_searchName = newName;
                    DoSearch(m_searchName, m_categories.ToArray());
                }
            }

            if (!string.IsNullOrEmpty(m_errorString))
            {
                EditorGUILayout.HelpBox(m_errorString, MessageType.Error, true);
            }

            using (new GUILayout.VerticalScope("box"))
            {
                var categories = new HashSet<string>(m_categories);
                m_showCategories = EditorGUILayout.Foldout(m_showCategories, "Categories");
                if (m_showCategories)
                {
                    EditorGUI.BeginChangeCheck();
                    foreach (var iconCat in s_iconCategories)
                    {
                        if (EditorGUILayout.Toggle(iconCat, categories.Contains(iconCat)))
                        {
                            categories.Add(iconCat);
                        }
                        else
                        {
                            categories.Remove(iconCat);
                        }
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        m_categories.Clear();
                        m_categories.AddRange(categories);
                        DoSearch(m_searchName, m_categories.ToArray());
                    }
                }         
            }

            GUILayout.Space(2);

            using (var scroll = new EditorGUILayout.ScrollViewScope(m_scrollPosition))
            {
                GUI.enabled = currentScript != null;
                m_scrollPosition = scroll.scrollPosition;
                int current = 0;
                while (current < m_textures.Count)
                {
                    var buttonWidth = 32;
                    var buttonHeight = 32;
                    int x = 0;

                    using (new GUILayout.HorizontalScope())
                        do
                        {
                            if (current >= m_textures.Count) break;

                            using (new GUILayout.VerticalScope("groupBackground", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
                                if (GUILayout.Button(m_textures[current], "IconButton", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight), GUILayout.ExpandHeight(false)))
                                {
                                    AssignIconToScript(m_textures[current]);
                                }
                            GUILayout.Space(2);
                              
                            x += buttonWidth + 10;
                            ++current;
                        } while (x + buttonWidth < Screen.width - 22);
                    GUILayout.Space(2);
                }
                GUI.enabled = true;
            }
        }

        private void AssignIconToScript(Texture2D texture2D)
        {
            foreach (MonoScript script in Selection.objects)
            {
                var iconPath = IconUtility.GetIconPath(script);
                if (string.IsNullOrEmpty(iconPath))
                {
                    Debug.LogErrorFormat("Couldn't get icon path for {0}", currentScript);
                    return;
                }

                var dirName = Path.GetDirectoryName(iconPath);
                Directory.CreateDirectory(Path.GetDirectoryName(iconPath));
                AssetDatabase.ImportAsset(dirName);

                File.WriteAllBytes(iconPath, texture2D.EncodeToPNG());
                AssetDatabase.ImportAsset(iconPath);
            }
        }

        void OnDisable()
        {
            if(m_searchQuery!=null)   m_searchQuery.Dispose();
            foreach (var iconQuery in m_iconQueries)
            {
                iconQuery.Dispose();
            }
        }

        private void DoSearch(string searchName, string[] categories)
        {
            m_textures.Clear();
            m_downloadQueue.Clear();

            if(m_searchQuery!=null)   m_searchQuery.Dispose();
            foreach (var iconQuery in m_iconQueries)
            {
                iconQuery.Dispose();
            }

            var platforms = categories.Length > 0 ? "&platform=" + WWW.EscapeURL(string.Join(",", categories)):"";

            m_searchQuery = new EditorWWW("https://api.icons8.com/api/iconsets/search?amount=100&term="+WWW.EscapeURL(searchName)+platforms, www =>
            {
                if (!string.IsNullOrEmpty(www.error))
                {
                    m_errorString = www.error;
                    m_searchQuery.Dispose();
                    return;
                }
                else
                {
                    XDocument doc = XDocument.Parse(www.text);

                    m_downloadQueue = doc.Descendants("icon")
                        .Select(x => x.Descendants("png"))
                        .Select(x => x.Descendants("png")
                            .Select(y=>new IconEntry()
                            {
                                link = y.Attribute("link").Value,
                                width = int.Parse(y.Attribute("width").Value),
                                height = int.Parse(y.Attribute("height").Value),
                            }).First(y=>y.width>=64))                         
                        .ToList();

                    var maxSimulatneousDownloads = 4;
                    for (int i = 0; i < maxSimulatneousDownloads; i++)
                    {
                        DownloadNextIcon();
                    }
                }               
                m_searchQuery.Dispose();
            });          
        }

        private void DownloadNextIcon(WWW completedWWW=null)
        {
            if (completedWWW != null)
            {
                if (!string.IsNullOrEmpty(completedWWW.error))
                {
                    Debug.LogErrorFormat("{0}:{1}", completedWWW.url, completedWWW.error, this);
                }
                else if(completedWWW.texture!=null)
                {
                    m_textures.Add(completedWWW.texture);
                    Repaint();
                }

                completedWWW.Dispose();
            }

            m_iconQueries.RemoveAll(x=>x.www == completedWWW);
          
            if (m_downloadQueue.Count == 0)
            {
                return;
            }

            var firstIcon = m_downloadQueue.First();
            m_downloadQueue.RemoveAt(0);
            m_iconQueries.Add(new EditorWWW(firstIcon.link, DownloadNextIcon));    
        }
    }
}
#endif
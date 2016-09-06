using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor;
using UnityEngine;

public class Cocos2dxTextureAtlasSlicer : EditorWindow
{
    [MenuItem("Assets/Slice Sprite Using Coscos2d-x Plist")]
    public static void TextureAtlasSlicerWindow()
    {
        CreateInstance<Cocos2dxTextureAtlasSlicer>().Show();
    }

    [MenuItem("Assets/Slice Sprite Using Coscos2d-x Plist", true)]
    public static bool ValidateTextureAtlasSlicerWindow()
    {
        var assetPath = GetTexturePathIfPlistExists(GetSelectedTexture());

        if (assetPath != null)
        {
            var textureImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            return textureImporter && textureImporter.textureType == TextureImporterType.Sprite ||
                   textureImporter.textureType == TextureImporterType.Advanced;
        }

        // TODO: 批处理纹理
        return false;
    }

    private static Texture2D GetSelectedTexture()
    {
        if (Selection.objects.Length == 1 && Selection.activeObject is Texture2D)
        {
            return Selection.activeObject as Texture2D;
        }
        return null;
    }

    private static string GetTexturePathIfPlistExists(Texture2D selectedTexture, Action<string> doIfPlistExists = null)
    {
        if (selectedTexture == null) return null;

        var assetPath = AssetDatabase.GetAssetPath(selectedTexture);
        var extension = Path.GetExtension(assetPath);
        var pathWithoutExtension = assetPath.Remove(assetPath.Length - extension.Length, extension.Length);
        var plistPath = pathWithoutExtension + ".plist";

        if (File.Exists(plistPath))
        {
            if (doIfPlistExists != null) doIfPlistExists.Invoke(plistPath);
            return assetPath;
        }
        return null;
    }

    public TextureImporter importer;
    private Texture2D selectedTexture;
    public SpriteAlignment spriteAlignment = SpriteAlignment.Center;
    public Vector2 customOffset = new Vector2(0.5f, 0.5f);
    private string plistContent;

    public Cocos2dxTextureAtlasSlicer()
    {
        titleContent = new GUIContent("Plist Slicer");
    }

    public void OnSelectionChange()
    {
        UseSelectedTexture();
    }

    private void UseSelectedTexture()
    {
        selectedTexture = GetSelectedTexture();
        var assetPath = GetTexturePathIfPlistExists(selectedTexture, (plistPath) =>
        {
            using (FileStream file = new FileStream(plistPath, FileMode.Open))
            {
                byte[] byteArray = new byte[(int)file.Length];
                file.Read(byteArray, 0, byteArray.Length);
                plistContent = System.Text.Encoding.Default.GetString(byteArray);
                file.Close();
                file.Dispose();
            }
        });

        importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

        if (importer != null)
        {
            ParseXML();
        }
        else
        {
            subTextures = null;
        }

        Repaint();
    }

    private SubTexture[] subTextures;
    private int wantedWidth, wantedHeight;

    private void ParseXML()
    {
        try
        {
            var document = new XmlDocument();
            document.LoadXml(plistContent);
            XmlNodeList frames = new PlistFinder(document.DocumentElement.ChildNodes[0]).FindValueByKey("frames").ChildNodes;

            ArrayList subTexs = new ArrayList();
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].Name.ToLower() == "key")
                {
                    SubTexture subTex = new SubTexture();
                    subTex.name = frames[i].InnerText;

                    PlistFinder finder = new PlistFinder(frames[++i]);
                    XmlNode rotatedNode = finder.FindValueByKey("textureRotated");
                    rotatedNode = rotatedNode ?? finder.FindValueByKey("rotated");
                    bool isRotated = (rotatedNode.Name.ToLower() == "true");

                    XmlNode rectNode = finder.FindValueByKey("textureRect");
                    rectNode = rectNode ?? finder.FindValueByKey("frame");
                    string rect = rectNode.InnerText;

                    var ints = rect.Replace('{', ' ').Replace('}', ' ').Split(new char[] { ',' })
                                   .Select(num => Int32.Parse(num.Trim())).ToArray();

                    subTex.width = isRotated ? ints[3] : ints[2];
                    subTex.height = isRotated ? ints[2] : ints[3];
                    subTex.x = ints[0];
                    subTex.y = ints[1];
                    subTexs.Add(subTex);
                }
            }

            subTextures = subTexs.Cast<SubTexture>().ToArray();

            wantedWidth = 0;
            wantedHeight = 0;

            foreach (var subTexture in subTextures)
            {
                var right = subTexture.x + subTexture.width;
                var bottom = subTexture.y + subTexture.height;

                wantedWidth = Mathf.Max(wantedWidth, right);
                wantedHeight = Mathf.Max(wantedHeight, bottom);
            }
        }
        catch (Exception)
        {
            subTextures = null;
        }
    }

    public void OnEnable()
    {
        UseSelectedTexture();
    }

    public void OnGUI()
    {
        if (importer == null)
        {
            EditorGUILayout.LabelField("Please select a texture to slice.");
            return;
        }
        EditorGUI.BeginDisabledGroup(focusedWindow != this);
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("Texture", Selection.activeObject, typeof(Texture), false);
            EditorGUI.EndDisabledGroup();

            if (importer.textureType != TextureImporterType.Sprite &&
                importer.textureType != TextureImporterType.Advanced)
            {
                EditorGUILayout.LabelField("The Texture Type needs to be Sprite or Advanced!");
            }

            EditorGUI.BeginDisabledGroup((importer.textureType != TextureImporterType.Sprite &&
                                          importer.textureType != TextureImporterType.Advanced));
            {
                spriteAlignment = (SpriteAlignment)EditorGUILayout.EnumPopup("Pivot", spriteAlignment);

                EditorGUI.BeginDisabledGroup(spriteAlignment != SpriteAlignment.Custom);
                EditorGUILayout.Vector2Field("Custom Offset", customOffset);
                EditorGUI.EndDisabledGroup();

                var needsToResizeTexture = wantedWidth > selectedTexture.width || wantedHeight > selectedTexture.height;

                if (needsToResizeTexture)
                {
                    EditorGUILayout.LabelField(
                        string.Format("Texture size too small."
                                      + " It needs to be at least {0} by {1} pixels!",
                            wantedWidth,
                            wantedHeight));
                    EditorGUILayout.LabelField("Try changing the Max Size property in the importer.");
                }

                if (subTextures == null || subTextures.Length == 0)
                {
                    EditorGUILayout.LabelField("Could not find any SubTextures in XML.");
                }

                EditorGUI.BeginDisabledGroup(needsToResizeTexture || subTextures == null ||
                                             subTextures.Length == 0);
                if (GUILayout.Button("Slice"))
                {
                    PerformSlice();
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUI.EndDisabledGroup();
        }
        EditorGUI.EndDisabledGroup();
    }

    private void PerformSlice()
    {
        if (importer == null)
        {
            return;
        }

        var textureHeight = selectedTexture.height;

        var needsUpdate = false;

        if (importer.spriteImportMode != SpriteImportMode.Multiple)
        {
            needsUpdate = true;
            importer.spriteImportMode = SpriteImportMode.Multiple;
        }

        var wantedSpriteSheet = (from subTexture in subTextures
                                 let actualY = textureHeight - (subTexture.y + subTexture.height)
                                 select new SpriteMetaData
                                 {
                                     alignment = (int)spriteAlignment,
                                     border = new Vector4(),
                                     name = subTexture.name,
                                     pivot = GetPivotValue(spriteAlignment, customOffset),
                                     rect = new Rect(subTexture.x, actualY, subTexture.width, subTexture.height)
                                 }).ToArray();
        if (!needsUpdate && !importer.spritesheet.SequenceEqual(wantedSpriteSheet))
        {
            needsUpdate = true;
            importer.spritesheet = wantedSpriteSheet;
        }

        if (needsUpdate)
        {
            EditorUtility.SetDirty(importer);

            try
            {
                AssetDatabase.StartAssetEditing();
                AssetDatabase.ImportAsset(importer.assetPath);

                EditorUtility.DisplayDialog("Success!", "The sprite was sliced successfully.", "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Error", "There was an exception while trying to reimport the image." +
                                                     "\nPlease check the console log for details.", "OK");
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }
        else
        {
            EditorUtility.DisplayDialog("Nope!", "The sprite is already sliced according to this Cocos2d-x Plist file.", "OK");
        }
    }

    //SpriteEditorUtility
    public static Vector2 GetPivotValue(SpriteAlignment alignment, Vector2 customOffset)
    {
        switch (alignment)
        {
            case SpriteAlignment.Center:
                return new Vector2(0.5f, 0.5f);
            case SpriteAlignment.TopLeft:
                return new Vector2(0.0f, 1f);
            case SpriteAlignment.TopCenter:
                return new Vector2(0.5f, 1f);
            case SpriteAlignment.TopRight:
                return new Vector2(1f, 1f);
            case SpriteAlignment.LeftCenter:
                return new Vector2(0.0f, 0.5f);
            case SpriteAlignment.RightCenter:
                return new Vector2(1f, 0.5f);
            case SpriteAlignment.BottomLeft:
                return new Vector2(0.0f, 0.0f);
            case SpriteAlignment.BottomCenter:
                return new Vector2(0.5f, 0.0f);
            case SpriteAlignment.BottomRight:
                return new Vector2(1f, 0.0f);
            case SpriteAlignment.Custom:
                return customOffset;
            default:
                return Vector2.zero;
        }
    }

    private struct SubTexture
    {
        public int width;
        public int height;
        public int x;
        public int y;
        public string name;
    }

    private class PlistFinder
    {
        private Hashtable cachedMap = new Hashtable();
        private int lastPosition = 0;
        private XmlNode rootNode;

        public PlistFinder(XmlNode rootNode)
        {
            this.rootNode = rootNode;
        }

        public XmlNode FindValueByKey(string key)
        {
            XmlNode rlt = cachedMap[key] as XmlNode;
            if (rlt != null) return rlt;

            XmlNodeList childs = rootNode.ChildNodes;
            for (int i = lastPosition; i < childs.Count; i++)
            {
                XmlNode child = childs[i];
                if (child.Name.ToLower() == "key")
                {
                    i++;
                    cachedMap[child.InnerText] = childs[i];
                    if (child.InnerText == key)
                    {
                        rlt = childs[i];
                        break;
                    }
                }
            }
            return rlt;
        }
    }
}
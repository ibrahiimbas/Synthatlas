// Assets/Editor/SynthatlasPacker.cs
using UnityEngine;
using UnityEditor;
using System.Xml.Linq;
using System.Linq;
using System.IO;
using System.Collections.Generic;

public class SynthatlasPacker
{
    [MenuItem("Tools/Import Synthatlas Atlas or Sheet")]
    static void ImportAtlas()
    {
        string xmlPath = EditorUtility.OpenFilePanel(
            "Select Synthatlas XML", "", "xml");
        if (string.IsNullOrEmpty(xmlPath)) return;

        XDocument doc;
        try { doc = XDocument.Load(xmlPath); }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("Error",
                $"Could not parse XML:\n{ex.Message}", "OK");
            return;
        }

        var root = doc.Root;
        if (root == null)
        {
            EditorUtility.DisplayDialog("Error", "Empty XML file.", "OK");
            return;
        }

        string rootName       = root.Name.LocalName;
        bool isTextureAtlas   = rootName == "TextureAtlas";
        bool isAnimationSheet = rootName == "AnimationSheet";
        bool isMultiAnimation = rootName == "MultiAnimationSheet";

        if (!isTextureAtlas && !isAnimationSheet && !isMultiAnimation)
        {
            EditorUtility.DisplayDialog("Error",
                $"Unknown XML format: <{rootName}>", "OK");
            return;
        }

        if (!int.TryParse(root.Attribute("height")?.Value, out int atlasH) || atlasH <= 0)
        {
            EditorUtility.DisplayDialog("Error",
                "Missing or invalid 'height' attribute.", "OK");
            return;
        }

        string xmlDir      = Path.GetDirectoryName(xmlPath);
        string xmlBaseName = Path.GetFileNameWithoutExtension(xmlPath);
        string imgFile, srcImgPath;

        if (isTextureAtlas)
        {
            imgFile    = root.Attribute("imagePath")?.Value ?? "";
            srcImgPath = Path.Combine(xmlDir, imgFile);
        }
        else
        {
            string tryPng = Path.Combine(xmlDir, xmlBaseName + ".png");
            string tryJpg = Path.Combine(xmlDir, xmlBaseName + ".jpg");

            if      (File.Exists(tryPng)) { srcImgPath = tryPng; imgFile = xmlBaseName + ".png"; }
            else if (File.Exists(tryJpg)) { srcImgPath = tryJpg; imgFile = xmlBaseName + ".jpg"; }
            else                          { srcImgPath = "";      imgFile = xmlBaseName + ".png"; }
        }

        if (!File.Exists(srcImgPath))
        {
            EditorUtility.DisplayDialog("Image Not Found",
                "Could not find the atlas image automatically.\nPlease select manually.", "OK");

            srcImgPath = EditorUtility.OpenFilePanel(
                "Select Atlas Image", xmlDir, "png,jpg,jpeg");

            if (string.IsNullOrEmpty(srcImgPath)) return;
            imgFile = Path.GetFileName(srcImgPath);
        }

        string destImgPath = Path.Combine(Application.dataPath, imgFile);

        if (File.Exists(destImgPath))
        {
            bool ow = EditorUtility.DisplayDialog("File Exists",
                $"'{imgFile}' already exists. Overwrite?", "Overwrite", "Cancel");
            if (!ow) return;
        }

        File.Copy(srcImgPath, destImgPath, overwrite: true);
        AssetDatabase.Refresh();

        string assetPath = "Assets/" + imgFile;
        var    ti        = AssetImporter.GetAtPath(assetPath) as TextureImporter;

        if (ti == null)
        {
            EditorUtility.DisplayDialog("Error",
                $"TextureImporter not found for:\n{assetPath}", "OK");
            return;
        }

        List<SpriteMetaData> spriteSheet;
        string formatLabel;

        if (isTextureAtlas)
        { spriteSheet = BuildFromTextureAtlas(root, atlasH);      formatLabel = "Texture Atlas"; }
        else if (isAnimationSheet)
        { spriteSheet = BuildFromAnimationSheet(root, atlasH);    formatLabel = "Animation Sheet"; }
        else
        { spriteSheet = BuildFromMultiAnimationSheet(root, atlasH); formatLabel = "Multi Animation Sheet"; }

        if (spriteSheet.Count == 0)
        {
            EditorUtility.DisplayDialog("Warning",
                $"No sprites found in <{rootName}>.", "OK");
            return;
        }

        ti.textureType         = TextureImporterType.Sprite;
        ti.spriteImportMode    = SpriteImportMode.Multiple;
        ti.filterMode          = FilterMode.Point;
        ti.mipmapEnabled       = false;
        ti.alphaIsTransparency = true;
        ti.spritesheet         = spriteSheet.ToArray();

        ti.SaveAndReimport();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Import Complete",
            $"Format:  {formatLabel}\nSprites: {spriteSheet.Count}\nFile:    {imgFile}", "OK");

        var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (imported != null) EditorGUIUtility.PingObject(imported);
    }

    static List<SpriteMetaData> BuildFromTextureAtlas(XElement root, int atlasH)
    {
        var list = new List<SpriteMetaData>();
        foreach (var sub in root.Descendants().Where(e => e.Name.LocalName == "SubTexture"))
        {
            string name = sub.Attribute("name")?.Value ?? "sprite";
            if (!TryParseRect(sub, "x", "y", "width", "height",
                out int x, out int y, out int w, out int h)) continue;
            list.Add(MakeSprite(name, x, y, w, h, atlasH));
        }
        return list;
    }

    static List<SpriteMetaData> BuildFromAnimationSheet(XElement root, int atlasH)
    {
        var list = new List<SpriteMetaData>();
        var frames = root.Descendants()
            .Where(e => e.Name.LocalName == "Frame")
            .OrderBy(e => int.TryParse(e.Attribute("index")?.Value, out int i) ? i : 0);
        foreach (var f in frames)
        {
            string name = f.Attribute("name")?.Value ?? "frame";
            if (!TryParseRect(f, "x", "y", "w", "h",
                out int x, out int y, out int w, out int h)) continue;
            list.Add(MakeSprite(name, x, y, w, h, atlasH));
        }
        return list;
    }

    static List<SpriteMetaData> BuildFromMultiAnimationSheet(XElement root, int atlasH)
    {
        var list = new List<SpriteMetaData>();
        foreach (var anim in root.Elements().Where(e => e.Name.LocalName == "Animation"))
        {
            string animName = anim.Attribute("name")?.Value ?? "anim";
            var frames = anim.Elements()
                .Where(e => e.Name.LocalName == "Frame")
                .OrderBy(e => int.TryParse(e.Attribute("index")?.Value, out int i) ? i : 0);
            foreach (var f in frames)
            {
                string frameName   = f.Attribute("name")?.Value ?? "frame";
                string spriteName  = frameName.StartsWith(animName)
                    ? frameName : $"{animName}_{frameName}";
                if (!TryParseRect(f, "x", "y", "w", "h",
                    out int x, out int y, out int w, out int h)) continue;
                list.Add(MakeSprite(spriteName, x, y, w, h, atlasH));
            }
        }
        return list;
    }

    static bool TryParseRect(XElement e,
        string xA, string yA, string wA, string hA,
        out int x, out int y, out int w, out int h)
    {
        x = y = w = h = 0;
        return int.TryParse(e.Attribute(xA)?.Value, out x)
            && int.TryParse(e.Attribute(yA)?.Value, out y)
            && int.TryParse(e.Attribute(wA)?.Value, out w)
            && int.TryParse(e.Attribute(hA)?.Value, out h)
            && w > 0 && h > 0;
    }

    static SpriteMetaData MakeSprite(
        string name, int x, int y, int w, int h, int atlasH)
    {
        return new SpriteMetaData
        {
            name      = name,
            rect      = new Rect(x, atlasH - y - h, w, h),
            pivot     = new Vector2(0.5f, 0.5f),
            border    = Vector4.zero,
            alignment = (int)SpriteAlignment.Center
        };
    }

    [MenuItem("Tools/Import Synthatlas Atlas or Sheet", validate = true)]
    static bool ValidateImport() => !EditorApplication.isPlaying;
}
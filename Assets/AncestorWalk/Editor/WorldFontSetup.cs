using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;
using TMPro;
namespace AncestorWalk.Editor
{
    public static class WorldFontSetup
    {
        [MenuItem("Ancestor Walk/Prepare Stable World Text")]
        public static void Prepare()
        {
            const string path="Assets/AncestorWalk/Resources/Fonts/LatoSDF.asset";
            var asset=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if(asset==null)
            {
                asset=TMP_FontAsset.CreateFontAsset(Resources.Load<Font>("Fonts/Lato-Regular"),72,8,GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic,false);
                asset.name="Lato SDF";
                string chars=new string(Enumerable.Range(32,95).Concat(Enumerable.Range(160,96)).Select(x=>(char)x).ToArray())+"‘’“”–—…←↑→↓↔↗−";
                asset.TryAddCharacters(chars,out string missing);
                asset.atlasPopulationMode=AtlasPopulationMode.Static;
                asset.material.shader=Shader.Find("TextMeshPro/Mobile/Distance Field");
                asset.material.SetFloat("_CullMode",0);
                asset.atlasTextures[0].filterMode=FilterMode.Bilinear;
                AssetDatabase.CreateAsset(asset,path);
                AssetDatabase.AddObjectToAsset(asset.material,asset);
                foreach(var texture in asset.atlasTextures)AssetDatabase.AddObjectToAsset(texture,asset);
                Debug.Log("Prepared static Lato distance-field font; unsupported optional glyphs: "+missing);
            }
            asset.material.shader=Resources.Load<Shader>("Shaders/WorldSDF");
            EditorUtility.SetDirty(asset.material);
            foreach(string name in new[]{"Mobile","PC"})
            {
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/"+name+"_RPAsset.asset");
                pipeline.msaaSampleCount=4;EditorUtility.SetDirty(pipeline);
            }
            AssetDatabase.SaveAssets();
        }
    }
}

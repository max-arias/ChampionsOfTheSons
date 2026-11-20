using System.Text.Json;
using RedLoader;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// Development stand-in for the bundle's weapon prefabs (COS-93 spike): reads a single-mesh glTF (KayKit Fantasy
/// Weapons Bits, CC0) from a folder on disk at runtime. Not shipped: the release uses the bundle prefabs, and without
/// either the weapon falls back to a primitive. Only what those files use is supported: one mesh, one primitive,
/// float POSITION / NORMAL / TEXCOORD_0, ushort or uint indices, one PNG base color texture.
/// </summary>
internal static class GltfStandIn
{
    /// <summary>Folder with the .gltf files; override with the COTS_WEAPON_MODELS environment variable.</summary>
    public static string Folder => Environment.GetEnvironmentVariable("COTS_WEAPON_MODELS")
        ?? @"D:\dev\asset-sources\KayKit_FantasyWeaponsBits_1.0_FREE\Assets\gltf";

    private static readonly Dictionary<string, (Mesh Mesh, Texture2D? Tex)?> Cache = new();

    public static bool Available(string name) => File.Exists(Path.Combine(Folder, name + ".gltf"));

    /// <summary>A new GameObject with the model (grip at the origin, blade along +Y as authored), or null.</summary>
    public static GameObject? Create(string name, Shader shader)
    {
        var loaded = Load(name);
        if (loaded == null) return null;
        var go = new GameObject("cots-standin-" + name);
        go.AddComponent<MeshFilter>().sharedMesh = loaded.Value.Mesh;
        var mat = new Material(shader);
        if (loaded.Value.Tex != null)
        {
            mat.SetTexture("_BaseColorMap", loaded.Value.Tex);
            mat.SetTexture("_MainTex", loaded.Value.Tex);
        }
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0.4f);
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    private static (Mesh Mesh, Texture2D? Tex)? Load(string name)
    {
        if (Cache.TryGetValue(name, out var cached)) return cached;
        (Mesh, Texture2D?)? result = null;
        try
        {
            var path = Path.Combine(Folder, name + ".gltf");
            if (File.Exists(path)) result = Parse(path);
        }
        catch (Exception e)
        {
            RLog.Warning($"Champions of the Sons: stand-in model {name} failed: {e.Message}");
        }
        Cache[name] = result;
        return result;
    }

    private static (Mesh, Texture2D?) Parse(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var buffers = root.GetProperty("buffers").EnumerateArray()
            .Select(b => File.ReadAllBytes(Path.Combine(dir, b.GetProperty("uri").GetString()!))).ToList();
        var views = root.GetProperty("bufferViews").EnumerateArray().ToList();
        var accessors = root.GetProperty("accessors").EnumerateArray().ToList();

        (byte[] Data, int Offset, int Count, int Stride) Span(int accessor, int compSize, int comps)
        {
            var a = accessors[accessor];
            var v = views[a.GetProperty("bufferView").GetInt32()];
            int offset = (v.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0) + (a.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
            int stride = v.TryGetProperty("byteStride", out var s) ? s.GetInt32() : compSize * comps;
            return (buffers[v.GetProperty("buffer").GetInt32()], offset, a.GetProperty("count").GetInt32(), stride);
        }
        float[] Floats(int accessor, int comps)
        {
            var (data, offset, count, stride) = Span(accessor, 4, comps);
            var r = new float[count * comps];
            for (int i = 0; i < count; i++)
                for (int c = 0; c < comps; c++) r[i * comps + c] = BitConverter.ToSingle(data, offset + i * stride + c * 4);
            return r;
        }

        var prim = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
        var attrs = prim.GetProperty("attributes");
        var pos = Floats(attrs.GetProperty("POSITION").GetInt32(), 3);
        int n = pos.Length / 3;
        // glTF is right-handed (+Z forward), Unity left-handed: mirror X and flip the winding.
        var verts = new Vector3[n];
        for (int i = 0; i < n; i++) verts[i] = new Vector3(-pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
        var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
        mesh.vertices = verts;
        if (attrs.TryGetProperty("NORMAL", out var na))
        {
            var nr = Floats(na.GetInt32(), 3);
            var normals = new Vector3[n];
            for (int i = 0; i < n; i++) normals[i] = new Vector3(-nr[i * 3], nr[i * 3 + 1], nr[i * 3 + 2]);
            mesh.normals = normals;
        }
        if (attrs.TryGetProperty("TEXCOORD_0", out var ta))
        {
            var uv = Floats(ta.GetInt32(), 2);
            var uvs = new Vector2[n];
            for (int i = 0; i < n; i++) uvs[i] = new Vector2(uv[i * 2], 1 - uv[i * 2 + 1]);
            mesh.uv = uvs;
        }
        int ia = prim.GetProperty("indices").GetInt32();
        int componentType = accessors[ia].GetProperty("componentType").GetInt32();
        int size = componentType == 5125 ? 4 : 2;
        var (idata, ioffset, icount, istride) = Span(ia, size, 1);
        var tris = new int[icount];
        for (int i = 0; i < icount; i++)
            tris[i] = size == 4 ? (int)BitConverter.ToUInt32(idata, ioffset + i * istride) : BitConverter.ToUInt16(idata, ioffset + i * istride);
        for (int i = 0; i + 2 < icount; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
        mesh.triangles = tris;
        if (!attrs.TryGetProperty("NORMAL", out _)) mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Texture2D? tex = null;
        if (root.TryGetProperty("images", out var images) && images.GetArrayLength() > 0 && images[0].TryGetProperty("uri", out var uri))
        {
            var file = Path.Combine(dir, uri.GetString()!);
            if (File.Exists(file))
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = Path.GetFileNameWithoutExtension(file), filterMode = FilterMode.Point };
                ImageConversion.LoadImage(tex, File.ReadAllBytes(file), false);
            }
        }
        return (mesh, tex);
    }
}

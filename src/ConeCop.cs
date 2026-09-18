using System;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Police;
using MelonLoader;
using UnityEngine;

namespace CoordinatedPolice;

internal static class ConeCop
{
    private static GameObject? hat;
    private static Mesh? mesh;
    private static Material? orange;
    private static Material? white;
    private static float next_seconds;
    private static bool failed;

    public static void reset()
    {
        if (hat != null) UnityEngine.Object.Destroy(hat);
        if (mesh != null) UnityEngine.Object.Destroy(mesh);
        if (orange != null) UnityEngine.Object.Destroy(orange);
        if (white != null) UnityEngine.Object.Destroy(white);
        hat = null;
        mesh = null;
        orange = null;
        white = null;
        next_seconds = 0;
        failed = false;
    }

    public static void tick()
    {
        if (failed || Time.unscaledTime < next_seconds || !LoadManager.InstanceExists ||
            !LoadManager.Instance.IsGameLoaded || !TimeManager.InstanceExists) return;
        next_seconds = Time.unscaledTime + 5f;
        try
        {
            PoliceOfficer? selected = null;
            uint best = uint.MaxValue;
            int count = Math.Min(128, PoliceOfficer.Officers.Count);
            for (int i = 0; i < count; i++)
            {
                PoliceOfficer officer = PoliceOfficer.Officers[i];
                if (officer == null || string.IsNullOrEmpty(officer.ID)) continue;
                uint score = DailyChance.score(officer.ID, TimeManager.Instance.ElapsedDays);
                if (score % 256 != 0 || score >= best) continue;
                best = score;
                selected = officer;
            }
            if (selected == null || selected.Avatar == null || selected.Avatar.HeadBone == null)
            {
                if (hat != null) hat.SetActive(false);
                return;
            }
            if (hat == null) create();
            Transform head = selected.Avatar.HeadBone;
            if (hat!.transform.parent != head)
            {
                hat.layer = selected.Avatar.gameObject.layer;
                hat.transform.SetParent(head, false);
                hat.transform.localPosition = new Vector3(0, 0.12f, 0);
                hat.transform.localRotation = Quaternion.identity;
                hat.transform.localScale = Vector3.one;
                MelonLogger.Msg("Police: An officer has questionable headwear today.");
            }
            hat.SetActive(true);
        }
        catch (Exception error)
        {
            reset();
            failed = true;
            MelonLogger.Warning($"Police: Cone cosmetic disabled for this scene: {error.Message}");
        }
    }

    private static void create()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        if (shader == null) throw new InvalidOperationException("No cone material shader available.");
        orange = new Material(shader);
        white = new Material(shader);
        orange.color = new Color(1f, 0.3f, 0.03f, 1);
        white.color = Color.white;
        if (orange.HasProperty("_BaseColor")) orange.SetColor("_BaseColor", orange.color);
        if (white.HasProperty("_BaseColor")) white.SetColor("_BaseColor", white.color);
        var vertices = new Vector3[68];
        var orange_triangles = new int[198];
        var white_triangles = new int[96];
        int orange_count = 0;
        for (int ring = 0; ring < 4; ring++)
        {
            float height = ring * 0.12f;
            float radius = 0.15f * (1f - ring / 3f) + 0.012f;
            for (int i = 0; i < 16; i++)
            {
                float angle = i * MathF.PI / 8f;
                vertices[ring * 16 + i] = new Vector3(MathF.Cos(angle) * radius, height, MathF.Sin(angle) * radius);
                if (ring == 3) continue;
                int a = ring * 16 + i;
                int b = ring * 16 + (i + 1) % 16;
                int[] triangles = ring == 1 ? white_triangles : orange_triangles;
                int cursor = ring == 1 ? i * 6 : orange_count;
                triangles[cursor] = a;
                triangles[cursor + 1] = a + 16;
                triangles[cursor + 2] = b;
                triangles[cursor + 3] = b;
                triangles[cursor + 4] = a + 16;
                triangles[cursor + 5] = b + 16;
                if (ring != 1) orange_count += 6;
            }
        }
        vertices[64] = new Vector3(-0.2f, 0, -0.2f);
        vertices[65] = new Vector3(-0.2f, 0, 0.2f);
        vertices[66] = new Vector3(0.2f, 0, 0.2f);
        vertices[67] = new Vector3(0.2f, 0, -0.2f);
        int[] base_triangles = { 64, 65, 66, 64, 66, 67 };
        Array.Copy(base_triangles, 0, orange_triangles, orange_count, 6);
        mesh = new Mesh { vertices = vertices, subMeshCount = 2 };
        mesh.SetTriangles(orange_triangles, 0);
        mesh.SetTriangles(white_triangles, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        hat = new GameObject("CoordinatedPolice.Cone");
        hat.AddComponent<MeshFilter>().sharedMesh = mesh;
        hat.AddComponent<MeshRenderer>().sharedMaterials = new[] { orange, white };
    }
}

using System.Collections.Generic;
using UnityEngine;

public static class Art
{
    static Material baseMat;
    static readonly Dictionary<Color, Material> cache = new();

    public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    public static Material M(Color c)
    {
        if (cache.TryGetValue(c, out var m)) return m;
        baseMat ??= Resources.Load<Material>("Base");
        m = new Material(baseMat) { color = c };
        cache[c] = m;
        return m;
    }

    public static GameObject Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Color c, bool collider = false)
    {
        var g = GameObject.CreatePrimitive(t);
        if (!collider) Object.Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = M(c);
        return g;
    }
}

public class Chibi : MonoBehaviour
{
    public Transform body;
    public float speed01;
    public bool cheer;
    float phase;

    void Start() { phase = Random.value * 10; }

    void Update()
    {
        phase += Time.deltaTime * (cheer ? 14 : 3 + speed01 * 9);
        float b = Mathf.Sin(phase);
        float sq = 1 + b * (cheer ? 0.08f : 0.03f + speed01 * 0.03f);
        body.localScale = new Vector3(2 - sq, sq, 2 - sq);
        body.localPosition = new Vector3(0, Mathf.Max(0, b) * (cheer ? 0.35f : speed01 * 0.12f), 0);
    }

    // kind: player, boss, hr, landlord, mentor
    public static Chibi Make(string kind, Vector3 pos, float yaw)
    {
        var root = new GameObject("Chibi_" + kind);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0, yaw, 0);
        var body = new GameObject("body").transform;
        body.SetParent(root.transform, false);
        var ch = root.AddComponent<Chibi>();
        ch.body = body;

        Color skin = Art.Hex("#FFD9B8"), outfit, feet = Art.Hex("#4A3B3B");
        switch (kind)
        {
            case "boss": outfit = Art.Hex("#3D5A99"); skin = Art.Hex("#FFC9A3"); break;
            case "hr": outfit = Art.Hex("#B57EDC"); break;
            case "landlord": outfit = Art.Hex("#6BAA6B"); skin = Art.Hex("#F2C29B"); break;
            case "mentor": outfit = Art.Hex("#2B2B3A"); break;
            default: outfit = Art.Hex("#3CC6B5"); break;
        }
        float k = kind == "boss" ? 1.15f : 1f;
        var B = body;
        Art.Prim(PrimitiveType.Sphere, B, new Vector3(0, 0.55f, 0), new Vector3(0.95f * k, 0.85f, 0.85f * k), outfit);
        Art.Prim(PrimitiveType.Sphere, B, new Vector3(0, 1.45f, 0), new Vector3(1.05f, 1f, 1f), skin);
        Art.Prim(PrimitiveType.Sphere, B, new Vector3(-0.22f, 0.1f, 0.05f), new Vector3(0.3f, 0.2f, 0.4f), feet);
        Art.Prim(PrimitiveType.Sphere, B, new Vector3(0.22f, 0.1f, 0.05f), new Vector3(0.3f, 0.2f, 0.4f), feet);
        Art.Prim(PrimitiveType.Sphere, B, new Vector3(-0.5f * k, 0.6f, 0.05f), new Vector3(0.25f, 0.4f, 0.25f), outfit);
        Art.Prim(PrimitiveType.Sphere, B, new Vector3(0.5f * k, 0.6f, 0.05f), new Vector3(0.25f, 0.4f, 0.25f), outfit);
        // face
        foreach (var sx in new[] { -1f, 1f })
        {
            Art.Prim(PrimitiveType.Sphere, B, new Vector3(0.19f * sx, 1.5f, 0.44f), new Vector3(0.17f, 0.22f, 0.1f), Art.Hex("#222222"));
            Art.Prim(PrimitiveType.Sphere, B, new Vector3(0.19f * sx + 0.03f, 1.55f, 0.49f), new Vector3(0.06f, 0.06f, 0.03f), Color.white);
            Art.Prim(PrimitiveType.Sphere, B, new Vector3(0.32f * sx, 1.33f, 0.4f), new Vector3(0.14f, 0.08f, 0.05f), Art.Hex("#FF9AA8"));
        }
        Art.Prim(PrimitiveType.Sphere, B, new Vector3(0, 1.32f, 0.48f), new Vector3(0.12f, 0.05f, 0.04f), Art.Hex("#B8505A"));

        switch (kind)
        {
            case "player":
                Art.Prim(PrimitiveType.Sphere, B, new Vector3(0, 1.75f, -0.08f), new Vector3(1.1f, 0.6f, 1.05f), Art.Hex("#6B4226"));
                break;
            case "boss":
                Art.Prim(PrimitiveType.Sphere, B, new Vector3(-0.48f, 1.6f, -0.05f), new Vector3(0.25f, 0.35f, 0.5f), Art.Hex("#BBBBBB"));
                Art.Prim(PrimitiveType.Sphere, B, new Vector3(0.48f, 1.6f, -0.05f), new Vector3(0.25f, 0.35f, 0.5f), Art.Hex("#BBBBBB"));
                var bl = Art.Prim(PrimitiveType.Cube, B, new Vector3(-0.2f, 1.7f, 0.45f), new Vector3(0.25f, 0.06f, 0.05f), Art.Hex("#333333"));
                bl.transform.localRotation = Quaternion.Euler(0, 0, -20);
                var br = Art.Prim(PrimitiveType.Cube, B, new Vector3(0.2f, 1.7f, 0.45f), new Vector3(0.25f, 0.06f, 0.05f), Art.Hex("#333333"));
                br.transform.localRotation = Quaternion.Euler(0, 0, 20);
                Art.Prim(PrimitiveType.Cube, B, new Vector3(0, 0.75f, 0.42f), new Vector3(0.14f, 0.45f, 0.05f), Art.Hex("#E63946"));
                Art.Prim(PrimitiveType.Cylinder, B, new Vector3(0.65f, 0.7f, 0.2f), new Vector3(0.22f, 0.13f, 0.22f), Color.white);
                break;
            case "hr":
                Art.Prim(PrimitiveType.Sphere, B, new Vector3(0, 1.72f, -0.08f), new Vector3(1.1f, 0.6f, 1.05f), Art.Hex("#E07A3F"));
                Art.Prim(PrimitiveType.Sphere, B, new Vector3(0, 2.05f, -0.15f), new Vector3(0.45f, 0.45f, 0.45f), Art.Hex("#E07A3F"));
                foreach (var sx in new[] { -1f, 1f })
                    Art.Prim(PrimitiveType.Cylinder, B, new Vector3(0.19f * sx, 1.5f, 0.47f), new Vector3(0.3f, 0.01f, 0.3f), Art.Hex("#E91E63")).transform.localRotation = Quaternion.Euler(90, 0, 0);
                Art.Prim(PrimitiveType.Cube, B, new Vector3(0.6f, 0.65f, 0.3f), new Vector3(0.35f, 0.45f, 0.05f), Art.Hex("#8D6E63"));
                break;
            case "landlord":
                Art.Prim(PrimitiveType.Cylinder, B, new Vector3(0, 2.0f, 0), new Vector3(0.7f, 0.3f, 0.7f), Art.Hex("#222222"));
                Art.Prim(PrimitiveType.Cylinder, B, new Vector3(0, 1.85f, 0), new Vector3(1.1f, 0.03f, 1.1f), Art.Hex("#222222"));
                Art.Prim(PrimitiveType.Sphere, B, new Vector3(-0.12f, 1.4f, 0.5f), new Vector3(0.22f, 0.07f, 0.06f), Art.Hex("#5D4037"));
                Art.Prim(PrimitiveType.Sphere, B, new Vector3(0.12f, 1.4f, 0.5f), new Vector3(0.22f, 0.07f, 0.06f), Art.Hex("#5D4037"));
                Art.Prim(PrimitiveType.Cube, B, new Vector3(0.62f, 0.6f, 0.25f), new Vector3(0.08f, 0.3f, 0.04f), Art.Hex("#FFC107"));
                break;
            case "mentor":
                Art.Prim(PrimitiveType.Sphere, B, new Vector3(0, 1.72f, -0.08f), new Vector3(1.08f, 0.55f, 1.02f), Art.Hex("#8A8A8A"));
                Art.Prim(PrimitiveType.Cube, B, new Vector3(0, 0.85f, 0.4f), new Vector3(0.3f, 0.25f, 0.06f), Color.white);
                Art.Prim(PrimitiveType.Sphere, B, new Vector3(-0.1f, 1.38f, 0.5f), new Vector3(0.2f, 0.07f, 0.06f), Art.Hex("#5A5A5A"));
                Art.Prim(PrimitiveType.Sphere, B, new Vector3(0.1f, 1.38f, 0.5f), new Vector3(0.2f, 0.07f, 0.06f), Art.Hex("#5A5A5A"));
                Art.Prim(PrimitiveType.Cube, B, new Vector3(0.55f, 0.6f, 0.2f), new Vector3(0.12f, 0.4f, 0.3f), Art.Hex("#B71C1C"));
                break;
        }
        return ch;
    }
}

public class Floaty : MonoBehaviour
{
    public float baseY = 1f;
    public bool seen;
    Transform ring;
    float t;
    public void SetRing(Transform r) { ring = r; }

    void Update()
    {
        t += Time.deltaTime;
        transform.GetChild(0).localPosition = new Vector3(0, baseY + Mathf.Sin(t * 2) * 0.12f, 0);
        transform.GetChild(0).localRotation = Quaternion.Euler(0, t * 60, 0);
        if (ring)
        {
            float s = seen ? 1.2f : 1.3f + Mathf.Sin(t * 4) * 0.15f;
            ring.localScale = new Vector3(s, 0.02f, s);
        }
    }
}

public class Door : MonoBehaviour
{
    public bool open;
    void Update()
    {
        var p = transform.position;
        p.y = Mathf.MoveTowards(p.y, open ? 4.2f : 1.25f, Time.deltaTime * 3);
        transform.position = p;
    }
}

public class Follower : MonoBehaviour
{
    public Transform target;
    float t;
    void Update()
    {
        if (!target) return;
        t += Time.deltaTime;
        var want = target.position + target.right * -1.1f + Vector3.up * (0.9f + Mathf.Sin(t * 2.5f) * 0.15f) - target.forward * 0.4f;
        transform.position = Vector3.Lerp(transform.position, want, Time.deltaTime * 4);
        var look = target.position + target.forward * 3 - transform.position; look.y = 0;
        if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), Time.deltaTime * 5);
    }
}

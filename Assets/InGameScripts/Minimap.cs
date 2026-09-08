using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tiny bottom-left radar showing every live enemy relative to the player.
///
/// Drop this on the Manager GameObject and assign <see cref="player"/> (or leave
/// it null and it finds the Player tag). Everything else - canvas, sprites, dots
/// - is built at runtime, so there is nothing to wire up in the scene.
///
/// Up on the minimap is the direction the player is facing. Enemies beyond
/// <see cref="worldRange"/> are pinned to the rim in a dimmer colour so you still
/// get a bearing on them.
/// </summary>
public class Minimap : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public EnemyManager enemyManager;

    [Header("Toggle")]
    public bool showMinimap = true;
    [Tooltip("Set to None to disable the runtime toggle.")]
    public KeyCode toggleKey = KeyCode.None;

    [Header("Layout")]
    [Tooltip("Diameter of the radar in pixels (reference resolution 1920x1080).")]
    public float sizePixels = 170f;
    [Tooltip("Distance from the bottom-left screen corner in pixels.")]
    public Vector2 margin = new Vector2(24f, 24f);

    [Header("Scale")]
    [Tooltip("World-space radius (metres) mapped to the radar rim.")]
    public float worldRange = 40f;

    [Header("Dots")]
    public float enemyDotSize = 11f;
    public float playerMarkerSize = 15f;
    public Color enemyDotColor = new Color(1f, 0.85f, 0.15f, 1f);   // matches the yellow alert
    public Color enemyOffRangeColor = new Color(1f, 0.55f, 0.1f, 0.55f);
    public Color playerMarkerColor = new Color(0.35f, 0.9f, 1f, 1f);
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.45f);
    public Color ringColor = new Color(1f, 1f, 1f, 0.28f);

    [Header("Canvas")]
    [Tooltip("Sorting order. High so the radar stays above the HUD.")]
    public int sortingOrder = 500;

    // ---- Runtime ----
    Canvas canvas;
    RectTransform root;
    RectTransform dotParent;
    RectTransform playerMarker;

    Sprite circleSprite;
    Sprite ringSprite;
    Sprite arrowSprite;

    readonly List<Image> dotPool = new List<Image>();

    float Radius { get { return sizePixels * 0.5f; } }

    void Start()
    {
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (enemyManager == null) enemyManager = EnemyManager.Instance;
        if (enemyManager == null) enemyManager = FindObjectOfType<EnemyManager>();

        BuildUI();
    }

    void OnDestroy()
    {
        if (circleSprite != null) Destroy(circleSprite.texture);
        if (ringSprite != null) Destroy(ringSprite.texture);
        if (arrowSprite != null) Destroy(arrowSprite.texture);
    }

    void LateUpdate()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
            showMinimap = !showMinimap;

        if (root == null) return;

        if (!showMinimap || player == null)
        {
            if (root.gameObject.activeSelf) root.gameObject.SetActive(false);
            return;
        }
        if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);

        UpdateDots();
    }

    void UpdateDots()
    {
        if (enemyManager == null) enemyManager = EnemyManager.Instance;

        List<Enemy> enemies = enemyManager != null ? enemyManager.GetAllEnemies() : null;
        int used = 0;

        if (enemies != null && worldRange > 0f)
        {
            Vector3 origin = player.position;

            // Rotate world offsets into player space so "up" is where they look.
            float yaw = player.eulerAngles.y * Mathf.Deg2Rad;
            float sin = Mathf.Sin(yaw);
            float cos = Mathf.Cos(yaw);

            float pixelsPerMetre = Radius / worldRange;

            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy e = enemies[i];
                if (e == null) continue;

                Vector3 d = e.transform.position - origin;

                // Inverse yaw rotation on the XZ plane.
                float localX = d.x * cos - d.z * sin;
                float localZ = d.x * sin + d.z * cos;

                Vector2 p = new Vector2(localX, localZ) * pixelsPerMetre;

                // Keep the rim clear enough that a dot on the edge stays visible.
                float maxR = Radius - enemyDotSize * 0.5f - 2f;
                bool offRange = p.magnitude > maxR;
                if (offRange) p = p.normalized * maxR;

                Image dot = GetDot(used++);
                dot.rectTransform.anchoredPosition = p;
                dot.color = offRange ? enemyOffRangeColor : enemyDotColor;
            }
        }

        for (int i = used; i < dotPool.Count; i++)
            if (dotPool[i].gameObject.activeSelf) dotPool[i].gameObject.SetActive(false);
    }

    Image GetDot(int index)
    {
        while (dotPool.Count <= index)
        {
            Image img = NewImage("EnemyDot", dotParent, circleSprite, enemyDotColor);
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            img.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            img.rectTransform.sizeDelta = new Vector2(enemyDotSize, enemyDotSize);
            dotPool.Add(img);
        }

        Image dot = dotPool[index];
        if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
        return dot;
    }

    // =====================================================================
    //  UI CONSTRUCTION
    // =====================================================================

    void BuildUI()
    {
        circleSprite = MakeCircleSprite(128, 0f);
        ringSprite = MakeCircleSprite(128, 0.9f);
        arrowSprite = MakeArrowSprite(64);

        GameObject canvasGO = new GameObject("MinimapCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);

        canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Background disc, anchored to the bottom-left corner.
        Image bg = NewImage("MinimapRoot", canvasGO.GetComponent<RectTransform>(), circleSprite, backgroundColor);
        root = bg.rectTransform;
        root.anchorMin = root.anchorMax = Vector2.zero;
        root.pivot = Vector2.zero;
        root.sizeDelta = new Vector2(sizePixels, sizePixels);
        root.anchoredPosition = margin;

        Image ring = NewImage("Ring", root, ringSprite, ringColor);
        Stretch(ring.rectTransform);

        GameObject dotParentGO = new GameObject("Dots", typeof(RectTransform));
        dotParent = dotParentGO.GetComponent<RectTransform>();
        dotParent.SetParent(root, false);
        Stretch(dotParent);

        Image marker = NewImage("PlayerMarker", root, arrowSprite, playerMarkerColor);
        playerMarker = marker.rectTransform;
        playerMarker.anchorMin = playerMarker.anchorMax = new Vector2(0.5f, 0.5f);
        playerMarker.pivot = new Vector2(0.5f, 0.5f);
        playerMarker.sizeDelta = new Vector2(playerMarkerSize, playerMarkerSize);
        playerMarker.anchoredPosition = Vector2.zero;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;   // the radar never eats clicks
        return img;
    }

    /// <summary>
    /// Filled disc when innerRadius01 is 0, a ring when it is between 0 and 1.
    /// </summary>
    static Sprite MakeCircleSprite(int size, float innerRadius01)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        float r = size * 0.5f;
        float inner = r * innerRadius01;
        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r;
                float dy = y + 0.5f - r;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                // 1px feather on both edges so the disc does not look jagged.
                float a = Mathf.Clamp01(r - dist);
                if (inner > 0f) a = Mathf.Min(a, Mathf.Clamp01(dist - inner));

                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    /// <summary>Upward pointing triangle for the player marker.</summary>
    static Sprite MakeArrowSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            float t = (float)y / (size - 1);           // 0 at the base, 1 at the tip
            float halfWidth = (1f - t) * size * 0.5f;  // narrows toward the tip
            float centre = size * 0.5f;

            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - centre);
                float a = Mathf.Clamp01(halfWidth - dx);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }
}

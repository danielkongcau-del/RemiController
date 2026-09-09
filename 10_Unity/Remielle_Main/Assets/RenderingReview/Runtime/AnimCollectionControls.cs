// zcode 2026-09-07, AnimCollection scene: per-group accessory visibility
// toggles. Defaults follow the native display/store seven-mesh set (body,
// face, eyebrow, hair, hair-shadow, wings visible; every attachment hidden —
// the display chain does not draw them). Groups mirror the runtime event
// granularity from visibility-events.json: the QTE weapon quintet toggles
// under one label atomically, Weapon_01/05 are never event-driven, cannons
// are wing-mounted visual parts, Floater_02 has its own label. The prefab
// keeps all renderers; GameObject names in the prefab carry an SMR_ prefix.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AnimCollectionControls : MonoBehaviour
{
    static readonly string[] QteWeapons = { "Remielle_Weapon_02_L", "Remielle_Weapon_02_R", "Remielle_Weapon_03_L", "Remielle_Weapon_03_R", "Remielle_Weapon_04" };
    static readonly string[] AlwaysWeapons = { "Remielle_Weapon_01", "Remielle_Weapon_05_L", "Remielle_Weapon_05_R" };

    // Initial state mirrors the native display seven-mesh set.
    public bool wingsVisible = true;
    public bool qteWeaponsVisible = false;
    public bool alwaysWeaponsVisible = false;
    public bool cannonsVisible = false;
    public bool stickersVisible = false;
    public bool floaterVisible = false;

    Dictionary<string, Renderer> renderersByPart;
    Dictionary<string, bool> defaultState;

    public int RuntimePartCount => renderersByPart != null ? renderersByPart.Count : -1;

    public bool VerifyRuntimeDefaults()
    {
        if (renderersByPart == null) return false;
        foreach (var pair in renderersByPart)
            if (pair.Value.enabled != defaultState[pair.Key]) return false;
        return true;
    }

    void Start()
    {
        // Prefab part nodes carry an SMR_ prefix over the source mesh name.
        renderersByPart = GetComponentsInChildren<Renderer>(true)
            .Where(r => r.name.StartsWith("SMR_") && r.name.Length > 4)
            .ToDictionary(r => r.name.Substring(4), r => r);
        Apply();
        // Snapshot the default visible set so the runtime audit can detect
        // any component failing to apply the display-set defaults.
        defaultState = renderersByPart.ToDictionary(p => p.Key, p => p.Value.enabled);
    }

    public void Apply()
    {
        if (renderersByPart == null) return;
        foreach (var pair in renderersByPart)
        {
            bool on;
            if (pair.Key == "Remielle_Wings") on = wingsVisible;
            else if (QteWeapons.Contains(pair.Key)) on = qteWeaponsVisible;
            else if (AlwaysWeapons.Contains(pair.Key)) on = alwaysWeaponsVisible;
            else if (pair.Key.StartsWith("Remielle_Cannon_")) on = cannonsVisible;
            else if (pair.Key.StartsWith("Remielle_Sticker_")) on = stickersVisible;
            else if (pair.Key == "Remielle_Floater_02") on = floaterVisible;
            else on = true; // body/face/eyebrow/hair/hair-shadow stay visible
            if (pair.Value.enabled != on) pair.Value.enabled = on;
        }
    }

    public void SetAllAttachments(bool on)
    {
        wingsVisible = qteWeaponsVisible = alwaysWeaponsVisible = cannonsVisible = stickersVisible = floaterVisible = on;
        Apply();
    }

    void OnGUI()
    {
        // Column to the right of the review panel (which occupies
        // x 12..232, full height) — no overlap, same top alignment.
        // Content height: label + 6 toggles + button row + box padding.
        GUILayout.BeginArea(new Rect(244f, 12f, 260f, 212f), GUI.skin.box);
        GUILayout.Label("AnimCollection parts");
        Toggle("Wings", ref wingsVisible);
        Toggle("QTE weapons (02/03 L-R, 04)", ref qteWeaponsVisible);
        Toggle("Rest weapons (01, 05 L-R)", ref alwaysWeaponsVisible);
        Toggle("Cannons x8", ref cannonsVisible);
        Toggle("Stickers", ref stickersVisible);
        Toggle("Floater_02", ref floaterVisible);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("All on")) SetAllAttachments(true);
        if (GUILayout.Button("Display default")) { wingsVisible = true; qteWeaponsVisible = alwaysWeaponsVisible = cannonsVisible = stickersVisible = floaterVisible = false; Apply(); }
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    void Toggle(string label, ref bool value)
    {
        bool next = GUILayout.Toggle(value, label);
        if (next != value) { value = next; Apply(); }
    }
}

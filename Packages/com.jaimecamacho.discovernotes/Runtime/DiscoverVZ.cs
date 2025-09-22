using UnityEngine;

/// <summary>
/// DiscoverVZ (legacy): mantenido para compatibilidad. Toda la funcionalidad vive ahora
/// dentro de <see cref="GameObjectNotes"/>.
/// </summary>
[AddComponentMenu("VirtualZone/DiscoverVZ (Legacy)")]
[DisallowMultipleComponent]
[
    System.Obsolete(
        "DiscoverVZ ha sido fusionado con GameObjectNotes. Usa GameObjectNotes para " +
        "crear y mantener la documentación visual.")
]
public class DiscoverVZ : MonoBehaviour
{
    public string discoverName = "Discover";
    public DiscoverCategory category = DiscoverCategory.Other;
    public Texture2D image;
    [TextArea] public string description = "Description of the component.";
    public System.Collections.Generic.List<DiscoverSection> sections = new System.Collections.Generic.List<DiscoverSection>();

#if UNITY_EDITOR
    void Reset()
    {
        if (GetComponent<GameObjectNotes>() == null)
        {
            UnityEditor.Undo.AddComponent<GameObjectNotes>(gameObject);
        }
    }

    [ContextMenu("Convertir a GameObjectNotes y eliminar")]
    public void ConvertAndRemove()
    {
        var notes = GetComponent<GameObjectNotes>();
        if (notes == null) notes = gameObject.AddComponent<GameObjectNotes>();

        notes.ImportFromDiscoverVZ();
        UnityEditor.Undo.DestroyObjectImmediate(this);
    }
#endif
}

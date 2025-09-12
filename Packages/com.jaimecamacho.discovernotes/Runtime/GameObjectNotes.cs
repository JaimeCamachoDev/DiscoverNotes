using UnityEngine;
using System;

public class GameObjectNotes : MonoBehaviour
{
    public enum DisplayMode { Edit = 0, Fixed = 1 }

    [Header("Notas del GameObject")]
    [SerializeField] private string author = "";
    [SerializeField] private string dateCreated = ""; // dd/MM/yyyy
    [SerializeField] private string category = "Info";

    [Space(8)]
    [TextArea(3, 15)]
    [SerializeField] private string notes = "";

    [Header("Visualización")]
    [SerializeField] private DisplayMode displayMode = DisplayMode.Edit;

    [Header("Mostrar tooltip en Jerarquía")]
    [SerializeField] private bool showInHierarchy = true;

    // Propiedades públicas
    public string Author => author;
    public string DateCreated => dateCreated;
    public string Category => category;
    public string NotesText => notes;
    public DisplayMode Mode => displayMode;

    // El tooltip y la vista fija aplican rich text sólo en modo Fixed
    public bool RenderRichText => displayMode == DisplayMode.Fixed;

    public bool ShowInHierarchy => showInHierarchy;

#if UNITY_EDITOR
    void OnValidate() => CoerceDefaults();
#endif

    void Awake() => CoerceDefaults();

    void CoerceDefaults()
    {
        if (string.IsNullOrEmpty(dateCreated))
            dateCreated = DateTime.Now.ToString("dd/MM/yyyy");

#if UNITY_EDITOR
        var authors = NoteStylesProvider.GetAuthors();
        if (authors != null && authors.Length > 0)
        {
            if (string.IsNullOrEmpty(author) || Array.IndexOf(authors, author) < 0)
                author = authors[0];
        }

        var cats = NoteStylesProvider.GetCategoryNames();
        if (cats != null && cats.Length > 0)
        {
            if (string.IsNullOrEmpty(category) || Array.IndexOf(cats, category) < 0)
                category = cats[0];
        }
#endif
    }
}

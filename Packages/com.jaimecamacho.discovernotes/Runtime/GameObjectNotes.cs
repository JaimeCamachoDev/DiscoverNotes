using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static GameObjectNotes;

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

    [Serializable]
    public class NoteData
    {
        public string author = "";
        public string dateCreated = "";         // dd/MM/yyyy
        public string category = "Info";

        public string discoverName = string.Empty;
        public DiscoverCategory discoverCategory = DiscoverCategory.Other;

        [TextArea(2, 10)]
        public string discoverSummary = "";

        [Header("Contenido estructurado")]
        public List<DiscoverSection> discoverSections = new List<DiscoverSection>();

        [Header("Notas detalladas (texto plano)")]
        [TextArea(3, 15)]
        public string notes = "";

        public DisplayMode displayMode = DisplayMode.Edit;

        // Se podrá ocultar/editar independiente del resto (campo por nota)
        public bool showInHierarchy = true;

        public bool HasDiscoverContent()
        {
            return !string.IsNullOrEmpty(discoverSummary)
                || (discoverSections != null && discoverSections.Count > 0);
        }
    }

    // Nueva lista de notas
    [SerializeField] private List<NoteData> notesList = new List<NoteData>();
    public IReadOnlyList<NoteData> NotesList => notesList;

    // Regla de borrado: Fixed + cuerpo vacío
    public static bool IsDeleted(NoteData n)
        => n != null
           && n.displayMode == DisplayMode.Fixed
           && string.IsNullOrEmpty(n.notes)
           && string.IsNullOrEmpty(n.discoverSummary)
           && (n.discoverSections == null || n.discoverSections.Count == 0);


#if UNITY_EDITOR
    void OnValidate() => CoerceDefaults();
#endif

    void Awake() => CoerceDefaults();

    void CoerceDefaults()
    {
        // --- MIGRACIÓN DESDE CAMPOS LEGACY (si existían y la lista está vacía) ---
        if ((notesList == null || notesList.Count == 0) &&
            (!string.IsNullOrEmpty(notes) || !string.IsNullOrEmpty(author) || !string.IsNullOrEmpty(category)))
        {
            if (notesList == null) notesList = new List<NoteData>();
            notesList.Add(new NoteData
            {
                author = author ?? "",
                dateCreated = string.IsNullOrEmpty(dateCreated)
                    ? DateTime.Now.ToString("dd/MM/yyyy") : dateCreated,
                category = string.IsNullOrEmpty(category) ? "Info" : category,
                notes = notes ?? "",
                displayMode = displayMode,
                showInHierarchy = showInHierarchy
            });
        }

        if (notesList == null) notesList = new List<NoteData>();

        // --- NORMALIZACIÓN POR NOTA ---
        for (int i = 0; i < notesList.Count; i++)
        {
            var n = notesList[i];
            if (n == null) { notesList[i] = n = new NoteData(); }

            if (string.IsNullOrEmpty(n.dateCreated))
                n.dateCreated = DateTime.Now.ToString("dd/MM/yyyy");

            if (string.IsNullOrEmpty(n.discoverName) || n.discoverName == "Discover")
                n.discoverName = gameObject.name;

            if (n.discoverSections == null)
                n.discoverSections = new List<DiscoverSection>();

            for (int s = 0; s < n.discoverSections.Count; s++)
            {
                var section = n.discoverSections[s];
                if (section == null) { n.discoverSections[s] = section = new DiscoverSection(); }
                if (section.actions == null) section.actions = new List<DiscoverAction>();
            }

#if UNITY_EDITOR
            var authors = NoteStylesProvider.GetAuthors();
            if (authors != null && authors.Length > 0)
            {
                if (string.IsNullOrEmpty(n.author) || Array.IndexOf(authors, n.author) < 0)
                    n.author = authors[0];
            }

            var cats = NoteStylesProvider.GetCategoryNames();
            if (cats != null && cats.Length > 0)
            {
                if (string.IsNullOrEmpty(n.category) || Array.IndexOf(cats, n.category) < 0)
                    n.category = cats[0];
            }
#endif
        }
        PruneDeletedNotes();

    }
    public bool PruneDeletedNotes()
    {
        if (notesList == null) return false;
        int before = notesList.Count;
        notesList.RemoveAll(n => IsDeleted(n)); // Fixed + cuerpo vacío
        return notesList.Count != before;
    }


#if UNITY_EDITOR
    //using UnityEditor;

    [MenuItem("CONTEXT/GameObjectNotes/Añadir nota")]
    private static void Ctx_AddNote(MenuCommand cmd)
    {
        var comp = (GameObjectNotes)cmd.context;
        Undo.RecordObject(comp, "Añadir nota");

        if (comp.notesList == null) comp.notesList = new List<NoteData>();
        var n = new NoteData
        {
            author = (NoteStylesProvider.GetAuthors()?.Length ?? 0) > 0
                     ? NoteStylesProvider.GetAuthors()[0] : "",
            dateCreated = DateTime.Now.ToString("dd/MM/yyyy"),
            category = (NoteStylesProvider.GetCategoryNames()?.Length ?? 0) > 0
                       ? NoteStylesProvider.GetCategoryNames()[0] : "Info",
            discoverName = comp.gameObject.name,
            discoverCategory = DiscoverCategory.Other,
            discoverSummary = string.Empty,
            discoverSections = new List<DiscoverSection>(),
            notes = string.Empty,
            displayMode = DisplayMode.Edit,
            showInHierarchy = true
        };
        comp.notesList.Add(n);

        EditorUtility.SetDirty(comp);
    }

    #endif
}

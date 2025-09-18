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

    [Header("Visualizaci�n")]
    [SerializeField] private DisplayMode displayMode = DisplayMode.Edit;

    [Header("Mostrar tooltip en Jerarqu�a")]
    [SerializeField] private bool showInHierarchy = true;

    // Propiedades p�blicas
    public string Author => author;
    public string DateCreated => dateCreated;
    public string Category => category;
    public string NotesText => notes;
    public DisplayMode Mode => displayMode;

    // El tooltip y la vista fija aplican rich text s�lo en modo Fixed
    public bool RenderRichText => displayMode == DisplayMode.Fixed;

    public bool ShowInHierarchy => showInHierarchy;

    [Serializable]
    public class NoteData
    {
        public string author = "";
        public string dateCreated = "";         // dd/MM/yyyy
        public string category = "Info";

        [TextArea(3, 15)]
        public string notes = "";

        public DisplayMode displayMode = DisplayMode.Edit;

        // Se podr� ocultar/editar independiente del resto (campo por nota)
        public bool showInHierarchy = true;
    }

    // Nueva lista de notas
    [SerializeField] private List<NoteData> notesList = new List<NoteData>();
    public IReadOnlyList<NoteData> NotesList => notesList;

    // Regla de borrado: Fixed + cuerpo vac�o
    public static bool IsDeleted(NoteData n)
        => n != null && n.displayMode == DisplayMode.Fixed && string.IsNullOrEmpty(n.notes);


#if UNITY_EDITOR
    void OnValidate() => CoerceDefaults();
#endif

    void Awake() => CoerceDefaults();

    void CoerceDefaults()
    {
        // --- MIGRACI�N DESDE CAMPOS LEGACY (si exist�an y la lista est� vac�a) ---
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

        // --- NORMALIZACI�N POR NOTA ---
        for (int i = 0; i < notesList.Count; i++)
        {
            var n = notesList[i];
            if (n == null) { notesList[i] = n = new NoteData(); }

            if (string.IsNullOrEmpty(n.dateCreated))
                n.dateCreated = DateTime.Now.ToString("dd/MM/yyyy");

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
        notesList.RemoveAll(n => IsDeleted(n)); // Fixed + cuerpo vac�o
        return notesList.Count != before;
    }


#if UNITY_EDITOR
    //using UnityEditor;

    [MenuItem("CONTEXT/GameObjectNotes/A�adir nota")]
    private static void Ctx_AddNote(MenuCommand cmd)
    {
        var comp = (GameObjectNotes)cmd.context;
        Undo.RecordObject(comp, "A�adir nota");

        if (comp.notesList == null) comp.notesList = new List<NoteData>();
        var n = new NoteData
        {
            author = (NoteStylesProvider.GetAuthors()?.Length ?? 0) > 0
                     ? NoteStylesProvider.GetAuthors()[0] : "",
            dateCreated = DateTime.Now.ToString("dd/MM/yyyy"),
            category = (NoteStylesProvider.GetCategoryNames()?.Length ?? 0) > 0
                       ? NoteStylesProvider.GetCategoryNames()[0] : "Info",
            notes = "",
            displayMode = DisplayMode.Edit,
            showInHierarchy = true
        };
        comp.notesList.Add(n);

        EditorUtility.SetDirty(comp);
    }
    #endif
}

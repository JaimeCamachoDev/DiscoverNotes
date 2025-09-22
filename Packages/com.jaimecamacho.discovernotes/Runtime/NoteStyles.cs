using UnityEngine;
using System;
using System.Collections.Generic;

[Serializable]
public class NoteCategory
{
    public string name = "Info";
    public Texture2D icon;
    public Color cardBackground = new Color(0.12f, 0.12f, 0.14f, 0.3f);
    public Color tooltipAccentBar = new Color(0.28f, 0.55f, 1.00f, 1f);
    public Color tooltipBackground = new Color(0.12f, 0.12f, 0.14f, 1f);
}

public class NoteStyles : ScriptableObject
{
    [Header("Authors (dropdown)")]
    public List<string> authors = new List<string>
    {
        "Carlos","David","Avelino","Gaspar","Annimo"
    };

    [Header("Categories")]
    public List<NoteCategory> categories = new List<NoteCategory>
    {
        new NoteCategory{ name="Info" }, // No hace falta definir colores, usa los por defecto
        new NoteCategory{
            name="Warning",
            cardBackground=new Color(0.3f,0.3f,0.05f,0.6f),
            tooltipAccentBar=new Color(0.98f,0.78f,0.15f,1f),
            tooltipBackground=new Color(0.15f,0.13f,0.05f,1f)
        },
        new NoteCategory{
            name="Error",
            cardBackground=new Color(0.3f,0.08f,0.08f,0.6f),
            tooltipAccentBar=new Color(1.00f,0.40f,0.40f,1f),
            tooltipBackground=new Color(0.16f,0.08f,0.08f,1f)
        },
        new NoteCategory{
            name="Todo",
            cardBackground=new Color(0.60f,0.45f,1.00f,0.15f),
            tooltipAccentBar=new Color(0.60f,0.45f,1.00f,1f),
            tooltipBackground=new Color(0.10f,0.10f,0.15f,1f)
        },
        new NoteCategory{
            name="Important",
            cardBackground=new Color(1.00f,1.0f,1.0f,0.3f),
            tooltipAccentBar=new Color(1.00f,1f,1f,1f),
            tooltipBackground=new Color(0.6f,0.6f,0.6f,1f)
        }
    };

    [Header("Discover disciplines (dropdown)")]
    public List<string> discoverDisciplines = new List<string>
    {
        "FX","Audio","Gameplay","UI / UX", "Environment", "Systems / Code", "Workflow / Pipeline", "Other"
    };

#if UNITY_EDITOR
    void OnValidate()
    {
        NoteStylesProvider.NotifyStylesModified(this);
    }
#endif
}

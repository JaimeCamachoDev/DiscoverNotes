using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Categories used to classify Discover notes inside <see cref="GameObjectNotes"/>.
/// </summary>
public enum DiscoverCategory
{
    [InspectorName("FX")] FX,
    [InspectorName("Audio")] Audio,
    [InspectorName("Gameplay")] Gameplay,
    [InspectorName("UI / UX")] UIUX,
    [InspectorName("Environment")] Environment,
    [InspectorName("Systems / Code")] SystemsCode,
    [InspectorName("Workflow / Pipeline")] WorkflowPipeline,
    [InspectorName("Other")] Other
}

/// <summary>
/// Helper utilities shared by runtime and editor code to present Discover categories with
/// user-friendly labels.
/// </summary>
public static class DiscoverCategoryUtility
{
    static readonly DiscoverCategory[] s_values =
        (DiscoverCategory[])Enum.GetValues(typeof(DiscoverCategory));

    static readonly string[] s_displayNames = BuildDisplayNames();

    static string[] BuildDisplayNames()
    {
        var names = new string[s_values.Length];
        for (int i = 0; i < s_values.Length; i++)
            names[i] = GetDisplayName(s_values[i]);
        return names;
    }

    public static IReadOnlyList<DiscoverCategory> Values => s_values;

    public static string[] GetDisplayNamesCopy()
    {
        var copy = new string[s_displayNames.Length];
        Array.Copy(s_displayNames, copy, copy.Length);
        return copy;
    }

    public static string GetDisplayName(DiscoverCategory category)
    {
        switch (category)
        {
            case DiscoverCategory.FX:
                return "FX";
            case DiscoverCategory.Audio:
                return "Audio";
            case DiscoverCategory.Gameplay:
                return "Gameplay";
            case DiscoverCategory.UIUX:
                return "UI / UX";
            case DiscoverCategory.Environment:
                return "Environment";
            case DiscoverCategory.SystemsCode:
                return "Systems / Code";
            case DiscoverCategory.WorkflowPipeline:
                return "Workflow / Pipeline";
            case DiscoverCategory.Other:
                return "Other";
        }

        var members = typeof(DiscoverCategory).GetMember(category.ToString());
        if (members != null && members.Length > 0)
        {
            var attr = Attribute.GetCustomAttribute(members[0], typeof(InspectorNameAttribute)) as InspectorNameAttribute;
            if (attr != null && !string.IsNullOrEmpty(attr.displayName))
                return attr.displayName;
        }

        // Fallback to enum name when no attribute is present.
        return category.ToString();
    }
}

/// <summary>
/// A section of structured information inside a Discover note. Sections allow grouping
/// rich descriptions and navigation actions.
/// </summary>
[Serializable]
public class DiscoverSection
{
    public string sectionName;

    // NUEVO: objeto objetivo de la sección
    public GameObject target;

    [TextArea(2, 6)]
    public string sectionContent;

    // Oculto: antiguo sistema de acciones (mantener temporalmente por compatibilidad)
    [HideInInspector, Obsolete("Actions eliminado. Usa 'target' en la sección.")]
    public List<DiscoverAction> actions = new List<DiscoverAction>();
}

/// <summary>
/// Interactive action rendered inside a Discover section.
/// </summary>
[Serializable]
public class DiscoverAction
{
    [Tooltip("Friendly description for the action button.")]
    public string description = "Select Target";

    [Tooltip("Target object that will be pinged and framed when the action is executed.")]
    public GameObject target;

    [Tooltip("Optional helper text to explain what to inspect once the target is selected.")]
    public string hint = string.Empty;
}

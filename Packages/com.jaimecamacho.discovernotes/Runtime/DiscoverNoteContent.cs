using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Categories used to classify Discover notes. Shared between the legacy DiscoverVZ
/// component and the new multi-note workflow inside <see cref="GameObjectNotes"/>.
/// </summary>
public enum DiscoverCategory
{
    VisualEffects,
    Audio,
    Gameplay,
    UI,
    Environment,
    Systems,
    Workflow,
    Other
}

/// <summary>
/// A section of structured information inside a Discover note. Sections allow grouping
/// rich descriptions, optional imagery and navigation actions.
/// </summary>
[Serializable]
public class DiscoverSection
{
    [Tooltip("Title displayed for this section inside the Discover note.")]
    public string sectionName = "Section";

    [Tooltip("Optional representative image. Useful for quick visual references or diagrams.")]
    public Texture2D image;

    [TextArea]
    [Tooltip("Long form description or Markdown-like content for the section.")]
    public string sectionContent = "Section Content";

    [Tooltip("Contextual actions such as scene jumps or asset selections associated with the section.")]
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

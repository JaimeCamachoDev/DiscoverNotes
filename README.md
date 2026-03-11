# DiscoverNotes

DiscoverNotes is a Unity package for adding visual, structured documentation directly to GameObjects and production scenes. It keeps implementation context, review notes, warnings, links, and team annotations close to the content they describe.

## Highlights

- Inspector-based note editing for GameObjects.
- Fixed and edit display modes.
- Categories, authors, and discipline tagging.
- Rich text, links, checklists, and inline image references.
- Hierarchy tooltip support for quick review.
- Structured Discover sections for production notes.
- VAT UV atlas tooling for visual reference workflows.
- Distributed as `com.jaimecamacho.discovernotes`.

## Installation

Install through Unity Package Manager with a Git URL:

```json
{
  "dependencies": {
    "com.jaimecamacho.discovernotes": "https://github.com/JaimeCamachoDev/DiscoverNotes.git?path=/Packages/com.jaimecamacho.discovernotes"
  }
}
```

You can also use `Window > Package Manager > Add package from git URL...` and paste:

```text
https://github.com/JaimeCamachoDev/DiscoverNotes.git?path=/Packages/com.jaimecamacho.discovernotes
```

## What It Solves

DiscoverNotes helps teams document decisions where they happen: inside the Unity Editor. Instead of scattering context across chats, tickets, and temporary comments, you can attach that information directly to the relevant GameObject.

Typical use cases:

- Explaining scene setup decisions.
- Marking warnings, blockers, and follow-up tasks.
- Linking to internal tools, docs, or external references.
- Leaving handoff notes for art, design, tech art, FX, audio, or gameplay.
- Building review-friendly notes for VAT and content validation workflows.

## Main Features

### GameObject Notes

The `GameObjectNotes` component lets you create one or more notes per object with:

- Title and summary fields.
- Category, discipline, and author metadata.
- Plain-text editing with formatted rendering in fixed mode.
- Support for links, checklist items, color tags, size tags, and inline references.

### Discover Sections

Each note can include structured sections to split information into clearer blocks for reviews, pipelines, and handoff documentation.

### Hierarchy Feedback

Notes can be surfaced in the Unity hierarchy through tooltip-style previews, which makes it easier to review important context without opening every object individually.

### Visual VAT Support

The package includes VAT UV atlas support for generating visual reference textures directly from note data.

## Quick Start

1. Import the package into your Unity project.
2. Add the `GameObjectNotes` component to any GameObject.
3. Create a note and assign its category, discipline, and author.
4. Write the note body and optional Discover sections.
5. Switch between edit mode and fixed mode to preview the final rendering.

## Markup Examples

DiscoverNotes supports lightweight authoring patterns inside notes:

```text
[tag](Carlos) escribe un [link externo](https://example.com/)

[check] Review collision boxes

[bold]Important[/bold]
[italics]Needs revision[/italics]

[color=red]High priority[/color]
[size=20]Large heading[/size]
```

## Screenshots

The README is prepared to host component screenshots under `Packages/com.jaimecamacho.discovernotes/Documentation~/images/`.

Suggested screenshot filenames:

- `discovernotes-manifest.png`
- `discovernotes-editor.png`
- `discovernotes-preview.png`

Suggested gallery block:

```md
![Package manifest](Packages/com.jaimecamacho.discovernotes/Documentation~/images/discovernotes-manifest.png)
![Component editor](Packages/com.jaimecamacho.discovernotes/Documentation~/images/discovernotes-editor.png)
![Rendered note preview](Packages/com.jaimecamacho.discovernotes/Documentation~/images/discovernotes-preview.png)
```

## Repository

- Repository: [JaimeCamachoDev/DiscoverNotes](https://github.com/JaimeCamachoDev/DiscoverNotes)
- Git URL: [https://github.com/JaimeCamachoDev/DiscoverNotes.git](https://github.com/JaimeCamachoDev/DiscoverNotes.git)
- Author: [Jaime Camacho](https://github.com/JaimeCamachoDev)
- License: [MIT](LICENSE)
- Changelog: [CHANGELOG.md](CHANGELOG.md)

## Unity Version

Current package metadata targets Unity `6000.0.0f1`.

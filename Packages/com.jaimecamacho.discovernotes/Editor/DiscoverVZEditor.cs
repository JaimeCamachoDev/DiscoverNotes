using UnityEditor;
using UnityEngine;

namespace DiscoverNotes.Editor
{
    [CustomEditor(typeof(DiscoverVZ))]
    public class DiscoverVZEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Displays a simple note on the DiscoverVZ component.", MessageType.Info);
        }
    }
}

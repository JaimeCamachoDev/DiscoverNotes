using UnityEngine;

namespace DiscoverNotes
{
    /// <summary>
    /// Simple component that stores a note string in the scene.
    /// </summary>
    public class DiscoverVZ : MonoBehaviour
    {
        [TextArea]
        [SerializeField]
        private string note;

        /// <summary>
        /// Gets the note associated with this component.
        /// </summary>
        public string Note => note;
    }
}

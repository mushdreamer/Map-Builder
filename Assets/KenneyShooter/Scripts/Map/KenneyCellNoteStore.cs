using System.Collections.Generic;
using UnityEngine;

namespace KenneyShooter
{
    /// <summary>
    /// Scene-side storage for cell notes on KenneySampleMap.
    /// Anchor is bottom-left; width/height grow right and up.
    /// </summary>
    [DisallowMultipleComponent]
    public class KenneyCellNoteStore : MonoBehaviour
    {
        public List<KenneyCellNote> notes = new List<KenneyCellNote>();

        /// <summary>
        /// Looks up a note by layer and bottom-left anchor cell.
        /// </summary>
        public bool TryGet(string layer, int x, int y, out KenneyCellNote note)
        {
            note = null;
            if (notes == null) return false;
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                if (n == null) continue;
                if (!n.MatchesAnchor(layer, x, y)) continue;
                note = n;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Inserts or replaces the note at the same layer + anchor.
        /// </summary>
        public void Upsert(KenneyCellNote note)
        {
            if (note == null) return;
            if (notes == null) notes = new List<KenneyCellNote>();
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                if (n == null) continue;
                if (!n.MatchesAnchor(note.layer, note.x, note.y)) continue;
                notes[i] = note;
                return;
            }
            notes.Add(note);
        }

        /// <summary>
        /// Removes the note at layer + anchor. Returns true if something was removed.
        /// </summary>
        public bool Remove(string layer, int x, int y)
        {
            if (notes == null) return false;
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                var n = notes[i];
                if (n == null || !n.MatchesAnchor(layer, x, y)) continue;
                notes.RemoveAt(i);
                return true;
            }
            return false;
        }

        public void ReplaceAll(List<KenneyCellNote> source)
        {
            notes = source != null
                ? new List<KenneyCellNote>(source)
                : new List<KenneyCellNote>();
        }
    }
}

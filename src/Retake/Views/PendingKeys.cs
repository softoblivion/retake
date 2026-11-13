using System;
using System.Collections.Generic;

namespace Retake.Views
{
    /// <summary>
    /// Keys pressed while a note is saved and its field is closed: they wait, in order, and the review runs them once
    /// the save ends (dropped when the editor comes back). A held key's repeats and keys past <see cref="Limit"/> are
    /// not kept, nor keys <paramref name="waits"/> refuses (those that would act on the note being saved). Pure; the
    /// review supplies the keys.
    /// </summary>
    public sealed class PendingKeys<T>
    {
        /// <summary>The most keys that wait for one save.</summary>
        public const int Limit = 4;

        private readonly Func<T, bool> _waits;
        private readonly List<T> _keys = new List<T>();

        /// <param name="waits">True for a key that may wait for the save.</param>
        public PendingKeys(Func<T, bool> waits)
        {
            _waits = waits;
        }

        public int Count => _keys.Count;

        /// <summary>Keeps <paramref name="key"/> for after the save; false when it is not kept.</summary>
        public bool Add(T key, bool repeat)
        {
            if (repeat || _keys.Count >= Limit || !_waits(key)) return false;
            _keys.Add(key);
            return true;
        }

        /// <summary>The kept keys in the order they were pressed; the queue is empty afterwards.</summary>
        public IList<T> Take()
        {
            var keys = new List<T>(_keys);
            _keys.Clear();
            return keys;
        }

        public void Clear() => _keys.Clear();
    }
}

using System;
using System.Collections;
using UnityEngine;

namespace LambdaLabs.UnityRepl.Editor.Transport
{
    /// <summary>
    /// Hidden MonoBehaviour that runs user coroutines via Unity's native scheduler
    /// while Play Mode is active. Supports WaitForEndOfFrame, WaitForFixedUpdate, etc.
    /// </summary>
    internal class ReplCoroutineHost : MonoBehaviour
    {
        internal class Tracker
        {
            public IEnumerator Inner;
            public object LastValue;
            public Exception Error;
            public bool Done;
            public Coroutine Handle;
        }

        public Tracker Run(IEnumerator inner)
        {
            var t = new Tracker { Inner = inner };
            t.Handle = StartCoroutine(Track(t));
            return t;
        }

        public void Stop(Tracker t)
        {
            if (t == null) return;
            if (t.Handle != null)
            {
                try { StopCoroutine(t.Handle); } catch { }
                t.Handle = null;
            }
            try { (t.Inner as IDisposable)?.Dispose(); } catch { }
            t.Done = true;
        }

        private static IEnumerator Track(Tracker t)
        {
            while (true)
            {
                bool moved;
                try { moved = t.Inner.MoveNext(); }
                catch (Exception ex) { t.Error = ex; t.Done = true; yield break; }
                if (!moved) { t.Done = true; yield break; }
                if (t.Inner.Current != null) t.LastValue = t.Inner.Current;
                yield return t.Inner.Current;
            }
        }
    }
}

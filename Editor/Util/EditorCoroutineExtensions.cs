using System.Collections;
using UnityEditor;

namespace marijnz.EditorCoroutines
{
	internal static class EditorCoroutineExtensions
	{
		private static EditorCoroutines.EditorCoroutine StartCoroutine(this EditorWindow thisRef, IEnumerator coroutine)
		{
			return EditorCoroutines.StartCoroutine(coroutine, thisRef);
		}

        private static EditorCoroutines.EditorCoroutine StartCoroutine(this EditorWindow thisRef, string methodName)
		{
			return EditorCoroutines.StartCoroutine(methodName, thisRef);
		}

        private static EditorCoroutines.EditorCoroutine StartCoroutine(this EditorWindow thisRef, string methodName, object value)
		{
			return EditorCoroutines.StartCoroutine(methodName, value, thisRef);
		}

        private static void StopCoroutine(this EditorWindow thisRef, IEnumerator coroutine)
		{
			EditorCoroutines.StopCoroutine(coroutine, thisRef);
		}

        private static void StopCoroutine(this EditorWindow thisRef, string methodName)
		{
			EditorCoroutines.StopCoroutine(methodName, thisRef);
		}

        private static void StopAllCoroutines(this EditorWindow thisRef)
		{
			EditorCoroutines.StopAllCoroutines(thisRef);
		}
	}
}
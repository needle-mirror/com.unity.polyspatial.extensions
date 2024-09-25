using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using UnityEditor;
using UnityEngine;

using Unity.PolySpatial.Extensions;

#if POLYSPATIAL_INTERNAL

namespace PolySpatial.Extensions.Editor
{
    /// Dummy editor class to make sure editor assembly works
    public class DummyEditorWindow : EditorWindow
    {
        bool runRecursively = true;
        bool excludeLeaves = false;

        bool sortSiblingsByName = false;

        private Vector2 scrollPosition;

        [MenuItem("Window/PolySpatial/Dummy Editor")]
        public static void ShowWindow()
        {
            EditorWindow.GetWindow(typeof(DummyEditorWindow), false, "Dummy Editor");
        }

        void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            {
                runRecursively = EditorGUILayout.Toggle("Recurse", runRecursively);
                excludeLeaves = EditorGUILayout.Toggle("Exclude Leaves", excludeLeaves);
                GUILayout.Space(10);

                sortSiblingsByName = EditorGUILayout.Toggle("Sort Siblings", sortSiblingsByName);
                GUILayout.Space(10);
            }
            EditorGUILayout.EndScrollView();

            GUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            bool apply = GUILayout.Button("Apply Edits");

            GUILayout.Space(10);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);

            if (apply)
            {
                Undo.RegisterCompleteObjectUndo(this, "Batch Edits");
                ApplyEdits();
            }
        }

        void ApplyEdits()
        {
            var gos = Selection.gameObjects;
            foreach (GameObject go in gos)
            {
                ApplyOperation(go);
            }
        }

        void ApplyOperation(GameObject go)
        {
            if (excludeLeaves && go.transform.childCount == 0)
            {
                return;
            }

            if (sortSiblingsByName)
            {
               SortSiblings(Selection.gameObjects);
            }

            if (runRecursively)
            {
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    ApplyOperation(go.transform.GetChild(i).gameObject);
                }
            }
        }

        private void SortSiblings(GameObject[] gos)
        {
           HashSet<Transform> parents = new HashSet<Transform>();
           foreach (GameObject obj in gos)
           {
               if (obj.transform.parent == null)
                   continue;

               parents.Add(obj.transform.parent);
           }

           List<Transform> siblings = new List<Transform>();
           foreach (var parent in parents)
           {
               for (int i = 0; i < parent.transform.childCount; ++i)
               {
                   siblings.Add(parent.transform.GetChild(i));
               }

               // Make sure editor assembly can access runtime assembly
               siblings.Sort((a, b) => Utils.LexicalCompare(a.name, b.name));

               for (int i = 0; i < siblings.Count; i++)
               {
                   siblings[i].SetSiblingIndex(i);
               }
           }
        }
    }
}

#endif

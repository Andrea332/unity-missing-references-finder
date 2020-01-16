﻿using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MissingReferencesFinder : MonoBehaviour {
    private class ObjectData {
        public float ExpectedProgress;
        public GameObject GameObject;
    }
    
    [MenuItem("Tools/Find Missing References/In current scene", false, 50)]
    public static void FindMissingReferencesInCurrentScene() {
        var scene = SceneManager.GetActiveScene();
        showInitialProgressBar(scene.path);
        
        var rootObjects = scene.GetRootGameObjects();
        
        var queue = new Queue<ObjectData>();
        foreach (var rootObject in rootObjects) {
            queue.Enqueue(new ObjectData{ExpectedProgress = 1/(float)rootObjects.Length, GameObject = rootObject});
        }

        var finished = findMissingReferencesInScene(scene, 1);
        showFinishDialog(!finished);
    }

    [MenuItem("Tools/Find Missing References/In all scenes in build", false, 51)]
    public static void FindMissingReferencesInAllScenesInBuild() {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).ToList();
        
        var finished = true;
        foreach (var scene in scenes) {
            var s = EditorSceneManager.OpenScene(scene.path);
            finished = findMissingReferencesInScene(s, 1/(float)scenes.Count());
            if (!finished) break;
        }
        showFinishDialog(!finished);
    }
    
    [MenuItem("Tools/Find Missing References/In all scenes in project", false, 51)]
    public static void FindMissingReferencesInAllScenes() {
        var scenes = EditorBuildSettings.scenes;
        
        var finished = true;
        foreach (var scene in scenes) {
            var s = EditorSceneManager.OpenScene(scene.path);
            finished = findMissingReferencesInScene(s, 1 /(float)scenes.Count());
            if (!finished) break;
        }
        showFinishDialog(!finished);
    }

    [MenuItem("Tools/Find Missing References/In assets", false, 52)]
    public static void FindMissingReferencesInAssets() {
        showInitialProgressBar("all assets");
        var allAssetPaths = AssetDatabase.GetAllAssetPaths();
        var objs = allAssetPaths
                   .Where(isProjectAsset)
                   .ToArray();

        var finished = findMissingReferences("Project", objs);
        showFinishDialog(!finished);
    }
    
    [MenuItem("Tools/Find Missing References/Everywhere", false, 51)]
    public static void FindMissingReferencesEverywhere() {
        var scenes = EditorBuildSettings.scenes;
        var progressWeight = 1 / (float)(scenes.Length + 1);
        
        var finished = true;
        foreach (var scene in scenes) {
            var s = EditorSceneManager.OpenScene(scene.path);
            finished = findMissingReferencesInScene(s, progressWeight);
            if (!finished) break;
        }

        if (finished) {
            var allAssetPaths = AssetDatabase.GetAllAssetPaths();
            var objs = allAssetPaths
                       .Where(isProjectAsset)
                       .ToArray();

            finished = findMissingReferences("Project", objs, 1 / (float)(scenes.Length), progressWeight);
        }
        
        showFinishDialog(!finished);
    }

    private static bool isProjectAsset(string path) {
#if UNITY_EDITOR_OSX
        return !path.StartsWith("/");
#else
        return path.Substring(1, 2) != ":/";
#endif
    }

    private static bool findMissingReferences(string context, string[] paths, float initialProgress = 0f, float progressWeight = 1f) {
        var wasCancelled = false;
        for (var i = 0; i < paths.Length; i++) {
            var obj = AssetDatabase.LoadAssetAtPath(paths[i], typeof(GameObject)) as GameObject;
            if (obj == null || !obj) continue;
            
            if (wasCancelled || EditorUtility.DisplayCancelableProgressBar("Searching missing references in assets.",
                                                                           $"{paths[i]}",
                                                                           initialProgress + ((i / (float) paths.Length)*progressWeight))) {
                return false;
            }

            findMissingReferences(context, obj);
        }

        return true;
    }

    private static void findMissingReferences(string context, GameObject go, bool findInChildren = false) {
        var components = go.GetComponents<Component>();

        for (var j = 0; j < components.Length; j++) {
            var c = components[j];
            if (!c) {
                Debug.LogError("Missing Component in GO: " + FullPath(go), go);
                continue;
            }

            var so = new SerializedObject(c);
            var sp = so.GetIterator();

            while (sp.NextVisible(true)) {
                if (sp.propertyType == SerializedPropertyType.ObjectReference) {
                    if (sp.objectReferenceValue           == null
                     && sp.objectReferenceInstanceIDValue != 0) {
                        ShowError(context, go, c.GetType().Name, ObjectNames.NicifyVariableName(sp.name));
                    }
                }
            }
        }

        if (findInChildren) {
            foreach (Transform child in go.transform) {
                findMissingReferences(context, child.gameObject, true);
            }
        }
    }

    private static bool findMissingReferencesInScene(Scene scene, float progressWeightByScene) {
        var rootObjects = scene.GetRootGameObjects();
        
        var queue = new Queue<ObjectData>();
        foreach (var rootObject in rootObjects) {
            queue.Enqueue(new ObjectData{ExpectedProgress = progressWeightByScene /(float)rootObjects.Length, GameObject = rootObject});
        }
        
        return findMissingReferences(scene.path, queue, true);
    }
    
    private static bool findMissingReferences(string context, Queue<ObjectData> queue, bool findInChildren = false) {
        var currentProgress = 0f;
        while (queue.Any()) {
            var data = queue.Dequeue();
            var go = data.GameObject;
            var components = go.GetComponents<Component>();
        
            float progressEachComponent;
            if (findInChildren) {
                progressEachComponent = (data.ExpectedProgress) / (float)(components.Length + go.transform.childCount);
            } else {
                progressEachComponent = data.ExpectedProgress / (float)components.Length;   
            }
            
            for (var j = 0; j < components.Length; j++) {
                currentProgress += progressEachComponent;
                if (EditorUtility.DisplayCancelableProgressBar($"Searching missing references in {context}",
                                                               go.name,
                                                               currentProgress)) {
                    return false;
                }
            
                var c = components[j];
                if (!c) {
                    Debug.LogError("Missing Component in GO: " + FullPath(go), go);
                    continue;
                }

                using (var so = new SerializedObject(c)) {
                    using (var sp = so.GetIterator()) {
                        while (sp.NextVisible(true)) {
                            if (sp.propertyType == SerializedPropertyType.ObjectReference) {
                                if (sp.objectReferenceValue           == null
                                 && sp.objectReferenceInstanceIDValue != 0) {
                                    ShowError(context, go, c.GetType().Name, ObjectNames.NicifyVariableName(sp.name));
                                }
                            }
                        }
                    }
                }
            }

            if (findInChildren) {
                foreach (Transform child in go.transform) {
                    if (child.gameObject == go) continue;
                    queue.Enqueue(new ObjectData{ExpectedProgress = progressEachComponent, GameObject = child.gameObject});
                }
            }
        }

        return true;
    }

    private static void showInitialProgressBar(string searchContext, bool clearConsole = true) {
        if (clearConsole) {
            Debug.ClearDeveloperConsole();  
        }
        EditorUtility.DisplayProgressBar("Missing References Finder", $"Preparing search in {searchContext}", 0f);
    }
    
    private static void showFinishDialog(bool wasCancelled) {
        EditorUtility.ClearProgressBar();
        EditorUtility.DisplayDialog("Missing References Finder",
                                    wasCancelled ?
                                        "Process cancelled. Current results are shown as errors in the console." :
                                        "Finished finding missing references. Results are shown as errors in the console.",
                                    "Ok");
    }

    private const string err = "Missing Ref in: [{3}]{0}. Component: {1}, Property: {2}";

    private static void ShowError(string context, GameObject go, string c, string property) {
        Debug.LogError(string.Format(err, FullPath(go), c, property, context), go);
    }

    private static string FullPath(GameObject go) {
        var parent = go.transform.parent; 
        return parent == null ? go.name : FullPath(parent.gameObject) + "/" + go.name;
    }
}
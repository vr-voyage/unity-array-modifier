using UnityEngine;
using UnityEditor;
using System;



namespace Myy
{
[CustomEditor(typeof(ArrayModifier))]
[CanEditMultipleObjects]
public class ArrayModifierEditor : Editor
{
    SerializedProperty meshToReplicateSerialized;
    SerializedProperty nTimesReplicateSerialized;
    SerializedProperty relativeOffsetPositionSerialized;
    SerializedProperty relativeOffsetRotationSerialized;
    SerializedProperty relativeOffsetScaleSerialized;

    ArrayModifier arrayModifier;

    Mesh oldMesh;
    int oldRepeat;
    Vector3 oldOffsetPos;
    Vector3 oldOffsetRot;
    Vector3 oldOffsetScale;

    Mesh generatedMesh = null;
    bool moveScaleToMesh = true;

    public static string FilesystemFriendlyName(string name)
    {
        var invalids = System.IO.Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
    }

    public static string AssetFolder(string assetPath)
    {
        int lastIndex = assetPath.LastIndexOf('/');
        lastIndex = (lastIndex >= 0) ? lastIndex : assetPath.Length;
        return assetPath.Substring(0, lastIndex);
    }

    void OnEnable()
    {
        meshToReplicateSerialized = serializedObject.FindProperty("meshToReplicate");
        nTimesReplicateSerialized = serializedObject.FindProperty("nTimes");
        relativeOffsetPositionSerialized = serializedObject.FindProperty("cloneOffsetPosition");
        relativeOffsetRotationSerialized = serializedObject.FindProperty("cloneOffsetRotation");
        relativeOffsetScaleSerialized    = serializedObject.FindProperty("cloneOffsetScale");
        arrayModifier = (ArrayModifier) serializedObject.targetObject;

        oldMesh = arrayModifier.meshToReplicate;
        oldRepeat = arrayModifier.nTimes;
        oldOffsetPos = arrayModifier.cloneOffsetPosition;
        oldOffsetRot = arrayModifier.cloneOffsetRotation;
        oldOffsetScale = arrayModifier.cloneOffsetScale;
    }

    bool ValuesChanged()
    {
        return 
            (oldMesh          != arrayModifier.meshToReplicate) 
            | (oldRepeat      != arrayModifier.nTimes)
            | (oldOffsetPos   != arrayModifier.cloneOffsetPosition);
            /*| (oldOffsetRot   != arrayModifier.cloneOffsetRotation)
            | (oldOffsetScale != arrayModifier.cloneOffsetScale);*/
    }

    void ValuesSyncWithArrayModifier()
    {
        oldMesh        = arrayModifier.meshToReplicate;
        oldRepeat      = arrayModifier.nTimes;
        oldOffsetPos   = arrayModifier.cloneOffsetPosition;
        /*oldOffsetRot   = arrayModifier.cloneOffsetRotation;
        olOffsetScale = arrayModifier.cloneOffsetScale;*/
    }

    Mesh GenerateDuplicatedMesh(Mesh replicatedMesh, Vector3 positionOffset)
    {
        Mesh toReturn = null;
        try
        {
            int nRepeats = Mathf.Max(arrayModifier.nTimes, 0);

            CombineInstance[] instancesCombiner = new CombineInstance[arrayModifier.nTimes];
            for (int i = 0; i < nRepeats; i++)
            {
                /* CombineInstance is a structure, so we have to
                 * copy it on the stack, modify the copy and store the
                 * copy back.
                 */
                CombineInstance instance     = instancesCombiner[i];
                Vector3 currentOffset        = positionOffset      * i;

                instance.mesh      = replicatedMesh;
                instance.transform = Matrix4x4.Translate(currentOffset);

                instancesCombiner[i] = instance;
            }

            toReturn = new Mesh();
            toReturn.CombineMeshes(instancesCombiner);
        }
        catch (Exception e)
        {
            Debug.LogError("Could not generate the duplicated mesh");
            Debug.LogException(e);
        }

        return toReturn;
    }

    public override void OnInspectorGUI()
    {
        MeshFilter meshFilter = arrayModifier.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            EditorGUILayout.HelpBox("A Mesh Filter component is required for the operation", MessageType.Error);
            return;
        }

        serializedObject.Update();
        EditorGUILayout.PropertyField(meshToReplicateSerialized);
        EditorGUILayout.PropertyField(nTimesReplicateSerialized);
        EditorGUILayout.PropertyField(relativeOffsetPositionSerialized);
        serializedObject.ApplyModifiedProperties();
        

        try
        {

            Mesh replicatedMesh = arrayModifier.meshToReplicate;
            if (replicatedMesh != null && ValuesChanged())
            {

                Mesh newMesh = GenerateDuplicatedMesh(replicatedMesh, arrayModifier.cloneOffsetPosition);
                if (newMesh == null)
                {
                    Debug.LogError("No mesh generated");
                }

                meshFilter.sharedMesh = newMesh;
                generatedMesh = newMesh;
                ValuesSyncWithArrayModifier();
            }


            if (generatedMesh != null)
            {
                GUILayout.Toggle(moveScaleToMesh, "Transfer scale to mesh");
                if (GUILayout.Button("Save generated mesh"))
                {
                    string currentTime = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    string newMeshDirPath = "Assets/";
                    string replicatedMeshPath = AssetDatabase.GetAssetPath(arrayModifier.meshToReplicate);
                    if (replicatedMeshPath != null && replicatedMeshPath.StartsWith("Assets/"))
                    {
                        newMeshDirPath = AssetFolder(replicatedMeshPath);
                    }
                    string newMeshName = $"{FilesystemFriendlyName(arrayModifier.gameObject.name)}-{currentTime}.mesh";
                    string newMeshFilePath = $"{newMeshDirPath}/{newMeshName}";

                    Debug.Log($"Path {newMeshFilePath}");

                    if (moveScaleToMesh)
                    {
                        /* - Make new base mesh, scaled to the current object scale.
                         * - Make a new duplicated mesh, using the new base
                         * - Save the new base mesh (the duplicate will be saved just after this block)
                         * - Reset the current object scale to 1
                         * - Display the new duplicate
                         */
                        CombineInstance instance = new CombineInstance
                        {
                            mesh = replicatedMesh,
                            transform = Matrix4x4.Scale(arrayModifier.transform.localScale) * Matrix4x4.Translate(Vector3.zero)
                        };
                        Mesh newReplicatedMesh = new Mesh();
                        CombineInstance[] combination = new CombineInstance[] { instance };
                        newReplicatedMesh.CombineMeshes(combination);

                        Vector3 newOffset = Vector3.Scale(arrayModifier.cloneOffsetPosition, arrayModifier.transform.localScale);

                        Mesh newMesh = GenerateDuplicatedMesh(newReplicatedMesh, newOffset);
                        if (newMesh == null)
                        {
                            Debug.LogError("Could not generate scaled mesh. Bailing out");
                            return;
                        }

                        /* Save the new base */
                        string baseMeshFilePath = $"{newMeshDirPath}/{newMeshName}-Base.mesh";
                        AssetDatabase.CreateAsset(newReplicatedMesh, baseMeshFilePath);

                        /* Use the new duplicate */
                        
                        arrayModifier.cloneOffsetPosition = newOffset;
                        arrayModifier.transform.localScale = Vector3.one;
                        
                        arrayModifier.meshToReplicate      = newReplicatedMesh;
                        meshFilter.sharedMesh              = newMesh;
                        generatedMesh                      = newMesh;
                    }

                    AssetDatabase.CreateAsset(generatedMesh, newMeshFilePath);
                    
                }
            }   

        }
        catch
        {
            
        }

    }
}
}

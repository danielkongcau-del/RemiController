using System;
using UnityEngine;

// Patches only recovered dynamic fields. Captured material/light parameters
// remain byte-preserved; shaders have independent VS/PS constant namespaces.
public static class RemielleNativeUIConstants
{
    [Serializable] public struct Field
    {
        public string name;
        public int offsetBytes, storageBytes;
    }
    public sealed class Frame
    {
        public Matrix4x4 sceneToProfile, view, projection, cameraProjection, vp;
        public Matrix4x4 nonJitteredProjection,nonJitteredVP;
        public Vector2 jitterPixels;
        public Matrix4x4 previousView, previousProjection, previousVP, previousSceneToProfile;
        public Vector4 cameraPosition, screenSize, orthoParams;
        public bool hasHistory;

        public Frame(Camera camera, int width, int height, Matrix4x4 workingFrame, Frame previous, Vector2 jitter=default)
        {
            if (!camera || width < 1 || height < 1) throw new ArgumentException("Invalid native UI camera");
            sceneToProfile = workingFrame;
            cameraProjection = camera.projectionMatrix;
            nonJitteredProjection = GL.GetGPUProjectionMatrix(cameraProjection, true);
            projection=nonJitteredProjection;jitterPixels=jitter;
            if(jitter!=Vector2.zero)
            {
                // Capture VP shifts clip x by -2*jitterUV.x*w and clip y by
                // +2*jitterUV.y*w. Motion matrices remain non-jittered.
                projection.SetRow(0,projection.GetRow(0)-2f*jitter.x/width*projection.GetRow(3));
                projection.SetRow(1,projection.GetRow(1)+2f*jitter.y/height*projection.GetRow(3));
            }
            view = camera.worldToCameraMatrix * workingFrame.inverse;
            vp = projection * view;
            nonJitteredVP=nonJitteredProjection*view;
            var position = workingFrame.MultiplyPoint3x4(camera.transform.position);
            cameraPosition = new Vector4(position.x, position.y, position.z, 0);
            screenSize = new Vector4(width, height, 1f / width, 1f / height);
            orthoParams = new Vector4(camera.orthographicSize * camera.aspect, camera.orthographicSize, 0, camera.orthographic ? 1 : 0);
            hasHistory = previous != null;
            previousView = previous?.view ?? view;
            previousProjection = previous?.nonJitteredProjection ?? nonJitteredProjection;
            previousVP = previous?.nonJitteredVP ?? nonJitteredVP;
            previousSceneToProfile = previous?.sceneToProfile ?? workingFrame;
        }
    }

    public static int Patch(byte[] destination, Field[] fields, Frame frame, RemielleNativeUIMeshBinding mesh, Transform nativePelvis, Transform nativeHead)
    {
        var objectToWorld = frame.sceneToProfile * mesh.ObjectToWorld;
        var previousObject = frame.previousSceneToProfile * mesh.PreviousObjectToWorld;
        var headInverse = (frame.sceneToProfile * nativeHead.localToWorldMatrix).inverse;
        bool motion = frame.hasHistory && mesh.HasHistory;
        int changed = 0;
        foreach (var field in fields)
        {
            Matrix4x4 matrix;
            bool isMatrix = true;
            switch (field.name)
            {
                case "unity_ObjectToWorld": matrix = objectToWorld; break;
                case "unity_WorldToObject": matrix = objectToWorld.inverse; break;
                case "unity_MatrixPreviousM": matrix = previousObject; break;
                case "unity_MatrixPreviousMI": matrix = previousObject.inverse; break;
                case "unity_MatrixVP": matrix = frame.vp; break;
                case "_NonJitteredViewProjMatrix": matrix = frame.nonJitteredVP; break;
                case "_PrevViewProjMatrix": matrix = frame.previousVP; break;
                case "unity_MatrixV": matrix = frame.view; break;
                case "unity_MatrixInvV": matrix = frame.view.inverse; break;
                case "unity_CameraProjection": matrix = frame.cameraProjection; break;
                case "glstate_matrix_projection": matrix = frame.projection; break;
                case "_NonJitteredProjMatrix": matrix = frame.nonJitteredProjection; break;
                case "_PrevViewMatrix": matrix = frame.previousView; break;
                case "_PrevProjMatrix": matrix = frame.previousProjection; break;
                default: matrix = default; isMatrix = false; break;
            }
            if (isMatrix)
            {
                if (field.storageBytes != 64) throw new InvalidOperationException("Native matrix extent mismatch: " + field.name);
                for (int i = 0; i < 16; i++) Write(destination, field, i, matrix[i]);
                changed++; continue;
            }
            Vector4 vector;
            switch (field.name)
            {
                case "_WorldSpaceCameraPos": vector = frame.cameraPosition; break;
                case "_ScreenSize": vector = frame.screenSize; break;
                case "unity_OrthoParams": vector = frame.orthoParams; break;
                case "_ClipSpaceOffset": vector = Vector4.zero; break;
                case "_MiddlePointPosition":
                    var point = frame.sceneToProfile.MultiplyPoint3x4(nativePelvis.position);
                    vector = new Vector4(point.x, point.y, point.z, 1); break;
                case "_HeadMatrixWS2OS0": vector = headInverse.GetRow(0); break;
                case "_HeadMatrixWS2OS1": vector = headInverse.GetRow(1); break;
                case "_HeadMatrixWS2OS2": vector = headInverse.GetRow(2); break;
                case "unity_WorldTransformParams":
                    Write(destination, field, 3, objectToWorld.determinant < 0 ? -1 : 1); changed++; continue;
                case "unity_MotionVectorsParams":
                    Write(destination, field, 0, motion ? 1 : 0); Write(destination, field, 1, motion ? 1 : 0); changed++; continue;
                default: continue;
            }
            if (field.storageBytes > 16) throw new InvalidOperationException("Native vector extent mismatch: " + field.name);
            for (int i = 0; i < field.storageBytes / 4; i++) Write(destination, field, i, vector[i]);
            changed++;
        }
        return changed;
    }

    static void Write(byte[] destination, Field field, int component, float value)
    {
        int offset = field.offsetBytes + component * 4;
        if (field.offsetBytes < 0 || component < 0 || component * 4 >= field.storageBytes)
            throw new InvalidOperationException("Invalid native constant write");
        // The compiled shader may omit unused trailing matrix columns. They
        // remain in source reflection but lie beyond the declared GPU buffer.
        if (offset >= destination.Length) return;
        if (offset + 4 > destination.Length || !float.IsFinite(value)) throw new InvalidOperationException("Invalid native constant value");
        BitConverter.TryWriteBytes(destination.AsSpan(offset, 4), value);
    }
}

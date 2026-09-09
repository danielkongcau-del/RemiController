using System;
using UnityEngine;

namespace Remielle.Controller
{
    // Added only for an explicit probe rerender. Normal preview has no image effect.
    public sealed class ControllerHdrProbeTap : MonoBehaviour
    {
        public Action<RenderTexture> Observe;
        void OnRenderImage(RenderTexture source,RenderTexture destination)
        {
            Observe?.Invoke(source);
            Graphics.Blit(source,destination);
        }
    }
}

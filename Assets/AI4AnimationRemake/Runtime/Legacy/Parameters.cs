using System;
using UnityEngine;

// Compatibility shell for the original SIGGRAPH 2020 Parameters asset.
// Keep the class name, field names, and script GUID stable so Unity can
// deserialize BasketballModel.asset without rewriting its float buffers.
[CreateAssetMenu(menuName = "AI4Animation/Legacy Parameters", fileName = "Parameters")]
public sealed class Parameters : ScriptableObject
{
    public Buffer[] Buffers = Array.Empty<Buffer>();

    [Serializable]
    public sealed class Buffer
    {
        public string ID = string.Empty;
        public float[] Values = Array.Empty<float>();
    }
}

#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.InputSystem;

namespace Utilities
{
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public class QuaternionRotationProcessor : InputProcessor<Quaternion>
    {
        public enum RotationAxis
        {
            X,
            Y,
            Z
        }
        
        [Tooltip("Axis to rotate around")]
        public RotationAxis Axis = RotationAxis.X;

        [Tooltip("Amount to rotate")] 
        public float Rotation = 0f;
        
#if UNITY_EDITOR
        static QuaternionRotationProcessor()
        {
            Initialize();
        }
#endif

        [RuntimeInitializeOnLoadMethod]
        static void Initialize()
        {
            InputSystem.RegisterProcessor<QuaternionRotationProcessor>();
        }
        
        /// <summary>
        /// Scale the given <paramref name="value"/> by <see cref="factor"/>.
        /// </summary>
        /// <param name="value">Input value.</param>
        /// <param name="control">Ignored.</param>
        /// <returns>Scaled value.</returns>
        public override Quaternion Process(Quaternion value, InputControl control)
        {
            
            return value * Quaternion.Euler((Axis==RotationAxis.X?Vector3.right:Axis==RotationAxis.Y?Vector3.up:Vector3.forward) * Rotation);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"QuaternionRotation(axis={Axis},rotation={Rotation})";
        }
    }
}
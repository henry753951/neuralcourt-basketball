using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class BasketballLegacyCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 selfOffset = new(0f, 1f, -1.5f);
        [SerializeField] private Vector3 targetOffset = new(0f, 1.35f, -1f);
        [SerializeField, Range(0f, 1f)] private float damping;
        [SerializeField, Range(0f, 10f)] private float distanceScale = 2f;

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 currentPosition = transform.position;
            Quaternion currentRotation = transform.rotation;
            Vector3 desiredPosition = target.position + distanceScale * selfOffset;
            Vector3 lookDirection = target.position + targetOffset - desiredPosition;
            Quaternion desiredRotation = lookDirection.sqrMagnitude > 1e-10f
                ? Quaternion.LookRotation(lookDirection, Vector3.up)
                : currentRotation;
            float weight = 1f - damping;
            transform.SetPositionAndRotation(
                Vector3.Lerp(currentPosition, desiredPosition, weight),
                Quaternion.Lerp(currentRotation, desiredRotation, weight));
        }

#if UNITY_EDITOR
        public void Configure(Transform followTarget)
        {
            target = followTarget;
            selfOffset = new Vector3(0f, 1f, -1.5f);
            targetOffset = new Vector3(0f, 1.35f, -1f);
            damping = 0f;
            distanceScale = 2f;
        }
#endif
    }
}

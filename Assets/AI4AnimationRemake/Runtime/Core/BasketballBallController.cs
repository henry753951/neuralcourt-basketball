using UnityEngine;

namespace CrowdEyes.AI4Animation.Basketball
{
    public enum BasketballBallAuthorityState
    {
        Controlled,
        Held,
        Released,
        FreePhysics,
        Reacquiring
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class BasketballBallController : MonoBehaviour
    {
        [SerializeField, Min(0.001f)]
        private float radius = 0.125f;

        [SerializeField]
        private BasketballBallAuthorityState state = BasketballBallAuthorityState.Controlled;

        [SerializeField, Min(0.1f)]
        private float reacquireMaximumSpeed = 7.5f;

        [SerializeField, Min(1f)]
        private float reacquireMaximumAngularSpeed = 1080f;

        [SerializeField, Min(0.001f)]
        private float reacquireCompletionDistance = 0.015f;

        private Rigidbody body;
        private Vector3 controlledVelocity;
        private bool blendControlledPose;

        public float Radius => radius;
        public BasketballBallAuthorityState State => state;
        public Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();
        public Vector3 Velocity => Body.isKinematic ? controlledVelocity : Body.linearVelocity;

        private void OnEnable()
        {
            ApplyAuthority();
        }

        public void SetState(BasketballBallAuthorityState value)
        {
            state = value;
            blendControlledPose = value == BasketballBallAuthorityState.Reacquiring;
            ApplyAuthority();
        }

        public void SetControlledPose(Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            if (state != BasketballBallAuthorityState.Controlled &&
                state != BasketballBallAuthorityState.Held &&
                state != BasketballBallAuthorityState.Reacquiring)
            {
                return;
            }

            if (blendControlledPose)
            {
                float deltaTime = Mathf.Max(0f, Time.deltaTime);
                Vector3 nextPosition = Vector3.MoveTowards(
                    transform.position,
                    position,
                    reacquireMaximumSpeed * deltaTime);
                Quaternion nextRotation = Quaternion.RotateTowards(
                    transform.rotation,
                    rotation,
                    reacquireMaximumAngularSpeed * deltaTime);
                transform.SetPositionAndRotation(
                    nextPosition,
                    nextRotation);
                if ((nextPosition - position).sqrMagnitude <=
                    reacquireCompletionDistance * reacquireCompletionDistance)
                {
                    blendControlledPose = false;
                }
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
            }
            controlledVelocity = velocity;
        }

        public void Release(Vector3 linearVelocity, Vector3 angularVelocity)
        {
            SetState(BasketballBallAuthorityState.Released);
            controlledVelocity = linearVelocity;
            Body.linearVelocity = linearVelocity;
            Body.angularVelocity = angularVelocity;
        }

        public void ReleaseFromPose(
            Vector3 position,
            Quaternion rotation,
            Vector3 linearVelocity,
            Vector3 angularVelocity)
        {
            transform.SetPositionAndRotation(position, rotation);
            Release(linearVelocity, angularVelocity);
        }

        public void BeginReacquire()
        {
            SetState(BasketballBallAuthorityState.Reacquiring);
        }

        public void CompleteReacquire(bool held)
        {
            state = held
                ? BasketballBallAuthorityState.Held
                : BasketballBallAuthorityState.Controlled;
            ApplyAuthority();
        }

        public void BeginControlledHandoff(bool held)
        {
            state = held
                ? BasketballBallAuthorityState.Held
                : BasketballBallAuthorityState.Controlled;
            blendControlledPose = true;
            ApplyAuthority();
        }

        private void FixedUpdate()
        {
            if (state == BasketballBallAuthorityState.Released)
            {
                state = BasketballBallAuthorityState.FreePhysics;
            }
        }

        private void ApplyAuthority()
        {
            bool physicsAuthority = state == BasketballBallAuthorityState.Released ||
                                    state == BasketballBallAuthorityState.FreePhysics;
            Body.isKinematic = !physicsAuthority;
            if (!physicsAuthority)
            {
                Body.useGravity = false;
            }
            else
            {
                Body.useGravity = true;
            }
        }

#if UNITY_EDITOR
        public void Configure(float value, BasketballBallAuthorityState initialState)
        {
            radius = value;
            state = initialState;
            blendControlledPose = false;
        }
#endif
    }
}

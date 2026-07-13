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

        private Rigidbody body;
        private Vector3 controlledVelocity;

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

            transform.SetPositionAndRotation(position, rotation);
            controlledVelocity = velocity;
        }

        public void Release(Vector3 linearVelocity, Vector3 angularVelocity)
        {
            state = BasketballBallAuthorityState.Released;
            ApplyAuthority();
            controlledVelocity = linearVelocity;
            Body.linearVelocity = linearVelocity;
            Body.angularVelocity = angularVelocity;
        }

        public void BeginReacquire()
        {
            SetState(BasketballBallAuthorityState.Reacquiring);
        }

        public void CompleteReacquire(bool held)
        {
            SetState(held ? BasketballBallAuthorityState.Held : BasketballBallAuthorityState.Controlled);
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
        }
#endif
    }
}

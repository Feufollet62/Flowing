using UnityEngine;
using UnityEngine.InputSystem;

namespace Gameplay
{
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerController : MonoBehaviour
    {
        #region Variables
        [Header("Camera")]
        [SerializeField] private float sensitivity = 1;
        [SerializeField] private float fov = 90;
        
        [Header("Movement")] 
        [SerializeField] private float accelerationMax = 30f;
        [SerializeField] [Range(0,1)] private float accelAirMultiplier = .3f;
        [SerializeField] private float speedMax = 8f;

        [SerializeField] [Range(0, 1)] private float minGroundDotProduct = .9f;

        private bool _isControllable = true;

        private Camera _cam;
        private Rigidbody _rb;
        private PlayerInput _input;

        private Vector2 _inputLook, _lastMousePos;
        private Vector3 _velocity, _desiredVelocity, _inputMovement;
        private Vector3 _contactNormal;

        private int _groundContactCount, _stepsSinceGrounded;
        private bool Grounded => _groundContactCount > 0;

        #endregion

        #region Built-in Functions

        private void Start()
        {
            _cam = Camera.main;
            _rb = GetComponent<Rigidbody>();
            _input = GetComponent<PlayerInput>();

            _cam.fieldOfView = fov;
        }

        private void Update()
        {
            _cam.fieldOfView = fov; // REMOVE THIS AT SOME POINT
            
            ReadInput();
            Look();
        }

        private void FixedUpdate()
        {
            _stepsSinceGrounded++;

            // if not grounded, try SnapToGround()
            if (Grounded || SnapToGround())
            {
                _stepsSinceGrounded = 0;
                if (_groundContactCount > 1)
                {
                    _contactNormal.Normalize();
                }
            }
            else _contactNormal = Vector3.up;
            
            Move();
            ClearContactState();
        }

        private void OnCollisionEnter(Collision collision)
        {
            EvaluateCollision(collision);
        }

        private void OnCollisionStay(Collision collisionInfo)
        {
            EvaluateCollision(collisionInfo);
        }
        #endregion

        #region Custom Functions

        // https://youtu.be/5tOOstXaIKE
        // Maybe apply sensitivity here ?
        public void OnMovement(InputAction.CallbackContext context)
        {
            if (_isControllable)
            {
                Vector2 rawInput = context.ReadValue<Vector2>();
                _inputMovement = new Vector3(rawInput.x, 0, rawInput.y);
            }
            else
            {
                _inputMovement = Vector3.zero;
            }
        }

        public void OnLookController(InputAction.CallbackContext context)
        {
            if (_isControllable)
            {
                Vector2 rawInput = context.ReadValue<Vector2>(); // Please test this
                _inputLook = rawInput;
            }
            else
            {
                _inputLook = Vector2.zero;
            }
        }

        public void OnLookMouseX(InputAction.CallbackContext context)
        {
            if (_isControllable)
            {
                float rawInput = context.ReadValue<float>();
                _inputLook.x = rawInput;
            }
            else
            {
                _inputLook.x = 0f;
            }
        }
        
        public void OnLookMouseY(InputAction.CallbackContext context)
        {
            if (_isControllable)
            {
                float rawInput = context.ReadValue<float>();
                _inputLook.y = rawInput;
            }
            else
            {
                _inputLook.y = 0f;
            }
        }

        private void ReadInput()
        {
            Transform tr = transform;
            
            _desiredVelocity = tr.forward * _inputMovement.z + tr.right * _inputMovement.x; // What about diagonal faster than straight ?
            _desiredVelocity *= speedMax;
        }

        #region Camera

        private void Look()
        {
            print(_inputLook.ToString("f3"));
            
            // Camera rotation
            Quaternion camRotation = _cam.transform.rotation;
            
            camRotation *= Quaternion.Euler(-_inputLook.y * sensitivity, 0,0);

            _cam.transform.rotation = camRotation;
            
            // CHANGE THIS
            Quaternion rbRotation = _rb.rotation;

            rbRotation *= Quaternion.Euler(0,_inputLook.x * sensitivity,0);
            
            _rb.rotation = rbRotation;
        }

        #endregion
        
        #region Movement
        /// <summary>
        /// Calculate and add desired velocity to current velocity.
        /// </summary>
        private void Move()
        {
            _velocity = _rb.velocity;

            Vector3 xAxis = ProjectOnContactPlane(Vector3.right).normalized;
            Vector3 zAxis = ProjectOnContactPlane(Vector3.forward).normalized;

            float currentX = Vector3.Dot(_velocity, xAxis);
            float currentZ = Vector3.Dot(_velocity, zAxis);

            float acceleration = Grounded ? accelerationMax : accelerationMax * accelAirMultiplier;
            float maxSpeedChange = acceleration * Time.deltaTime;

            float newX = Mathf.MoveTowards(currentX, _desiredVelocity.x, maxSpeedChange);
            float newZ = Mathf.MoveTowards(currentZ, _desiredVelocity.z, maxSpeedChange);

            _velocity += xAxis * (newX - currentX) + zAxis * (newZ - currentZ);

            _rb.velocity = _velocity;
        }

        /// <summary>
        /// Check new or continued collisions for ground contact.
        /// Adds all ground normals to _contactNormal.
        /// </summary>
        private void EvaluateCollision(Collision col)
        {
            for (int i = 0; i < col.contactCount; i++)
            {
                Vector3 normal = col.GetContact(i).normal;
                
                if (normal.y >= minGroundDotProduct)
                {
                    _groundContactCount += 1;
                    _contactNormal += normal;
                }
            }
        }

        /// <summary>
        /// Resets ground contact(s) before the next frame.
        /// </summary>
        private void ClearContactState()
        {
            _groundContactCount = 0;
            _contactNormal = Vector3.zero;
        }

        /// <summary>
        /// Returns the input vector projected on the current contact plane.
        /// </summary>
        /// <param name="vector"></param>
        /// Vector to project.
        /// <returns></returns>
        private Vector3 ProjectOnContactPlane(Vector3 vector)
        {
            return vector - _contactNormal * Vector3.Dot(vector, _contactNormal);
        }

        /// <summary>
        /// Attempts to snap the player to the ground. Useful when going down ramps.
        /// </summary>
        /// <returns> True if the player was successfully grounded. False if no ground was found. </returns>
        private bool SnapToGround()
        {
            // Were we grounded a frame ago ?
            if (_stepsSinceGrounded > 1)
            {
                return false;
            }

            // Is there something underneath the player ?
            if (!Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit))
            {
                return false;
            }

            // Is it ground ?
            if (hit.normal.y < minGroundDotProduct)
            {
                return false;
            }

            // Snapping the player
            _groundContactCount = 1;
            _contactNormal = hit.normal;
            float speed = _velocity.magnitude;
            float dot = Vector3.Dot(_velocity, hit.normal);

            if (dot > 0f) // Velocity needs adjusting when going up a ramp
            {
                _velocity = (_velocity - hit.normal * dot).normalized * speed;
                _rb.velocity = _velocity;
            }
            
            return true;
        }
        #endregion
        
        #endregion
    }
}
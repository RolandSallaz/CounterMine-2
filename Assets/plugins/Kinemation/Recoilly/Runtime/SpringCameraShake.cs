using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Kinemation.Recoilly.Runtime
{
    [Serializable]
    public struct SpringShakeProfile
    {
        [SerializeField] public VectorSpringData springData;
        [SerializeField] public float dampSpeed;
        [SerializeField] public Vector2 pitch;
        [SerializeField] public Vector2 yaw;
        [SerializeField] public Vector2 roll;

        public Vector3 GetRandomTarget()
        {
            return new Vector3(Random.Range(pitch.x, pitch.y), Random.Range(yaw.x, yaw.y),
                Random.Range(roll.x, roll.y));
        }
    }
    
    public class SpringCameraShake : MonoBehaviour
    {
        [SerializeField] private SpringShakeProfile shakeProfile;
        private Vector3 _dampedTarget;
        private Vector3 _target;
        private Quaternion _baseLocalRotation;
        private bool _hasBaseLocalRotation;

        private void Awake()
        {
            CaptureBaseRotation();
            EnsureUsableProfile();
        }

        private void OnEnable()
        {
            CaptureBaseRotation();
        }

        private void Reset()
        {
            ApplyDefaultProfile();
            CaptureBaseRotation();
        }

        private void OnValidate()
        {
            EnsureUsableProfile();
        }

        // Should be applied after camera stabilization logic
        private void LateUpdate()
        {
            if (!_hasBaseLocalRotation)
                CaptureBaseRotation();

            // Interpolate
            _target = AnimToolkitLib.SpringInterp(_target, Vector3.zero, ref shakeProfile.springData);
            _dampedTarget = AnimToolkitLib.Glerp(_dampedTarget, _target, shakeProfile.dampSpeed);
            
            transform.localRotation = _baseLocalRotation * Quaternion.Euler(_dampedTarget);
        }

        public void PlayCameraShake()
        {
            EnsureUsableProfile();
            _target = shakeProfile.GetRandomTarget();
            _dampedTarget = _target * 0.35f;
        }

        public void EnsureUsableProfile()
        {
            bool hasRotationRange =
                !Mathf.Approximately(shakeProfile.pitch.x, 0f) ||
                !Mathf.Approximately(shakeProfile.pitch.y, 0f) ||
                !Mathf.Approximately(shakeProfile.yaw.x, 0f) ||
                !Mathf.Approximately(shakeProfile.yaw.y, 0f) ||
                !Mathf.Approximately(shakeProfile.roll.x, 0f) ||
                !Mathf.Approximately(shakeProfile.roll.y, 0f);

            bool hasSpring =
                shakeProfile.dampSpeed > 0f &&
                shakeProfile.springData.scale.sqrMagnitude > 0f &&
                shakeProfile.springData.x.maxValue > 0f &&
                shakeProfile.springData.y.maxValue > 0f &&
                shakeProfile.springData.z.maxValue > 0f;

            if (!hasRotationRange || !hasSpring)
                ApplyDefaultProfile();
        }

        private void CaptureBaseRotation()
        {
            _baseLocalRotation = transform.localRotation;
            _hasBaseLocalRotation = true;
        }

        private void ApplyDefaultProfile()
        {
            shakeProfile = new SpringShakeProfile
            {
                springData = new VectorSpringData
                {
                    x = CreateSpring(5f, 0.5f, 7f, 8f),
                    y = CreateSpring(5f, 0.5f, 7f, 8f),
                    z = CreateSpring(5f, 0.5f, 7f, 8f),
                    scale = Vector3.one
                },
                dampSpeed = 26f,
                pitch = new Vector2(-3.25f, -2.25f),
                yaw = new Vector2(-0.35f, 0.35f),
                roll = new Vector2(-0.28f, 0.28f)
            };
        }

        private static SpringData CreateSpring(
            float stiffness,
            float damping,
            float speed,
            float maxValue)
        {
            SpringData data = new(stiffness, damping, speed);
            data.maxValue = maxValue;
            return data;
        }
    }
}

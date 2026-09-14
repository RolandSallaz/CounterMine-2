using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PhotonView))]
public class PlayerLeanController : MonoBehaviourPun, IPunObservable
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private PlayerCameraLook cameraLook;
    [SerializeField] private string spineBoneName = "Spine.002";
    [SerializeField, Min(0f)] private float transitionSpeed = 7f;
    [SerializeField] private float spineRoll = 14f;
    [SerializeField, Min(0f)] private float cameraOffset = 0.22f;

    private Transform spine;
    private Quaternion spineBaseRotation;
    private float cameraBaseX;
    private float currentLean;
    private float targetLean;

    private void Awake()
    {
        playerCamera ??= GetComponentInChildren<Camera>(true);
        cameraLook ??= GetComponent<PlayerCameraLook>();
        spine = FindTransform(transform, spineBoneName);

        if (spine != null)
        {
            spineBaseRotation = spine.localRotation;
        }

        if (playerCamera != null)
        {
            cameraBaseX = playerCamera.transform.localPosition.x;
        }
    }

    private void Update()
    {
        if (photonView.IsMine)
        {
            Keyboard keyboard = Keyboard.current;
            targetLean = (keyboard?.eKey.isPressed == true ? 1f : 0f) - (keyboard?.qKey.isPressed == true ? 1f : 0f);
        }

        currentLean = Mathf.MoveTowards(currentLean, targetLean, transitionSpeed * Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (spine != null)
        {
            spine.localRotation = spineBaseRotation * Quaternion.Euler(0f, 0f, currentLean * spineRoll);
        }

        if (playerCamera != null && photonView.IsMine)
        {
            Vector3 cameraPosition = playerCamera.transform.localPosition;
            cameraPosition.x = cameraBaseX + currentLean * cameraOffset;
            playerCamera.transform.localPosition = cameraPosition;
            cameraLook?.SetLean(currentLean);
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(currentLean);
        }
        else
        {
            targetLean = (float)stream.ReceiveNext();
        }
    }

    private static Transform FindTransform(Transform root, string transformName)
    {
        if (root.name == transformName)
        {
            return root;
        }

        for (int index = 0; index < root.childCount; index++)
        {
            Transform result = FindTransform(root.GetChild(index), transformName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}

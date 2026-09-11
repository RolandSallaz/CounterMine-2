using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviourPun
{
    [SerializeField] private float speed = 5f;

    private CharacterController controller;

    private void Awake() => controller = GetComponent<CharacterController>();

    private void Update()
    {
        if (!photonView.IsMine) return;

        Vector3 movement = ReadMovement();

        controller.Move(movement * speed * Time.deltaTime);
    }

    private static Vector3 ReadMovement()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return Vector3.zero;
        }

        float horizontal = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float vertical = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        return new Vector3(horizontal, 0f, vertical).normalized;
    }
}

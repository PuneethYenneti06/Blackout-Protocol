using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    public float walkSpeed = 3.5f;
    public float runSpeed = 5f;
    public float turnSpeed = 12f;
    public float gravity = -20f;
    public float jumpHeight = 1.2f;

    CharacterController cc;
    Animator animator;
    float verticalVelocity;
    static readonly int IdleHash = Animator.StringToHash("Idle");
    static readonly int WalkHash = Animator.StringToHash("Walk");
    static readonly int RunHash = Animator.StringToHash("Run");
    int currentState;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        Vector3 input = new Vector3(
            Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).normalized;

        // Move relative to the camera if there is one, otherwise relative to the world
        Vector3 camF = Vector3.forward;
        Vector3 camR = Vector3.right;
        UnityEngine.Camera mainCam = UnityEngine.Camera.main;
        if (mainCam != null)
        {
            camF = mainCam.transform.forward; camF.y = 0f; camF.Normalize();
            camR = mainCam.transform.right; camR.y = 0f; camR.Normalize();
        }

        Vector3 dir = camF * input.z + camR * input.x;
        bool moving = dir.sqrMagnitude > 0.01f;
        bool running = Input.GetKey(KeyCode.LeftShift);
        float speed = running ? runSpeed : walkSpeed;

        if (moving)
        {
            Quaternion target = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, turnSpeed * Time.deltaTime);
        }

        if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;

        if (cc.isGrounded && Input.GetKeyDown(KeyCode.Space))
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 move = (moving ? dir * speed : Vector3.zero) + Vector3.up * verticalVelocity;
        cc.Move(move * Time.deltaTime);

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            float animSpeed = moving ? (running ? 1f : 0.5f) : 0f;
            animator.SetFloat("Speed", animSpeed, 0.1f, Time.deltaTime);

            int wanted = !moving ? IdleHash : (running ? RunHash : WalkHash);
            if (wanted != currentState)
            {
                animator.CrossFadeInFixedTime(wanted, 0.1f);
                currentState = wanted;
            }
        }
    }
}
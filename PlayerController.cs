using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    Transform buttonTransform;
    [HideInInspector]
    public Vector3 buttonTarget;

    [Header("Debug Rays")]
    public bool pushRay = true;
    public bool currentYRay = true;
    public bool landedYRay = true;
    public bool slopeRays = true;

    [Header("Player Objects")]
    [Tooltip("The parent object of the player mesh")]
    public GameObject playerMesh;
    [Tooltip("The upper parent object of the player")]
    public GameObject player;
    public GameObject deathPrefab;

    private bool isCreated = false;
    private bool isDead = false;

    // Movement block
    private bool landState;
    private float canMove;

    // Height floats
    float currentPlayerHeight;
    float landedPlayerHeight;
    float heightDiff;
    // End height floats

    [Header("Player Movement Settings")]
    public float walkSpeed = 2;
    public float runSpeed = 6;
    private float movementSpeed = 2; // Can be private

    [Header("Player Push Settings")]
    [Tooltip("The rotation speed of the player rotating to the pushable object when the raycast hits a pushable object")]
    public float rotationSpeed = 5f;
    private float pushForce = 0f; // Can be private

    [Header("Pushable Axis Tags")]
    public string[] tagListPushAxis = { "posXPush", "negXPush", "posZPush", "negZPush" };

    public bool playerIsPushing; // Debugging purposes
    private bool playerIsInPushingTrigger; // Debugging purposes

    private bool rightSidePush; // Debugging purposes
    private bool leftSidePush; // Debugging purposes
    private bool backSidePush; // Debugging purposes
    private bool frontSidePush; // Debugging purposes

    // The pushable gameObject and it's Rigidbody
    GameObject pushableObject;
    Rigidbody pushableObjectRB;

    // The transforms of the different sides of the pushable
    Transform pushableObjectRightSide;
    Transform pushableObjectLeftSide;
    Transform pushableObjectBackSide;
    Transform pushableObjectFrontSide;

    [Header("Light Objects")]
    public float pushSpeedLight = 1;
    public float playerPushForceLight = 2.0f;
    public float pushSpeedAnimLight = 1f;
    [Header("Heavy Objects")]
    public float pushSpeedHeavy = 1;
    public float playerPushForceHeavy = 2.0f;
    public float pushSpeedAnimHeavy = 1f;

    [Header("Player Jump Settings")]
    public float gravity = -12;
    public float jumpHeight = 1;
    public float jumpHeightIdle = 1;
    float velocityY;
    float lastVelocityY = 0;
    [Header("Landing distances")]
    public float minHardLanding = 0f;
    public float maxHardLanding = 0f;
    public float playerDeathHeight = 0f;

    [Header("Raycast Settings")]
    public float distanceBelowPlayer = 0.3f;
    [Tooltip("The max y distance the player can snap to")]
    public float maxSnap = 0.1f;
    public float distanceForPush = 2f;
    public float distanceForInteractable = 2f;

    [Header("Smooth Settings")]
    [Tooltip("The time it takes to rotate, this value gets replaced by 'maxTurnSmoothTimePushing', when the player is pushing an object")]
    public float turnSmoothTime = 0.2f;
    [Tooltip("The time it takes to rotate while pushing")]
    public float maxTurnSmoothTimePushing = 20f;
    [Tooltip("The speed to rotate while pushing")]
    public float speedSmoothTime = 0.1f;
    float turnSmoothVelocity;
    float speedSmoothVelocity;
    float currentSpeed;

    [Tooltip("The control the player has while rotating in air")]
    [Range(0, 1)]
    public float airControlPercent;

    // Components
    Animator anim;
    Transform cameraT;
    CharacterController controller;
    PlayerDeath playerDeath;

    // References to animation layers
    int groundedStateHash = Animator.StringToHash("Base Layer.Locomotion.Movement");
    int pushStateHash = Animator.StringToHash("Base Layer.Locomotion.Pushing");

    //Raycast variables
    RaycastHit hit;
    Vector3 dirDown = Vector3.down;

    // Variables for airtime and snapping to slopes
    float airTimer;
    bool isJumping = false;
    bool slopeRay = true;

    // Button delay settings
    public float timeBetweenShots = 0.3333f;  // Allow 3 interactions second and prevent spamming
    private float timestamp;

    // Setups everything on start

    void Start()
    {
        anim = GetComponent<Animator>();
        cameraT = Camera.main.transform;
        controller = GetComponent<CharacterController>();

        playerDeath = GetComponent<PlayerDeath>();

        // Debugging rays in inspector
        pushRay = true;
        currentYRay = true;
        landedYRay = true;
        slopeRays = true;
    }

    // Updates every frame
    void Update()
    {
        // Checks if the player is pushing
        PushStates();

        // Checks if the player is pushing a button
        InteractButton();

        // Determines landing states; normal - hard - death
        heightDiff = Mathf.Abs(currentPlayerHeight - landedPlayerHeight);
        PlayerCurrentYPos();

        // Input
        Vector2 input = InputManager.MainJoystick();
        Vector2 inputDir = input.normalized;
        bool running = InputManager.XButton();

        canMove = anim.GetFloat("isLanding");

        if (canMove == 1)
        {
            landState = true;
        }
        else
        {
            landState = false;
        }

        if (!landState && !isDead)
        {
            Move(inputDir, running);
        }

        // Removes all remaining motion
        if (landState)
        {
            currentSpeed = 0f;
        }

        float animationSpeedPercent = ((running) ? currentSpeed / runSpeed : currentSpeed / movementSpeed * .5f);
        anim.SetFloat("speedPercent", animationSpeedPercent, speedSmoothTime, Time.deltaTime);

        // Check if walking or running
        if (animationSpeedPercent < 0.51f)
        {
            CallJumpWalk();
        }
        else
        {
            CallJumpRun();
        }
    }

    // Movement of the player

    void Move(Vector2 inputDir, bool running)
    {
        if (inputDir != Vector2.zero)
        {
            // When the player is not pushing we can create a moving direction
            if (!playerIsPushing)
            {
                float targetRotation = Mathf.Atan2(inputDir.x, inputDir.y) * Mathf.Rad2Deg + cameraT.eulerAngles.y;
                transform.eulerAngles = Vector3.up * Mathf.SmoothDampAngle(transform.eulerAngles.y, targetRotation, ref turnSmoothVelocity, GetModifiedSmoothTime(turnSmoothTime));
            }
        }

        float targetSpeed = ((running) ? runSpeed : movementSpeed) * inputDir.magnitude;
        currentSpeed = Mathf.SmoothDamp(currentSpeed, targetSpeed, ref speedSmoothVelocity, GetModifiedSmoothTime(speedSmoothTime));

        velocityY += Time.deltaTime * gravity;

        Vector3 velocity = transform.forward * currentSpeed + Vector3.up * velocityY;
        controller.Move(velocity * Time.deltaTime);

        currentSpeed = new Vector2(controller.velocity.x, controller.velocity.z).magnitude;

        float animationSpeedPercent = ((running) ? currentSpeed / runSpeed : currentSpeed / movementSpeed * .5f);

        // Checks if player is not grounded and is falling
        if (GroundCheck())
        {
            velocityY = 0;

            // When the player is grounded the slopeRays are active
            slopeRay = false;

            // Sets the airtimer to 0 when the player is grounded
            airTimer = 0;

            if (heightDiff > minHardLanding && heightDiff < maxHardLanding)
            {
                PlayerLandedYPos();

                anim.SetBool("hardLanding", true);
                anim.SetBool("onAir", false);
                anim.SetBool("onAirIdle", false);
            }            
            else if (heightDiff > playerDeathHeight)
            {
                gravity = -9.81f;
                playerDeath.CallPlayerDeath();
            }
            else
            {
                anim.SetBool("hardLanding", false);
                anim.SetBool("onAir", false);
                anim.SetBool("onAirIdle", false);

                isJumping = false;
                PlayerLandedYPos();
            }
        }
        else
        {
            // When the player is jumping or falling the slopeRays are inactive
            slopeRay = true;

            // Starts a timer when the player is in air
            airTimer += Time.deltaTime;

            lastVelocityY = controller.velocity.y;

            // Check animationSpeedPercent to determine which air state
            if (animationSpeedPercent < 0.51f)
            {
                // When the player is in air we wait for 0.15 sec before the airstate is set in the animator. We do this to make sure the player 
                // isn't falling for very short periods when the ground state isn't true for very short times.
                if (airTimer > 0.15f)
                {
                    anim.SetBool("onAirIdle", true);
                }
            }
            else
            {
                // When the player is in air we wait for 0.15 sec before the airstate is set in the animator. We do this to make sure the player 
                // isn't falling for very short periods when the ground state isn't true for very short times.
                if (airTimer > 0.15f)
                {
                    anim.SetBool("onAir", true);
                }
            }
            anim.SetBool("hardLanding", false);

            PlayerLandedYPos();
        }
    }

    // Jumpstates of the player

    void CallJumpRun()
    {
        AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
        if (GroundCheck() && stateInfo.fullPathHash == groundedStateHash)
        {
            if (InputManager.AButton())
            {
                // Is jumping bool is set to true so the slopeRays aren't active
                isJumping = true;
                float jumpVelocityRun = Mathf.Sqrt(-2 * gravity * jumpHeight);
                velocityY = jumpVelocityRun;
                anim.SetTrigger("JumpRun");
            }
        }
    }

    void CallJumpWalk()
    {
        AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
        if (GroundCheck() && stateInfo.fullPathHash == groundedStateHash)
        {
            if (InputManager.AButton())
            {
                // Is jumping bool is set to true so the slopeRays aren't active
                isJumping = true;
                float jumpVelocityWalk = Mathf.Sqrt(-2 * gravity * jumpHeightIdle);
                velocityY = jumpVelocityWalk;
                anim.SetTrigger("JumpWalk");
            }
        }
    }

   public void InteractButton()
    {
        if (InteractButtonCheck())
        {
            if (Time.time >= timestamp && InputManager.YButton())
            {
                anim.SetTrigger("isPushingButton");
                timestamp = Time.time + timeBetweenShots;
            }
        }
    }

    // Creates a slider that determines how much control the player has while in mid-air

    float GetModifiedSmoothTime(float smoothTime)
    {
        if (GroundCheck())
        {
            return smoothTime;
        }
        if (airControlPercent == 0)
        {
            return float.MaxValue;
        }
        return smoothTime / airControlPercent;
    }

    // Checks if the player is colliding with rigidbodies
    
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.collider.attachedRigidbody;

        if (body == null || body.isKinematic)
            return;

        if (hit.moveDirection.y < -.3f)
            return;

        Vector3 pushDirection = new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z);
        body.velocity = pushForce * pushDirection;
    }

    /*****************************************************************************************************
    ** Below we handle the pushing for 2 types of objects, each with their own weight. 
    ** We create a raycast forward of the player that checks for pushable tags i.e. "PushabeleLight"
    ** and add different movementSpeeds and pushForces accordingly. We also get some objects, components
    ** and tranforms to use as rotation helpers.
    ** When the player is moving while pushing we set animations and tell the player to rotate towards
    ** the pushableObject based on the side the player is standing. Therefore we use triggers around the
    ** pushableObject.
    ** Another thing we do is removing all the velocity from the rigidBody attached to the pushableObject
    ** when the player stops pushing.
    *****************************************************************************************************/

    public void PushStates() {
        // Creating the raycast origin Vector3's
        Vector3 forward = transform.TransformDirection(Vector3.forward) * distanceForPush;
        Vector3 middle = controller.transform.position - new Vector3(0, -controller.height / 2, 0);

        // Inspector bool
        if (pushRay)
        {
            Debug.DrawRay(middle, forward, Color.cyan);
        }

        // Force the pushForce and movementSpeed to normal when the player is not pushing
        pushForce = 0f;
        movementSpeed = walkSpeed;

        // Draws a raycast in front of the player to check if the object in front of the player is a pushable object
        if (Physics.Raycast(middle, forward, out hit, distanceForPush))
        {
            if (InputManager.BButton() && playerIsInPushingTrigger)
            {
                PushableInfo();

                playerIsPushing = true;
                anim.SetBool("isPushing", true);

                if (hit.collider.tag == "PushableLight")
                {
                    pushForce = playerPushForceLight;
                    movementSpeed = pushSpeedLight;
                }
                else if (hit.collider.tag == "PushableHeavy")
                {
                    pushForce = playerPushForceHeavy;
                    movementSpeed = pushSpeedHeavy;
                }

                // Checks the players speed now instead off movement. This is neccesary when the player is pushing a pushable into a collider. 
                // The player and pushable never stop moving because of force.
                if (currentSpeed < 0.15f)
                {
                    //Removes all remaining velocity, when the player stops pushing
                    pushableObjectRB.velocity = Vector3.zero;
                    pushableObjectRB.angularVelocity = Vector3.zero;

                    anim.SetFloat("pushSpeedAnim", 0f);
                }
                else
                {
                    // Calls a rotation method
                    PushingRot();
                    if (hit.collider.tag == "PushableLight")
                    {
                        anim.SetFloat("pushSpeedAnim", pushSpeedAnimLight);
                    }
                    else if (hit.collider.tag == "PushableHeavy")
                    {
                        anim.SetFloat("pushSpeedAnim", pushSpeedAnimHeavy);
                    }
                }
            }
            else
            {
                anim.SetBool("isPushing", false);
                pushForce = 0f;
                movementSpeed = walkSpeed;
                playerIsPushing = false;
            }
        }
        else
        {
            anim.SetBool("isPushing", false);
            playerIsPushing = false;
        }

        // Setting the time it takes to rotate when pushing
        AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
        if (stateInfo.fullPathHash == pushStateHash)
        {
            turnSmoothTime = maxTurnSmoothTimePushing;
        }
        else
        {
            turnSmoothTime = 0.1f;
        }
    }

    public void PushableInfo()
    {
        // Getting the GameObject and it's Rigidbody
        pushableObject = hit.transform.gameObject;
        pushableObjectRB = pushableObject.GetComponent<Rigidbody>();

        // Getting the transforms of the pushing axis
        pushableObjectRightSide = pushableObject.gameObject.transform.GetChild(0);
        pushableObjectLeftSide = pushableObject.gameObject.transform.GetChild(1);
        pushableObjectBackSide = pushableObject.gameObject.transform.GetChild(2);
        pushableObjectFrontSide = pushableObject.gameObject.transform.GetChild(3);
    }

    // Getting on which side of the pushable the player is
    public void OnTriggerEnter(Collider other)
    {
        // Gets a list of tags to check
        foreach (string TagName in tagListPushAxis)
        {
            if (other.gameObject.tag == TagName)
            {
                playerIsInPushingTrigger = true;
            }
        }
        if (other.tag == "posXPush")
        {
            rightSidePush = true;
            leftSidePush = false;
            backSidePush = false;
            frontSidePush = false;
        }
        else if (other.tag == "negXPush")
        {
            rightSidePush = false;
            leftSidePush = true;
            backSidePush = false;
            frontSidePush = false;
        }
        else if (other.tag == "posZPush")
        {
            rightSidePush = false;
            leftSidePush = false;
            backSidePush = true;
            frontSidePush = false;
        }
        else if (other.tag == "negZPush")
        {
            rightSidePush = false;
            leftSidePush = false;
            backSidePush = false;
            frontSidePush = true;
        }
    }

    // Returning the booleans to the original state when the player exits

    private void OnTriggerExit(Collider other)
    {
        // Gets a list of tags to check
        foreach (string TagName in tagListPushAxis)
        {
            if (other.gameObject.tag == TagName)
            {
                rightSidePush = false;
                leftSidePush = false;
                backSidePush = false;
                frontSidePush = false;

                playerIsInPushingTrigger = false;
            }
        }
    }

    // Rotating the player towards the pushableObject depending on the side

    public void PushingRot()
    {
        if (rightSidePush)
        {
            controller.transform.rotation = Quaternion.Lerp(player.transform.rotation, pushableObjectRightSide.transform.rotation, rotationSpeed * Time.deltaTime);
        }
        if (leftSidePush)
        {
            controller.transform.rotation = Quaternion.Lerp(player.transform.rotation, pushableObjectLeftSide.transform.rotation, rotationSpeed * Time.deltaTime);
        }
        if (backSidePush)
        {
            controller.transform.rotation = Quaternion.Lerp(player.transform.rotation, pushableObjectBackSide.transform.rotation, rotationSpeed * Time.deltaTime);
        }
        if (frontSidePush)
        {
            controller.transform.rotation = Quaternion.Lerp(player.transform.rotation, pushableObjectFrontSide.transform.rotation, rotationSpeed * Time.deltaTime);
        }
    }

    // Raycast Setups

    // Raycast to get the players Y position

    void PlayerCurrentYPos()
    {
        RaycastHit currentYHit;

        // Inspector bool
        if (currentYRay)
        {
            Debug.DrawRay(transform.position, -Vector3.up * .1f, Color.yellow);
        }

        if (Physics.Raycast(transform.position, -Vector3.up, out currentYHit, .1f))
        {
            currentPlayerHeight = currentYHit.point.y;
        }
    }

    void PlayerLandedYPos()
    {
        RaycastHit landedYHit;

        // Inspector bool
        if (landedYRay)
        {
            Debug.DrawRay(transform.position, -Vector3.up * 1f, Color.blue);
        }

        if (Physics.Raycast(transform.position, -Vector3.up, out landedYHit, 1f))
        {
            landedPlayerHeight = landedYHit.point.y;
        }
    }

    // Checks if the CharacterController is grounded and checks for slopes, to correct the players y axis

    private bool GroundCheck()
    {
        RaycastHit hitSlope;

        // The lenght of the outer rays
        float sideRays = 0.4f;

        // Creates vectors for the origin of the vector3's
        Vector3 leftRootHeight = new Vector3(transform.position.x, transform.position.y + sideRays, transform.position.z);
        Vector3 rightRootHeight = new Vector3(transform.position.x, transform.position.y + sideRays, transform.position.z);

        Vector3 leftRoot = leftRootHeight + -transform.right * 0.4f;
        Vector3 rightRoot = rightRootHeight + transform.right * 0.4f;

        // Checks if the player isn't falling or jumping
        if (!isJumping && !slopeRay) {

            // Inspector bool
            if (slopeRays)
            {
                Debug.DrawRay(transform.position, dirDown * distanceBelowPlayer, Color.red);
                Debug.DrawRay(leftRoot, dirDown * (distanceBelowPlayer + sideRays), Color.red);
                Debug.DrawRay(rightRoot, dirDown * (distanceBelowPlayer + sideRays), Color.red);
            }

            if (Physics.Raycast(transform.position, dirDown, out hitSlope, distanceBelowPlayer) )
            {
                controller.Move(new Vector3(0, -hitSlope.distance, 0));
                return true;
            }
            else if (Physics.Raycast(leftRoot, dirDown, out hitSlope, distanceBelowPlayer + sideRays))
            {
                controller.Move(new Vector3(0, -hitSlope.distance, 0));
                return true;
            }
            else if (Physics.Raycast(rightRoot, dirDown, out hitSlope, distanceBelowPlayer + sideRays))
            {
                controller.Move(new Vector3(0, -hitSlope.distance, 0));
                return true;
            }
        }

        if (controller.isGrounded)
            return true;
        
        return false;
    }

    private bool InteractButtonCheck()
    {
        RaycastHit hitInteractableButton;

        Vector3 forward = transform.TransformDirection(Vector3.forward) * distanceForInteractable;
        Vector3 middle = controller.transform.position - new Vector3(0, -controller.height / 2.4f, 0);
        Vector3 middleLeft = middle + transform.right * 0.2f;
        Vector3 middleRight = middle + transform.right * 0.4f;

        Debug.DrawRay(middle, forward, Color.blue);
        Debug.DrawRay(middleLeft, forward, Color.blue);
        Debug.DrawRay(middleRight, forward, Color.blue);

        if (Physics.Raycast(middle, forward, out hitInteractableButton, distanceForInteractable))
        {
            if (hitInteractableButton.collider.tag == "InteractableButton")
            {
                buttonTransform = hitInteractableButton.transform.GetChild(0);
                buttonTarget = new Vector3(buttonTransform.position.x, buttonTransform.position.y, buttonTransform.position.z);
                return true;
            }
        }
        else if (Physics.Raycast(middleLeft, forward, out hitInteractableButton, distanceForInteractable))
        {
            if (hitInteractableButton.collider.tag == "InteractableButton")
            {
                buttonTransform = hitInteractableButton.transform.GetChild(0);
                buttonTarget = new Vector3(buttonTransform.position.x, buttonTransform.position.y, buttonTransform.position.z);
                return true;
            }
        }
        else if (Physics.Raycast(middleRight, forward, out hitInteractableButton, distanceForInteractable))
        {
            if (hitInteractableButton.collider.tag == "InteractableButton")
            {
                buttonTransform = hitInteractableButton.transform.GetChild(0);
                buttonTarget = new Vector3(buttonTransform.position.x, buttonTransform.position.y, buttonTransform.position.z);
                return true;
            }
        }
        return false;
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLibManager;
using LiteNetLib;

[RequireComponent(typeof(Rigidbody))]
public class SimpleCharacter : LiteNetLibBehaviour
{
    [Header("Movement settings")]
    public float moveSpeed = 5f;
    public float jumpHeight = 2f;
    public float mouseSensitivity = 100f;

    private SimpleCharacterInput tempInput = new SimpleCharacterInput();
    private SimpleCharacterResult tempResult = new SimpleCharacterResult();
    // Owner client and server would store it's inputs in this list
    private List<SimpleCharacterInput> inputList = new List<SimpleCharacterInput>();
    // This list stores results of movement and rotation. Needed for non-owner client interpolation
    private List<SimpleCharacterResult> resultList = new List<SimpleCharacterResult>();
    // Interpolation related variables
    private bool playData = false;
    private float dataStep = 0;
    private float lastTimestamp = 0;
    private Vector3 startPosition;
    private Quaternion startRotation;

    #region Temp components
    private Transform tempTransform;
    public Transform TempTransform
    {
        get
        {
            if (tempTransform == null)
                tempTransform = GetComponent<Transform>();
            return tempTransform;
        }
    }
    private Rigidbody tempRigidbody;
    public Rigidbody TempRigidbody
    {
        get
        {
            if (tempRigidbody == null)
                tempRigidbody = GetComponent<Rigidbody>();
            return tempRigidbody;
        }
    }
    #endregion

    protected virtual void Awake()
    {
        RegisterNetFunction("SendInput", new LiteNetLibFunction<SimpleCharacterInput>(SendInputCallback));
        RegisterNetFunction("SendResult", new LiteNetLibFunction<SimpleCharacterResult>(SendResultCallback));
    }

    protected virtual void Start()
    {
        SetStartPosition(TempTransform.position);
        SetStartRotation(TempTransform.rotation);
    }

    public void SetStartPosition(Vector3 position)
    {
        tempResult.position = position;
    }

    public void SetStartRotation(Quaternion rotation)
    {
        tempResult.rotation = rotation;
    }

    private void SendInput(SimpleCharacterInput input)
    {
        CallNetFunction("SendInput", DeliveryMethod.ReliableOrdered, FunctionReceivers.Server, input);
    }

    private void SendInputCallback(SimpleCharacterInput inputParam)
    {
        inputList.Add(inputParam);
    }

    private void SendResult(SimpleCharacterResult result)
    {
        CallNetFunction("SendResult", DeliveryMethod.ReliableOrdered, FunctionReceivers.All, result);
    }

    private void SendResultCallback(SimpleCharacterResult resultParam)
    {
        var result = resultParam;
        // Discard out of order results
        if (result.timestamp <= lastTimestamp)
            return;

        lastTimestamp = result.timestamp;
        // Non-owner client
        if (!IsOwnerClient && !IsServer)
        {
            //Adding results to the results list so they can be used in interpolation process
            result.timestamp = Time.time;
            resultList.Add(result);
        }
        
        // Owner client
        // Server client reconciliation process should be executed in order to client's rotation and position with server values but do it without jittering
        if (IsOwnerClient && !IsServer)
        {
            // Update client's position and rotation with ones from server 
            tempResult.rotation = result.rotation;
            tempResult.position = result.position;
            var foundIndex = -1;
            // Search recieved time stamp in client's inputs list
            for (var i = 0; i < inputList.Count; i++)
            {
                // If time stamp found run through all inputs starting from needed time stamp 
                if (inputList[i].timestamp > result.timestamp)
                {
                    foundIndex = i;
                    break;
                }
            }
            if (foundIndex == -1)
            {
                // Clear Inputs list if no needed records found
                inputList.Clear();
                return;
            }
            // Replay recorded inputs
            for (var i = foundIndex; i < inputList.Count; ++i)
            {
                tempResult.rotation = Rotate(inputList[i], tempResult);
                tempResult.position = Move(inputList[i], tempResult);
            }
            // Remove all inputs before time stamp
            int targetCount = inputList.Count - foundIndex;
            while (inputList.Count > targetCount)
            {
                inputList.RemoveAt(0);
            }
        }
    }

    private void Update()
    {
        if (IsOwnerClient)
        {
            tempInput.horizontal = Input.GetAxis("Horizontal");
            tempInput.vertical = Input.GetAxis("Vertical");
            tempInput.isJump = Input.GetButtonDown("Jump");
        }
    }

    private void FixedUpdate()
    {
        if (IsOwnerClient)
        {
            tempInput.timestamp = Time.time;
            // Client side prediction for non-authoritative client or plane movement and rotation for listen server/host
            var lastPosition = tempResult.position;
            var lastRotation = tempResult.rotation;
            tempResult.rotation = Rotate(tempInput, tempResult);
            tempResult.position = Move(tempInput, tempResult);
            if (IsServer)
            {
                // Listen server/host part
                // Sending results to other clients(state sync)
                if (dataStep >= sendInterval)
                {
                    if (Vector3.Distance(tempResult.position, lastPosition) > 0 || Quaternion.Angle(tempResult.rotation, lastRotation) > 0)
                    {
                        tempResult.timestamp = tempInput.timestamp;
                        SendResult(tempResult);
                    }
                    dataStep = 0;
                }
                dataStep += Time.fixedDeltaTime;
            }
            else
            {
                // Owner client. Non-authoritative part
                // Add inputs to the inputs list so they could be used during reconciliation process
                if (Vector3.Distance(tempResult.position, lastPosition) > 0 || Quaternion.Angle(tempResult.rotation, lastRotation) > 0)
                {
                    inputList.Add(tempInput);
                }
                // Sending inputs to the server
                // Unfortunately there is now method overload for [Command] so I need to write several almost similar functions
                // This one is needed to save on network traffic
                if (Vector3.Distance(tempResult.position, lastPosition) <= 0)
                {
                    tempInput.horizontal = 0;
                    tempInput.vertical = 0;
                }
                SendInput(tempInput);
            }
        }
        else
        {
            if (IsServer)
            {
                // Check if there is atleast one record in inputs list
                if (inputList.Count == 0)
                    return;

                // Move and rotate part. Nothing interesting here
                var input = inputList[0];
                inputList.RemoveAt(0);
                var lastPosition = tempResult.position;
                var lastRotation = tempResult.rotation;
                tempResult.rotation = Rotate(input, tempResult);
                tempResult.position = Move(input, tempResult);

                // Sending results to other clients(state sync)
                if (dataStep >= sendInterval)
                {
                    if (Vector3.Distance(tempResult.position, lastPosition) > 0 || Quaternion.Angle(tempResult.rotation, lastRotation) > 0)
                    {
                        tempResult.timestamp = input.timestamp;
                        SendResult(tempResult);
                    }
                    dataStep = 0;
                }
                dataStep += Time.fixedDeltaTime;
            }
            else
            {
                // Non-owner client
                // there should be at least two records in the results list so it would be possible to interpolate between them in case if there would be some dropped packed or latency spike
                // And yes this stupid structure should be here because it should start playing data when there are at least two records and continue playing even if there is only one record left 
                if (resultList.Count == 0)
                    playData = false;

                if (resultList.Count >= 2)
                    playData = true;

                if (playData)
                {
                    if (dataStep == 0)
                    {
                        startPosition = tempResult.position;
                        startRotation = tempResult.rotation;
                    }
                    float step = 1f / sendInterval;
                    tempResult.rotation = Quaternion.Slerp(startRotation, resultList[0].rotation, dataStep);
                    tempResult.position = Vector3.Lerp(startPosition, resultList[0].position, dataStep);
                    dataStep += step * Time.fixedDeltaTime;
                    if (dataStep >= 1)
                    {
                        dataStep = 0;
                        resultList.RemoveAt(0);
                    }
                }
                UpdateRotation(tempResult.rotation);
                UpdatePosition(tempResult.position);
            }
        }
    }
    
    // Next virtual functions can be changed in inherited class for custom movement and rotation mechanics
    // So it would be possible to control for example humanoid or vehicle from one script just by changing controlled pawn
    public virtual void UpdatePosition(Vector3 newPosition)
    {
        TempTransform.position = newPosition;
    }

    public virtual void UpdateRotation(Quaternion newRotation)
    {
        TempTransform.rotation = newRotation;
    }

    public virtual Vector3 Move(SimpleCharacterInput input, SimpleCharacterResult current)
    {
        Vector3 velocity = TempRigidbody.velocity;
        var moveDirection = new Vector3(input.horizontal, 0, input.vertical);
        {
            var moveDirectionMagnitude = moveDirection.sqrMagnitude;
            if (moveDirectionMagnitude > 1)
                moveDirection = moveDirection.normalized;
            
            var targetVelocity = moveDirection * moveSpeed;

            // Apply a force that attempts to reach our target velocity
            Vector3 velocityChange = (targetVelocity - velocity);
            velocityChange.x = Mathf.Clamp(velocityChange.x, -moveSpeed, moveSpeed);
            velocityChange.y = 0;
            velocityChange.z = Mathf.Clamp(velocityChange.z, -moveSpeed, moveSpeed);
            TempRigidbody.AddForce(velocityChange, ForceMode.VelocityChange);
        }
        if (input.isJump)
            TempRigidbody.velocity = new Vector3(velocity.x, CalculateJumpVerticalSpeed(), velocity.z);
        return TempTransform.position;
    }

    protected float CalculateJumpVerticalSpeed()
    {
        // From the jump height and gravity we deduce the upwards speed 
        // for the character to reach at the apex.
        return Mathf.Sqrt(2f * jumpHeight * -Physics.gravity.y);
    }

    public virtual Quaternion Rotate(SimpleCharacterInput input, SimpleCharacterResult current)
    {
        TempTransform.rotation = current.rotation;
        return TempTransform.rotation;
    }
}

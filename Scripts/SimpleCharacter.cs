using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLibHighLevel;

public class SimpleCharacter : LiteNetLibBehaviour
{
    [Header("Network settings")]
    public float sendInterval = 0.05f;
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
    private float step = 0;

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
    #endregion

    protected virtual void Awake()
    {
        RegisterNetFunction("SendInput", new LiteNetLibFunction<SimpleCharacterInputField>(SendInputCallback));
        RegisterNetFunction("SendResult", new LiteNetLibFunction<SimpleCharacterResultField>(SendResultCallback));
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
        CallNetFunction("SendInput", FunctionReceivers.Server, input);
    }

    private void SendInputCallback(SimpleCharacterInputField inputParam)
    {
        var input = inputParam.Value;
        inputList.Add(input);
    }

    private void SendResult(SimpleCharacterResult result)
    {
        CallNetFunction("SendResult", FunctionReceivers.All, result);
    }

    private void SendResultCallback(SimpleCharacterResultField resultParam)
    {
        var result = resultParam.Value;
        // Discard out of order results
        if (result.timestamp <= lastTimestamp)
            return;

        lastTimestamp = result.timestamp;
        // Non-owner client
        if (!IsLocalClient && !IsServer)
        {
            //Adding results to the results list so they can be used in interpolation process
            result.timestamp = Time.time;
            resultList.Add(result);
        }
        
        // Owner client
        // Server client reconciliation process should be executed in order to client's rotation and position with server values but do it without jittering
        if (IsLocalClient && !IsServer)
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
                while (inputList.Count != 0)
                {
                    inputList.RemoveAt(0);
                }
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
        if (IsLocalClient)
        {
            tempInput.horizontal = Input.GetAxis("Horizontal");
            tempInput.vertical = Input.GetAxis("Vertical");
            tempInput.isJump = Input.GetButtonDown("Jump");
        }
    }

    private void FixedUpdate()
    {
        if (IsLocalClient)
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
                var inputs = inputList[0];
                inputList.RemoveAt(0);
                var lastPosition = tempResult.position;
                var lastRotation = tempResult.rotation;
                tempResult.rotation = Rotate(inputs, tempResult);
                tempResult.position = Move(inputs, tempResult);

                // Sending results to other clients(state sync)
                if (dataStep >= sendInterval)
                {
                    if (Vector3.Distance(tempResult.position, lastPosition) > 0 || Quaternion.Angle(tempResult.rotation, lastRotation) > 0)
                    {
                        tempResult.timestamp = inputs.timestamp;
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
                    step = 1f / sendInterval;
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
        TempTransform.position = current.position;
        TempTransform.Translate(Vector3.ClampMagnitude(new Vector3(input.horizontal, 0, input.vertical), 1) * moveSpeed * Time.fixedDeltaTime);
        return TempTransform.position;
    }

    public virtual Quaternion Rotate(SimpleCharacterInput input, SimpleCharacterResult current)
    {
        TempTransform.rotation = current.rotation;
        return TempTransform.rotation;
    }
}
